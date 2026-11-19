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
// SKP_Silk_init_encoder_FIX.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_init_encoder_FIX.c（编码器状态初始化/复位）
//
// PORT-NOTE: 本文件主函数与 C 文件同名（SKP_Silk_init_encoder_FIX），
//   C# 禁止成员名与其包含类型同名（CS0542），故类名加后缀 _impl，
//   方法名保持 C 原名（本簇统一约定，详见 SKP_Silk_enc_API.cs 文件头）。
//   C 的整结构体 SKP_memset( psEnc, 0, sizeof( SKP_Silk_encoder_state_FIX ) )
//   在地基中未提供对应 Clear()，此处按地基解码器 Clear() 的同款风格手工逐字段清零。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 其他簇（类名 = C 文件名，PORTING.md §1）
using static AaR.Server.Silk.SKP_Silk_VAD;                         // SKP_Silk_VAD_Init（类名无冲突）

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_init_encoder_FIX.c —— 初始化 Silk 编码器状态。
/// </summary>
internal static class SKP_Silk_init_encoder_FIX_impl
{
    /*********************************/
    /* Initialize Silk Encoder state */
    /*********************************/
    public static int SKP_Silk_init_encoder_FIX(
        SKP_Silk_encoder_state_FIX  psEnc                 /* I/O  Pointer to Silk FIX encoder state       */
    ) {
        int ret = 0;
        /* Clear the entire encoder state */
        // PORT-NOTE: C 的 SKP_memset( psEnc, 0, sizeof( SKP_Silk_encoder_state_FIX ) )。
        //   地基未为编码器状态提供 Clear()，以下手工逐字段清零（字段清单与
        //   SKP_Silk_structs.cs 逐一对应，含子结构 Clear() 与重采样器状态清零，
        //   风格同地基 SKP_Silk_decoder_state.Clear()）。
        SKP_memset_encoder_state_FIX( psEnc );

        /* PORT-NOTE: C 条件编译 #if HIGH_PASS_INPUT —— 值为 1，保留该分支 */
        {
            psEnc.variable_HP_smth1_Q15 = 200844; /* = SKP_Silk_log2(70)_Q0; */
            psEnc.variable_HP_smth2_Q15 = 200844; /* = SKP_Silk_log2(70)_Q0; */
        }

        /* Used to deactivate e.g. LSF interpolation and fluctuation reduction */
        psEnc.sCmn.first_frame_after_reset = 1;

        /* Initialize Silk VAD */
        ret += SKP_Silk_VAD_Init( psEnc.sCmn.sVAD );

        /* Initialize NSQ */
        psEnc.sCmn.sNSQ.prev_inv_gain_Q16      = 65536;
        psEnc.sCmn.sNSQ_LBRR.prev_inv_gain_Q16 = 65536;

        return( ret );
    }

