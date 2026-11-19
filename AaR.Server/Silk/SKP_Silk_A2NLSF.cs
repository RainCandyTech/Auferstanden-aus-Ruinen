#nullable disable
// ***********************************************************************
// SKP_Silk_A2NLSF.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_A2NLSF.c
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

/* Conversion between prediction filter coefficients and NLSFs  */
/* Requires the order to be an even number                      */
/* A piecewise linear approximation maps LSF <-> cos(LSF)       */
/* Therefore the result is not accurate NLSFs, but the two      */
/* function are accurate inverses of each other                 */

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_bwexpander_32_impl;
// SKP_Silk_LSFCosTab_FIX_Q12 表来自 SKP_Silk_LSF_cos_table.c（表簇代理产出，
// using static 裸名引用，与 C 调用点形态一致）。
using static AaR.Server.Silk.SKP_Silk_LSF_cos_table;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_A2NLSF.c —— Compute Normalized Line Spectral Frequencies (NLSFs)
/// from whitening filter coefficients.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_A2NLSF_impl
{
    /* Number of binary divisions */
    private const int BIN_DIV_STEPS_A2NLSF_FIX = 3;    /* must be no higher than 16 - log2( LSF_COS_TAB_SZ_FIX ) */
    private const int QPoly = 16;
    private const int MAX_ITERATIONS_A2NLSF_FIX = 30;

    /* Flag for using 2x as many cosine sampling points, reduces the risk of missing a root */
    // PORT-NOTE: OVERSAMPLE_COSINE_TABLE == 0（默认构建），按 §7 只移植 #if 的 #else 分支。
    private const int OVERSAMPLE_COSINE_TABLE = 0;

    /* Helper function for A2NLSF(..)                    */
    /* Transforms polynomials from cos(n*f) to cos(f)^n  */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_Silk_A2NLSF_trans_poly(
        int[] p, int pOff,   /* I/O    Polynomial                                */
        int dd               /* I      Polynomial order (= filter order / 2 )    */
    )
    {
        int k, n;

        for (k = 2; k <= dd; k++)
        {
            for (n = dd; n > k; n--)
            {
                p[pOff + n - 2] -= p[pOff + n];
            }
            p[pOff + k - 2] -= SKP_LSHIFT(p[pOff + k], 1);
        }
    }

    // PORT-NOTE: 默认 gcc 构建中 EMBEDDED_ARM 未定义（= 0），故取 #if EMBEDDED_ARM<6
    // 的 SKP_INLINE 版本；ARMv6 的 extern 版本不移植（§7）。
    /* Helper function for A2NLSF(..)                    */
    /* Polynomial evaluation                             */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_Silk_A2NLSF_eval_poly(    /* return the polynomial evaluation, in QPoly */
        int[] p, int pOff,    /* I    Polynomial, QPoly        */
        int x,                /* I    Evaluation point, Q12    */
        int dd                /* I    Order                    */
    )
    {
        int n;
        int x_Q16, y32;

        y32 = p[pOff + dd];                                    /* QPoly */
        x_Q16 = SKP_LSHIFT(x, 4);
        for (n = dd - 1; n >= 0; n--)
        {
            y32 = SKP_SMLAWW(p[pOff + n], y32, x_Q16);       /* QPoly */
        }
        return y32;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_Silk_A2NLSF_init(
         int[] a_Q16, int a_Q16Off,
         int[] P, int POff,
         int[] Q, int QOff,
         int dd
    )
    {
        int k;

        /* Convert filter coefs to even and odd polynomials */
        P[POff + dd] = SKP_LSHIFT(1, QPoly);
        Q[QOff + dd] = SKP_LSHIFT(1, QPoly);
        for (k = 0; k < dd; k++)
        {
            // PORT-NOTE: #if( QPoly ... ) 三分支按 QPoly == 16 取中间分支（§7）。
            P[POff + k] = -a_Q16[a_Q16Off + dd - k - 1] - a_Q16[a_Q16Off + dd + k]; // QPoly
            Q[QOff + k] = -a_Q16[a_Q16Off + dd - k - 1] + a_Q16[a_Q16Off + dd + k]; // QPoly
        }

        /* Divide out zeros as we have that for even filter orders, */
        /* z =  1 is always a root in Q, and                        */
        /* z = -1 is always a root in P                             */
        for (k = dd; k > 0; k--)
        {
            P[POff + k - 1] -= P[POff + k];
            Q[QOff + k - 1] += Q[QOff + k];
        }

        /* Transform polynomials from cos(n*f) to cos(f)^n */
        SKP_Silk_A2NLSF_trans_poly(P, POff, dd);
        SKP_Silk_A2NLSF_trans_poly(Q, QOff, dd);
    }

    /* Compute Normalized Line Spectral Frequencies (NLSFs) from whitening filter coefficients        */
    /* If not all roots are found, the a_Q16 coefficients are bandwidth expanded until convergence.    */
    public static void SKP_Silk_A2NLSF(
        int[] NLSF, int NLSFOff,                     /* O    Normalized Line Spectral Frequencies, Q15 (0 - (2^15-1)), [d]    */
        int[] a_Q16, int a_Q16Off,                  /* I/O  Monic whitening filter coefficients in Q16 [d]                   */
        int d                                        /* I    Filter order (must be even)                                      */
    )
    {
        int i, k, m, dd, root_ix, ffrac;
        int xlo, xhi, xmid;
        int ylo, yhi, ymid;
        int nom, den;
        int[] P = new int[SKP_Silk_MAX_ORDER_LPC / 2 + 1];
        int[] Q = new int[SKP_Silk_MAX_ORDER_LPC / 2 + 1];
        int[][] PQ = new int[2][];
        int[] p;

        /* Store pointers to array */
        PQ[0] = P;
        PQ[1] = Q;

        dd = SKP_RSHIFT(d, 1);

        SKP_Silk_A2NLSF_init(a_Q16, a_Q16Off, P, 0, Q, 0, dd);

        /* Find roots, alternating between P and Q */
        p = P;    /* Pointer to polynomial */

        xlo = SKP_Silk_LSFCosTab_FIX_Q12[0]; // Q12
        ylo = SKP_Silk_A2NLSF_eval_poly(p, 0, xlo, dd);

        if (ylo < 0)
        {
            /* Set the first NLSF to zero and move on to the next */
            NLSF[NLSFOff + 0] = 0;
            p = Q;                      /* Pointer to polynomial */
            ylo = SKP_Silk_A2NLSF_eval_poly(p, 0, xlo, dd);
            root_ix = 1;                /* Index of current root */
        }
        else
        {
            root_ix = 0;                /* Index of current root */
        }
        k = 1;                          /* Loop counter */
        i = 0;                          /* Counter for bandwidth expansions applied */
        while (true)
        {
            /* Evaluate polynomial */
            xhi = SKP_Silk_LSFCosTab_FIX_Q12[k]; /* Q12 */
            yhi = SKP_Silk_A2NLSF_eval_poly(p, 0, xhi, dd);

            /* Detect zero crossing */
            if ((ylo <= 0 && yhi >= 0) || (ylo >= 0 && yhi <= 0))
            {
                /* Binary division */
                ffrac = -256;
                for (m = 0; m < BIN_DIV_STEPS_A2NLSF_FIX; m++)
                {
                    /* Evaluate polynomial */
                    xmid = SKP_RSHIFT_ROUND(xlo + xhi, 1);
                    ymid = SKP_Silk_A2NLSF_eval_poly(p, 0, xmid, dd);

                    /* Detect zero crossing */
                    if ((ylo <= 0 && ymid >= 0) || (ylo >= 0 && ymid <= 0))
                    {
                        /* Reduce frequency */
                        xhi = xmid;
                        yhi = ymid;
                    }
                    else
                    {
                        /* Increase frequency */
                        xlo = xmid;
                        ylo = ymid;
                        ffrac = SKP_ADD_RSHIFT(ffrac, 128, m);
                    }
                }

                /* Interpolate */
                if (SKP_abs(ylo) < 65536)
                {
                    /* Avoid dividing by zero */
                    den = ylo - yhi;
                    nom = SKP_LSHIFT(ylo, 8 - BIN_DIV_STEPS_A2NLSF_FIX) + SKP_RSHIFT(den, 1);
                    if (den != 0)
                    {
                        ffrac += SKP_DIV32(nom, den);
                    }
                }
                else
                {
                    /* No risk of dividing by zero because abs(ylo - yhi) >= abs(ylo) >= 65536 */
                    ffrac += SKP_DIV32(ylo, SKP_RSHIFT(ylo - yhi, 8 - BIN_DIV_STEPS_A2NLSF_FIX));
                }
                NLSF[NLSFOff + root_ix] = (int)SKP_min_32(SKP_LSHIFT((int)k, 8) + ffrac, short.MaxValue /* SKP_int16_MAX */);

                root_ix++;        /* Next root */
                if (root_ix >= d)
                {
                    /* Found all roots */
                    break;
                }
                /* Alternate pointer to polynomial */
                p = PQ[root_ix & 1];

                /* Evaluate polynomial */
                xlo = SKP_Silk_LSFCosTab_FIX_Q12[k - 1]; // Q12
                ylo = SKP_LSHIFT(1 - (root_ix & 2), 12);
            }
            else
            {
                /* Increment loop counter */
                k++;
                xlo = xhi;
                ylo = yhi;

                if (k > LSF_COS_TAB_SZ_FIX)
                {
                    i++;
                    if (i > MAX_ITERATIONS_A2NLSF_FIX)
                    {
                        /* Set NLSFs to white spectrum and exit */
                        NLSF[NLSFOff + 0] = SKP_DIV32_16(1 << 15, d + 1);
                        for (k = 1; k < d; k++)
                        {
                            NLSF[NLSFOff + k] = SKP_SMULBB(k + 1, NLSF[NLSFOff + 0]);
                        }
                        return;
                    }

                    /* Error: Apply progressively more bandwidth expansion and run again */
                    SKP_Silk_bwexpander_32(a_Q16, a_Q16Off, d, 65536 - SKP_SMULBB(10 + i, i)); // 10_Q16 = 0.00015

                    SKP_Silk_A2NLSF_init(a_Q16, a_Q16Off, P, 0, Q, 0, dd);
                    p = P;                            /* Pointer to polynomial */
                    xlo = SKP_Silk_LSFCosTab_FIX_Q12[0]; // Q12
                    ylo = SKP_Silk_A2NLSF_eval_poly(p, 0, xlo, dd);
                    if (ylo < 0)
                    {
                        /* Set the first NLSF to zero and move on to the next */
                        NLSF[NLSFOff + 0] = 0;
                        p = Q;                        /* Pointer to polynomial */
                        ylo = SKP_Silk_A2NLSF_eval_poly(p, 0, xlo, dd);
                        root_ix = 1;                  /* Index of current root */
                    }
                    else
                    {
                        root_ix = 0;                  /* Index of current root */
                    }
                    k = 1;                            /* Reset loop counter */
                }
            }
        }
    }
}
