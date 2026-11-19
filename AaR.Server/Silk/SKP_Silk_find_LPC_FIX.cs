#nullable disable
// ***********************************************************************
// SKP_Silk_find_LPC_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_find_LPC_FIX.c
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
using static AaR.Server.Silk.SKP_Silk_burg_modified_impl;
using static AaR.Server.Silk.SKP_Silk_bwexpander_32_impl;
using static AaR.Server.Silk.SKP_Silk_A2NLSF_impl;
using static AaR.Server.Silk.SKP_Silk_interpolate_impl;
using static AaR.Server.Silk.SKP_Silk_NLSF2A_stable_impl;
using static AaR.Server.Silk.SKP_Silk_MA;
using static AaR.Server.Silk.SKP_Silk_sum_sqr_shift_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_find_LPC_FIX.c —— Finds LPC vector from correlations, and converts to NLSF.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_find_LPC_FIX_impl
{
    /* Finds LPC vector from correlations, and converts to NLSF */
    public static void SKP_Silk_find_LPC_FIX(
        int[] NLSF_Q15, int NLSF_Q15Off,        /* O    NLSFs                                                                       */
        out int interpIndex,                     /* O    NLSF interpolation index, only used for NLSF interpolation                  */
        int[] prev_NLSFq_Q15, int prev_NLSFq_Q15Off, /* I    previous NLSFs, only used for NLSF interpolation                            */
        int useInterpolatedNLSFs,                /* I    Flag                                                                        */
        int LPC_order,                           /* I    LPC order                                                                   */
        short[] x, int xOff,                     /* I    Input signal                                                                */
        int subfr_length                         /* I    Input signal subframe length including preceeding samples                   */
    )
    {
        int k;
        int[] a_Q16 = new int[MAX_LPC_ORDER];
        int isInterpLower, shift;
        short[] S = new short[MAX_LPC_ORDER];
        int res_nrg0, res_nrg1;
        int rshift0, rshift1;

        /* Used only for LSF interpolation */
        int[] a_tmp_Q16 = new int[MAX_LPC_ORDER];
        int res_nrg_interp, res_nrg, res_tmp_nrg;
        int res_nrg_interp_Q, res_nrg_Q, res_tmp_nrg_Q;
        short[] a_tmp_Q12 = new short[MAX_LPC_ORDER];
        int[] NLSF0_Q15 = new int[MAX_LPC_ORDER];
        short[] LPC_res = new short[(MAX_FRAME_LENGTH + NB_SUBFR * MAX_LPC_ORDER) / 2];

        /* Default: no interpolation */
        interpIndex = 4;

        /* Burg AR analysis for the full frame */
        SKP_Silk_burg_modified(out res_nrg, out res_nrg_Q, a_Q16, 0, x, xOff, subfr_length, NB_SUBFR, SKP_FIX_CONST(FIND_LPC_COND_FAC, 32), LPC_order);

        SKP_Silk_bwexpander_32(a_Q16, 0, LPC_order, SKP_FIX_CONST(FIND_LPC_CHIRP, 16));

        if (useInterpolatedNLSFs == 1)
        {

            /* Optimal solution for last 10 ms */
            SKP_Silk_burg_modified(out res_tmp_nrg, out res_tmp_nrg_Q, a_tmp_Q16, 0, x, xOff + (NB_SUBFR >> 1) * subfr_length,
                subfr_length, (NB_SUBFR >> 1), SKP_FIX_CONST(FIND_LPC_COND_FAC, 32), LPC_order);

            SKP_Silk_bwexpander_32(a_tmp_Q16, 0, LPC_order, SKP_FIX_CONST(FIND_LPC_CHIRP, 16));

            /* subtract residual energy here, as that's easier than adding it to the    */
            /* residual energy of the first 10 ms in each iteration of the search below */
            shift = res_tmp_nrg_Q - res_nrg_Q;
            if (shift >= 0)
            {
                if (shift < 32)
                {
                    res_nrg = res_nrg - SKP_RSHIFT(res_tmp_nrg, shift);
                }
            }
            else
            {
                res_nrg = SKP_RSHIFT(res_nrg, -shift) - res_tmp_nrg;
                res_nrg_Q = res_tmp_nrg_Q;
            }

            /* Convert to NLSFs */
            SKP_Silk_A2NLSF(NLSF_Q15, NLSF_Q15Off, a_tmp_Q16, 0, LPC_order);

            /* Search over interpolation indices to find the one with lowest residual energy */
            for (k = 3; k >= 0; k--)
            {
                /* Interpolate NLSFs for first half */
                SKP_Silk_interpolate(NLSF0_Q15, 0, prev_NLSFq_Q15, prev_NLSFq_Q15Off, NLSF_Q15, NLSF_Q15Off, k, LPC_order);

                /* Convert to LPC for residual energy evaluation */
                SKP_Silk_NLSF2A_stable(a_tmp_Q12, 0, NLSF0_Q15, 0, LPC_order);

                /* Calculate residual energy with NLSF interpolation */
                SKP_memset(S, 0, (short)0, LPC_order);
                SKP_Silk_LPC_analysis_filter(x, xOff, a_tmp_Q12, 0, S, 0, LPC_res, 0, 2 * subfr_length, LPC_order);

                SKP_Silk_sum_sqr_shift(out res_nrg0, out rshift0, LPC_res, LPC_order, subfr_length - LPC_order);
                SKP_Silk_sum_sqr_shift(out res_nrg1, out rshift1, LPC_res, LPC_order + subfr_length, subfr_length - LPC_order);

                /* Add subframe energies from first half frame */
                shift = rshift0 - rshift1;
                if (shift >= 0)
                {
                    res_nrg1 = SKP_RSHIFT(res_nrg1, shift);
                    res_nrg_interp_Q = -rshift0;
                }
                else
                {
                    res_nrg0 = SKP_RSHIFT(res_nrg0, -shift);
                    res_nrg_interp_Q = -rshift1;
                }
                res_nrg_interp = SKP_ADD32(res_nrg0, res_nrg1);

                /* Compare with first half energy without NLSF interpolation, or best interpolated value so far */
                shift = res_nrg_interp_Q - res_nrg_Q;
                if (shift >= 0)
                {
                    if (SKP_RSHIFT(res_nrg_interp, shift) < res_nrg)
                    {
                        isInterpLower = 1;    /* SKP_TRUE */
                    }
                    else
                    {
                        isInterpLower = 0;    /* SKP_FALSE */
                    }
                }
                else
                {
                    if (-shift < 32)
                    {
                        if (res_nrg_interp < SKP_RSHIFT(res_nrg, -shift))
                        {
                            isInterpLower = 1;    /* SKP_TRUE */
                        }
                        else
                        {
                            isInterpLower = 0;    /* SKP_FALSE */
                        }
                    }
                    else
                    {
                        isInterpLower = 0;    /* SKP_FALSE */
                    }
                }

                /* Determine whether current interpolated NLSFs are best so far */
                if (isInterpLower == 1 /* SKP_TRUE */)
                {
                    /* Interpolation has lower residual energy */
                    res_nrg = res_nrg_interp;
                    res_nrg_Q = res_nrg_interp_Q;
                    interpIndex = k;
                }
            }
        }

        if (interpIndex == 4)
        {
            /* NLSF interpolation is currently inactive, calculate NLSFs from full frame AR coefficients */
            SKP_Silk_A2NLSF(NLSF_Q15, NLSF_Q15Off, a_Q16, 0, LPC_order);
        }
    }
}
