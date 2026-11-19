#nullable disable
// ***********************************************************************
// SKP_Silk_VQ_nearest_neighbor_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_VQ_nearest_neighbor_FIX.c
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
/// SKP_Silk_VQ_nearest_neighbor_FIX.c —— Entropy constrained MATRIX-weighted VQ,
/// hard-coded to 5-element vectors, for a single input data vector.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE: 本文件函数名（SKP_Silk_VQ_WMat_EC_FIX）与文件名不同，无 CS0542 冲突，
// 类名保持 C 文件名。
// PORT-NOTE: 函数体位于 #if !defined(_SYSTEM_IS_BIG_ENDIAN) 之内（§7：大端分支不移植），
// 即打包 int32 版本。
internal static class SKP_Silk_VQ_nearest_neighbor_FIX
{
    /* Entropy constrained MATRIX-weighted VQ, hard-coded to 5-element vectors, for a single input data vector */
    // PORT-NOTE: 形参 ind 在 C 中仅在找到更优码字时写入（*ind = k），并非所有路径都写，
    // C# 的 out 要求全路径赋值（CS0177），故按"先读后写/条件写"语义用 ref 承载；
    // rate_dist_Q14 在函数入口无条件先写（= SKP_int32_MAX），可为 out。
    public static void SKP_Silk_VQ_WMat_EC_FIX(
        ref int ind,                        /* O    index of best codebook vector               */
        out int rate_dist_Q14,              /* O    best weighted quantization error + mu * rate*/
        short[] in_Q14, int in_Q14Off,      /* I    input vector to be quantized                */
        int[] W_Q18, int W_Q18Off,          /* I    weighting matrix                            */
        short[] cb_Q14, int cb_Q14Off,      /* I    codebook                                    */
        short[] cl_Q6, int cl_Q6Off,        /* I    code length for each codebook vector        */
        int mu_Q8,                          /* I    tradeoff between weighted error and rate    */
        int L                               /* I    number of vectors in codebook               */
    )
    {
        int k;
        // PORT-NOTE: C 的指针 cb_row_Q14（沿码本逐行步进 LTP_ORDER 个元素）→ 数组偏移。
        int cb_row_Q14Off;
        int sum1_Q14, sum2_Q16, diff_Q14_01, diff_Q14_23, diff_Q14_4;

        /* Loop over codebook */
        rate_dist_Q14 = int.MaxValue;    /* SKP_int32_MAX */
        cb_row_Q14Off = cb_Q14Off;
        for (k = 0; k < L; k++)
        {
            /* Pack pairs of int16 values per int32 */
            diff_Q14_01 = (ushort)(in_Q14[in_Q14Off] - cb_Q14[cb_row_Q14Off]) | SKP_LSHIFT((int)in_Q14[in_Q14Off + 1] - cb_Q14[cb_row_Q14Off + 1], 16);
            diff_Q14_23 = (ushort)(in_Q14[in_Q14Off + 2] - cb_Q14[cb_row_Q14Off + 2]) | SKP_LSHIFT((int)in_Q14[in_Q14Off + 3] - cb_Q14[cb_row_Q14Off + 3], 16);
            diff_Q14_4 = in_Q14[in_Q14Off + 4] - cb_Q14[cb_row_Q14Off + 4];

            /* Weighted rate */
            sum1_Q14 = SKP_SMULBB(mu_Q8, cl_Q6[cl_Q6Off + k]);

            /* Add weighted quantization error, assuming W_Q18 is symmetric */
            /* NOTE: the code below loads two int16 values as one int32, and multiplies each using the  */
            /* SMLAWB and SMLAWT instructions. On a big-endian CPU the two int16 variables would be     */
            /* loaded in reverse order and the code will give the wrong result. In that case swapping   */
            /* the SMLAWB and SMLAWT instructions should solve the problem.                             */
            /* first row of W_Q18 */
            sum2_Q16 = SKP_SMULWT(W_Q18[W_Q18Off + 1], diff_Q14_01);
            sum2_Q16 = SKP_SMLAWB(sum2_Q16, W_Q18[W_Q18Off + 2], diff_Q14_23);
            sum2_Q16 = SKP_SMLAWT(sum2_Q16, W_Q18[W_Q18Off + 3], diff_Q14_23);
            sum2_Q16 = SKP_SMLAWB(sum2_Q16, W_Q18[W_Q18Off + 4], diff_Q14_4);
            sum2_Q16 = SKP_LSHIFT(sum2_Q16, 1);
            sum2_Q16 = SKP_SMLAWB(sum2_Q16, W_Q18[W_Q18Off + 0], diff_Q14_01);
            sum1_Q14 = SKP_SMLAWB(sum1_Q14, sum2_Q16, diff_Q14_01);

            /* second row of W_Q18 */
            sum2_Q16 = SKP_SMULWB(W_Q18[W_Q18Off + 7], diff_Q14_23);
            sum2_Q16 = SKP_SMLAWT(sum2_Q16, W_Q18[W_Q18Off + 8], diff_Q14_23);
            sum2_Q16 = SKP_SMLAWB(sum2_Q16, W_Q18[W_Q18Off + 9], diff_Q14_4);
            sum2_Q16 = SKP_LSHIFT(sum2_Q16, 1);
            sum2_Q16 = SKP_SMLAWT(sum2_Q16, W_Q18[W_Q18Off + 6], diff_Q14_01);
            sum1_Q14 = SKP_SMLAWT(sum1_Q14, sum2_Q16, diff_Q14_01);

            /* third row of W_Q18 */
            sum2_Q16 = SKP_SMULWT(W_Q18[W_Q18Off + 13], diff_Q14_23);
            sum2_Q16 = SKP_SMLAWB(sum2_Q16, W_Q18[W_Q18Off + 14], diff_Q14_4);
            sum2_Q16 = SKP_LSHIFT(sum2_Q16, 1);
            sum2_Q16 = SKP_SMLAWB(sum2_Q16, W_Q18[W_Q18Off + 12], diff_Q14_23);
            sum1_Q14 = SKP_SMLAWB(sum1_Q14, sum2_Q16, diff_Q14_23);

            /* fourth row of W_Q18 */
            sum2_Q16 = SKP_SMULWB(W_Q18[W_Q18Off + 19], diff_Q14_4);
            sum2_Q16 = SKP_LSHIFT(sum2_Q16, 1);
            sum2_Q16 = SKP_SMLAWT(sum2_Q16, W_Q18[W_Q18Off + 18], diff_Q14_23);
            sum1_Q14 = SKP_SMLAWT(sum1_Q14, sum2_Q16, diff_Q14_23);

            /* last row of W_Q18 */
            sum2_Q16 = SKP_SMULWB(W_Q18[W_Q18Off + 24], diff_Q14_4);
            sum1_Q14 = SKP_SMLAWB(sum1_Q14, sum2_Q16, diff_Q14_4);

            /* find best */
            if (sum1_Q14 < rate_dist_Q14)
            {
                rate_dist_Q14 = sum1_Q14;
                ind = k;
            }

            /* Go to next cbk vector */
            cb_row_Q14Off += LTP_ORDER;
        }
    }
}
