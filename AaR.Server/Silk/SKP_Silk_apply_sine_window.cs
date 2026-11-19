#nullable disable
// ***********************************************************************
// SKP_Silk_apply_sine_window.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_apply_sine_window.c
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

/// <summary>SKP_Silk_apply_sine_window.c —— Apply sine window to signal vector.</summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_apply_sine_window_impl
{
    /* Apply sine window to signal vector.                                      */
    /* Window types:                                                            */
    /*    1 -> sine window from 0 to pi/2                                       */
    /*    2 -> sine window from pi/2 to pi                                      */
    /* Every other sample is linearly interpolated, for speed.                  */
    /* Window length must be between 16 and 120 (incl) and a multiple of 4.     */

    /* Matlab code for table:
       for k=16:9*4:16+2*9*4, fprintf(' %7.d,', -round(65536*pi ./ (k:4:k+8*4))); fprintf('\n'); end
    */
    internal static readonly short[] freq_table_Q16 = new short[]
    {
       12111,    9804,    8235,    7100,    6239,    5565,    5022,    4575,    4202,
        3885,    3612,    3375,    3167,    2984,    2820,    2674,    2542,    2422,
        2313,    2214,    2123,    2038,    1961,    1889,    1822,    1760,    1702,
    };

    public static void SKP_Silk_apply_sine_window(
        short[] px_win, int px_winOff,     /* O    Pointer to windowed signal                  */
        short[] px, int pxOff,              /* I    Pointer to input signal                     */
        int win_type,                       /* I    Selects a window type                       */
        int length                          /* I    Window length, multiple of 4                */
    )
    {
        int k, f_Q16, c_Q16;
        int S0_Q16, S1_Q16;
        int px32;

        /* Frequency */
        k = (length >> 2) - 4;
        f_Q16 = (int)freq_table_Q16[k];

        /* Factor used for cosine approximation */
        c_Q16 = SKP_SMULWB(f_Q16, -f_Q16);

        /* initialize state */
        if (win_type == 1)
        {
            /* start from 0 */
            S0_Q16 = 0;
            /* approximation of sin(f) */
            S1_Q16 = f_Q16 + SKP_RSHIFT(length, 3);
        }
        else
        {
            /* start from 1 */
            S0_Q16 = (1 << 16);
            /* approximation of cos(f) */
            S1_Q16 = (1 << 16) + SKP_RSHIFT(c_Q16, 1) + SKP_RSHIFT(length, 4);
        }

        /* Uses the recursive equation:   sin(n*f) = 2 * cos(f) * sin((n-1)*f) - sin((n-2)*f)    */
        /* 4 samples at a time */
        for (k = 0; k < length; k += 4)
        {
            // PORT-NOTE: C 代码 px32 = *( (SKP_int32 *)&px[ k ] ) 一次读两个 int16。
            // 小端内存布局下低 16 位 = px[k]、高 16 位 = px[k+1]，展开为等价表达式；
            // SKP_SMULWB 取其低 16 位、SKP_SMULWT 取其高 16 位，与 C 逐位一致。
            px32 = ((int)px[pxOff + k + 1] << 16) | (int)(ushort)px[pxOff + k];    /* load two values at once */
            px_win[px_winOff + k] = (short)SKP_SMULWB(SKP_RSHIFT(S0_Q16 + S1_Q16, 1), px32);
            px_win[px_winOff + k + 1] = (short)SKP_SMULWT(S1_Q16, px32);
            S0_Q16 = SKP_SMULWB(S1_Q16, c_Q16) + SKP_LSHIFT(S1_Q16, 1) - S0_Q16 + 1;
            S0_Q16 = SKP_min(S0_Q16, (1 << 16));

            px32 = ((int)px[pxOff + k + 3] << 16) | (int)(ushort)px[pxOff + k + 2];  /* load two values at once */
            px_win[px_winOff + k + 2] = (short)SKP_SMULWB(SKP_RSHIFT(S0_Q16 + S1_Q16, 1), px32);
            px_win[px_winOff + k + 3] = (short)SKP_SMULWT(S0_Q16, px32);
            S1_Q16 = SKP_SMULWB(S0_Q16, c_Q16) + SKP_LSHIFT(S0_Q16, 1) - S1_Q16;
            S1_Q16 = SKP_min(S1_Q16, (1 << 16));
        }
    }
}
