#nullable disable
// ***********************************************************************
// SKP_Silk_find_pred_coefs_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_find_pred_coefs_FIX.c
//（C 文件内含的版权注释按原样以 // 形式保留如下）
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

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_Inlines;
using static AaR.Server.Silk.SKP_Silk_find_LTP_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_quant_LTP_gains_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_LTP_scale_ctrl_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_LTP_analysis_filter_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_scale_copy_vector16_impl;
using static AaR.Server.Silk.SKP_Silk_find_LPC_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_process_NLSFs_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_residual_energy_FIX_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_find_pred_coefs_FIX.c —— Find prediction coefficients (LTP + LPC) for the current frame.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_find_pred_coefs_FIX_impl
{
    // PORT-NOTE: TIC(...)/TOC(...) 在 SKP_Silk_main_FIX.h 中为空宏，按 §4 删除。
    public static void SKP_Silk_find_pred_coefs_FIX(
        SKP_Silk_encoder_state_FIX psEnc,         /* I/O  encoder state                               */
        SKP_Silk_encoder_control_FIX psEncCtrl,   /* I/O  encoder control                             */
        short[] res_pitch, int res_pitchOff       /* I    Residual from pitch analysis                */
    )
    {
        int i;
        int[] WLTP = new int[NB_SUBFR * LTP_ORDER * LTP_ORDER];
        int[] invGains_Q16 = new int[NB_SUBFR];
        int[] local_gains = new int[NB_SUBFR];
        int[] Wght_Q15 = new int[NB_SUBFR];
        int[] NLSF_Q15 = new int[MAX_LPC_ORDER];
        // PORT-NOTE: C 的指针 x_ptr/x_pre_ptr（分别在 psEnc->x_buf 与 LPC_in_pre 内步进）→ 数组偏移。
        int x_ptrOff;
        int x_pre_ptrOff;
        short[] LPC_in_pre = new short[NB_SUBFR * MAX_LPC_ORDER + MAX_FRAME_LENGTH];
        int tmp, min_gain_Q16;
        int[] LTP_corrs_rshift = new int[NB_SUBFR];


        /* weighting for weighted least squares */
        min_gain_Q16 = int.MaxValue >> 6;    /* SKP_int32_MAX >> 6 */
        for (i = 0; i < NB_SUBFR; i++)
        {
            min_gain_Q16 = SKP_min(min_gain_Q16, psEncCtrl.Gains_Q16[i]);
        }
        for (i = 0; i < NB_SUBFR; i++)
        {
            /* Divide to Q16 */
            /* Invert and normalize gains, and ensure that maximum invGains_Q16 is within range of a 16 bit int */
            invGains_Q16[i] = SKP_DIV32_varQ(min_gain_Q16, psEncCtrl.Gains_Q16[i], 16 - 2);

            /* Ensure Wght_Q15 a minimum value 1 */
            invGains_Q16[i] = SKP_max(invGains_Q16[i], 363);

            /* Square the inverted gains */
            tmp = SKP_SMULWB(invGains_Q16[i], invGains_Q16[i]);
            Wght_Q15[i] = SKP_RSHIFT(tmp, 1);

            /* Invert the inverted and normalized gains */
            local_gains[i] = SKP_DIV32((1 << 16), invGains_Q16[i]);
        }

        if (psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED)
        {
            /**********/
            /* VOICED */
            /**********/

            /* LTP analysis */
            SKP_Silk_find_LTP_FIX(psEncCtrl.LTPCoef_Q14, 0, WLTP, 0, out psEncCtrl.LTPredCodGain_Q7, res_pitch, res_pitchOff,
                res_pitch, res_pitchOff + SKP_RSHIFT(psEnc.sCmn.frame_length, 1), psEncCtrl.sCmn.pitchL, 0, Wght_Q15, 0,
                psEnc.sCmn.subfr_length, psEnc.sCmn.frame_length, LTP_corrs_rshift, 0);


            /* Quantize LTP gain parameters */
            SKP_Silk_quant_LTP_gains_FIX(psEncCtrl.LTPCoef_Q14, 0, psEncCtrl.sCmn.LTPIndex, 0, ref psEncCtrl.sCmn.PERIndex,
                WLTP, 0, psEnc.mu_LTP_Q8, psEnc.sCmn.LTPQuantLowComplexity);

            /* Control LTP scaling */
            SKP_Silk_LTP_scale_ctrl_FIX(psEnc, psEncCtrl);

            /* Create LTP residual */
            SKP_Silk_LTP_analysis_filter_FIX(LPC_in_pre, 0, psEnc.x_buf, psEnc.sCmn.frame_length - psEnc.sCmn.predictLPCOrder,
                psEncCtrl.LTPCoef_Q14, 0, psEncCtrl.sCmn.pitchL, 0, invGains_Q16, 0, psEnc.sCmn.subfr_length, psEnc.sCmn.predictLPCOrder);

        }
        else
        {
            /************/
            /* UNVOICED */
            /************/
            /* Create signal with prepended subframes, scaled by inverse gains */
            x_ptrOff = psEnc.sCmn.frame_length - psEnc.sCmn.predictLPCOrder;
            x_pre_ptrOff = 0;
            for (i = 0; i < NB_SUBFR; i++)
            {
                SKP_Silk_scale_copy_vector16(LPC_in_pre, x_pre_ptrOff, psEnc.x_buf, x_ptrOff, invGains_Q16[i],
                    psEnc.sCmn.subfr_length + psEnc.sCmn.predictLPCOrder);
                x_pre_ptrOff += psEnc.sCmn.subfr_length + psEnc.sCmn.predictLPCOrder;
                x_ptrOff += psEnc.sCmn.subfr_length;
            }

            SKP_memset(psEncCtrl.LTPCoef_Q14, 0, (short)0, NB_SUBFR * LTP_ORDER);
            psEncCtrl.LTPredCodGain_Q7 = 0;
        }

        /* LPC_in_pre contains the LTP-filtered input for voiced, and the unfiltered input for unvoiced */
        SKP_Silk_find_LPC_FIX(NLSF_Q15, 0, out psEncCtrl.sCmn.NLSFInterpCoef_Q2, psEnc.sPred.prev_NLSFq_Q15, 0,
            psEnc.sCmn.useInterpolatedNLSFs * (1 - psEnc.sCmn.first_frame_after_reset), psEnc.sCmn.predictLPCOrder,
            LPC_in_pre, 0, psEnc.sCmn.subfr_length + psEnc.sCmn.predictLPCOrder);


        /* Quantize LSFs */
        SKP_Silk_process_NLSFs_FIX(psEnc, psEncCtrl, NLSF_Q15, 0);

        /* Calculate residual energy using quantized LPC coefficients */
        SKP_Silk_residual_energy_FIX(psEncCtrl.ResNrg, 0, psEncCtrl.ResNrgQ, 0, LPC_in_pre, 0, psEncCtrl.PredCoef_Q12, 0, local_gains, 0,
            psEnc.sCmn.subfr_length, psEnc.sCmn.predictLPCOrder);

        /* Copy to prediction struct for use in next frame for fluctuation reduction */
        SKP_memcpy(psEnc.sPred.prev_NLSFq_Q15, 0, NLSF_Q15, 0, psEnc.sCmn.predictLPCOrder);

    }
}
