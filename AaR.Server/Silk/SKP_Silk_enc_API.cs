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
// SKP_Silk_enc_API.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_enc_API.c
//   （编码器顶层 API：Get_Encoder_Size / QueryEncoder / InitEncoder / Encode）
// 本文件另含 src/SKP_Silk_setup_complexity.h 的内联函数
//   SKP_Silk_setup_complexity（该 .h 仅被 SKP_Silk_control_codec_FIX.c 包含，
//   按本簇任务规约移植到本文件，保持函数名）。
//
// PORT-NOTE（本簇统一约定，全文适用）：
//   C# 禁止成员名与其直接包含类型同名（CS0542），且当命名空间内存在与方法同名的
//   类型时裸名调用不可解析（CS1955）。凡 C 文件内存在与文件名同名的函数，该文件的
//   C# 类名加后缀 _impl，方法名保持 C 原名不动；跨簇调用一律仍按 PORTING.md §3
//   裸名 + 偏移规则书写。本簇受影响（类名加 _impl）的文件：
//   encode_frame_FIX / init_encoder_FIX / decode_frame / decode_parameters /
//   decode_pitch / decode_pulses / decode_core / decoder_set_fs，
//   以及本文件的 SKP_Silk_setup_complexity（类名 SKP_Silk_setup_complexity_impl）。
//   集成时如采用其他统一后缀，仅需机械替换类名；方法名与调用点一律不变。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 地基：错误码
using static AaR.Server.Silk.SKP_Silk_errors;
// 本簇其他文件
using static AaR.Server.Silk.SKP_Silk_encode_frame_FIX_impl;    // SKP_Silk_encode_frame_FIX
using static AaR.Server.Silk.SKP_Silk_init_encoder_FIX_impl;     // SKP_Silk_init_encoder_FIX
using static AaR.Server.Silk.SKP_Silk_control_codec_FIX;          // SKP_Silk_control_encoder_FIX
// 其他簇（类名 = C 文件名，PORTING.md §1）
// PORT-NOTE: SKP_Silk_detect_SWB_input.c / SKP_Silk_resampler.c 的主函数与其文件同名，
//   对应簇的类名若在集成期因 CS0542 统一加后缀，仅需更新下面两行 using static。
using static AaR.Server.Silk.SKP_Silk_detect_SWB_input_impl;          // SKP_Silk_detect_SWB_input
using static AaR.Server.Silk.SKP_Silk_resampler_impl;                 // SKP_Silk_resampler / SKP_Silk_resampler_init

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_enc_API.c —— 编码器顶层 API。
/// PORT-NOTE: C 的 void* 状态 + EncControlStruct 传参改为强类型（本簇任务规约第 2 条）。
/// </summary>
internal static class SKP_Silk_enc_API
{
    /****************************************/
    /* Encoder functions                    */
    /****************************************/

    // PORT-NOTE: C 版本向 *encSizeBytes 写入 sizeof( SKP_Silk_encoder_state_FIX )。
    // 托管侧状态为类实例，无需查询字节数，移植为无参并返回 0。
    public static int SKP_Silk_SDK_Get_Encoder_Size()
    {
        int ret = 0;

        return ret;
    }


    /***************************************/
    /* Read control structure from encoder */
    /***************************************/
    public static int SKP_Silk_SDK_QueryEncoder(
        SKP_Silk_encoder_state_FIX psEnc,                /* I:   State Vector                                    */
        SKP_SILK_SDK_EncControlStruct encStatus           /* O:   Control Structure                               */
    )
    {
        int ret = 0;

        encStatus.API_sampleRate        = psEnc.sCmn.API_fs_Hz;
        encStatus.maxInternalSampleRate = SKP_SMULBB( psEnc.sCmn.maxInternal_fs_kHz, 1000 );
        encStatus.packetSize            = (int)SKP_DIV32_16( psEnc.sCmn.API_fs_Hz * psEnc.sCmn.PacketSize_ms, 1000 );  /* convert samples -> ms */
        encStatus.bitRate               = psEnc.sCmn.TargetRate_bps;
        encStatus.packetLossPercentage  = psEnc.sCmn.PacketLoss_perc;
        encStatus.complexity            = psEnc.sCmn.Complexity;
        encStatus.useInBandFEC          = psEnc.sCmn.useInBandFEC;
        encStatus.useDTX                = psEnc.sCmn.useDTX;
        return ret;
    }

