#nullable disable
// ***********************************************************************
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.
// Redistribution and use in source and binary forms, with or without
// modification, (subject to the limitations in the disclaimer below)
// are permitted provided that the following conditions are met:
// - Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
// - Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
// - Neither the name of Skype Limited, nor the names of specific
// contributors, may be used to endorse or promote products derived from
// this software without specific prior written permission.
// NO EXPRESS OR IMPLIED LICENSES ARE GRANTED TO ANY PARTY'S PATENT RIGHTS
// BY THIS LICENSE. THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
// CONTRIBUTORS ''AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING,
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
// COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
// NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF
// USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
// ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
// THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ***********************************************************************
//
// SKP_Silk_encode_frame_FIX.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_encode_frame_FIX.c（编码器核心 + LBRR 冗余编码）
//
// PORT-NOTE: 本文件主函数与 C 文件同名（SKP_Silk_encode_frame_FIX），
//   C# 禁止成员名与其包含类型同名（CS0542），故类名加后缀 _impl，
//   方法名保持 C 原名（本簇统一约定，详见 SKP_Silk_enc_API.cs 文件头）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 地基：错误码
using static AaR.Server.Silk.SKP_Silk_errors;
// 本簇其他文件
using static AaR.Server.Silk.SKP_Silk_control_codec_FIX;           // SKP_Silk_LBRR_ctrl_FIX
// 其他簇（类名 = C 文件名，PORTING.md §1）
// PORT-NOTE: 下列文件的函数与其文件同名（CS0542/CS1955 受影响文件），若集成期
//   类名统一加后缀，仅需更新对应 using static 行；调用点一律不变。
using static AaR.Server.Silk.SKP_Silk_VAD;                         // SKP_Silk_VAD_GetSA_Q8（类名无冲突）
using static AaR.Server.Silk.SKP_Silk_HP_variable_cutoff_FIX_impl;      // SKP_Silk_HP_variable_cutoff_FIX
using static AaR.Server.Silk.SKP_Silk_LP_variable_cutoff_impl;          // SKP_Silk_LP_variable_cutoff
using static AaR.Server.Silk.SKP_Silk_find_pitch_lags_FIX_impl;        // SKP_Silk_find_pitch_lags_FIX
using static AaR.Server.Silk.SKP_Silk_noise_shape_analysis_FIX_impl;   // SKP_Silk_noise_shape_analysis_FIX
using static AaR.Server.Silk.SKP_Silk_prefilter_FIX_impl;              // SKP_Silk_prefilter_FIX
using static AaR.Server.Silk.SKP_Silk_find_pred_coefs_FIX_impl;        // SKP_Silk_find_pred_coefs_FIX
using static AaR.Server.Silk.SKP_Silk_process_gains_FIX_impl;          // SKP_Silk_process_gains_FIX
using static AaR.Server.Silk.SKP_Silk_NSQ_impl;                                 // SKP_Silk_NSQ
using static AaR.Server.Silk.SKP_Silk_NSQ_del_dec_impl;                 // SKP_Silk_NSQ_del_dec
using static AaR.Server.Silk.SKP_Silk_encode_parameters_impl;         // SKP_Silk_encode_parameters
using static AaR.Server.Silk.SKP_Silk_range_coder;                  // range_enc_init / range_encoder /
                                                                   // range_coder_get_length / range_enc_wrap_up
