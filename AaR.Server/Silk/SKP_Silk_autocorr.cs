#nullable disable
// ***********************************************************************
// SKP_Silk_autocorr.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_autocorr.c
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
/// SKP_Silk_autocorr.c —— Calculates the autocorrelation. The result has
/// 29 non-zero bits for the first correlation, to leave some room for
/// adding white noise fractions etc.（Copyright 2008 (c), Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_autocorr_impl
{
    /* Compute autocorrelation */
    public static void SKP_Silk_autocorr(
        int[] results, int resultsOff,      /* O    Result (length correlationCount)            */
        out int scale,                      /* O    Scaling of the correlation vector           */
        short[] inputData, int inputDataOff,/* I    Input data to correlate                     */
        int inputDataSize,                  /* I    Length of input                             */
        int correlationCount                /* I    Number of correlation taps to compute       */
    )
    {
        int i, lz, nRightShifts, corrCount;
        long corr64;

        corrCount = SKP_min_int(inputDataSize, correlationCount);

        /* compute energy (zero-lag correlation) */
        corr64 = SKP_Silk_inner_prod_aligned_impl.SKP_Silk_inner_prod16_aligned_64(inputData, inputDataOff, inputData, inputDataOff, inputDataSize);

        /* deal with all-zero input data */
        corr64 += 1;

        /* number of leading zeros */
        lz = SKP_Silk_CLZ64(corr64);

        /* scaling: number of right shifts applied to correlations */
        nRightShifts = 35 - lz;
        scale = nRightShifts;

        if (nRightShifts <= 0)
        {
            results[resultsOff + 0] = SKP_LSHIFT((int)corr64, -nRightShifts);

            /* compute remaining correlations based on int32 inner product */
            for (i = 1; i < corrCount; i++)
            {
                results[resultsOff + i] = SKP_LSHIFT(SKP_Silk_inner_prod_aligned_impl.SKP_Silk_inner_prod_aligned(inputData, inputDataOff, inputData, inputDataOff + i, inputDataSize - i), -nRightShifts);
            }
        }
        else
        {
            results[resultsOff + 0] = (int)SKP_RSHIFT64(corr64, nRightShifts);

            /* compute remaining correlations based on int64 inner product */
            for (i = 1; i < corrCount; i++)
            {
                results[resultsOff + i] = (int)SKP_RSHIFT64(SKP_Silk_inner_prod_aligned_impl.SKP_Silk_inner_prod16_aligned_64(inputData, inputDataOff, inputData, inputDataOff + i, inputDataSize - i), nRightShifts);
            }
        }
    }
}
