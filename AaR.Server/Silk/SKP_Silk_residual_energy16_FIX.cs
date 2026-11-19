#nullable disable
// ***********************************************************************
// SKP_Silk_residual_energy16_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_residual_energy16_FIX.c
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

/* Residual energy: nrg = wxx - 2 * wXx * c + c' * wXX * c */

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_residual_energy16_FIX.c —— Residual energy: nrg = wxx - 2*wXx*c + c'*wXX*c.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE: 本文件函数名（SKP_Silk_residual_energy16_covar_FIX）与文件名不同，无
// CS0542 冲突，类名保持 C 文件名。
internal static class SKP_Silk_residual_energy16_FIX
{
    /* Residual energy: nrg = wxx - 2 * wXx * c + c' * wXX * c */
    public static int SKP_Silk_residual_energy16_covar_FIX(
        short[] c, int cOff,               /* I    Prediction vector                           */
        int[] wXX, int wXXOff,             /* I    Correlation matrix                          */
        int[] wXx, int wXxOff,             /* I    Correlation vector                           */
        int wxx,                            /* I    Signal energy                               */
        int D,                              /* I    Dimension                                   */
        int cQ                               /* I    Q value for c vector 0 - 15                 */
    )
    {
        int i, j, lshifts, Qxtra;
        int c_max, w_max, tmp, tmp2, nrg;
        int[] cn = new int[MAX_MATRIX_SIZE];
        // PORT-NOTE: C 的指针 pRow（指向 wXX 的第 i 行）→ 数组偏移。
        int pRowOff;

        /* Safety checks */

        lshifts = 16 - cQ;
        Qxtra = lshifts;

        c_max = 0;
        for (i = 0; i < D; i++)
        {
            c_max = SKP_max_32(c_max, SKP_abs((int)c[cOff + i]));
        }
        Qxtra = SKP_min_int(Qxtra, SKP_Silk_CLZ32(c_max) - 17);

        w_max = SKP_max_32(wXX[wXXOff + 0], wXX[wXXOff + D * D - 1]);
        Qxtra = SKP_min_int(Qxtra, SKP_Silk_CLZ32(SKP_MUL(D, SKP_RSHIFT(SKP_SMULWB(w_max, c_max), 4))) - 5);
        Qxtra = SKP_max_int(Qxtra, 0);
        for (i = 0; i < D; i++)
        {
            cn[i] = SKP_LSHIFT((int)c[cOff + i], Qxtra);
        }
        lshifts -= Qxtra;

        /* Compute wxx - 2 * wXx * c */
        tmp = 0;
        for (i = 0; i < D; i++)
        {
            tmp = SKP_SMLAWB(tmp, wXx[wXxOff + i], cn[i]);
        }
        nrg = SKP_RSHIFT(wxx, 1 + lshifts) - tmp;                         /* Q: -lshifts - 1 */

        /* Add c' * wXX * c, assuming wXX is symmetric */
        tmp2 = 0;
        for (i = 0; i < D; i++)
        {
            tmp = 0;
            pRowOff = wXXOff + i * D;
            for (j = i + 1; j < D; j++)
            {
                tmp = SKP_SMLAWB(tmp, wXX[pRowOff + j], cn[j]);
            }
            tmp = SKP_SMLAWB(tmp, SKP_RSHIFT(wXX[pRowOff + i], 1), cn[i]);
            tmp2 = SKP_SMLAWB(tmp2, tmp, cn[i]);
        }
        nrg = SKP_ADD_LSHIFT32(nrg, tmp2, lshifts);                       /* Q: -lshifts - 1 */

        /* Keep one bit free always, because we add them for LSF interpolation */
        if (nrg < 1)
        {
            nrg = 1;
        }
        else if (nrg > SKP_RSHIFT(int.MaxValue /* SKP_int32_MAX */, lshifts + 2))
        {
            nrg = int.MaxValue >> 1;    /* SKP_int32_MAX >> 1 */
        }
        else
        {
            nrg = SKP_LSHIFT(nrg, lshifts + 1);                           /* Q0 */
        }
        return nrg;

    }
}