    /// <summary>
    /// PORT-NOTE: 仅对应 C 的整结构体清零 memset，非 C 导出函数。
    /// </summary>
    private static void SKP_memset_encoder_state_FIX( SKP_Silk_encoder_state_FIX psEnc )
    {
        /* ---- 公共部分 SKP_Silk_encoder_state（sCmn） ---- */
        psEnc.sCmn.sRC.Clear();
        psEnc.sCmn.sRC_LBRR.Clear();
        psEnc.sCmn.sNSQ.Clear();
        psEnc.sCmn.sNSQ_LBRR.Clear();

        /* HIGH_PASS_INPUT = 1 */
        Array.Clear( psEnc.sCmn.In_HP_State, 0, 2 );
        /* SWITCH_TRANSITION_FILTERING = 1 */
        psEnc.sCmn.sLP.Clear();
        psEnc.sCmn.sVAD.Clear();

        psEnc.sCmn.LBRRprevLastGainIndex = 0;
        psEnc.sCmn.prev_sigtype = 0;
        psEnc.sCmn.typeOffsetPrev = 0;
        psEnc.sCmn.prevLag = 0;
        psEnc.sCmn.prev_lagIndex = 0;
        psEnc.sCmn.API_fs_Hz = 0;
        psEnc.sCmn.prev_API_fs_Hz = 0;
        psEnc.sCmn.maxInternal_fs_kHz = 0;
        psEnc.sCmn.fs_kHz = 0;
        psEnc.sCmn.fs_kHz_changed = 0;
        psEnc.sCmn.frame_length = 0;
        psEnc.sCmn.subfr_length = 0;
        psEnc.sCmn.la_pitch = 0;
        psEnc.sCmn.la_shape = 0;
        psEnc.sCmn.shapeWinLength = 0;
        psEnc.sCmn.TargetRate_bps = 0;
        psEnc.sCmn.PacketSize_ms = 0;
        psEnc.sCmn.PacketLoss_perc = 0;
        psEnc.sCmn.frameCounter = 0;
        psEnc.sCmn.Complexity = 0;
        psEnc.sCmn.nStatesDelayedDecision = 0;
        psEnc.sCmn.useInterpolatedNLSFs = 0;
        psEnc.sCmn.shapingLPCOrder = 0;
        psEnc.sCmn.predictLPCOrder = 0;
        psEnc.sCmn.pitchEstimationComplexity = 0;
        psEnc.sCmn.pitchEstimationLPCOrder = 0;
        psEnc.sCmn.pitchEstimationThreshold_Q16 = 0;
        psEnc.sCmn.LTPQuantLowComplexity = 0;
        psEnc.sCmn.NLSF_MSVQ_Survivors = 0;
        psEnc.sCmn.first_frame_after_reset = 0;
        psEnc.sCmn.controlled_since_last_payload = 0;
        psEnc.sCmn.warping_Q16 = 0;

        Array.Clear( psEnc.sCmn.inputBuf, 0, psEnc.sCmn.inputBuf.Length );
        psEnc.sCmn.inputBufIx = 0;
        psEnc.sCmn.nFramesInPayloadBuf = 0;
        psEnc.sCmn.nBytesInPayloadBuf = 0;

        psEnc.sCmn.frames_since_onset = 0;

        psEnc.sCmn.psNLSF_CB[ 0 ] = null;
        psEnc.sCmn.psNLSF_CB[ 1 ] = null;

        /* inband LBRR 结构 */
        psEnc.sCmn.LBRR_buffer[ 0 ].Clear();
        psEnc.sCmn.LBRR_buffer[ 1 ].Clear();
        psEnc.sCmn.oldest_LBRR_idx = 0;
        psEnc.sCmn.useInBandFEC = 0;
        psEnc.sCmn.LBRR_enabled = 0;
        psEnc.sCmn.LBRR_GainIncreases = 0;

        /* 码率控制 */
        psEnc.sCmn.bitrateDiff = 0;
        psEnc.sCmn.bitrate_threshold_up = 0;
        psEnc.sCmn.bitrate_threshold_down = 0;

        /* 重采样器状态（地基未提供 Clear()，手工清零，风格同解码器 Clear()） */
        Array.Clear( psEnc.sCmn.resampler_state.sIIR, 0, psEnc.sCmn.resampler_state.sIIR.Length );
        Array.Clear( psEnc.sCmn.resampler_state.sFIR, 0, psEnc.sCmn.resampler_state.sFIR.Length );
        Array.Clear( psEnc.sCmn.resampler_state.sDown2, 0, psEnc.sCmn.resampler_state.sDown2.Length );
        psEnc.sCmn.resampler_state.resampler_function = null;
        psEnc.sCmn.resampler_state.up2_function = null;
        psEnc.sCmn.resampler_state.batchSize = 0; psEnc.sCmn.resampler_state.invRatio_Q16 = 0;
        psEnc.sCmn.resampler_state.FIR_Fracs = 0; psEnc.sCmn.resampler_state.input2x = 0;
        psEnc.sCmn.resampler_state.Coefs = null;
        Array.Clear( psEnc.sCmn.resampler_state.sDownPre, 0, psEnc.sCmn.resampler_state.sDownPre.Length );
        Array.Clear( psEnc.sCmn.resampler_state.sUpPost, 0, psEnc.sCmn.resampler_state.sUpPost.Length );
        psEnc.sCmn.resampler_state.down_pre_function = null;
        psEnc.sCmn.resampler_state.up_post_function = null;
        psEnc.sCmn.resampler_state.batchSizePrePost = 0; psEnc.sCmn.resampler_state.ratio_Q16 = 0;
        psEnc.sCmn.resampler_state.nPreDownsamplers = 0; psEnc.sCmn.resampler_state.nPostUpsamplers = 0;
        psEnc.sCmn.resampler_state.magic_number = 0;

        /* DTX */
        psEnc.sCmn.noSpeechCounter = 0;
        psEnc.sCmn.useDTX = 0;
        psEnc.sCmn.inDTX = 0;
        psEnc.sCmn.vadFlag = 0;

        /* SWB 输入检测结构 */
        psEnc.sCmn.sSWBdetect.Clear();

        /* 缓冲 */
        Array.Clear( psEnc.sCmn.q, 0, psEnc.sCmn.q.Length );
        Array.Clear( psEnc.sCmn.q_LBRR, 0, psEnc.sCmn.q_LBRR.Length );

        /* ---- FIX 部分 ---- */
        psEnc.variable_HP_smth1_Q15 = 0;
        psEnc.variable_HP_smth2_Q15 = 0;

        psEnc.sShape.Clear();
        psEnc.sPrefilt.Clear();
        psEnc.sPred.Clear();

        Array.Clear( psEnc.x_buf, 0, psEnc.x_buf.Length );
        psEnc.LTPCorr_Q15 = 0;
        psEnc.mu_LTP_Q8 = 0;
        psEnc.SNR_dB_Q7 = 0;
        psEnc.avgGain_Q16 = 0;
        psEnc.avgGain_Q16_one_bit_per_sample = 0;
        psEnc.BufferedInChannel_ms = 0;
        psEnc.speech_activity_Q8 = 0;

        /* LTP 尺度控制参数 */
        psEnc.prevLTPredCodGain_Q7 = 0;
        psEnc.HPLTPredCodGain_Q7 = 0;

        psEnc.inBandFEC_SNR_comp_Q8 = 0;
    }
}
