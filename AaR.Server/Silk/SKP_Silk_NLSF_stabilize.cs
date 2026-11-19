#nullable disable
// ***********************************************************************
// SKP_Silk_NLSF_stabilize.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NLSF_stabilize.c
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

/* NLSF stabilizer:                                         */
/*                                                          */
/* - Moves NLSFs futher apart if they are too close         */
/* - Moves NLSFs away from borders if they are too close    */
/* - High effort to achieve a modification with minimum     */
/*     Euclidean distance to input vector                   */
/* - Output are sorted NLSF coefficients                    */
/*                                                          */

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_sort;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_NLSF_stabilize.c —— NLSF stabilizer, for a single input data vector.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_NLSF_stabilize_impl
{
    /* Constant Definitions */
    private const int MAX_LOOPS = 20;

    /* NLSF stabilizer, for a single input data vector */
    public static void SKP_Silk_NLSF_stabilize(
          int[] NLSF_Q15, int NLSF_Q15Off,        /* I/O:  Unstable/stabilized normalized LSF vector in Q15 [L]                    */
          int[] NDeltaMin_Q15, int NDeltaMin_Q15Off, /* I:    Normalized delta min vector in Q15, NDeltaMin_Q15[L] must be >= 1 [L+1] */
          int L                                    /* I:    Number of NLSF parameters in the input vector                           */
    )
    {
        int center_freq_Q15, diff_Q15, min_center_Q15, max_center_Q15;
        int min_diff_Q15;
        int loops;
        int i, I = 0, k;

        /* This is necessary to ensure an output within range of a SKP_int16 */

        for (loops = 0; loops < MAX_LOOPS; loops++)
        {
            /**************************/
            /* Find smallest distance */
            /**************************/
            /* First element */
            min_diff_Q15 = NLSF_Q15[NLSF_Q15Off + 0] - NDeltaMin_Q15[NDeltaMin_Q15Off + 0];
            I = 0;
            /* Middle elements */
            for (i = 1; i <= L - 1; i++)
            {
                diff_Q15 = NLSF_Q15[NLSF_Q15Off + i] - (NLSF_Q15[NLSF_Q15Off + i - 1] + NDeltaMin_Q15[NDeltaMin_Q15Off + i]);
                if (diff_Q15 < min_diff_Q15)
                {
                    min_diff_Q15 = diff_Q15;
                    I = i;
                }
            }
            /* Last element */
            diff_Q15 = (1 << 15) - (NLSF_Q15[NLSF_Q15Off + L - 1] + NDeltaMin_Q15[NDeltaMin_Q15Off + L]);
            if (diff_Q15 < min_diff_Q15)
            {
                min_diff_Q15 = diff_Q15;
                I = L;
            }

            /***************************************************/
            /* Now check if the smallest distance non-negative */
            /***************************************************/
            if (min_diff_Q15 >= 0)
            {
                return;
            }

            if (I == 0)
            {
                /* Move away from lower limit */
                NLSF_Q15[NLSF_Q15Off + 0] = NDeltaMin_Q15[NDeltaMin_Q15Off + 0];

            }
            else if (I == L)
            {
                /* Move away from higher limit */
                NLSF_Q15[NLSF_Q15Off + L - 1] = (1 << 15) - NDeltaMin_Q15[NDeltaMin_Q15Off + L];

            }
            else
            {
                /* Find the lower extreme for the location of the current center frequency */
                min_center_Q15 = 0;
                for (k = 0; k < I; k++)
                {
                    min_center_Q15 += NDeltaMin_Q15[NDeltaMin_Q15Off + k];
                }
                min_center_Q15 += SKP_RSHIFT(NDeltaMin_Q15[NDeltaMin_Q15Off + I], 1);

                /* Find the upper extreme for the location of the current center frequency */
                max_center_Q15 = (1 << 15);
                for (k = L; k > I; k--)
                {
                    max_center_Q15 -= NDeltaMin_Q15[NDeltaMin_Q15Off + k];
                }
                max_center_Q15 -= (NDeltaMin_Q15[NDeltaMin_Q15Off + I] - SKP_RSHIFT(NDeltaMin_Q15[NDeltaMin_Q15Off + I], 1));

                /* Move apart, sorted by value, keeping the same center frequency */
                center_freq_Q15 = SKP_LIMIT_32(SKP_RSHIFT_ROUND((int)NLSF_Q15[NLSF_Q15Off + I - 1] + (int)NLSF_Q15[NLSF_Q15Off + I], 1),
                    min_center_Q15, max_center_Q15);
                NLSF_Q15[NLSF_Q15Off + I - 1] = center_freq_Q15 - SKP_RSHIFT(NDeltaMin_Q15[NDeltaMin_Q15Off + I], 1);
                NLSF_Q15[NLSF_Q15Off + I] = NLSF_Q15[NLSF_Q15Off + I - 1] + NDeltaMin_Q15[NDeltaMin_Q15Off + I];
            }
        }

        /* Safe and simple fall back method, which is less ideal than the above */
        if (loops == MAX_LOOPS)
        {
            /* Insertion sort (fast for already almost sorted arrays):   */
            /* Best case:  O(n)   for an already sorted array            */
            /* Worst case: O(n^2) for an inversely sorted array          */
            SKP_Silk_insertion_sort_increasing_all_values(NLSF_Q15, NLSF_Q15Off, L);

            /* First NLSF should be no less than NDeltaMin[0] */
            NLSF_Q15[NLSF_Q15Off + 0] = SKP_max_int(NLSF_Q15[NLSF_Q15Off + 0], NDeltaMin_Q15[NDeltaMin_Q15Off + 0]);

            /* Keep delta_min distance between the NLSFs */
            for (i = 1; i < L; i++)
                NLSF_Q15[NLSF_Q15Off + i] = SKP_max_int(NLSF_Q15[NLSF_Q15Off + i], NLSF_Q15[NLSF_Q15Off + i - 1] + NDeltaMin_Q15[NDeltaMin_Q15Off + i]);

            /* Last NLSF should be no higher than 1 - NDeltaMin[L] */
            NLSF_Q15[NLSF_Q15Off + L - 1] = SKP_min_int(NLSF_Q15[NLSF_Q15Off + L - 1], (1 << 15) - NDeltaMin_Q15[NDeltaMin_Q15Off + L]);

            /* Keep NDeltaMin distance between the NLSFs */
            for (i = L - 2; i >= 0; i--)
                NLSF_Q15[NLSF_Q15Off + i] = SKP_min_int(NLSF_Q15[NLSF_Q15Off + i], NLSF_Q15[NLSF_Q15Off + i + 1] - NDeltaMin_Q15[NDeltaMin_Q15Off + i + 1]);
        }
    }
}