using static AaR.Server.Silk.SKP_Silk_gain_quant;                   // SKP_Silk_gains_dequant
// 表（其他簇）
using static AaR.Server.Silk.SKP_Silk_tables_other;                 // SKP_Silk_FrameTermination_CDF

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_encode_frame_FIX.c —— 编码器主函数与 LBRR 冗余编码。
/// </summary>
internal static class SKP_Silk_encode_frame_FIX_impl
{
    /****************/
    /* Encode frame */
    /****************/
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）：
    //   pCode → byte[] + pCodeOff；pnBytesOut → ref short；pIn → short[] + pInOff。
    //   C 局部指针 x_frame / res_pitch_frame 改为整型偏移（基址分别为 psEnc.x_buf / res_pitch）。
    public static int SKP_Silk_encode_frame_FIX(
        SKP_Silk_encoder_state_FIX      psEnc,             /* I/O  Pointer to Silk FIX encoder state           */
        byte[]                          pCode, int pCodeOff,/* O    Pointer to payload                          */
        ref short                       pnBytesOut,        /* I/O  Pointer to number of payload bytes          */
                                                            /*      input: max length; output: used             */
        short[]                         pIn, int pInOff    /* I    Pointer to input speech frame               */
    )
    {
        // PORT-NOTE: C 为栈上未初始化结构；托管 new() 零初始化。所有被读取的字段
        //   都在读取前由编码管线写入，与 gcc 参考构建行为一致。
        SKP_Silk_encoder_control_FIX sEncCtrl = new();
        int     nBytes, ret = 0;
        int     x_frame, res_pitch_frame;              /* 偏移：基址分别为 psEnc.x_buf / res_pitch */
        short[] xfw = new short[ MAX_FRAME_LENGTH ];
        short[] pIn_HP = new short[ MAX_FRAME_LENGTH ];
        short[] res_pitch = new short[ 2 * MAX_FRAME_LENGTH + LA_PITCH_MAX ];
        int     LBRR_idx, frame_terminator, SNR_dB_Q7;
        ushort[] FrameTermination_CDF;
        /* Low bitrate redundancy parameters */
        byte[]   LBRRpayload = new byte[ MAX_ARITHM_BYTES ];
        short    nBytesLBRR;


        sEncCtrl.sCmn.Seed = psEnc.sCmn.frameCounter++ & 3;
        /**************************************************************/
        /* Setup Input Pointers, and insert frame in input buffer    */
        /*************************************************************/
        x_frame         = psEnc.sCmn.frame_length; /* start of frame to encode */
        res_pitch_frame = psEnc.sCmn.frame_length; /* start of pitch LPC residual frame */

        /****************************/
        /* Voice Activity Detection */
        /****************************/
        // PORT-NOTE: C 的标量出参 &psEnc->speech_activity_Q8 / &SNR_dB_Q7 /
        //   &sEncCtrl.input_tilt_Q15 均为只写（VAD.c 内先写后读），按 out 传递
        //   （原型见 SKP_Silk_main.h）。
        ret = SKP_Silk_VAD_GetSA_Q8( psEnc.sCmn.sVAD, out psEnc.speech_activity_Q8, out SNR_dB_Q7,
                                     sEncCtrl.input_quality_bands_Q15, 0, out sEncCtrl.input_tilt_Q15,
                                     pIn, pInOff, psEnc.sCmn.frame_length );

        /*******************************************/
        /* High-pass filtering of the input signal */
        /*******************************************/
        /* PORT-NOTE: C 条件编译 #if HIGH_PASS_INPUT —— 值为 1，保留该分支 */
        {
            /* Variable high-pass filter */
            SKP_Silk_HP_variable_cutoff_FIX( psEnc, sEncCtrl, pIn_HP, 0, pIn, pInOff );
        }

        /* PORT-NOTE: C 条件编译 #if SWITCH_TRANSITION_FILTERING —— 值为 1，保留该分支 */
        {
            /* Ensure smooth bandwidth transitions */
            SKP_Silk_LP_variable_cutoff( psEnc.sCmn.sLP, psEnc.x_buf, x_frame + LA_SHAPE_MS * psEnc.sCmn.fs_kHz,
                pIn_HP, 0, psEnc.sCmn.frame_length );
        }

        /*****************************************/
        /* Find pitch lags, initial LPC analysis */
        /*****************************************/
        SKP_Silk_find_pitch_lags_FIX( psEnc, sEncCtrl, res_pitch, 0, psEnc.x_buf, x_frame );

        /************************/
        /* Noise shape analysis */
        /************************/
        SKP_Silk_noise_shape_analysis_FIX( psEnc, sEncCtrl, res_pitch, res_pitch_frame, psEnc.x_buf, x_frame );

        /*****************************************/
        /* Prefiltering for noise shaper         */
        /*****************************************/
        SKP_Silk_prefilter_FIX( psEnc, sEncCtrl, xfw, 0, psEnc.x_buf, x_frame );

        /***************************************************/
        /* Find linear prediction coefficients (LPC + LTP) */
        /***************************************************/
        SKP_Silk_find_pred_coefs_FIX( psEnc, sEncCtrl, res_pitch, 0 );

        /****************************************/
        /* Process gains                        */
        /****************************************/
        SKP_Silk_process_gains_FIX( psEnc, sEncCtrl );


        /****************************************/
        /* Low Bitrate Redundant Encoding       */
        /****************************************/
        nBytesLBRR = MAX_ARITHM_BYTES;
        SKP_Silk_LBRR_encode_FIX( psEnc, sEncCtrl, LBRRpayload, 0, ref nBytesLBRR, xfw, 0 );

        /*****************************************/
        /* Noise shaping quantization            */
        /*****************************************/
        // PORT-NOTE: C 原型 PredCoef_Q12[ 2 * MAX_LPC_ORDER ] 为平铺指针，但 NSQ 内部
        //   会以 +MAX_LPC_ORDER 步进取第二行（C 结构体两行连续）。地基结构为交错数组
        //   short[2][]，行不连续，故按交错数组传参（short[][] PredCoef_Q12，无偏移），
        //   调用点传 sEncCtrl.PredCoef_Q12 整体。若 NSQ 簇最终签名不同，仅需调整
        //   本处两处调用点。
        if( psEnc.sCmn.nStatesDelayedDecision > 1 || psEnc.sCmn.warping_Q16 > 0 ) {
            SKP_Silk_NSQ_del_dec( psEnc.sCmn, sEncCtrl.sCmn, psEnc.sCmn.sNSQ, xfw, 0,
                psEnc.sCmn.q, 0, sEncCtrl.sCmn.NLSFInterpCoef_Q2,
                sEncCtrl.PredCoef_Q12, sEncCtrl.LTPCoef_Q14, 0, sEncCtrl.AR2_Q13, 0, sEncCtrl.HarmShapeGain_Q14, 0,
                sEncCtrl.Tilt_Q14, 0, sEncCtrl.LF_shp_Q14, 0, sEncCtrl.Gains_Q16, 0, sEncCtrl.Lambda_Q10,
                sEncCtrl.LTP_scale_Q14 );
        } else {
            SKP_Silk_NSQ( psEnc.sCmn, sEncCtrl.sCmn, psEnc.sCmn.sNSQ, xfw, 0,
                psEnc.sCmn.q, 0, sEncCtrl.sCmn.NLSFInterpCoef_Q2,
                sEncCtrl.PredCoef_Q12, sEncCtrl.LTPCoef_Q14, 0, sEncCtrl.AR2_Q13, 0, sEncCtrl.HarmShapeGain_Q14, 0,
                sEncCtrl.Tilt_Q14, 0, sEncCtrl.LF_shp_Q14, 0, sEncCtrl.Gains_Q16, 0, sEncCtrl.Lambda_Q10,
                sEncCtrl.LTP_scale_Q14 );
        }

        /**************************************************/
        /* Convert speech activity into VAD and DTX flags */
        /**************************************************/
        if( psEnc.speech_activity_Q8 < SKP_FIX_CONST( SPEECH_ACTIVITY_DTX_THRES, 8 ) ) {
            psEnc.sCmn.vadFlag = NO_VOICE_ACTIVITY;
            psEnc.sCmn.noSpeechCounter++;
            if( psEnc.sCmn.noSpeechCounter > NO_SPEECH_FRAMES_BEFORE_DTX ) {
                psEnc.sCmn.inDTX = 1;
            }
            if( psEnc.sCmn.noSpeechCounter > MAX_CONSECUTIVE_DTX + NO_SPEECH_FRAMES_BEFORE_DTX ) {
                psEnc.sCmn.noSpeechCounter = NO_SPEECH_FRAMES_BEFORE_DTX;
                psEnc.sCmn.inDTX           = 0;
            }
        } else {
            psEnc.sCmn.noSpeechCounter = 0;
            psEnc.sCmn.inDTX           = 0;
            psEnc.sCmn.vadFlag         = VOICE_ACTIVITY;
        }

        /****************************************/
        /* Initialize range coder               */
        /****************************************/
        if( psEnc.sCmn.nFramesInPayloadBuf == 0 ) {
            SKP_Silk_range_enc_init( psEnc.sCmn.sRC );
            psEnc.sCmn.nBytesInPayloadBuf = 0;
        }

        /****************************************/
        /* Encode Parameters                    */
        /****************************************/
        SKP_Silk_encode_parameters( psEnc.sCmn, sEncCtrl.sCmn, psEnc.sCmn.sRC, psEnc.sCmn.q, 0 );
        FrameTermination_CDF = SKP_Silk_FrameTermination_CDF;

        /****************************************/
        /* Update Buffers and State             */
        /****************************************/
        /* Update input buffer */
        SKP_memmove( psEnc.x_buf, 0, psEnc.x_buf, psEnc.sCmn.frame_length,
            ( psEnc.sCmn.frame_length + LA_SHAPE_MS * psEnc.sCmn.fs_kHz ) );

        /* Parameters needed for next frame */
        psEnc.sCmn.prev_sigtype            = sEncCtrl.sCmn.sigtype;
        psEnc.sCmn.prevLag                 = sEncCtrl.sCmn.pitchL[ NB_SUBFR - 1];
        psEnc.sCmn.first_frame_after_reset = 0;

        if( psEnc.sCmn.sRC.error != 0 ) {
            /* Encoder returned error: clear payload buffer */
            psEnc.sCmn.nFramesInPayloadBuf = 0;
        } else {
            psEnc.sCmn.nFramesInPayloadBuf++;
        }

        /****************************************/
        /* Finalize payload and copy to output  */
        /****************************************/
        if( psEnc.sCmn.nFramesInPayloadBuf * FRAME_LENGTH_MS >= psEnc.sCmn.PacketSize_ms ) {

            LBRR_idx = ( psEnc.sCmn.oldest_LBRR_idx + 1 ) & LBRR_IDX_MASK;

            /* Check if FEC information should be added */
            frame_terminator = SKP_SILK_LAST_FRAME;
            if( psEnc.sCmn.LBRR_buffer[ LBRR_idx ].usage == SKP_SILK_ADD_LBRR_TO_PLUS1 ) {
                frame_terminator = SKP_SILK_LBRR_VER1;
            }
            if( psEnc.sCmn.LBRR_buffer[ psEnc.sCmn.oldest_LBRR_idx ].usage == SKP_SILK_ADD_LBRR_TO_PLUS2 ) {
                frame_terminator = SKP_SILK_LBRR_VER2;
                LBRR_idx = psEnc.sCmn.oldest_LBRR_idx;
            }

            /* Add the frame termination info to stream */
            SKP_Silk_range_encoder( psEnc.sCmn.sRC, frame_terminator, FrameTermination_CDF, 0 );

            /* Payload length so far */
            SKP_Silk_range_coder_get_length( psEnc.sCmn.sRC, out nBytes );

            /* Check that there is enough space in external output buffer, and move data */
            if( pnBytesOut >= nBytes ) {
                SKP_Silk_range_enc_wrap_up( psEnc.sCmn.sRC );
                SKP_memcpy( pCode, pCodeOff, psEnc.sCmn.sRC.buffer, 0, nBytes );

                if( frame_terminator > SKP_SILK_MORE_FRAMES &&
                        pnBytesOut >= nBytes + psEnc.sCmn.LBRR_buffer[ LBRR_idx ].nBytes ) {
                    /* Get old packet and add to payload. */
                    SKP_memcpy( pCode, pCodeOff + nBytes,
                        psEnc.sCmn.LBRR_buffer[ LBRR_idx ].payload, 0,
                        psEnc.sCmn.LBRR_buffer[ LBRR_idx ].nBytes );
                    nBytes += psEnc.sCmn.LBRR_buffer[ LBRR_idx ].nBytes;
                }

                pnBytesOut = (short)nBytes;

                /* Update FEC buffer */
                SKP_memcpy( psEnc.sCmn.LBRR_buffer[ psEnc.sCmn.oldest_LBRR_idx ].payload, 0, LBRRpayload, 0,
                    nBytesLBRR );
                psEnc.sCmn.LBRR_buffer[ psEnc.sCmn.oldest_LBRR_idx ].nBytes = nBytesLBRR;
                /* The line below describes how FEC should be used */
                psEnc.sCmn.LBRR_buffer[ psEnc.sCmn.oldest_LBRR_idx ].usage = sEncCtrl.sCmn.LBRR_usage;
                psEnc.sCmn.oldest_LBRR_idx = ( psEnc.sCmn.oldest_LBRR_idx + 1 ) & LBRR_IDX_MASK;

            } else {
                /* Not enough space: Payload will be discarded */
                pnBytesOut = 0;
                nBytes      = 0;
                ret = SKP_SILK_ENC_PAYLOAD_BUF_TOO_SHORT;
            }

            /* Reset the number of frames in payload buffer */
            psEnc.sCmn.nFramesInPayloadBuf = 0;
        } else {
            /* No payload this time */
            pnBytesOut = 0;

            /* Encode that more frames follows */
            frame_terminator = SKP_SILK_MORE_FRAMES;
            SKP_Silk_range_encoder( psEnc.sCmn.sRC, frame_terminator, FrameTermination_CDF, 0 );

            /* Payload length so far */
            SKP_Silk_range_coder_get_length( psEnc.sCmn.sRC, out nBytes );

        }

        /* Check for arithmetic coder errors */
        if( psEnc.sCmn.sRC.error != 0 ) {
            ret = SKP_SILK_ENC_INTERNAL_ERROR;
        }

        /* Simulate number of ms buffered in channel because of exceeding TargetRate */
        /* SKP_assert( ... ) × 2 —— C 构建中为空，删除 */
        psEnc.BufferedInChannel_ms   += SKP_DIV32( 8 * 1000 * ( nBytes - psEnc.sCmn.nBytesInPayloadBuf ), psEnc.sCmn.TargetRate_bps );
        psEnc.BufferedInChannel_ms   -= FRAME_LENGTH_MS;
        psEnc.BufferedInChannel_ms    = SKP_LIMIT_int( psEnc.BufferedInChannel_ms, 0, 100 );
        psEnc.sCmn.nBytesInPayloadBuf = nBytes;

        if( psEnc.speech_activity_Q8 > SKP_FIX_CONST( WB_DETECT_ACTIVE_SPEECH_LEVEL_THRES, 8 ) ) {
            psEnc.sCmn.sSWBdetect.ActiveSpeech_ms = SKP_ADD_POS_SAT32( psEnc.sCmn.sSWBdetect.ActiveSpeech_ms, FRAME_LENGTH_MS );
        }


        return( ret );
    }

