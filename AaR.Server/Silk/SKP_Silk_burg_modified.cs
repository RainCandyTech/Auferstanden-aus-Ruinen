#nullable disable
// ***********************************************************************
// SKP_Silk_burg_modified.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_burg_modified.c
//（C 文件内含的版权注释按原样以 // 形式保留如下）
//
//                                                                      *
// SKP_Silk_burg_modified.c                                           *
//                                                                      *
// Calculates the reflection coefficients from the input vector         *
// Input vector contains nb_subfr sub vectors of length L_sub + D       *
//                                                                      *
// Copyright 2009 (c), Skype Limited                                    *
// Date: 100105                                                         *
//                                                                      *
//（另含文件头 Skype Limited 2006-2012 BSD 风格版权与免责声明，全文同
//   SKP_Silk_macros.cs 文件尾所载，此处略。——原始 .c 文件第 1-26 行为该声明）
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_sum_sqr_shift_impl;
using static AaR.Server.Silk.SKP_Silk_inner_prod_aligned_impl;
using static AaR.Server.Silk.SKP_Silk_Inlines;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_burg_modified.c —— Compute reflection coefficients from input signal.
///（Copyright 2009 (c), Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_burg_modified_impl
{
    private const int MAX_FRAME_SIZE = 544; // subfr_length * nb_subfr = ( 0.005 * 24000 + 16 ) * 4 = 544
    private const int MAX_NB_SUBFR = 4;

    private const int QA = 25;
    private const int N_BITS_HEAD_ROOM = 2;
    private const int MIN_RSHIFTS = -16;
    private const int MAX_RSHIFTS = 32 - QA;

    /* Compute reflection coefficients from input signal */
    public static void SKP_Silk_burg_modified(
        out int res_nrg,               /* O    residual energy                                                 */
        out int res_nrg_Q,             /* O    residual energy Q value                                         */
        int[] A_Q16, int A_Q16Off,     /* O    prediction coefficients (length order)                          */
        short[] x, int xOff,           /* I    input signal, length: nb_subfr * ( D + subfr_length )           */
        int subfr_length,              /* I    input signal subframe length (including D preceeding samples)   */
        int nb_subfr,                   /* I    number of subframes stacked in x                                */
        int WhiteNoiseFrac_Q32,        /* I    fraction added to zero-lag autocorrelation                      */
        int D                          /* I    order                                                           */
    )
    {
        int k, n, s, lz, rshifts, rshifts_extra;
        int C0, num, nrg, rc_Q31, Atmp_QA, Atmp1, tmp1, tmp2, x1, x2;
        // PORT-NOTE: C 的指针 x_ptr（在 x 数组内步进）→ 数组偏移，下同。
        int x_ptrOff;

        int[] C_first_row = new int[SKP_Silk_MAX_ORDER_LPC];
        int[] C_last_row = new int[SKP_Silk_MAX_ORDER_LPC];
        int[] Af_QA = new int[SKP_Silk_MAX_ORDER_LPC];

        int[] CAf = new int[SKP_Silk_MAX_ORDER_LPC + 1];
        int[] CAb = new int[SKP_Silk_MAX_ORDER_LPC + 1];


        /* Compute autocorrelations, added over subframes */
        SKP_Silk_sum_sqr_shift(out C0, out rshifts, x, xOff, nb_subfr * subfr_length);
        if (rshifts > MAX_RSHIFTS)
        {
            C0 = SKP_LSHIFT32(C0, rshifts - MAX_RSHIFTS);
            rshifts = MAX_RSHIFTS;
        }
        else
        {
            lz = SKP_Silk_CLZ32(C0) - 1;
            rshifts_extra = N_BITS_HEAD_ROOM - lz;
            if (rshifts_extra > 0)
            {
                rshifts_extra = SKP_min(rshifts_extra, MAX_RSHIFTS - rshifts);
                C0 = SKP_RSHIFT32(C0, rshifts_extra);
            }
            else
            {
                rshifts_extra = SKP_max(rshifts_extra, MIN_RSHIFTS - rshifts);
                C0 = SKP_LSHIFT32(C0, -rshifts_extra);
            }
            rshifts += rshifts_extra;
        }
        SKP_memset(C_first_row, 0, 0, SKP_Silk_MAX_ORDER_LPC);
        if (rshifts > 0)
        {
            for (s = 0; s < nb_subfr; s++)
            {
                x_ptrOff = xOff + s * subfr_length;
                for (n = 1; n < D + 1; n++)
                {
                    C_first_row[n - 1] += (int)SKP_RSHIFT64(
                        SKP_Silk_inner_prod16_aligned_64(x, x_ptrOff, x, x_ptrOff + n, subfr_length - n), rshifts);
                }
            }
        }
        else
        {
            for (s = 0; s < nb_subfr; s++)
            {
                x_ptrOff = xOff + s * subfr_length;
                for (n = 1; n < D + 1; n++)
                {
                    C_first_row[n - 1] += SKP_LSHIFT32(
                        SKP_Silk_inner_prod_aligned(x, x_ptrOff, x, x_ptrOff + n, subfr_length - n), -rshifts);
                }
            }
        }
        SKP_memcpy(C_last_row, 0, C_first_row, 0, SKP_Silk_MAX_ORDER_LPC);

        /* Initialize */
        // PORT-NOTE: C 链式赋值 CAb[0] = CAf[0] = (...) 拆为两条语句，赋值结果相同。
        CAf[0] = C0 + SKP_SMMUL(WhiteNoiseFrac_Q32, C0) + 1;         // Q(-rshifts)
        CAb[0] = CAf[0];

        for (n = 0; n < D; n++)
        {
            /* Update first row of correlation matrix (without first element) */
            /* Update last row of correlation matrix (without last element, stored in reversed order) */
            /* Update C * Af */
            /* Update C * flipud(Af) (stored in reversed order) */
            if (rshifts > -2)
            {
                for (s = 0; s < nb_subfr; s++)
                {
                    x_ptrOff = xOff + s * subfr_length;
                    x1 = -SKP_LSHIFT32((int)x[x_ptrOff + n], 16 - rshifts);                     // Q(16-rshifts)
                    x2 = -SKP_LSHIFT32((int)x[x_ptrOff + subfr_length - n - 1], 16 - rshifts); // Q(16-rshifts)
                    tmp1 = SKP_LSHIFT32((int)x[x_ptrOff + n], QA - 16);                        // Q(QA-16)
                    tmp2 = SKP_LSHIFT32((int)x[x_ptrOff + subfr_length - n - 1], QA - 16);      // Q(QA-16)
                    for (k = 0; k < n; k++)
                    {
                        C_first_row[k] = SKP_SMLAWB(C_first_row[k], x1, x[x_ptrOff + n - k - 1]);               // Q( -rshifts )
                        C_last_row[k] = SKP_SMLAWB(C_last_row[k], x2, x[x_ptrOff + subfr_length - n + k]);      // Q( -rshifts )
                        Atmp_QA = Af_QA[k];
                        tmp1 = SKP_SMLAWB(tmp1, Atmp_QA, x[x_ptrOff + n - k - 1]);                              // Q(QA-16)
                        tmp2 = SKP_SMLAWB(tmp2, Atmp_QA, x[x_ptrOff + subfr_length - n + k]);                   // Q(QA-16)
                    }
                    tmp1 = SKP_LSHIFT32(-tmp1, 32 - QA - rshifts);                               // Q(16-rshifts)
                    tmp2 = SKP_LSHIFT32(-tmp2, 32 - QA - rshifts);                               // Q(16-rshifts)
                    for (k = 0; k <= n; k++)
                    {
                        CAf[k] = SKP_SMLAWB(CAf[k], tmp1, x[x_ptrOff + n - k]);                 // Q( -rshift )
                        CAb[k] = SKP_SMLAWB(CAb[k], tmp2, x[x_ptrOff + subfr_length - n + k - 1]); // Q( -rshift )
                    }
                }
            }
            else
            {
                for (s = 0; s < nb_subfr; s++)
                {
                    x_ptrOff = xOff + s * subfr_length;
                    x1 = -SKP_LSHIFT32((int)x[x_ptrOff + n], -rshifts);                        // Q( -rshifts )
                    x2 = -SKP_LSHIFT32((int)x[x_ptrOff + subfr_length - n - 1], -rshifts);     // Q( -rshifts )
                    tmp1 = SKP_LSHIFT32((int)x[x_ptrOff + n], 17);                              // Q17
                    tmp2 = SKP_LSHIFT32((int)x[x_ptrOff + subfr_length - n - 1], 17);          // Q17
                    for (k = 0; k < n; k++)
                    {
                        C_first_row[k] = SKP_MLA(C_first_row[k], x1, x[x_ptrOff + n - k - 1]);               // Q( -rshifts )
                        C_last_row[k] = SKP_MLA(C_last_row[k], x2, x[x_ptrOff + subfr_length - n + k]);      // Q( -rshifts )
                        Atmp1 = SKP_RSHIFT_ROUND(Af_QA[k], QA - 17);                             // Q17
                        tmp1 = SKP_MLA(tmp1, x[x_ptrOff + n - k - 1], Atmp1);                    // Q17
                        tmp2 = SKP_MLA(tmp2, x[x_ptrOff + subfr_length - n + k], Atmp1);        // Q17
                    }
                    tmp1 = -tmp1;                                                                    // Q17
                    tmp2 = -tmp2;                                                                    // Q17
                    for (k = 0; k <= n; k++)
                    {
                        CAf[k] = SKP_SMLAWW(CAf[k], tmp1,
                            SKP_LSHIFT32((int)x[x_ptrOff + n - k], -rshifts - 1));                 // Q( -rshift )
                        CAb[k] = SKP_SMLAWW(CAb[k], tmp2,
                            SKP_LSHIFT32((int)x[x_ptrOff + subfr_length - n + k - 1], -rshifts - 1)); // Q( -rshift )
                    }
                }
            }

            /* Calculate nominator and denominator for the next order reflection (parcor) coefficient */
            tmp1 = C_first_row[n];                                                            // Q( -rshifts )
            tmp2 = C_last_row[n];                                                             // Q( -rshifts )
            num = 0;                                                                           // Q( -rshifts )
            nrg = SKP_ADD32(CAb[0], CAf[0]);                                                  // Q( 1-rshifts )
            for (k = 0; k < n; k++)
            {
                Atmp_QA = Af_QA[k];
                lz = SKP_Silk_CLZ32(SKP_abs(Atmp_QA)) - 1;
                lz = SKP_min(32 - QA, lz);
                Atmp1 = SKP_LSHIFT32(Atmp_QA, lz);                                            // Q( QA + lz )

                tmp1 = SKP_ADD_LSHIFT32(tmp1, SKP_SMMUL(C_last_row[n - k - 1], Atmp1), 32 - QA - lz);    // Q( -rshifts )
                tmp2 = SKP_ADD_LSHIFT32(tmp2, SKP_SMMUL(C_first_row[n - k - 1], Atmp1), 32 - QA - lz);    // Q( -rshifts )
                num = SKP_ADD_LSHIFT32(num, SKP_SMMUL(CAb[n - k], Atmp1), 32 - QA - lz);                  // Q( -rshifts )
                nrg = SKP_ADD_LSHIFT32(nrg, SKP_SMMUL(SKP_ADD32(CAb[k + 1], CAf[k + 1]),
                    Atmp1), 32 - QA - lz);                                                    // Q( 1-rshifts )
            }
            CAf[n + 1] = tmp1;                                                                // Q( -rshifts )
            CAb[n + 1] = tmp2;                                                                // Q( -rshifts )
            num = SKP_ADD32(num, tmp2);                                                       // Q( -rshifts )
            num = SKP_LSHIFT32(-num, 1);                                                      // Q( 1-rshifts )

            /* Calculate the next order reflection (parcor) coefficient */
            if (SKP_abs(num) < nrg)
            {
                rc_Q31 = SKP_DIV32_varQ(num, nrg, 31);
            }
            else
            {
                /* Negative energy or ratio too high; set remaining coefficients to zero and exit loop */
                SKP_memset(Af_QA, n, 0, D - n);
                break;
            }

            /* Update the AR coefficients */
            for (k = 0; k < (n + 1) >> 1; k++)
            {
                tmp1 = Af_QA[k];                                                              // QA
                tmp2 = Af_QA[n - k - 1];                                                      // QA
                Af_QA[k] = SKP_ADD_LSHIFT32(tmp1, SKP_SMMUL(tmp2, rc_Q31), 1);                // QA
                Af_QA[n - k - 1] = SKP_ADD_LSHIFT32(tmp2, SKP_SMMUL(tmp1, rc_Q31), 1);        // QA
            }
            Af_QA[n] = SKP_RSHIFT32(rc_Q31, 31 - QA);                                         // QA

            /* Update C * Af and C * Ab */
            for (k = 0; k <= n + 1; k++)
            {
                tmp1 = CAf[k];                                                                // Q( -rshifts )
                tmp2 = CAb[n - k + 1];                                                        // Q( -rshifts )
                CAf[k] = SKP_ADD_LSHIFT32(tmp1, SKP_SMMUL(tmp2, rc_Q31), 1);                  // Q( -rshifts )
                CAb[n - k + 1] = SKP_ADD_LSHIFT32(tmp2, SKP_SMMUL(tmp1, rc_Q31), 1);          // Q( -rshifts )
            }
        }

        /* Return residual energy */
        nrg = CAf[0];                                                                        // Q( -rshifts )
        tmp1 = 1 << 16;                                                                         // Q16
        for (k = 0; k < D; k++)
        {
            Atmp1 = SKP_RSHIFT_ROUND(Af_QA[k], QA - 16);                                    // Q16
            nrg = SKP_SMLAWW(nrg, CAf[k + 1], Atmp1);                                      // Q( -rshifts )
            tmp1 = SKP_SMLAWW(tmp1, Atmp1, Atmp1);                                            // Q16
            A_Q16[A_Q16Off + k] = -Atmp1;
        }
        res_nrg = SKP_SMLAWW(nrg, SKP_SMMUL(WhiteNoiseFrac_Q32, C0), -tmp1);               // Q( -rshifts )
        res_nrg_Q = -rshifts;
    }
}
