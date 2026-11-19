#nullable disable
// ***********************************************************************
// SKP_Silk_NLSF_VQ_sum_error_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NLSF_VQ_sum_error_FIX.c
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

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_NLSF_VQ_sum_error_FIX.c —— Compute weighted quantization errors for an
/// LPC_order element input vector, over one codebook stage.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
// PORT-NOTE: 源文件整体位于 #if ( (!defined(__mips__)) && (EMBEDDED_ARM < 6) ) 之内，
// 默认构建条件为真（EMBEDDED_ARM 未定义 = 0），按 §7 移植该分支。
internal static class SKP_Silk_NLSF_VQ_sum_error_FIX_impl
{
    /* Compute weighted quantization errors for an LPC_order element input vector, over one codebook stage */
    public static void SKP_Silk_NLSF_VQ_sum_error_FIX(
        int[] err_Q20, int err_Q20Off,       /* O    Weighted quantization errors  [N*K]         */
        int[] in_Q15, int in_Q15Off,         /* I    Input vectors to be quantized [N*LPC_order] */
        int[] w_Q6, int w_Q6Off,             /* I    Weighting vectors             [N*LPC_order] */
        short[] pCB_Q15, int pCB_Q15Off,    /* I    Codebook vectors              [K*LPC_order] */
        int N,                               /* I    Number of input vectors                     */
        int K,                               /* I    Number of codebook vectors                  */
        int LPC_order                         /* I    Number of LPCs                              */
    )
    {
        int i, n, m;
        int diff_Q15, sum_error, Wtmp_Q6;
        int[] Wcpy_Q6 = new int[MAX_LPC_ORDER / 2];
        // PORT-NOTE: C 的指针 cb_vec_Q15（在码本内逐元素步进）→ 数组 + 偏移。
        int cb_vec_Q15Off;

        /* Copy to local stack and pack two weights per int32 */
        for (m = 0; m < SKP_RSHIFT(LPC_order, 1); m++)
        {
            Wcpy_Q6[m] = w_Q6[w_Q6Off + 2 * m] | SKP_LSHIFT(w_Q6[w_Q6Off + 2 * m + 1], 16);
        }

        /* Loop over input vectors */
        for (n = 0; n < N; n++)
        {
            /* Loop over codebook */
            cb_vec_Q15Off = pCB_Q15Off;
            for (i = 0; i < K; i++)
            {
                sum_error = 0;
                for (m = 0; m < LPC_order; m += 2)
                {
                    /* Get two weights packed in an int32 */
                    Wtmp_Q6 = Wcpy_Q6[SKP_RSHIFT(m, 1)];

                    /* Compute weighted squared quantization error for index m */
                    diff_Q15 = in_Q15[in_Q15Off + m] - pCB_Q15[cb_vec_Q15Off++]; // range: [ -32767 : 32767 ]
                    sum_error = SKP_SMLAWB(sum_error, SKP_SMULBB(diff_Q15, diff_Q15), Wtmp_Q6);

                    /* Compute weighted squared quantization error for index m + 1 */
                    diff_Q15 = in_Q15[in_Q15Off + m + 1] - pCB_Q15[cb_vec_Q15Off++]; // range: [ -32767 : 32767 ]
                    sum_error = SKP_SMLAWT(sum_error, SKP_SMULBB(diff_Q15, diff_Q15), Wtmp_Q6);
                }
                err_Q20[err_Q20Off + i] = sum_error;
            }
            err_Q20Off += K;
            in_Q15Off += LPC_order;
        }
    }
}
