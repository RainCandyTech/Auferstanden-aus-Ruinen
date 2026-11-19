#nullable disable
// ***********************************************************************
// SKP_Silk_NLSF2A.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NLSF2A.c
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

/* conversion between prediction filter coefficients and LSFs   */
/* order should be even                                         */
/* a piecewise linear approximation maps LSF <-> cos(LSF)       */
/* therefore the result is not accurate LSFs, but the two       */
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
/// SKP_Silk_NLSF2A.c —— compute whitening filter coefficients from normalized
/// line spectral frequencies.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_NLSF2A_impl
{
    /* helper function for NLSF2A(..) */
    // PORT-NOTE: 参数名 out 为 C# 上下文关键字，按原名以 @out 转义；指针参数按 §3 改为数组 + 偏移。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_Silk_NLSF2A_find_poly(
        int[] @out, int outOff,      /* o    intermediate polynomial, Q20            */
        int[] cLSF, int cLSFOff,     /* i    vector of interleaved 2*cos(LSFs), Q20  */
        int dd                       /* i    polynomial order (= 1/2 * filter order) */
    )
    {
        int k, n;
        int ftmp;

        @out[outOff + 0] = SKP_LSHIFT(1, 20);
        @out[outOff + 1] = -cLSF[cLSFOff + 0];
        for (k = 1; k < dd; k++)
        {
            ftmp = cLSF[cLSFOff + 2 * k];            // Q20
            @out[outOff + k + 1] = SKP_LSHIFT(@out[outOff + k - 1], 1) - (int)SKP_RSHIFT_ROUND64(SKP_SMULL(ftmp, @out[outOff + k]), 20);
            for (n = k; n > 1; n--)
            {
                @out[outOff + n] += @out[outOff + n - 2] - (int)SKP_RSHIFT_ROUND64(SKP_SMULL(ftmp, @out[outOff + n - 1]), 20);
            }
            @out[outOff + 1] -= ftmp;
        }
    }

    /* compute whitening filter coefficients from normalized line spectral frequencies */
    public static void SKP_Silk_NLSF2A(
        short[] a, int aOff,          /* o    monic whitening filter coefficients in Q12,  [d]    */
        int[] NLSF, int NLSFOff,      /* i    normalized line spectral frequencies in Q15, [d]    */
        int d                         /* i    filter order (should be even)                       */
    )
    {
        int k, i, dd;
        int[] cos_LSF_Q20 = new int[SKP_Silk_MAX_ORDER_LPC];
        int[] P = new int[SKP_Silk_MAX_ORDER_LPC / 2 + 1];
        int[] Q = new int[SKP_Silk_MAX_ORDER_LPC / 2 + 1];
        int Ptmp, Qtmp;
        int f_int;
        int f_frac;
        int cos_val, delta;
        int[] a_int32 = new int[SKP_Silk_MAX_ORDER_LPC];
        int maxabs, absval, idx = 0, sc_Q16;

        /* convert LSFs to 2*cos(LSF(i)), using piecewise linear curve from table */
        for (k = 0; k < d; k++)
        {

            /* f_int on a scale 0-127 (rounded down) */
            f_int = SKP_RSHIFT(NLSF[NLSFOff + k], 15 - 7);

            /* f_frac, range: 0..255 */
            f_frac = NLSF[NLSFOff + k] - SKP_LSHIFT(f_int, 15 - 7);

            /* Read start and end value from table */
            cos_val = SKP_Silk_LSFCosTab_FIX_Q12[f_int];                /* Q12 */
            delta = SKP_Silk_LSFCosTab_FIX_Q12[f_int + 1] - cos_val;  /* Q12, with a range of 0..200 */

            /* Linear interpolation */
            cos_LSF_Q20[k] = SKP_LSHIFT(cos_val, 8) + SKP_MUL(delta, f_frac); /* Q20 */
        }

        dd = SKP_RSHIFT(d, 1);

        /* generate even and odd polynomials using convolution */
        SKP_Silk_NLSF2A_find_poly(P, 0, cos_LSF_Q20, 0, dd);
        SKP_Silk_NLSF2A_find_poly(Q, 0, cos_LSF_Q20, 1, dd);

        /* convert even and odd polynomials to SKP_int32 Q12 filter coefs */
        for (k = 0; k < dd; k++)
        {
            Ptmp = P[k + 1] + P[k];
            Qtmp = Q[k + 1] - Q[k];

            /* the Ptmp and Qtmp values at this stage need to fit in int32 */

            a_int32[k] = -SKP_RSHIFT_ROUND(Ptmp + Qtmp, 9); /* Q20 -> Q12 */
            a_int32[d - k - 1] = SKP_RSHIFT_ROUND(Qtmp - Ptmp, 9); /* Q20 -> Q12 */
        }

        /* Limit the maximum absolute value of the prediction coefficients */
        for (i = 0; i < 10; i++)
        {
            /* Find maximum absolute value and its index */
            maxabs = 0;
            for (k = 0; k < d; k++)
            {
                absval = SKP_abs(a_int32[k]);
                if (absval > maxabs)
                {
                    maxabs = absval;
                    idx = k;
                }
            }

            if (maxabs > short.MaxValue /* SKP_int16_MAX */)
            {
                /* Reduce magnitude of prediction coefficients */
                maxabs = SKP_min(maxabs, 98369); // ( SKP_int32_MAX / ( 65470 >> 2 ) ) + SKP_int16_MAX = 98369
                sc_Q16 = 65470 - SKP_DIV32(SKP_MUL(65470 >> 2, maxabs - short.MaxValue /* SKP_int16_MAX */),
                                            SKP_RSHIFT32(SKP_MUL(maxabs, idx + 1), 2));
                SKP_Silk_bwexpander_32(a_int32, 0, d, sc_Q16);
            }
            else
            {
                break;
            }
        }

        /* Reached the last iteration */
        if (i == 10)
        {
            for (k = 0; k < d; k++)
            {
                a_int32[k] = SKP_SAT16(a_int32[k]);
            }
        }

        /* Return as SKP_int16 Q12 coefficients */
        for (k = 0; k < d; k++)
        {
            a[aOff + k] = (short)a_int32[k];
        }
    }
}
