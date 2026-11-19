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
// SKP_Silk_decode_parameters.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_decode_parameters.c（从 payload 解码全部编码参数）
//
// PORT-NOTE: 本文件主函数与 C 文件同名（SKP_Silk_decode_parameters），
//   C# 禁止成员名与其包含类型同名（CS0542），故类名加后缀 _impl，
//   方法名保持 C 原名（本簇统一约定，详见 SKP_Silk_enc_API.cs 文件头）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 本簇其他文件
using static AaR.Server.Silk.SKP_Silk_decoder_set_fs_impl;        // SKP_Silk_decoder_set_fs
using static AaR.Server.Silk.SKP_Silk_decode_pitch_impl;           // SKP_Silk_decode_pitch
using static AaR.Server.Silk.SKP_Silk_decode_pulses_impl;         // SKP_Silk_decode_pulses
// 其他簇（类名 = C 文件名，PORTING.md §1）
// PORT-NOTE: SKP_Silk_NLSF_MSVQ_decode.c / SKP_Silk_NLSF2A_stable.c /
//   SKP_Silk_bwexpander.c 的主函数与其文件同名（CS0542/CS1955 受影响文件），
//   若集成期类名统一加后缀，仅需更新对应 using static 行；调用点一律不变。
using static AaR.Server.Silk.SKP_Silk_range_coder;                 // SKP_Silk_range_decoder / _multi /
                                                                   // range_coder_get_length / check_after_decoding
using static AaR.Server.Silk.SKP_Silk_gain_quant;                   // SKP_Silk_gains_dequant
using static AaR.Server.Silk.SKP_Silk_NLSF_MSVQ_decode_impl;            // SKP_Silk_NLSF_MSVQ_decode
using static AaR.Server.Silk.SKP_Silk_NLSF2A_stable_impl;               // SKP_Silk_NLSF2A_stable
using static AaR.Server.Silk.SKP_Silk_bwexpander_impl;                   // SKP_Silk_bwexpander
// 表（其他簇）
using static AaR.Server.Silk.SKP_Silk_tables_other;                 // SamplingRates / NLSF_interpolation_factor /
                                                                   // vadflag / Seed / FrameTermination CDF
