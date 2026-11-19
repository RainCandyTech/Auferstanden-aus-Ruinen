#nullable disable
// ***********************************************************************
// SKP_Silk_LTP_analysis_filter_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_LTP_analysis_filter_FIX.c
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
/// SKP_Silk_LTP_analysis_filter_FIX.c —— LTP analysis filter.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_LTP_analysis_filter_FIX_impl
{
    public static void SKP_Silk_LTP_analysis_filter_FIX(
        short[] LTP_res, int LTP_resOff,                    /* O:   LTP residual signal of length NB_SUBFR * ( pre_length + subfr_length )  */
        short[] x, int xOff,                                /* I:   Pointer to input signal with at least max( pitchL ) preceeding samples  */
        short[] LTPCoef_Q14, int LTPCoef_Q14Off,            /* I:   LTP_ORDER LTP coefficients for each NB_SUBFR subframe                   */
        int[] pitchL, int pitchLOff,                        /* I:   Pitch lag, one for each subframe                                        */
        int[] invGains_Q16, int invGains_Q16Off,            /* I:   Inverse quantization gains, one for each subframe                       */
        int subfr_length,                                   /* I:   Length of each subframe                                                 */
        int pre_length                                      /* I:   Length of the preceeding samples starting at &x[0] for each subframe    */
    )
    {
        // PORT-NOTE: C 的指针 x_ptr/x_lag_ptr/LTP_res_ptr（均在对应数组内步进）→ 数组 + 偏移。
        int x_ptrOff, x_lag_ptrOff;
        short[] Btmp_Q14 = new short[LTP_ORDER];
        int LTP_res_ptrOff;
        int k, i, j;
        int LTP_est;

        x_ptrOff = xOff;
        LTP_res_ptrOff = LTP_resOff;
        for (k = 0; k < NB_SUBFR; k++)
        {

            x_lag_ptrOff = x_ptrOff - pitchL[pitchLOff + k];
            for (i = 0; i < LTP_ORDER; i++)
            {
                Btmp_Q14[i] = LTPCoef_Q14[LTPCoef_Q14Off + k * LTP_ORDER + i];
            }

            /* LTP analysis FIR filter */
            for (i = 0; i < subfr_length + pre_length; i++)
            {
                LTP_res[LTP_res_ptrOff + i] = x[x_ptrOff + i];

                /* Long-term prediction */
                LTP_est = SKP_SMULBB(x[x_lag_ptrOff + LTP_ORDER / 2], Btmp_Q14[0]);
                for (j = 1; j < LTP_ORDER; j++)
                {
                    LTP_est = SKP_SMLABB_ovflw(LTP_est, x[x_lag_ptrOff + LTP_ORDER / 2 - j], Btmp_Q14[j]);
                }
                LTP_est = SKP_RSHIFT_ROUND(LTP_est, 14); // round and -> Q0

                /* Subtract long-term prediction */
                LTP_res[LTP_res_ptrOff + i] = (short)SKP_SAT16((int)x[x_ptrOff + i] - LTP_est);

                /* Scale residual */
                // PORT-NOTE: C 中 int32 结果隐式截断写入 int16 数组元素，按 §2 显式 (short)。
                LTP_res[LTP_res_ptrOff + i] = (short)SKP_SMULWB(invGains_Q16[invGains_Q16Off + k], LTP_res[LTP_res_ptrOff + i]);

                x_lag_ptrOff++;
            }

            /* Update pointers */
            LTP_res_ptrOff += subfr_length + pre_length;
            x_ptrOff += subfr_length;
        }
    }
}
