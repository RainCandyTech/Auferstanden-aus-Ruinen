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
// SKP_Silk_control_codec_FIX.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_control_codec_FIX.c（编码器参数控制：采样率/包长/复杂度/码率/LBRR）
//   SKP_Silk_setup_complexity 的实现位于 SKP_Silk_enc_API.cs（内联头
//   src/SKP_Silk_setup_complexity.h 按任务规约移植于该文件）。
//
// PORT-NOTE: 整结构体 SKP_memset → 地基 .Clear()（PORTING.md §4）；
//   x_buf 缓冲搬运用地基 SKP_memcpy 助手；SWITCH_TRANSITION_FILTERING = 1 分支照抄。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 地基：错误码
using static AaR.Server.Silk.SKP_Silk_errors;
// 本簇其他文件
using static AaR.Server.Silk.SKP_Silk_setup_complexity_impl;      // SKP_Silk_setup_complexity（SKP_Silk_enc_API.cs）
// 其他簇（类名 = C 文件名，PORTING.md §1）
// PORT-NOTE: SKP_Silk_control_audio_bandwidth.c / SKP_Silk_resampler.c /
//   SKP_Silk_LBRR_reset.c 的主函数与其文件同名（CS0542/CS1955 受影响文件），
//   若集成期类名统一加后缀，仅需更新对应 using static 行；调用点一律不变。
using static AaR.Server.Silk.SKP_Silk_control_audio_bandwidth_impl;    // SKP_Silk_control_audio_bandwidth
using static AaR.Server.Silk.SKP_Silk_resampler_impl;                  // SKP_Silk_resampler_init / SKP_Silk_resampler
using static AaR.Server.Silk.SKP_Silk_LBRR_reset_impl;                      // SKP_Silk_LBRR_reset
// 表（其他簇）
using static AaR.Server.Silk.SKP_Silk_tables_other;               // TargetRate_table_* / SNR_table_Q1
using static AaR.Server.Silk.SKP_Silk_tables_NLSF_CB0_10;         // SKP_Silk_NLSF_CB0_10
using static AaR.Server.Silk.SKP_Silk_tables_NLSF_CB1_10;           // SKP_Silk_NLSF_CB1_10
using static AaR.Server.Silk.SKP_Silk_tables_NLSF_CB0_16;           // SKP_Silk_NLSF_CB0_16
using static AaR.Server.Silk.SKP_Silk_tables_NLSF_CB1_16;           // SKP_Silk_NLSF_CB1_16

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_control_codec_FIX.c —— 编码器参数控制。
/// </summary>
internal static class SKP_Silk_control_codec_FIX
{
    /* PORT-NOTE: C 文件顶部的 5 个前向声明在 C# 中不需要，原顺序保持。 */

    /* Control encoder */
    public static int SKP_Silk_control_encoder_FIX(
        SKP_Silk_encoder_state_FIX  psEnc,                /* I/O  Pointer to Silk encoder state           */
        int                         PacketSize_ms,        /* I    Packet length (ms)                      */
        int                         TargetRate_bps,       /* I    Target max bitrate (bps)                */
        int                         PacketLoss_perc,      /* I    Packet loss rate (in percent)           */
        int                         DTX_enabled,          /* I    Enable / disable DTX                    */
        int                         Complexity            /* I    Complexity (0->low; 1->medium; 2->high) */
    )
    {
        int   fs_kHz, ret = 0;

        if( psEnc.sCmn.controlled_since_last_payload != 0 ) {
            if( psEnc.sCmn.API_fs_Hz != psEnc.sCmn.prev_API_fs_Hz && psEnc.sCmn.fs_kHz > 0 ) {
                /* Change in API sampling rate in the middle of encoding a packet */
                ret += SKP_Silk_setup_resamplers_FIX( psEnc, psEnc.sCmn.fs_kHz );
            }
            return ret;
        }

        /* Beyond this point we know that there are no previously coded frames in the payload buffer */

        /********************************************/
        /* Determine internal sampling rate         */
        /********************************************/
        fs_kHz = SKP_Silk_control_audio_bandwidth( psEnc.sCmn, TargetRate_bps );

        /********************************************/
        /* Prepare resampler and buffered data      */
        /********************************************/
        ret += SKP_Silk_setup_resamplers_FIX( psEnc, fs_kHz );

        /********************************************/
        /* Set packet size                          */
        /********************************************/
        ret += SKP_Silk_setup_packetsize_FIX( psEnc, PacketSize_ms );

        /********************************************/
        /* Set internal sampling frequency          */
        /********************************************/
        ret += SKP_Silk_setup_fs_FIX( psEnc, fs_kHz );

        /********************************************/
        /* Set encoding complexity                  */
        /********************************************/
        ret += SKP_Silk_setup_complexity( psEnc.sCmn, Complexity );

        /********************************************/
        /* Set bitrate/coding quality               */
        /********************************************/
        ret += SKP_Silk_setup_rate_FIX( psEnc, TargetRate_bps );

        /********************************************/
        /* Set packet loss rate measured by farend  */
        /********************************************/
        if( ( PacketLoss_perc < 0 ) || ( PacketLoss_perc > 100 ) ) {
            ret = SKP_SILK_ENC_INVALID_LOSS_RATE;
        }
        psEnc.sCmn.PacketLoss_perc = PacketLoss_perc;

        /********************************************/
        /* Set LBRR usage                           */
        /********************************************/
        ret += SKP_Silk_setup_LBRR_FIX( psEnc );

        /********************************************/
        /* Set DTX mode                             */
        /********************************************/
        if( DTX_enabled < 0 || DTX_enabled > 1 ) {
            ret = SKP_SILK_ENC_INVALID_DTX_SETTING;
        }
        psEnc.sCmn.useDTX = DTX_enabled;
        psEnc.sCmn.controlled_since_last_payload = 1;

        return ret;
    }