    /*************************/
    /* Init or Reset encoder */
    /*************************/
    public static int SKP_Silk_SDK_InitEncoder(
        SKP_Silk_encoder_state_FIX psEnc,                /* I/O: State                                           */
        SKP_SILK_SDK_EncControlStruct encStatus           /* O:   Control structure                               */
    )
    {
        int ret = 0;


        /* Reset Encoder */
        if( ( ret += SKP_Silk_init_encoder_FIX( psEnc ) ) != 0 ) {
            /* SKP_assert( 0 ); —— C 构建中为空，删除 */
        }

        /* Read control structure */
        if( ( ret += SKP_Silk_SDK_QueryEncoder( psEnc, encStatus ) ) != 0 ) {
            /* SKP_assert( 0 ); —— C 构建中为空，删除 */
        }


        return ret;
    }

    /**************************/
    /* Encode frame with Silk */
    /**************************/
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）。C 中 samplesIn += nSamplesFromInput
    // 的指针步进改写为 samplesInOff 偏移步进；*nBytesOut 改为 ref short。
    public static int SKP_Silk_SDK_Encode(
        SKP_Silk_encoder_state_FIX psEnc,                /* I/O: State                                           */
        SKP_SILK_SDK_EncControlStruct encControl,         /* I:   Control structure                               */
        short[] samplesIn, int samplesInOff,              /* I:   Speech sample input vector                      */
        int nSamplesIn,                                   /* I:   Number of samples in input vector               */
        byte[] outData, int outDataOff,                   /* O:   Encoded output vector                           */
        ref short nBytesOut                               /* I/O: Number of bytes in outData (input: Max bytes)   */
    )
    {
        int   max_internal_fs_kHz, PacketSize_ms, PacketLoss_perc, UseInBandFEC, UseDTX, ret = 0;
        int   nSamplesToBuffer, Complexity, input_10ms, nSamplesFromInput = 0;
        int   TargetRate_bps, API_fs_Hz;
        short MaxBytesOut;

        /* Check sampling frequency first, to avoid divide by zero later */
        if( ( ( encControl.API_sampleRate        !=  8000 ) &&
              ( encControl.API_sampleRate        != 12000 ) &&
              ( encControl.API_sampleRate        != 16000 ) &&
              ( encControl.API_sampleRate        != 24000 ) &&
              ( encControl.API_sampleRate        != 32000 ) &&
              ( encControl.API_sampleRate        != 44100 ) &&
              ( encControl.API_sampleRate        != 48000 ) ) ||
            ( ( encControl.maxInternalSampleRate !=  8000 ) &&
              ( encControl.maxInternalSampleRate != 12000 ) &&
              ( encControl.maxInternalSampleRate != 16000 ) &&
              ( encControl.maxInternalSampleRate != 24000 ) ) ) {
            ret = SKP_SILK_ENC_FS_NOT_SUPPORTED;
            return( ret );
        }

        /* Set encoder parameters from control structure */
        API_fs_Hz           =                            encControl.API_sampleRate;
        max_internal_fs_kHz =                 (int)( encControl.maxInternalSampleRate >> 10 ) + 1;   /* convert Hz -> kHz */
        PacketSize_ms       = SKP_DIV32( 1000 * (int)encControl.packetSize, API_fs_Hz );
        TargetRate_bps      =                            encControl.bitRate;
        PacketLoss_perc     =                            encControl.packetLossPercentage;
        UseInBandFEC        =                            encControl.useInBandFEC;
        Complexity          =                            encControl.complexity;
        UseDTX              =                            encControl.useDTX;

        /* Save values in state */
        psEnc.sCmn.API_fs_Hz          = API_fs_Hz;
        psEnc.sCmn.maxInternal_fs_kHz = max_internal_fs_kHz;
        psEnc.sCmn.useInBandFEC       = UseInBandFEC;

        /* Only accept input lengths that are a multiple of 10 ms */
        input_10ms = SKP_DIV32( 100 * nSamplesIn, API_fs_Hz );
        if( input_10ms * API_fs_Hz != 100 * nSamplesIn || nSamplesIn < 0 ) {
            ret = SKP_SILK_ENC_INPUT_INVALID_NO_OF_SAMPLES;
            return( ret );
        }

        TargetRate_bps = SKP_LIMIT( TargetRate_bps, MIN_TARGET_RATE_BPS, MAX_TARGET_RATE_BPS );
        if( ( ret = SKP_Silk_control_encoder_FIX( psEnc, PacketSize_ms, TargetRate_bps,
                            PacketLoss_perc, UseDTX, Complexity) ) != 0 ) {
            return( ret );
        }

        /* Make sure no more than one packet can be produced */
        if( 1000 * (int)nSamplesIn > psEnc.sCmn.PacketSize_ms * API_fs_Hz ) {
            ret = SKP_SILK_ENC_INPUT_INVALID_NO_OF_SAMPLES;
            return( ret );
        }

        /* PORT-NOTE: C 条件编译 #if MAX_FS_KHZ > 16 —— MAX_FS_KHZ = 24 恒真，保留该分支 */
        {
            /* Detect energy above 8 kHz */
            if( SKP_min( API_fs_Hz, 1000 * max_internal_fs_kHz ) == 24000 &&
                    psEnc.sCmn.sSWBdetect.SWB_detected == 0 &&
                    psEnc.sCmn.sSWBdetect.WB_detected == 0 ) {
                SKP_Silk_detect_SWB_input( psEnc.sCmn.sSWBdetect, samplesIn, samplesInOff, (int)nSamplesIn );
            }
        }

        /* Input buffering/resampling and encoding */
        MaxBytesOut = 0;                    /* return 0 output bytes if no encoder called */
        while( true ) {
            nSamplesToBuffer = psEnc.sCmn.frame_length - psEnc.sCmn.inputBufIx;
            if( API_fs_Hz == SKP_SMULBB( 1000, psEnc.sCmn.fs_kHz ) ) {
                nSamplesToBuffer  = SKP_min_int( nSamplesToBuffer, nSamplesIn );
                nSamplesFromInput = nSamplesToBuffer;
                /* Copy to buffer */
                SKP_memcpy( psEnc.sCmn.inputBuf, psEnc.sCmn.inputBufIx, samplesIn, samplesInOff, nSamplesFromInput );
            } else {
                nSamplesToBuffer  = SKP_min( nSamplesToBuffer, 10 * input_10ms * psEnc.sCmn.fs_kHz );
                nSamplesFromInput = SKP_DIV32_16( nSamplesToBuffer * API_fs_Hz, psEnc.sCmn.fs_kHz * 1000 );
                /* Resample and write to buffer */
                ret += SKP_Silk_resampler( psEnc.sCmn.resampler_state,
                    psEnc.sCmn.inputBuf, psEnc.sCmn.inputBufIx, samplesIn, samplesInOff, nSamplesFromInput );
            }
            samplesInOff            += nSamplesFromInput;
            nSamplesIn              -= nSamplesFromInput;
            psEnc.sCmn.inputBufIx   += nSamplesToBuffer;

            /* Silk encoder */
            if( psEnc.sCmn.inputBufIx >= psEnc.sCmn.frame_length ) {

                /* Enough data in input buffer, so encode */
                if( MaxBytesOut == 0 ) {
                    /* No payload obtained so far */
                    MaxBytesOut = nBytesOut;
                    if( ( ret = SKP_Silk_encode_frame_FIX( psEnc, outData, outDataOff, ref MaxBytesOut,
                        psEnc.sCmn.inputBuf, 0 ) ) != 0 ) {
                        /* SKP_assert( 0 ); —— C 构建中为空，删除 */
                    }
                } else {
                    /* outData already contains a payload */
                    if( ( ret = SKP_Silk_encode_frame_FIX( psEnc, outData, outDataOff, ref nBytesOut,
                        psEnc.sCmn.inputBuf, 0 ) ) != 0 ) {
                        /* SKP_assert( 0 ); —— C 构建中为空，删除 */
                    }
                    /* Check that no second payload was created */
                    /* SKP_assert( *nBytesOut == 0 ); —— C 构建中为空，删除 */
                }
                psEnc.sCmn.inputBufIx = 0;
                psEnc.sCmn.controlled_since_last_payload = 0;

                if( nSamplesIn == 0 ) {
                    break;
                }
            } else {
                break;
            }
        }

        nBytesOut = MaxBytesOut;
        if( psEnc.sCmn.useDTX != 0 && psEnc.sCmn.inDTX != 0 ) {
            /* DTX simulation */
            nBytesOut = 0;
        }



        return ret;
    }
}

