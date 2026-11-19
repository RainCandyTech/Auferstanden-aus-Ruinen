#nullable disable
// ***********************************************************************
// SKP_Silk_LPC_inv_pred_gain.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_LPC_inv_pred_gain.c
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
/// SKP_Silk_LPC_inverse_pred_gain.c —— Compute inverse of LPC prediction gain,
/// and test if LPC coefficients are stable (all poles within unit circle).
///（Copyright 2008 (c), Skype Limited）
/// </summary>
internal static class SKP_Silk_LPC_inv_pred_gain
{
    private const int QA = 16;
    // PORT-NOTE: 原 C 文件级宏 #define A_LIMIT SKP_FIX_CONST( 0.99975, QA )。
    // 按 §4 规则以地基同名方法求值（float 语义已复现），求值结果 = 65520。
    internal static readonly int A_LIMIT = SKP_FIX_CONST(0.99975f, QA);

    /* Compute inverse of LPC prediction gain, and                          */
    /* test if LPC coefficients are stable (all poles within unit circle)   */
    // PORT-NOTE: C 的 static 内部函数照 §1 移植为类内 public static；
    // 参数 SKP_int32 A_QA[ 2 ][ SKP_Silk_MAX_ORDER_LPC ] 按指针→二维缓冲，
    // 以交错数组 int[][] 表达（§5），行内下标无基址偏移。
    public static int LPC_inverse_pred_gain_QA(        /* O:   Returns 1 if unstable, otherwise 0          */
        out int invGain_Q30,                           /* O:   Inverse prediction gain, Q30 energy domain  */
        int[][] A_QA,                                  /* I:   Prediction coefficients                     */
        int order                                      /* I:   Prediction order                            */
    )
    {
        int k, n, headrm;
        int rc_Q31, rc_mult1_Q30, rc_mult2_Q16, tmp_QA;
        int[] Aold_QA, Anew_QA;

        Anew_QA = A_QA[order & 1];

        invGain_Q30 = (1 << 30);
        for (k = order - 1; k > 0; k--)
        {
            /* Check for stability */
            if ((Anew_QA[k] > A_LIMIT) || (Anew_QA[k] < -A_LIMIT))
            {
                return 1;
            }

            /* Set RC equal to negated AR coef */
            rc_Q31 = -SKP_LSHIFT(Anew_QA[k], 31 - QA);

            /* rc_mult1_Q30 range: [ 1 : 2^30-1 ] */
            rc_mult1_Q30 = (int.MaxValue >> 1) - SKP_SMMUL(rc_Q31, rc_Q31);    /* SKP_int32_MAX >> 1 */

            /* rc_mult2_Q16 range: [ 2^16 : SKP_int32_MAX ] */
            rc_mult2_Q16 = SKP_INVERSE32_varQ(rc_mult1_Q30, 46);      /* 16 = 46 - 30 */

            /* Update inverse gain */
            /* invGain_Q30 range: [ 0 : 2^30 ] */
            invGain_Q30 = SKP_LSHIFT(SKP_SMMUL(invGain_Q30, rc_mult1_Q30), 2);

            /* Swap pointers */
            Aold_QA = Anew_QA;
            Anew_QA = A_QA[k & 1];

            /* Update AR coefficient */
            headrm = SKP_Silk_CLZ32(rc_mult2_Q16) - 1;
            rc_mult2_Q16 = SKP_LSHIFT(rc_mult2_Q16, headrm);          /* Q: 16 + headrm */
            for (n = 0; n < k; n++)
            {
                tmp_QA = Aold_QA[n] - SKP_LSHIFT(SKP_SMMUL(Aold_QA[k - n - 1], rc_Q31), 1);
                Anew_QA[n] = SKP_LSHIFT(SKP_SMMUL(tmp_QA, rc_mult2_Q16), 16 - headrm);
            }
        }

        /* Check for stability */
        if ((Anew_QA[0] > A_LIMIT) || (Anew_QA[0] < -A_LIMIT))
        {
            return 1;
        }

        /* Set RC equal to negated AR coef */
        rc_Q31 = -SKP_LSHIFT(Anew_QA[0], 31 - QA);

        /* Range: [ 1 : 2^30 ] */
        rc_mult1_Q30 = (int.MaxValue >> 1) - SKP_SMMUL(rc_Q31, rc_Q31);        /* SKP_int32_MAX >> 1 */

        /* Update inverse gain */
        /* Range: [ 0 : 2^30 ] */
        invGain_Q30 = SKP_LSHIFT(SKP_SMMUL(invGain_Q30, rc_mult1_Q30), 2);

        return 0;
    }

    /* For input in Q12 domain */
    // PORT-NOTE: 任务说明提到“内部用 SKP_Silk_LPC_inverse_pred_gain_Q24 递归调用自身（分块处理）”，
    // 但本参考源码 v1.0.9 的实际结构是：两个入口（Q12 / Q24）都调用本文件内的
    // static 辅助函数 LPC_inverse_pred_gain_QA，没有递归分块。按源码原样保持。
    public static int SKP_Silk_LPC_inverse_pred_gain(       /* O:   Returns 1 if unstable, otherwise 0          */
        out int invGain_Q30,                                 /* O:   Inverse prediction gain, Q30 energy domain  */
        short[] A_Q12, int A_Q12Off,                         /* I:   Prediction coefficients, Q12 [order]        */
        int order                                            /* I:   Prediction order                            */
    )
    {
        int k;
        // PORT-NOTE: C 局部数组 SKP_int32 Atmp_QA[ 2 ][ SKP_Silk_MAX_ORDER_LPC ] 按交错数组移植
        int[][] Atmp_QA = new int[][]
        {
            new int[SKP_Silk_MAX_ORDER_LPC],
            new int[SKP_Silk_MAX_ORDER_LPC],
        };
        int[] Anew_QA;

        Anew_QA = Atmp_QA[order & 1];

        /* Increase Q domain of the AR coefficients */
        for (k = 0; k < order; k++)
        {
            Anew_QA[k] = SKP_LSHIFT((int)A_Q12[A_Q12Off + k], QA - 12);
        }

        return LPC_inverse_pred_gain_QA(out invGain_Q30, Atmp_QA, order);
    }

    /* For input in Q24 domain */
    public static int SKP_Silk_LPC_inverse_pred_gain_Q24(   /* O:   Returns 1 if unstable, otherwise 0          */
        out int invGain_Q30,                                 /* O:   Inverse prediction gain, Q30 energy domain  */
        int[] A_Q24, int A_Q24Off,                           /* I:   Prediction coefficients, Q24 [order]        */
        int order                                            /* I:   Prediction order                            */
    )
    {
        int k;
        // PORT-NOTE: C 局部数组 SKP_int32 Atmp_QA[ 2 ][ SKP_Silk_MAX_ORDER_LPC ] 按交错数组移植
        int[][] Atmp_QA = new int[][]
        {
            new int[SKP_Silk_MAX_ORDER_LPC],
            new int[SKP_Silk_MAX_ORDER_LPC],
        };
        int[] Anew_QA;

        Anew_QA = Atmp_QA[order & 1];

        /* Increase Q domain of the AR coefficients */
        for (k = 0; k < order; k++)
        {
            Anew_QA[k] = SKP_RSHIFT_ROUND(A_Q24[A_Q24Off + k], 24 - QA);
        }

        return LPC_inverse_pred_gain_QA(out invGain_Q30, Atmp_QA, order);
    }
}