    /* Control low bitrate redundancy usage */
    public static void SKP_Silk_LBRR_ctrl_FIX(
        SKP_Silk_encoder_state_FIX      psEnc,             /* I    Encoder state FIX                           */
        SKP_Silk_encoder_control        psEncCtrlC          /* I/O  Encoder control                             */
    )
    {
        int LBRR_usage;

        if( psEnc.sCmn.LBRR_enabled != 0 ) {
            /* Control LBRR */

            /* Usage Control based on sensitivity and packet loss caracteristics */
            /* For now only enable adding to next for active frames. Make more complex later */
            LBRR_usage = SKP_SILK_NO_LBRR;
            if( psEnc.speech_activity_Q8 > SKP_FIX_CONST( LBRR_SPEECH_ACTIVITY_THRES, 8 ) && psEnc.sCmn.PacketLoss_perc > LBRR_LOSS_THRES ) { // nb! maybe multiply loss prob and speech activity
                LBRR_usage = SKP_SILK_ADD_LBRR_TO_PLUS1;
            }
            psEncCtrlC.LBRR_usage = LBRR_usage;
        } else {
            psEncCtrlC.LBRR_usage = SKP_SILK_NO_LBRR;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_Silk_setup_resamplers_FIX(
        SKP_Silk_encoder_state_FIX      psEnc,              /* I/O  Pointer to Silk encoder state FIX       */
        int                             fs_kHz              /* I    Internal sampling rate (kHz)            */
    )
    {
        int ret = SKP_SILK_NO_ERROR;

        if( psEnc.sCmn.fs_kHz != fs_kHz || psEnc.sCmn.prev_API_fs_Hz != psEnc.sCmn.API_fs_Hz ) {

            if( psEnc.sCmn.fs_kHz == 0 ) {
                /* Initialize the resampler for enc_API.c preparing resampling from API_fs_Hz to fs_kHz */
                ret += SKP_Silk_resampler_init( psEnc.sCmn.resampler_state, psEnc.sCmn.API_fs_Hz, fs_kHz * 1000 );
            } else {
                /* Allocate space for worst case temporary upsampling, 8 to 48 kHz, so a factor 6 */
                short[] x_buf_API_fs_Hz = new short[ ( 2 * MAX_FRAME_LENGTH + LA_SHAPE_MAX ) * ( MAX_API_FS_KHZ / 8 ) ];

                int nSamples_temp = SKP_LSHIFT( psEnc.sCmn.frame_length, 1 ) + LA_SHAPE_MS * psEnc.sCmn.fs_kHz;

                if( SKP_SMULBB( fs_kHz, 1000 ) < psEnc.sCmn.API_fs_Hz && psEnc.sCmn.fs_kHz != 0 ) {
                    /* Resample buffered data in x_buf to API_fs_Hz */

                    // PORT-NOTE: C 为栈上未初始化的临时重采样器状态；C 的
                    //   SKP_Silk_resampler_init 会先整结构清零，故托管 new() 的
                    //   零初始化与 C 语义一致。
                    SKP_Silk_resampler_state_struct temp_resampler_state = new();

                    /* Initialize resampler for temporary resampling of x_buf data to API_fs_Hz */
                    ret += SKP_Silk_resampler_init( temp_resampler_state, SKP_SMULBB( psEnc.sCmn.fs_kHz, 1000 ), psEnc.sCmn.API_fs_Hz );

                    /* Temporary resampling of x_buf data to API_fs_Hz */
                    ret += SKP_Silk_resampler( temp_resampler_state, x_buf_API_fs_Hz, 0, psEnc.x_buf, 0, nSamples_temp );

                    /* Calculate number of samples that has been temporarily upsampled */
                    nSamples_temp = SKP_DIV32_16( nSamples_temp * psEnc.sCmn.API_fs_Hz, SKP_SMULBB( psEnc.sCmn.fs_kHz, 1000 ) );

                    /* Initialize the resampler for enc_API.c preparing resampling from API_fs_Hz to fs_kHz */
                    ret += SKP_Silk_resampler_init( psEnc.sCmn.resampler_state, psEnc.sCmn.API_fs_Hz, SKP_SMULBB( fs_kHz, 1000 ) );

                } else {
                    /* Copy data */
                    SKP_memcpy( x_buf_API_fs_Hz, 0, psEnc.x_buf, 0, nSamples_temp );
                }

                if( 1000 * fs_kHz != psEnc.sCmn.API_fs_Hz ) {
                    /* Correct resampler state (unless resampling by a factor 1) by resampling buffered data from API_fs_Hz to fs_kHz */
                    ret += SKP_Silk_resampler( psEnc.sCmn.resampler_state, psEnc.x_buf, 0, x_buf_API_fs_Hz, 0, nSamples_temp );
                }
            }
        }

        psEnc.sCmn.prev_API_fs_Hz = psEnc.sCmn.API_fs_Hz;

        return(ret);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_Silk_setup_packetsize_FIX(
        SKP_Silk_encoder_state_FIX      psEnc,              /* I/O  Pointer to Silk encoder state FIX       */
        int                             PacketSize_ms        /* I    Packet length (ms)                      */
    )
    {
        int ret = SKP_SILK_NO_ERROR;

        /* Set packet size */
        if( ( PacketSize_ms !=  20 ) &&
            ( PacketSize_ms !=  40 ) &&
            ( PacketSize_ms !=  60 ) &&
            ( PacketSize_ms !=  80 ) &&
            ( PacketSize_ms != 100 ) ) {
            ret = SKP_SILK_ENC_PACKET_SIZE_NOT_SUPPORTED;
        } else {
            if( PacketSize_ms != psEnc.sCmn.PacketSize_ms ) {
                psEnc.sCmn.PacketSize_ms = PacketSize_ms;

                /* Packet length changes. Reset LBRR buffer */
                SKP_Silk_LBRR_reset( psEnc.sCmn );
            }
        }
        return(ret);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_Silk_setup_fs_FIX(
        SKP_Silk_encoder_state_FIX      psEnc,              /* I/O  Pointer to Silk encoder state FIX       */
        int                             fs_kHz               /* I    Internal sampling rate (kHz)            */
    )
    {
        int ret = SKP_SILK_NO_ERROR;

        /* Set internal sampling frequency */
        if( psEnc.sCmn.fs_kHz != fs_kHz ) {
            /* reset part of the state */
            psEnc.sShape.Clear();
            psEnc.sPrefilt.Clear();
            psEnc.sPred.Clear();
            psEnc.sCmn.sNSQ.Clear();
            SKP_memset( psEnc.sCmn.sNSQ_LBRR.xq, 0, (short)0, ( 2 * MAX_FRAME_LENGTH ) );
            // PORT-NOTE: C 为 SKP_memset( psEnc->sCmn.LBRR_buffer, 0, MAX_LBRR_DELAY * sizeof( SKP_SILK_LBRR_struct ) )
            //   —— 整数组清零，移植为逐元素 Clear()（引入局部 i 仅为循环计数）。
            for( int i = 0; i < MAX_LBRR_DELAY; i++ ) {
                psEnc.sCmn.LBRR_buffer[ i ].Clear();
            }
            /* PORT-NOTE: C 条件编译 #if SWITCH_TRANSITION_FILTERING —— 值为 1，保留该分支 */
            {
                SKP_memset( psEnc.sCmn.sLP.In_LP_State, 0, 0, 2 );
                if( psEnc.sCmn.sLP.mode == 1 ) {
                    /* Begin transition phase */
                    psEnc.sCmn.sLP.transition_frame_no = 1;
                } else {
                    /* End transition phase */
                    psEnc.sCmn.sLP.transition_frame_no = 0;
                }
            }
            psEnc.sCmn.inputBufIx          = 0;
            psEnc.sCmn.nFramesInPayloadBuf = 0;
            psEnc.sCmn.nBytesInPayloadBuf  = 0;
            psEnc.sCmn.oldest_LBRR_idx     = 0;
            psEnc.sCmn.TargetRate_bps      = 0; /* Ensures that psEnc->SNR_dB is recomputed */

            SKP_memset( psEnc.sPred.prev_NLSFq_Q15, 0, 0, MAX_LPC_ORDER );

            /* Initialize non-zero parameters */
            psEnc.sCmn.prevLag                     = 100;
            psEnc.sCmn.prev_sigtype                 = SIG_TYPE_UNVOICED;
            psEnc.sCmn.first_frame_after_reset      = 1;
            psEnc.sPrefilt.lagPrev                  = 100;
            psEnc.sShape.LastGainIndex              = 1;
            psEnc.sCmn.sNSQ.lagPrev                 = 100;
            psEnc.sCmn.sNSQ.prev_inv_gain_Q16        = 65536;
            psEnc.sCmn.sNSQ_LBRR.prev_inv_gain_Q16  = 65536;

            psEnc.sCmn.fs_kHz = fs_kHz;
            if( psEnc.sCmn.fs_kHz == 8 ) {
                psEnc.sCmn.predictLPCOrder = MIN_LPC_ORDER;
                psEnc.sCmn.psNLSF_CB[ 0 ]  = SKP_Silk_NLSF_CB0_10;
                psEnc.sCmn.psNLSF_CB[ 1 ]  = SKP_Silk_NLSF_CB1_10;
            } else {
                psEnc.sCmn.predictLPCOrder = MAX_LPC_ORDER;
                psEnc.sCmn.psNLSF_CB[ 0 ]  = SKP_Silk_NLSF_CB0_16;
                psEnc.sCmn.psNLSF_CB[ 1 ]  = SKP_Silk_NLSF_CB1_16;
            }
            psEnc.sCmn.frame_length   = SKP_SMULBB( FRAME_LENGTH_MS, fs_kHz );
            psEnc.sCmn.subfr_length   = SKP_DIV32_16( psEnc.sCmn.frame_length, NB_SUBFR );
            psEnc.sCmn.la_pitch       = SKP_SMULBB( LA_PITCH_MS, fs_kHz );
            psEnc.sPred.min_pitch_lag = SKP_SMULBB(  3, fs_kHz );
            psEnc.sPred.max_pitch_lag = SKP_SMULBB( 18, fs_kHz );
            psEnc.sPred.pitch_LPC_win_length = SKP_SMULBB( FIND_PITCH_LPC_WIN_MS, fs_kHz );
            if( psEnc.sCmn.fs_kHz == 24 ) {
                psEnc.mu_LTP_Q8 = SKP_FIX_CONST( MU_LTP_QUANT_SWB, 8 );
                // PORT-NOTE: C 宏 SKP_int32_MAX = 0x7FFFFFFF
                psEnc.sCmn.bitrate_threshold_up   = int.MaxValue;
                psEnc.sCmn.bitrate_threshold_down = SWB2WB_BITRATE_BPS;
            } else if( psEnc.sCmn.fs_kHz == 16 ) {
                psEnc.mu_LTP_Q8 = SKP_FIX_CONST( MU_LTP_QUANT_WB, 8 );
                psEnc.sCmn.bitrate_threshold_up   = WB2SWB_BITRATE_BPS;
                psEnc.sCmn.bitrate_threshold_down = WB2MB_BITRATE_BPS;
            } else if( psEnc.sCmn.fs_kHz == 12 ) {
                psEnc.mu_LTP_Q8 = SKP_FIX_CONST( MU_LTP_QUANT_MB, 8 );
                psEnc.sCmn.bitrate_threshold_up   = MB2WB_BITRATE_BPS;
                psEnc.sCmn.bitrate_threshold_down = MB2NB_BITRATE_BPS;
            } else {
                psEnc.mu_LTP_Q8 = SKP_FIX_CONST( MU_LTP_QUANT_NB, 8 );
                psEnc.sCmn.bitrate_threshold_up   = NB2MB_BITRATE_BPS;
                psEnc.sCmn.bitrate_threshold_down = 0;
            }
            psEnc.sCmn.fs_kHz_changed = 1;

            /* Check that settings are valid */
            /* SKP_assert( ( psEnc->sCmn.subfr_length * NB_SUBFR ) == psEnc->sCmn.frame_length ); —— 删除 */
        }
        return( ret );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_Silk_setup_rate_FIX(
        SKP_Silk_encoder_state_FIX      psEnc,              /* I/O  Pointer to Silk encoder state FIX       */
        int                             TargetRate_bps      /* I    Target max bitrate (if SNR_dB == 0)     */
    )
    {
        int   k, ret = SKP_SILK_NO_ERROR;
        int   frac_Q6;
        int[] rateTable;

        /* Set bitrate/coding quality */
        if( TargetRate_bps != psEnc.sCmn.TargetRate_bps ) {
            psEnc.sCmn.TargetRate_bps = TargetRate_bps;

            /* If new TargetRate_bps, translate to SNR_dB value */
            if( psEnc.sCmn.fs_kHz == 8 ) {
                rateTable = TargetRate_table_NB;
            } else if( psEnc.sCmn.fs_kHz == 12 ) {
                rateTable = TargetRate_table_MB;
            } else if( psEnc.sCmn.fs_kHz == 16 ) {
                rateTable = TargetRate_table_WB;
            } else {
                rateTable = TargetRate_table_SWB;
            }
            for( k = 1; k < TARGET_RATE_TAB_SZ; k++ ) {
                /* Find bitrate interval in table and interpolate */
                if( TargetRate_bps <= rateTable[ k ] ) {
                    frac_Q6 = SKP_DIV32( SKP_LSHIFT( TargetRate_bps - rateTable[ k - 1 ], 6 ),
                                                     rateTable[ k ] - rateTable[ k - 1 ] );
                    psEnc.SNR_dB_Q7 = SKP_LSHIFT( SNR_table_Q1[ k - 1 ], 6 ) + SKP_MUL( frac_Q6, SNR_table_Q1[ k ] - SNR_table_Q1[ k - 1 ] );
                    break;
                }
            }
        }
        return( ret );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_Silk_setup_LBRR_FIX(
        SKP_Silk_encoder_state_FIX      psEnc                 /* I/O  Pointer to Silk encoder state FIX       */
    )
    {
        int   ret = SKP_SILK_NO_ERROR;
        /* PORT-NOTE: C 条件编译 #if USE_LBRR —— 值为 1，仅移植该分支 */
        {
            int LBRRRate_thres_bps;

            if( psEnc.sCmn.useInBandFEC < 0 || psEnc.sCmn.useInBandFEC > 1 ) {
                ret = SKP_SILK_ENC_INVALID_INBAND_FEC_SETTING;
            }

            psEnc.sCmn.LBRR_enabled = psEnc.sCmn.useInBandFEC;
            if( psEnc.sCmn.fs_kHz == 8 ) {
                LBRRRate_thres_bps = INBAND_FEC_MIN_RATE_BPS - 9000;
            } else if( psEnc.sCmn.fs_kHz == 12 ) {
                LBRRRate_thres_bps = INBAND_FEC_MIN_RATE_BPS - 6000;
            } else if( psEnc.sCmn.fs_kHz == 16 ) {
                LBRRRate_thres_bps = INBAND_FEC_MIN_RATE_BPS - 3000;
            } else {
                LBRRRate_thres_bps = INBAND_FEC_MIN_RATE_BPS;
            }

            if( psEnc.sCmn.TargetRate_bps >= LBRRRate_thres_bps ) {
                /* Set gain increase / rate reduction for LBRR usage */
                /* Coarsely tuned with PESQ for now. */
                /* Linear regression coefs G = 8 - 0.5 * loss */
                /* Meaning that at 16% loss main rate and redundant rate is the same, -> G = 0 */
                psEnc.sCmn.LBRR_GainIncreases = SKP_max_int( 8 - SKP_RSHIFT( psEnc.sCmn.PacketLoss_perc, 1 ), 0 );

                /* Set main stream rate compensation */
                if( psEnc.sCmn.LBRR_enabled != 0 && psEnc.sCmn.PacketLoss_perc > LBRR_LOSS_THRES ) {
                    /* Tuned to give approx same mean / weighted bitrate as no inband FEC */
                    psEnc.inBandFEC_SNR_comp_Q8 = SKP_FIX_CONST( 6.0f, 8 ) - SKP_LSHIFT( psEnc.sCmn.LBRR_GainIncreases, 7 );
                } else {
                    psEnc.inBandFEC_SNR_comp_Q8 = 0;
                    psEnc.sCmn.LBRR_enabled     = 0;
                }
            } else {
                psEnc.inBandFEC_SNR_comp_Q8     = 0;
                psEnc.sCmn.LBRR_enabled         = 0;
            }
        }
        return ret;
    }
}
