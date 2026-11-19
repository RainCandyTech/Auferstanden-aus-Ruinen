#nullable disable
// ***********************************************************************
// SKP_Silk_warped_autocorrelation_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_warped_autocorrelation_FIX.c
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

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_warped_autocorrelation_FIX.c —— Autocorrelations for a warped frequency axis.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
// PORT-NOTE: 源文件整体位于 #if EMBEDDED_ARM<6 之内，默认构建条件为真
//（EMBEDDED_ARM 未定义 = 0），按 §7 移植该分支。
internal static class SKP_Silk_warped_autocorrelation_FIX_impl
{
    private const int QC = 10;
    private const int QS = 14;

    /* Autocorrelations for a warped frequency axis */
    public static void SKP_Silk_warped_autocorrelation_FIX(
          int[] corr, int corrOff,            /* O    Result [order + 1]                      */
          out int scale,                     /* O    Scaling of the correlation vector       */
          short[] input, int inputOff,        /* I    Input data to correlate                 */
          short warping_Q16,                  /* I    Warping coefficient                     */
          int length,                         /* I    Length of input                         */
          int order                           /* I    Correlation order (even)                */
    )
    {
        int n, i, lsh;
        int tmp1_QS, tmp2_QS;
        // PORT-NOTE: C 局部数组带 = { 0 } 初始化，C# new int[...] 默认清零，语义一致。
        int[] state_QS = new int[MAX_SHAPE_LPC_ORDER + 1];
        long[] corr_QC = new long[MAX_SHAPE_LPC_ORDER + 1];

        /* Order must be even */

        /* Loop over samples */
        for (n = 0; n < length; n++)
        {
            tmp1_QS = SKP_LSHIFT32((int)input[inputOff + n], QS);
            /* Loop over allpass sections */
            for (i = 0; i < order; i += 2)
            {
                /* Output of allpass section */
                tmp2_QS = SKP_SMLAWB(state_QS[i], state_QS[i + 1] - tmp1_QS, warping_Q16);
                state_QS[i] = tmp1_QS;
                corr_QC[i] += SKP_RSHIFT64(SKP_SMULL(tmp1_QS, state_QS[0]), 2 * QS - QC);
                /* Output of allpass section */
                tmp1_QS = SKP_SMLAWB(state_QS[i + 1], state_QS[i + 2] - tmp2_QS, warping_Q16);
                state_QS[i + 1] = tmp2_QS;
                corr_QC[i + 1] += SKP_RSHIFT64(SKP_SMULL(tmp2_QS, state_QS[0]), 2 * QS - QC);
            }
            state_QS[order] = tmp1_QS;
            corr_QC[order] += SKP_RSHIFT64(SKP_SMULL(tmp1_QS, state_QS[0]), 2 * QS - QC);
        }

        lsh = SKP_Silk_CLZ64(corr_QC[0]) - 35;
        lsh = SKP_LIMIT(lsh, -12 - QC, 30 - QC);
        scale = -(QC + lsh);
        if (lsh >= 0)
        {
            for (i = 0; i < order + 1; i++)
            {
                // PORT-NOTE: SKP_CHECK_FIT32 为恒等宏（§4），此处仅保留 (int) 截断。
                corr[corrOff + i] = (int)SKP_LSHIFT64(corr_QC[i], lsh);
            }
        }
        else
        {
            for (i = 0; i < order + 1; i++)
            {
                corr[corrOff + i] = (int)SKP_RSHIFT64(corr_QC[i], -lsh);
            }
        }
    }
}
