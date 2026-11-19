#nullable disable
// ***********************************************************************
// SKP_Silk_find_LTP_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_find_LTP_FIX.c
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
using static AaR.Server.Silk.SKP_Silk_sum_sqr_shift_impl;
using static AaR.Server.Silk.SKP_Silk_corrMatrix_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_regularize_correlations_FIX_impl;
using static AaR.Server.Silk.SKP_Silk_solve_LS_FIX;
using static AaR.Server.Silk.SKP_Silk_residual_energy16_FIX;
using static AaR.Server.Silk.SKP_Silk_scale_vector;
using static AaR.Server.Silk.SKP_Silk_lin2log_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_find_LTP_FIX.c —— Finds LTP vector from correlations.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_find_LTP_FIX_impl
{
    /* Head room for correlations                           */
    private const int LTP_CORRS_HEAD_ROOM = 2;

    // PORT-NOTE: C 文件顶部的 SKP_Silk_fit_LTP 前向声明省略（C# 无需前向声明），
    // 函数定义见本文件末尾，签名一致。

    // PORT-NOTE: C 形参 SKP_int *LTPredCodGain_Q7 可为 NULL（函数内判空跳过编码增益
    // 计算）；本移植中唯一调用点（SKP_Silk_find_pred_coefs_FIX）恒传非空引用，故按
    // §3.1 映射为 out，C 的 if( LTPredCodGain_Q7 != NULL ) 判空恒为真，判空分支不移植。
    public static void SKP_Silk_find_LTP_FIX(
        short[] b_Q14, int b_Q14Off,                     /* O    LTP coefs                                                   */
        int[] WLTP, int WLTPOff,                         /* O    Weight for LTP quantization                                 */
        out int LTPredCodGain_Q7,                         /* O    LTP coding gain                                             */
        short[] r_first, int r_firstOff,                  /* I    residual signal after LPC signal + state for first 10 ms    */
        short[] r_last, int r_lastOff,                    /* I    residual signal after LPC signal + state for last 10 ms     */
        int[] lag, int lagOff,                            /* I    LTP lags                                                    */
        int[] Wght_Q15, int Wght_Q15Off,                  /* I    weights                                                     */
        int subfr_length,                                /* I    subframe length                                             */
        int mem_offset,                                  /* I    number of samples in LTP memory                             */
        int[] corr_rshifts, int corr_rshiftsOff           /* O    right shifts applied to correlations                        */
    )
    {
        int i, k, lshift;
        // PORT-NOTE: C 的指针 r_ptr/lag_ptr（在 r_first 与 r_last 两段缓冲间切换）→
        // 数组引用 + 偏移成对保存。
        short[] r_ptr; int r_ptrOff;
        short[] lag_ptr; int lag_ptrOff;
        int b_Q14_ptrOff;

        int regu;
        int WLTP_ptrOff;
        int[] b_Q16 = new int[LTP_ORDER];
        int[] delta_b_Q14 = new int[LTP_ORDER];
        int[] d_Q14 = new int[NB_SUBFR];
        int[] nrg = new int[NB_SUBFR];
        int g_Q26;
        int[] w = new int[NB_SUBFR];
        int WLTP_max, max_abs_d_Q14, max_w_bits;

        int temp32, denom32;
        int extra_shifts;
        int rr_shifts, maxRshifts, maxRshifts_wxtra, LZs;
        int LPC_res_nrg, LPC_LTP_res_nrg, div_Q16;
        int[] Rr = new int[LTP_ORDER];
        int[] rr = new int[NB_SUBFR];
        int wd, m_Q12;

        b_Q14_ptrOff = b_Q14Off;
        WLTP_ptrOff = WLTPOff;
        r_ptr = r_first;
        r_ptrOff = r_firstOff + mem_offset;
        for (k = 0; k < NB_SUBFR; k++)
        {
            if (k == (NB_SUBFR >> 1))
            { /* shift residual for last 10 ms */
                r_ptr = r_last;
                r_ptrOff = r_lastOff + mem_offset;
            }
            lag_ptr = r_ptr;
            lag_ptrOff = r_ptrOff - (lag[lagOff + k] + LTP_ORDER / 2);

            SKP_Silk_sum_sqr_shift(out rr[k], out rr_shifts, r_ptr, r_ptrOff, subfr_length); /* rr[ k ] in Q( -rr_shifts ) */

            /* Assure headroom */
            LZs = SKP_Silk_CLZ32(rr[k]);
            if (LZs < LTP_CORRS_HEAD_ROOM)
            {
                rr[k] = SKP_RSHIFT_ROUND(rr[k], LTP_CORRS_HEAD_ROOM - LZs);
                rr_shifts += (LTP_CORRS_HEAD_ROOM - LZs);
            }
            corr_rshifts[corr_rshiftsOff + k] = rr_shifts;
            SKP_Silk_corrMatrix_FIX(lag_ptr, lag_ptrOff, subfr_length, LTP_ORDER, LTP_CORRS_HEAD_ROOM, WLTP, WLTP_ptrOff, ref corr_rshifts[corr_rshiftsOff + k]);  /* WLTP_fix_ptr in Q( -corr_rshifts[ k ] ) */

            /* The correlation vector always has lower max abs value than rr and/or RR so head room is assured */
            SKP_Silk_corrVector_FIX(lag_ptr, lag_ptrOff, r_ptr, r_ptrOff, subfr_length, LTP_ORDER, Rr, 0, corr_rshifts[corr_rshiftsOff + k]);  /* Rr_fix_ptr   in Q( -corr_rshifts[ k ] ) */
            if (corr_rshifts[corr_rshiftsOff + k] > rr_shifts)
            {
                rr[k] = SKP_RSHIFT(rr[k], corr_rshifts[corr_rshiftsOff + k] - rr_shifts); /* rr[ k ] in Q( -corr_rshifts[ k ] ) */
            }

            regu = 1;
            regu = SKP_SMLAWB(regu, rr[k], SKP_FIX_CONST(LTP_DAMPING / 3, 16));
            regu = SKP_SMLAWB(regu, WLTP[WLTP_ptrOff + matrix_idx(0, 0, LTP_ORDER)], SKP_FIX_CONST(LTP_DAMPING / 3, 16));
            regu = SKP_SMLAWB(regu, WLTP[WLTP_ptrOff + matrix_idx(LTP_ORDER - 1, LTP_ORDER - 1, LTP_ORDER)], SKP_FIX_CONST(LTP_DAMPING / 3, 16));
            SKP_Silk_regularize_correlations_FIX(WLTP, WLTP_ptrOff, ref rr[k], regu, LTP_ORDER);

            SKP_Silk_solve_LDL_FIX(WLTP, WLTP_ptrOff, LTP_ORDER, Rr, 0, b_Q16, 0); /* WLTP_fix_ptr and Rr_fix_ptr both in Q(-corr_rshifts[k]) */

            /* Limit and store in Q14 */
            SKP_Silk_fit_LTP(b_Q16, 0, b_Q14, b_Q14_ptrOff);

            /* Calculate residual energy */
            nrg[k] = SKP_Silk_residual_energy16_covar_FIX(b_Q14, b_Q14_ptrOff, WLTP, WLTP_ptrOff, Rr, 0, rr[k], LTP_ORDER, 14); /* nrg_fix in Q( -corr_rshifts[ k ] ) */

            /* temp = Wght[ k ] / ( nrg[ k ] * Wght[ k ] + 0.01f * subfr_length ); */
            extra_shifts = SKP_min_int(corr_rshifts[corr_rshiftsOff + k], LTP_CORRS_HEAD_ROOM);
            denom32 = SKP_LSHIFT_SAT32(SKP_SMULWB(nrg[k], Wght_Q15[Wght_Q15Off + k]), 1 + extra_shifts) + /* Q( -corr_rshifts[ k ] + extra_shifts ) */
                SKP_RSHIFT(SKP_SMULWB(subfr_length, 655), corr_rshifts[corr_rshiftsOff + k] - extra_shifts);    /* Q( -corr_rshifts[ k ] + extra_shifts ) */
            denom32 = SKP_max(denom32, 1);
            temp32 = SKP_DIV32(SKP_LSHIFT(Wght_Q15[Wght_Q15Off + k], 16), denom32);            /* Q( 15 + 16 + corr_rshifts[k] - extra_shifts ) */
            temp32 = SKP_RSHIFT(temp32, 31 + corr_rshifts[corr_rshiftsOff + k] - extra_shifts - 26);              /* Q26 */

            /* Limit temp such that the below scaling never wraps around */
            WLTP_max = 0;
            for (i = 0; i < LTP_ORDER * LTP_ORDER; i++)
            {
                WLTP_max = SKP_max(WLTP[WLTP_ptrOff + i], WLTP_max);
            }
            lshift = SKP_Silk_CLZ32(WLTP_max) - 1 - 3; /* keep 3 bits free for vq_nearest_neighbor_fix */
            if (26 - 18 + lshift < 31)
            {
                temp32 = SKP_min_32(temp32, SKP_LSHIFT((int)1, 26 - 18 + lshift));
            }

            SKP_Silk_scale_vector32_Q26_lshift_18(WLTP, WLTP_ptrOff, temp32, LTP_ORDER * LTP_ORDER); /* WLTP_ptr in Q( 18 - corr_rshifts[ k ] ) */

            w[k] = WLTP[WLTP_ptrOff + matrix_idx((LTP_ORDER >> 1), (LTP_ORDER >> 1), LTP_ORDER)]; /* w in Q( 18 - corr_rshifts[ k ] ) */

            r_ptrOff += subfr_length;
            b_Q14_ptrOff += LTP_ORDER;
            WLTP_ptrOff += LTP_ORDER * LTP_ORDER;
        }

        maxRshifts = 0;
        for (k = 0; k < NB_SUBFR; k++)
        {
            maxRshifts = SKP_max_int(corr_rshifts[corr_rshiftsOff + k], maxRshifts);
        }

        /* Compute LTP coding gain */
        {
            LPC_LTP_res_nrg = 0;
            LPC_res_nrg = 0;
            for (k = 0; k < NB_SUBFR; k++)
            {
                LPC_res_nrg = SKP_ADD32(LPC_res_nrg, SKP_RSHIFT(SKP_ADD32(SKP_SMULWB(rr[k], Wght_Q15[Wght_Q15Off + k]), 1), 1 + (maxRshifts - corr_rshifts[corr_rshiftsOff + k]))); /*  Q( -maxRshifts ) */
                LPC_LTP_res_nrg = SKP_ADD32(LPC_LTP_res_nrg, SKP_RSHIFT(SKP_ADD32(SKP_SMULWB(nrg[k], Wght_Q15[Wght_Q15Off + k]), 1), 1 + (maxRshifts - corr_rshifts[corr_rshiftsOff + k]))); /*  Q( -maxRshifts ) */
            }
            LPC_LTP_res_nrg = SKP_max(LPC_LTP_res_nrg, 1); /* avoid division by zero */

            div_Q16 = SKP_DIV32_varQ(LPC_res_nrg, LPC_LTP_res_nrg, 16);
            LTPredCodGain_Q7 = (int)SKP_SMULBB(3, SKP_Silk_lin2log(div_Q16) - (16 << 7));
        }

        /* smoothing */
        /* d = sum( B, 1 ); */
        b_Q14_ptrOff = b_Q14Off;
        for (k = 0; k < NB_SUBFR; k++)
        {
            d_Q14[k] = 0;
            for (i = 0; i < LTP_ORDER; i++)
            {
                d_Q14[k] += b_Q14[b_Q14_ptrOff + i];
            }
            b_Q14_ptrOff += LTP_ORDER;
        }

        /* m = ( w * d' ) / ( sum( w ) + 1e-3 ); */

        /* Find maximum absolute value of d_Q14 and the bits used by w in Q0 */
        max_abs_d_Q14 = 0;
        max_w_bits = 0;
        for (k = 0; k < NB_SUBFR; k++)
        {
            max_abs_d_Q14 = SKP_max_32(max_abs_d_Q14, SKP_abs(d_Q14[k]));
            /* w[ k ] is in Q( 18 - corr_rshifts[ k ] ) */
            /* Find bits needed in Q( 18 - maxRshifts ) */
            max_w_bits = SKP_max_32(max_w_bits, 32 - SKP_Silk_CLZ32(w[k]) + corr_rshifts[corr_rshiftsOff + k] - maxRshifts);
        }

        /* max_abs_d_Q14 = (5 << 15); worst case, i.e. LTP_ORDER * -SKP_int16_MIN */

        /* How many bits is needed for w*d' in Q( 18 - maxRshifts ) in the worst case, of all d_Q14's being equal to max_abs_d_Q14 */
        extra_shifts = max_w_bits + 32 - SKP_Silk_CLZ32(max_abs_d_Q14) - 14;

        /* Subtract what we got available; bits in output var plus maxRshifts */
        extra_shifts -= (32 - 1 - 2 + maxRshifts); /* Keep sign bit free as well as 2 bits for accumulation */
        extra_shifts = SKP_max_int(extra_shifts, 0);

        maxRshifts_wxtra = maxRshifts + extra_shifts;

        temp32 = SKP_RSHIFT(262, maxRshifts + extra_shifts) + 1; /* 1e-3f in Q( 18 - (maxRshifts + extra_shifts) ) */
        wd = 0;
        for (k = 0; k < NB_SUBFR; k++)
        {
            /* w has at least 2 bits of headroom so no overflow should happen */
            temp32 = SKP_ADD32(temp32, SKP_RSHIFT(w[k], maxRshifts_wxtra - corr_rshifts[corr_rshiftsOff + k]));                    /* Q( 18 - maxRshifts_wxtra ) */
            wd = SKP_ADD32(wd, SKP_LSHIFT(SKP_SMULWW(SKP_RSHIFT(w[k], maxRshifts_wxtra - corr_rshifts[corr_rshiftsOff + k]), d_Q14[k]), 2)); /* Q( 18 - maxRshifts_wxtra ) */
        }
        m_Q12 = SKP_DIV32_varQ(wd, temp32, 12);

        b_Q14_ptrOff = b_Q14Off;
        for (k = 0; k < NB_SUBFR; k++)
        {
            /* w_fix[ k ] from Q( 18 - corr_rshifts[ k ] ) to Q( 16 ) */
            if (2 - corr_rshifts[corr_rshiftsOff + k] > 0)
            {
                temp32 = SKP_RSHIFT(w[k], 2 - corr_rshifts[corr_rshiftsOff + k]);
            }
            else
            {
                temp32 = SKP_LSHIFT_SAT32(w[k], corr_rshifts[corr_rshiftsOff + k] - 2);
            }

            g_Q26 = SKP_MUL(
                SKP_DIV32(
                    SKP_FIX_CONST(LTP_SMOOTHING, 26),
                    SKP_RSHIFT(SKP_FIX_CONST(LTP_SMOOTHING, 26), 10) + temp32),                                       /* Q10 */
                SKP_LSHIFT_SAT32(SKP_SUB_SAT32(m_Q12, SKP_RSHIFT(d_Q14[k], 2)), 4));  /* Q16 */

            temp32 = 0;
            for (i = 0; i < LTP_ORDER; i++)
            {
                delta_b_Q14[i] = SKP_max_16(b_Q14[b_Q14_ptrOff + i], 1638);  /* 1638_Q14 = 0.1_Q0 */
                temp32 += delta_b_Q14[i];                          /* Q14 */
            }
            temp32 = SKP_DIV32(g_Q26, temp32); /* Q14->Q12 */
            for (i = 0; i < LTP_ORDER; i++)
            {
                // PORT-NOTE: C 中 int32 结果隐式截断写入 int16 数组元素，按 §2 显式 (short)。
                b_Q14[b_Q14_ptrOff + i] = (short)SKP_LIMIT_32((int)b_Q14[b_Q14_ptrOff + i] + SKP_SMULWB(SKP_LSHIFT_SAT32(temp32, 4), delta_b_Q14[i]), -16000, 28000);
            }
            b_Q14_ptrOff += LTP_ORDER;
        }
    }

    public static void SKP_Silk_fit_LTP(
        int[] LTP_coefs_Q16, int LTP_coefs_Q16Off,    /* I    [LTP_ORDER] */
        short[] LTP_coefs_Q14, int LTP_coefs_Q14Off    /* O    [LTP_ORDER] */
    )
    {
        int i;

        for (i = 0; i < LTP_ORDER; i++)
        {
            LTP_coefs_Q14[LTP_coefs_Q14Off + i] = (short)SKP_SAT16(SKP_RSHIFT_ROUND(LTP_coefs_Q16[LTP_coefs_Q16Off + i], 2));
        }
    }
}
