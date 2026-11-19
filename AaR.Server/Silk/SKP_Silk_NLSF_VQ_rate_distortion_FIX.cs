#nullable disable
// ***********************************************************************
// SKP_Silk_NLSF_VQ_rate_distortion_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NLSF_VQ_rate_distortion_FIX.c
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
using static AaR.Server.Silk.SKP_Silk_NLSF_VQ_sum_error_FIX_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_NLSF_VQ_rate_distortion_FIX.c —— Rate-Distortion calculations for
/// multiple input data vectors.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_NLSF_VQ_rate_distortion_FIX_impl
{
    /* Rate-Distortion calculations for multiple input data vectors */
    public static void SKP_Silk_NLSF_VQ_rate_distortion_FIX(
        int[] pRD_Q20, int pRD_Q20Off,          /* O    Rate-distortion values [psNLSF_CBS->nVectors*N] */
        SKP_Silk_NLSF_CBS psNLSF_CBS,            /* I    NLSF codebook stage struct                      */
        int[] in_Q15, int in_Q15Off,             /* I    Input vectors to be quantized                   */
        int[] w_Q6, int w_Q6Off,                 /* I    Weight vector                                   */
        int[] rate_acc_Q5, int rate_acc_Q5Off,   /* I    Accumulated rates from previous stage           */
        int mu_Q15,                              /* I    Weight between weighted error and rate          */
        int N,                                    /* I    Number of input vectors to be quantized         */
        int LPC_order                            /* I    LPC order                                       */
    )
    {
        int i, n;
        // PORT-NOTE: C 的指针 pRD_vec_Q20（在 pRD_Q20 内按行步进）→ 数组偏移。
        int pRD_vec_Q20Off;

        /* Compute weighted quantization errors for all input vectors over one codebook stage */
        SKP_Silk_NLSF_VQ_sum_error_FIX(pRD_Q20, pRD_Q20Off, in_Q15, in_Q15Off, w_Q6, w_Q6Off, psNLSF_CBS.CB_NLSF_Q15, 0,
            N, psNLSF_CBS.nVectors, LPC_order);

        /* Loop over input vectors */
        pRD_vec_Q20Off = pRD_Q20Off;
        for (n = 0; n < N; n++)
        {
            /* Add rate cost to error for each codebook vector */
            for (i = 0; i < psNLSF_CBS.nVectors; i++)
            {
                pRD_Q20[pRD_vec_Q20Off + i] = SKP_SMLABB(pRD_Q20[pRD_vec_Q20Off + i], rate_acc_Q5[rate_acc_Q5Off + n] + psNLSF_CBS.Rates_Q5[i], mu_Q15);
            }
            pRD_vec_Q20Off += psNLSF_CBS.nVectors;
        }
    }
}
