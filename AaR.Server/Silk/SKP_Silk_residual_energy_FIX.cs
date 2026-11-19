#nullable disable
// ***********************************************************************
// SKP_Silk_residual_energy_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_residual_energy_FIX.c
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

/* Calculates residual energies of input subframes where all subframes have LPC_order   */
/* of preceeding samples                                                                */

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_MA;
using static AaR.Server.Silk.SKP_Silk_sum_sqr_shift_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_residual_energy_FIX.c —— Calculates residual energies of input subframes.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_residual_energy_FIX_impl
{
    /* Calculates residual energies of input subframes where all subframes have LPC_order   */
    /* of preceeding samples                                                                */
    // PORT-NOTE: 形参 SKP_int16 a_Q12[ 2 ][ MAX_LPC_ORDER ]（二维数组，含 MAX_LPC_ORDER 行距）
    // 按交错数组 short[2][] 移植；a_Q12Off 为行偏移（第 0 行所在下标），行内元素
    // 从行首（偏移 0）开始，与地基 SKP_Silk_encoder_control_FIX.PredCoef_Q12 形态一致。
    public static void SKP_Silk_residual_energy_FIX(
          int[] nrgs, int nrgsOff,               /* O    Residual energy per subframe    */
          int[] nrgsQ, int nrgsQOff,             /* O    Q value per subframe            */
          short[] x, int xOff,                    /* I    Input signal                    */
          short[][] a_Q12, int a_Q12Off,         /* I    AR coefs for each frame half    */
          int[] gains, int gainsOff,             /* I    Quantization gains              */
          int subfr_length,                      /* I    Subframe length                 */
          int LPC_order                           /* I    LPC order                       */
    )
    {
        int offset, i, j, rshift, lz1, lz2;
        // PORT-NOTE: C 的指针 LPC_res_ptr/x_ptr（分别在 LPC_res/x 内步进）→ 数组偏移。
        int LPC_res_ptrOff;
        short[] LPC_res = new short[(MAX_FRAME_LENGTH + NB_SUBFR * MAX_LPC_ORDER) / 2];
        int x_ptrOff;
        short[] S = new short[MAX_LPC_ORDER];
        int tmp32;

        x_ptrOff = xOff;
        offset = LPC_order + subfr_length;

        /* Filter input to create the LPC residual for each frame half, and measure subframe energies */
        for (i = 0; i < 2; i++)
        {
            /* Calculate half frame LPC residual signal including preceeding samples */
            SKP_memset(S, 0, (short)0, LPC_order);
            SKP_Silk_LPC_analysis_filter(x, x_ptrOff, a_Q12[a_Q12Off + i], 0, S, 0, LPC_res, 0, (NB_SUBFR >> 1) * offset, LPC_order);

            /* Point to first subframe of the just calculated LPC residual signal */
            LPC_res_ptrOff = LPC_order;
            for (j = 0; j < (NB_SUBFR >> 1); j++)
            {
                /* Measure subframe energy */
                SKP_Silk_sum_sqr_shift(out nrgs[nrgsOff + i * (NB_SUBFR >> 1) + j], out rshift, LPC_res, LPC_res_ptrOff, subfr_length);

                /* Set Q values for the measured energy */
                nrgsQ[nrgsQOff + i * (NB_SUBFR >> 1) + j] = -rshift;

                /* Move to next subframe */
                LPC_res_ptrOff += offset;
            }
            /* Move to next frame half */
            x_ptrOff += (NB_SUBFR >> 1) * offset;
        }

        /* Apply the squared subframe gains */
        for (i = 0; i < NB_SUBFR; i++)
        {
            /* Fully upscale gains and energies */
            lz1 = SKP_Silk_CLZ32(nrgs[nrgsOff + i]) - 1;
            lz2 = SKP_Silk_CLZ32(gains[gainsOff + i]) - 1;

            tmp32 = SKP_LSHIFT32(gains[gainsOff + i], lz2);

            /* Find squared gains */
            tmp32 = SKP_SMMUL(tmp32, tmp32); // Q( 2 * lz2 - 32 )

            /* Scale energies */
            nrgs[nrgsOff + i] = SKP_SMMUL(tmp32, SKP_LSHIFT32(nrgs[nrgsOff + i], lz1)); // Q( nrgsQ[ i ] + lz1 + 2 * lz2 - 32 - 32 )
            nrgsQ[nrgsQOff + i] += lz1 + 2 * lz2 - 32 - 32;
        }
    }
}
