#nullable disable
// ***********************************************************************
// SKP_Silk_process_NLSFs_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_process_NLSFs_FIX.c
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

/* Limit, stabilize, convert and quantize NLSFs.    */

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_NLSF_VQ_weights_laroia_impl;
using static AaR.Server.Silk.SKP_Silk_interpolate_impl;
using static AaR.Server.Silk.SKP_Silk_NLSF_MSVQ_encode_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_NLSF2A_stable_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_process_NLSFs_FIX.c —— Limit, stabilize, convert and quantize NLSFs.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
// PORT-NOTE: TIC(...)/TOC(...) 为空宏，按 §4 删除。
internal static class SKP_Silk_process_NLSFs_FIX_impl
{
    /* Limit, stabilize, convert and quantize NLSFs.    */
    public static void SKP_Silk_process_NLSFs_FIX(
        SKP_Silk_encoder_state_FIX psEnc,           /* I/O  Encoder state FIX                           */
        SKP_Silk_encoder_control_FIX psEncCtrl,     /* I/O  Encoder control FIX                         */
        int[] pNLSF_Q15, int pNLSF_Q15Off           /* I/O  Normalized LSFs (quant out) (0 - (2^15-1))  */
    )
    {
        int doInterpolate;
        int[] pNLSFW_Q6 = new int[MAX_LPC_ORDER];
        int NLSF_mu_Q15, NLSF_mu_fluc_red_Q16;
        int i_sqr_Q15;
        SKP_Silk_NLSF_CB_struct psNLSF_CB;

        /* Used only for NLSF interpolation */
        int[] pNLSF0_temp_Q15 = new int[MAX_LPC_ORDER];
        int[] pNLSFW0_temp_Q6 = new int[MAX_LPC_ORDER];
        int i;

        /***********************/
        /* Calculate mu values */
        /***********************/
        if (psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED)
        {
            /* NLSF_mu           = 0.002f - 0.001f * psEnc->speech_activity; */
            /* NLSF_mu_fluc_red  = 0.1f   - 0.05f  * psEnc->speech_activity; */
            NLSF_mu_Q15 = SKP_SMLAWB(66, -8388, psEnc.speech_activity_Q8);
            NLSF_mu_fluc_red_Q16 = SKP_SMLAWB(6554, -838848, psEnc.speech_activity_Q8);
        }
        else
        {
            /* NLSF_mu           = 0.005f - 0.004f * psEnc->speech_activity; */
            /* NLSF_mu_fluc_red  = 0.2f   - 0.1f   * psEnc->speech_activity - 0.1f * psEncCtrl->sparseness; */
            NLSF_mu_Q15 = SKP_SMLAWB(164, -33554, psEnc.speech_activity_Q8);
            NLSF_mu_fluc_red_Q16 = SKP_SMLAWB(13107, -1677696, psEnc.speech_activity_Q8 + psEncCtrl.sparseness_Q8);
        }

        NLSF_mu_Q15 = SKP_max(NLSF_mu_Q15, 1);

        /* Calculate NLSF weights */
        SKP_Silk_NLSF_VQ_weights_laroia(pNLSFW_Q6, 0, pNLSF_Q15, pNLSF_Q15Off, psEnc.sCmn.predictLPCOrder);

        /* Update NLSF weights for interpolated NLSFs */
        // PORT-NOTE: C 的 && 产生 0/1 整型；C# 的 && 为 bool，按 C 语义转成 1/0，
        // 下游判空一律按 "!= 0"（与 C 整型真值一致）。
        doInterpolate = ((psEnc.sCmn.useInterpolatedNLSFs == 1) && (psEncCtrl.sCmn.NLSFInterpCoef_Q2 < (1 << 2))) ? 1 : 0;
        if (doInterpolate != 0)
        {

            /* Calculate the interpolated NLSF vector for the first half */
            SKP_Silk_interpolate(pNLSF0_temp_Q15, 0, psEnc.sPred.prev_NLSFq_Q15, 0, pNLSF_Q15, pNLSF_Q15Off,
                psEncCtrl.sCmn.NLSFInterpCoef_Q2, psEnc.sCmn.predictLPCOrder);

            /* Calculate first half NLSF weights for the interpolated NLSFs */
            SKP_Silk_NLSF_VQ_weights_laroia(pNLSFW0_temp_Q6, 0, pNLSF0_temp_Q15, 0, psEnc.sCmn.predictLPCOrder);

            /* Update NLSF weights with contribution from first half */
            i_sqr_Q15 = SKP_LSHIFT(SKP_SMULBB(psEncCtrl.sCmn.NLSFInterpCoef_Q2, psEncCtrl.sCmn.NLSFInterpCoef_Q2), 11);
            for (i = 0; i < psEnc.sCmn.predictLPCOrder; i++)
            {
                pNLSFW_Q6[i] = SKP_SMLAWB(SKP_RSHIFT(pNLSFW_Q6[i], 1), pNLSFW0_temp_Q6[i], i_sqr_Q15);
            }
        }

        /* Set pointer to the NLSF codebook for the current signal type and LPC order */
        psNLSF_CB = psEnc.sCmn.psNLSF_CB[psEncCtrl.sCmn.sigtype];

        /* Quantize NLSF parameters given the trained NLSF codebooks */
        SKP_Silk_NLSF_MSVQ_encode_FIX(psEncCtrl.sCmn.NLSFIndices, 0, pNLSF_Q15, pNLSF_Q15Off, psNLSF_CB,
            psEnc.sPred.prev_NLSFq_Q15, 0, pNLSFW_Q6, 0, NLSF_mu_Q15, NLSF_mu_fluc_red_Q16,
            psEnc.sCmn.NLSF_MSVQ_Survivors, psEnc.sCmn.predictLPCOrder, psEnc.sCmn.first_frame_after_reset);

        /* Convert quantized NLSFs back to LPC coefficients */
        SKP_Silk_NLSF2A_stable(psEncCtrl.PredCoef_Q12[1], 0, pNLSF_Q15, pNLSF_Q15Off, psEnc.sCmn.predictLPCOrder);

        if (doInterpolate != 0)
        {
            /* Calculate the interpolated, quantized LSF vector for the first half */
            SKP_Silk_interpolate(pNLSF0_temp_Q15, 0, psEnc.sPred.prev_NLSFq_Q15, 0, pNLSF_Q15, pNLSF_Q15Off,
                psEncCtrl.sCmn.NLSFInterpCoef_Q2, psEnc.sCmn.predictLPCOrder);

            /* Convert back to LPC coefficients */
            SKP_Silk_NLSF2A_stable(psEncCtrl.PredCoef_Q12[0], 0, pNLSF0_temp_Q15, 0, psEnc.sCmn.predictLPCOrder);

        }
        else
        {
            /* Copy LPC coefficients for first half from second half */
            SKP_memcpy(psEncCtrl.PredCoef_Q12[0], 0, psEncCtrl.PredCoef_Q12[1], 0, psEnc.sCmn.predictLPCOrder);
        }
    }
}