    /* Low BitRate Redundancy encoding functionality. Reuse all parameters but encode residual with lower bitrate */
    // PORT-NOTE: 指针 → 数组 + 偏移：pCode → byte[] + pCodeOff；pnBytesOut → ref short；xfw → short[] + xfwOff。
    public static void SKP_Silk_LBRR_encode_FIX(
        SKP_Silk_encoder_state_FIX      psEnc,             /* I/O  Pointer to Silk encoder state           */
        SKP_Silk_encoder_control_FIX    psEncCtrl,         /* I/O  Pointer to Silk encoder control struct  */
        byte[]                          pCode, int pCodeOff,/* O    Pointer to payload                      */
        ref short                       pnBytesOut,        /* I/O  Pointer to number of payload bytes      */
        short[]                         xfw, int xfwOff     /* I    Input signal                            */
    )
    {
        int[]   TempGainsIndices = new int[ NB_SUBFR ]; int frame_terminator;
        int     nBytes, nFramesInPayloadBuf;
        int[]   TempGains_Q16 = new int[ NB_SUBFR ];
        int     typeOffset, LTP_scaleIndex, Rate_only_parameters = 0;
        /*******************************************/
        /* Control use of inband LBRR              */
        /*******************************************/
        SKP_Silk_LBRR_ctrl_FIX( psEnc, psEncCtrl.sCmn );

        if( psEnc.sCmn.LBRR_enabled != 0 ) {
            /* Save original gains */
            SKP_memcpy( TempGainsIndices, 0, psEncCtrl.sCmn.GainsIndices, 0, NB_SUBFR );
            SKP_memcpy( TempGains_Q16, 0, psEncCtrl.Gains_Q16, 0, NB_SUBFR );

            typeOffset     = psEnc.sCmn.typeOffsetPrev; // Temp save as cannot be overwritten
            LTP_scaleIndex = psEncCtrl.sCmn.LTP_scaleIndex;

            /* Set max rate where quant signal is encoded */
            if( psEnc.sCmn.fs_kHz == 8 ) {
                Rate_only_parameters = 13500;
            } else if( psEnc.sCmn.fs_kHz == 12 ) {
                Rate_only_parameters = 15500;
            } else if( psEnc.sCmn.fs_kHz == 16 ) {
                Rate_only_parameters = 17500;
            } else if( psEnc.sCmn.fs_kHz == 24 ) {
                Rate_only_parameters = 19500;
            } else {
                /* SKP_assert( 0 ); —— C 构建中为空，删除 */
            }

            if( psEnc.sCmn.Complexity > 0 && psEnc.sCmn.TargetRate_bps > Rate_only_parameters ) {
                if( psEnc.sCmn.nFramesInPayloadBuf == 0 ) {
                    /* First frame in packet; copy everything */
                    // PORT-NOTE: C 的唯一整结构体拷贝（encode_frame_FIX.c:320）：
                    //   SKP_memcpy( &psEnc->sCmn.sNSQ_LBRR, &psEnc->sCmn.sNSQ, sizeof( SKP_Silk_nsq_state ) )
                    psEnc.sCmn.sNSQ_LBRR.CopyFrom( psEnc.sCmn.sNSQ );

                    psEnc.sCmn.LBRRprevLastGainIndex = psEnc.sShape.LastGainIndex;
                    /* Increase Gains to get target LBRR rate */
                    psEncCtrl.sCmn.GainsIndices[ 0 ] = psEncCtrl.sCmn.GainsIndices[ 0 ] + psEnc.sCmn.LBRR_GainIncreases;
                    psEncCtrl.sCmn.GainsIndices[ 0 ] = SKP_LIMIT_int( psEncCtrl.sCmn.GainsIndices[ 0 ], 0, N_LEVELS_QGAIN - 1 );
                }
                /* Decode to get gains in sync with decoder         */
                /* Overwrite unquantized gains with quantized gains */
                SKP_Silk_gains_dequant( psEncCtrl.Gains_Q16, 0, psEncCtrl.sCmn.GainsIndices, 0,
                    ref psEnc.sCmn.LBRRprevLastGainIndex, psEnc.sCmn.nFramesInPayloadBuf );

                /*****************************************/
                /* Noise shaping quantization            */
                /*****************************************/
                // PORT-NOTE: PredCoef_Q12 按交错数组传参（同上 SKP_Silk_NSQ 调用处说明）。
                if( psEnc.sCmn.nStatesDelayedDecision > 1 || psEnc.sCmn.warping_Q16 > 0 ) {
                    SKP_Silk_NSQ_del_dec( psEnc.sCmn, psEncCtrl.sCmn, psEnc.sCmn.sNSQ_LBRR, xfw, xfwOff, psEnc.sCmn.q_LBRR, 0,
                        psEncCtrl.sCmn.NLSFInterpCoef_Q2, psEncCtrl.PredCoef_Q12, psEncCtrl.LTPCoef_Q14, 0,
                        psEncCtrl.AR2_Q13, 0, psEncCtrl.HarmShapeGain_Q14, 0, psEncCtrl.Tilt_Q14, 0,
                        psEncCtrl.LF_shp_Q14, 0, psEncCtrl.Gains_Q16, 0, psEncCtrl.Lambda_Q10, psEncCtrl.LTP_scale_Q14 );
                } else {
                    SKP_Silk_NSQ( psEnc.sCmn, psEncCtrl.sCmn, psEnc.sCmn.sNSQ_LBRR, xfw, xfwOff, psEnc.sCmn.q_LBRR, 0,
                        psEncCtrl.sCmn.NLSFInterpCoef_Q2, psEncCtrl.PredCoef_Q12, psEncCtrl.LTPCoef_Q14, 0,
                        psEncCtrl.AR2_Q13, 0, psEncCtrl.HarmShapeGain_Q14, 0, psEncCtrl.Tilt_Q14, 0,
                        psEncCtrl.LF_shp_Q14, 0, psEncCtrl.Gains_Q16, 0, psEncCtrl.Lambda_Q10, psEncCtrl.LTP_scale_Q14 );
                }
            } else {
                SKP_memset( psEnc.sCmn.q_LBRR, 0, (sbyte)0, psEnc.sCmn.frame_length );
                psEncCtrl.sCmn.LTP_scaleIndex = 0;
            }
            /****************************************/
            /* Initialize arithmetic coder          */
            /****************************************/
            if( psEnc.sCmn.nFramesInPayloadBuf == 0 ) {
                SKP_Silk_range_enc_init( psEnc.sCmn.sRC_LBRR );
                psEnc.sCmn.nBytesInPayloadBuf = 0;
            }

            /****************************************/
            /* Encode Parameters                    */
            /****************************************/
            SKP_Silk_encode_parameters( psEnc.sCmn, psEncCtrl.sCmn,
                psEnc.sCmn.sRC_LBRR, psEnc.sCmn.q_LBRR, 0 );

            if( psEnc.sCmn.sRC_LBRR.error != 0 ) {
                /* Encoder returned error: clear payload buffer */
                nFramesInPayloadBuf = 0;
            } else {
                nFramesInPayloadBuf = psEnc.sCmn.nFramesInPayloadBuf + 1;
            }

            /****************************************/
            /* Finalize payload and copy to output  */
            /****************************************/
            if( SKP_SMULBB( nFramesInPayloadBuf, FRAME_LENGTH_MS ) >= psEnc.sCmn.PacketSize_ms ) {

                /* Check if FEC information should be added */
                frame_terminator = SKP_SILK_LAST_FRAME;

                /* Add the frame termination info to stream */
                SKP_Silk_range_encoder( psEnc.sCmn.sRC_LBRR, frame_terminator, SKP_Silk_FrameTermination_CDF, 0 );

                /* Payload length so far */
                SKP_Silk_range_coder_get_length( psEnc.sCmn.sRC_LBRR, out nBytes );

                /* Check that there is enough space in external output buffer and move data */
                if( pnBytesOut >= nBytes ) {
                    SKP_Silk_range_enc_wrap_up( psEnc.sCmn.sRC_LBRR );
                    SKP_memcpy( pCode, pCodeOff, psEnc.sCmn.sRC_LBRR.buffer, 0, nBytes );

                    pnBytesOut = (short)nBytes;
                } else {
                    /* Not enough space: payload will be discarded */
                    pnBytesOut = 0;
                    /* SKP_assert( 0 ); —— C 构建中为空，删除 */
                }
            } else {
                /* No payload this time */
                pnBytesOut = 0;

                /* Encode that more frames follows */
                frame_terminator = SKP_SILK_MORE_FRAMES;
                SKP_Silk_range_encoder( psEnc.sCmn.sRC_LBRR, frame_terminator, SKP_Silk_FrameTermination_CDF, 0 );
            }

            /* Restore original Gains */
            SKP_memcpy( psEncCtrl.sCmn.GainsIndices, 0, TempGainsIndices, 0, NB_SUBFR );
            SKP_memcpy( psEncCtrl.Gains_Q16, 0, TempGains_Q16, 0, NB_SUBFR );

            /* Restore LTP scale index and typeoffset */
            psEncCtrl.sCmn.LTP_scaleIndex = LTP_scaleIndex;
            psEnc.sCmn.typeOffsetPrev = typeOffset;
        }
    }
}