using static AaR.Server.Silk.SKP_Silk_tables_type_offset;          // SKP_Silk_type_offset_CDF 等
using static AaR.Server.Silk.SKP_Silk_tables_gain;                 // SKP_Silk_gain_CDF / delta_gain_CDF
using static AaR.Server.Silk.SKP_Silk_tables_pitch_lag;             // pitch_lag / pitch_contour CDF
using static AaR.Server.Silk.SKP_Silk_tables_LTP;                   // LTP 相关 CDF / 码本指针表

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_decode_parameters.c —— 从 payload 解码全部编码参数。
/// </summary>
internal static class SKP_Silk_decode_parameters_impl
{
    /* Decode parameters from payload */
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）：q 为输出数组加偏移。
    //   SKP_Silk_range_decoder 的 C 原型为 SKP_int data[]（单元素，纯只写），
    //   按 §3 规则 1 移植为 out int data（调用点 &标量 / &数组元素 / &字段 均合法）。
    public static void SKP_Silk_decode_parameters(
        SKP_Silk_decoder_state      psDec,                  /* I/O  State                                       */
        SKP_Silk_decoder_control    psDecCtrl,              /* I/O  Decoder control                             */
        int[]                       q, int qOff,             /* O    Excitation signal                           */
        int                         fullDecoding             /* I    Flag to tell if only arithmetic decoding    */
    )
    {
        int   i, k, Ix, fs_kHz_dec, nBytesUsed;
        int[]   Ixs = new int[ NB_SUBFR ];
        int[]   GainsIndices = new int[ NB_SUBFR ];
        int[]   NLSFIndices = new int[ NLSF_MSVQ_MAX_CB_STAGES ];
        int[]   pNLSF_Q15 = new int[ MAX_LPC_ORDER ], pNLSF0_Q15 = new int[ MAX_LPC_ORDER ];
        short[] cbk_ptr_Q14;
        SKP_Silk_NLSF_CB_struct psNLSF_CB = null;
        SKP_Silk_range_coder_state psRC = psDec.sRC;

        /************************/
        /* Decode sampling rate */
        /************************/
        /* only done for first frame of packet */
        if( psDec.nFramesDecoded == 0 ) {
            SKP_Silk_range_decoder( out Ix, psRC, SKP_Silk_SamplingRates_CDF, 0, SKP_Silk_SamplingRates_offset );

            /* check that sampling rate is supported */
            if( Ix < 0 || Ix > 3 ) {
                psRC.error = RANGE_CODER_ILLEGAL_SAMPLING_RATE;
                return;
            }
            fs_kHz_dec = SKP_Silk_SamplingRates_table[ Ix ];
            SKP_Silk_decoder_set_fs( psDec, fs_kHz_dec );
        }

        /*******************************************/
        /* Decode signal type and quantizer offset */
        /*******************************************/
        if( psDec.nFramesDecoded == 0 ) {
            /* first frame in packet: independent coding */
            SKP_Silk_range_decoder( out Ix, psRC, SKP_Silk_type_offset_CDF, 0, SKP_Silk_type_offset_CDF_offset );
        } else {
            /* condidtional coding */
            SKP_Silk_range_decoder( out Ix, psRC, SKP_Silk_type_offset_joint_CDF[ psDec.typeOffsetPrev ], 0,
                    SKP_Silk_type_offset_CDF_offset );
        }
        psDecCtrl.sigtype         = SKP_RSHIFT( Ix, 1 );
        psDecCtrl.QuantOffsetType = Ix & 1;
        psDec.typeOffsetPrev      = Ix;

        /****************/
        /* Decode gains */
        /****************/
        /* first subframe */
        if( psDec.nFramesDecoded == 0 ) {
            /* first frame in packet: independent coding */
            SKP_Silk_range_decoder( out GainsIndices[ 0 ], psRC, SKP_Silk_gain_CDF[ psDecCtrl.sigtype ], 0, SKP_Silk_gain_CDF_offset );
        } else {
            /* condidtional coding */
            SKP_Silk_range_decoder( out GainsIndices[ 0 ], psRC, SKP_Silk_delta_gain_CDF, 0, SKP_Silk_delta_gain_CDF_offset );
        }

        /* remaining subframes */
        for( i = 1; i < NB_SUBFR; i++ ) {
            SKP_Silk_range_decoder( out GainsIndices[ i ], psRC, SKP_Silk_delta_gain_CDF, 0, SKP_Silk_delta_gain_CDF_offset );
        }

        /* Dequant Gains */
        SKP_Silk_gains_dequant( psDecCtrl.Gains_Q16, 0, GainsIndices, 0, ref psDec.LastGainIndex, psDec.nFramesDecoded );
        /****************/
        /* Decode NLSFs */
        /****************/
        /* Set pointer to NLSF VQ CB for the current signal type */
        psNLSF_CB = psDec.psNLSF_CB[ psDecCtrl.sigtype ];

        /* Range decode NLSF path */
        // PORT-NOTE: C 原型 prob 为 SKP_uint16* 数组（每阶段一个起始指针）；
        //   按 §3 规则 4（指向子数组）传地基 StartPtr（ushort[][]），probOff=0。
        SKP_Silk_range_decoder_multi( NLSFIndices, 0, psRC, psNLSF_CB.StartPtr, 0, psNLSF_CB.MiddleIx, 0, psNLSF_CB.nStages );

        /* From the NLSF path, decode an NLSF vector */
        SKP_Silk_NLSF_MSVQ_decode( pNLSF_Q15, 0, psNLSF_CB, NLSFIndices, 0, psDec.LPC_order );

        /************************************/
        /* Decode NLSF interpolation factor */
        /************************************/
        SKP_Silk_range_decoder( out psDecCtrl.NLSFInterpCoef_Q2, psRC, SKP_Silk_NLSF_interpolation_factor_CDF, 0,
            SKP_Silk_NLSF_interpolation_factor_offset );

        /* If just reset, e.g., because internal Fs changed, do not allow interpolation */
        /* improves the case of packet loss in the first frame after a switch           */
        if( psDec.first_frame_after_reset == 1 ) {
            psDecCtrl.NLSFInterpCoef_Q2 = 4;
        }

        if( fullDecoding != 0 ) {
            /* Convert NLSF parameters to AR prediction filter coefficients */
            SKP_Silk_NLSF2A_stable( psDecCtrl.PredCoef_Q12[ 1 ], 0, pNLSF_Q15, 0, psDec.LPC_order );

            if( psDecCtrl.NLSFInterpCoef_Q2 < 4 ) {
                /* Calculation of the interpolated NLSF0 vector from the interpolation factor, */
                /* the previous NLSF1, and the current NLSF1                                   */
                for( i = 0; i < psDec.LPC_order; i++ ) {
                    pNLSF0_Q15[ i ] = psDec.prevNLSF_Q15[ i ] + SKP_RSHIFT( SKP_MUL( psDecCtrl.NLSFInterpCoef_Q2,
                        ( pNLSF_Q15[ i ] - psDec.prevNLSF_Q15[ i ] ) ), 2 );
                }

                /* Convert NLSF parameters to AR prediction filter coefficients */
                SKP_Silk_NLSF2A_stable( psDecCtrl.PredCoef_Q12[ 0 ], 0, pNLSF0_Q15, 0, psDec.LPC_order );
            } else {
                /* Copy LPC coefficients for first half from second half */
                SKP_memcpy( psDecCtrl.PredCoef_Q12[ 0 ], 0, psDecCtrl.PredCoef_Q12[ 1 ], 0,
                    psDec.LPC_order );
            }
        }

        SKP_memcpy( psDec.prevNLSF_Q15, 0, pNLSF_Q15, 0, psDec.LPC_order );

        /* After a packet loss do BWE of LPC coefs */
        if( psDec.lossCnt != 0 ) {
            SKP_Silk_bwexpander( psDecCtrl.PredCoef_Q12[ 0 ], 0, psDec.LPC_order, BWE_AFTER_LOSS_Q16 );
            SKP_Silk_bwexpander( psDecCtrl.PredCoef_Q12[ 1 ], 0, psDec.LPC_order, BWE_AFTER_LOSS_Q16 );
        }

        if( psDecCtrl.sigtype == SIG_TYPE_VOICED ) {
            /*********************/
            /* Decode pitch lags */
            /*********************/
            /* Get lag index */
            if( psDec.fs_kHz == 8 ) {
                SKP_Silk_range_decoder( out Ixs[ 0 ], psRC, SKP_Silk_pitch_lag_NB_CDF,  0, SKP_Silk_pitch_lag_NB_CDF_offset );
            } else if( psDec.fs_kHz == 12 ) {
                SKP_Silk_range_decoder( out Ixs[ 0 ], psRC, SKP_Silk_pitch_lag_MB_CDF,  0, SKP_Silk_pitch_lag_MB_CDF_offset );
            } else if( psDec.fs_kHz == 16 ) {
                SKP_Silk_range_decoder( out Ixs[ 0 ], psRC, SKP_Silk_pitch_lag_WB_CDF,  0, SKP_Silk_pitch_lag_WB_CDF_offset );
            } else {
                SKP_Silk_range_decoder( out Ixs[ 0 ], psRC, SKP_Silk_pitch_lag_SWB_CDF, 0, SKP_Silk_pitch_lag_SWB_CDF_offset );
            }

            /* Get countour index */
            if( psDec.fs_kHz == 8 ) {
                /* Less codevectors used in 8 khz mode */
                SKP_Silk_range_decoder( out Ixs[ 1 ], psRC, SKP_Silk_pitch_contour_NB_CDF, 0, SKP_Silk_pitch_contour_NB_CDF_offset );
            } else {
                /* Joint for 12, 16, and 24 khz */
                SKP_Silk_range_decoder( out Ixs[ 1 ], psRC, SKP_Silk_pitch_contour_CDF, 0, SKP_Silk_pitch_contour_CDF_offset );
            }

            /* Decode pitch values */
            SKP_Silk_decode_pitch( Ixs[ 0 ], Ixs[ 1 ], psDecCtrl.pitchL, 0, psDec.fs_kHz );

            /********************/
            /* Decode LTP gains */
            /********************/
            /* Decode PERIndex value */
            SKP_Silk_range_decoder( out psDecCtrl.PERIndex, psRC, SKP_Silk_LTP_per_index_CDF, 0,
                    SKP_Silk_LTP_per_index_CDF_offset );

            /* Decode Codebook Index */
            cbk_ptr_Q14 = SKP_Silk_LTP_vq_ptrs_Q14[ psDecCtrl.PERIndex ]; /* set pointer to start of codebook */

            for( k = 0; k < NB_SUBFR; k++ ) {
                SKP_Silk_range_decoder( out Ix, psRC, SKP_Silk_LTP_gain_CDF_ptrs[ psDecCtrl.PERIndex ], 0,
                    SKP_Silk_LTP_gain_CDF_offsets[ psDecCtrl.PERIndex ] );

                for( i = 0; i < LTP_ORDER; i++ ) {
                    psDecCtrl.LTPCoef_Q14[ k * LTP_ORDER + i ] = cbk_ptr_Q14[ Ix * LTP_ORDER + i ];
                }
            }

            /**********************/
            /* Decode LTP scaling */
            /**********************/
            SKP_Silk_range_decoder( out Ix, psRC, SKP_Silk_LTPscale_CDF, 0, SKP_Silk_LTPscale_offset );
            psDecCtrl.LTP_scale_Q14 = SKP_Silk_LTPScales_table_Q14[ Ix ];
        } else {
            /* SKP_assert( psDecCtrl->sigtype == SIG_TYPE_UNVOICED ); —— C 构建中为空，删除 */
            SKP_memset( psDecCtrl.pitchL,      0, 0,             NB_SUBFR );
            SKP_memset( psDecCtrl.LTPCoef_Q14, 0, (short)0, LTP_ORDER * NB_SUBFR );
            psDecCtrl.PERIndex      = 0;
            psDecCtrl.LTP_scale_Q14 = 0;
        }

        /***************/
        /* Decode seed */
        /***************/
        SKP_Silk_range_decoder( out Ix, psRC, SKP_Silk_Seed_CDF, 0, SKP_Silk_Seed_offset );
        psDecCtrl.Seed = (int)Ix;
        /*********************************************/
        /* Decode quantization indices of excitation */
        /*********************************************/
        SKP_Silk_decode_pulses( psRC, psDecCtrl, q, qOff, psDec.frame_length );

        /*********************************************/
        /* Decode VAD flag                           */
        /*********************************************/
        SKP_Silk_range_decoder( out psDec.vadFlag, psRC, SKP_Silk_vadflag_CDF, 0, SKP_Silk_vadflag_offset );

        /**************************************/
        /* Decode Frame termination indicator */
        /**************************************/
        SKP_Silk_range_decoder( out psDec.FrameTermination, psRC, SKP_Silk_FrameTermination_CDF, 0, SKP_Silk_FrameTermination_offset );

        /****************************************/
        /* get number of bytes used so far      */
        /****************************************/
        SKP_Silk_range_coder_get_length( psRC, out nBytesUsed );
        psDec.nBytesLeft = psRC.bufferLength - nBytesUsed;
        if( psDec.nBytesLeft < 0 ) {
            psRC.error = RANGE_CODER_READ_BEYOND_BUFFER;
        }

        /****************************************/
        /* check remaining bits in last byte    */
        /****************************************/
        if( psDec.nBytesLeft == 0 ) {
            SKP_Silk_range_coder_check_after_decoding( psRC );
        }
    }
}