/// <summary>
/// src/SKP_Silk_setup_complexity.h —— 复杂度设置辅助函数（内联头）。
/// PORT-NOTE: 该 .h 仅被 SKP_Silk_control_codec_FIX.c 包含；按本簇任务规约将其中
/// 函数移植为本文件内独立静态类。类名加后缀 _impl（CS0542：C# 禁止方法与其包含
/// 类同名），方法名保持 C 原名。
/// </summary>
internal static class SKP_Silk_setup_complexity_impl
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_Silk_setup_complexity(
        SKP_Silk_encoder_state psEncC,                  /* I/O  Pointer to Silk encoder state           */
        int Complexity                                  /* I    Complexity (0->low; 1->medium; 2->high) */
    )
    {
        int ret = SKP_SILK_NO_ERROR;

        /* Check that settings are valid */
        if( LOW_COMPLEXITY_ONLY != 0 && Complexity != 0 ) {
            ret = SKP_SILK_ENC_INVALID_COMPLEXITY_SETTING;
        }

        /* Set encoding complexity */
        if( Complexity == 0 || LOW_COMPLEXITY_ONLY != 0 ) {
            /* Low complexity */
            psEncC.Complexity                      = 0;
            psEncC.pitchEstimationComplexity       = PITCH_EST_COMPLEXITY_LC_MODE;
            psEncC.pitchEstimationThreshold_Q16    = SKP_FIX_CONST( FIND_PITCH_CORRELATION_THRESHOLD_LC_MODE, 16 );
            psEncC.pitchEstimationLPCOrder         = 6;
            psEncC.shapingLPCOrder                 = 8;
            psEncC.la_shape                        = 3 * psEncC.fs_kHz;
            psEncC.nStatesDelayedDecision          = 1;
            psEncC.useInterpolatedNLSFs            = 0;
            psEncC.LTPQuantLowComplexity           = 1;
            psEncC.NLSF_MSVQ_Survivors             = MAX_NLSF_MSVQ_SURVIVORS_LC_MODE;
            psEncC.warping_Q16                     = 0;
        } else if( Complexity == 1 ) {
            /* Medium complexity */
            psEncC.Complexity                      = 1;
            psEncC.pitchEstimationComplexity       = PITCH_EST_COMPLEXITY_MC_MODE;
            psEncC.pitchEstimationThreshold_Q16    = SKP_FIX_CONST( FIND_PITCH_CORRELATION_THRESHOLD_MC_MODE, 16 );
            psEncC.pitchEstimationLPCOrder         = 12;
            psEncC.shapingLPCOrder                 = 12;
            psEncC.la_shape                        = 5 * psEncC.fs_kHz;
            psEncC.nStatesDelayedDecision          = 2;
            psEncC.useInterpolatedNLSFs            = 0;
            psEncC.LTPQuantLowComplexity           = 0;
            psEncC.NLSF_MSVQ_Survivors             = MAX_NLSF_MSVQ_SURVIVORS_MC_MODE;
            psEncC.warping_Q16                     = psEncC.fs_kHz * SKP_FIX_CONST( WARPING_MULTIPLIER, 16 );
        } else if( Complexity == 2 ) {
            /* High complexity */
            psEncC.Complexity                      = 2;
            psEncC.pitchEstimationComplexity       = PITCH_EST_COMPLEXITY_HC_MODE;
            psEncC.pitchEstimationThreshold_Q16    = SKP_FIX_CONST( FIND_PITCH_CORRELATION_THRESHOLD_HC_MODE, 16 );
            psEncC.pitchEstimationLPCOrder         = 16;
            psEncC.shapingLPCOrder                 = 16;
            psEncC.la_shape                        = 5 * psEncC.fs_kHz;
            psEncC.nStatesDelayedDecision          = MAX_DEL_DEC_STATES;
            psEncC.useInterpolatedNLSFs            = 1;
            psEncC.LTPQuantLowComplexity           = 0;
            psEncC.NLSF_MSVQ_Survivors             = MAX_NLSF_MSVQ_SURVIVORS;
            psEncC.warping_Q16                     = psEncC.fs_kHz * SKP_FIX_CONST( WARPING_MULTIPLIER, 16 );
        } else {
            ret = SKP_SILK_ENC_INVALID_COMPLEXITY_SETTING;
        }

        /* Do not allow higher pitch estimation LPC order than predict LPC order */
        psEncC.pitchEstimationLPCOrder             = SKP_min_int( psEncC.pitchEstimationLPCOrder, psEncC.predictLPCOrder );
        psEncC.shapeWinLength                      = 5 * psEncC.fs_kHz + 2 * psEncC.la_shape;

        /* SKP_assert( ... ) × 6 —— C 构建中为空，删除 */

        return( ret );
    }
}
