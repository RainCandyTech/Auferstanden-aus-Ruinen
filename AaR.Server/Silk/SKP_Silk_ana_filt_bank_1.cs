#nullable disable
// ***********************************************************************
// SKP_Silk_ana_filt_bank_1.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_ana_filt_bank_1.c
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
/// SKP_Silk_ana_filt_bank_1.c —— Split signal into two decimated bands
/// using first-order allpass filters.（Copyright 2006 (c), Skype Limited，Date: 060221）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_ana_filt_bank_1_impl
{
    /* Coefficients for 2-band filter bank based on first-order allpass filters */
    // old
    internal static readonly short[] A_fb1_20 = new short[] { 5394 << 1 };
    /* wrap-around to negative number is intentional */
    // PORT-NOTE: 原字面量 (SKP_int16)(20623 << 1)，20623 << 1 = 41246 超出 int16 范围，回绕为 -24290
    internal static readonly short[] A_fb1_21 = new short[] { unchecked((short)(20623 << 1)) };

    /* Split signal into two decimated bands using first-order allpass filters */
    // PORT-NOTE: 参数名 in/out 为 C# 上下文关键字，按原名以 @in/@out 转义；指针参数按 §3 改为数组 + 偏移
    public static void SKP_Silk_ana_filt_bank_1(
        short[] @in, int inOff,        /* I:   Input signal [N]        */
        int[] S, int SOff,             /* I/O: State vector [2]        */
        short[] outL, int outLOff,     /* O:   Low band [N/2]          */
        short[] outH, int outHOff,     /* O:   High band [N/2]         */
        int[] scratch, int scratchOff, /* I:   Scratch memory [3*N/2]  */  // todo: remove - no longer used
        int N                          /* I:   Number of input samples */
    )
    {
        int k, N2 = SKP_RSHIFT(N, 1);
        int in32, X, Y, out_1, out_2;

        /* Internal variables and state are in Q10 format */
        for (k = 0; k < N2; k++)
        {
            /* Convert to Q10 */
            in32 = SKP_LSHIFT((int)@in[inOff + 2 * k], 10);

            /* All-pass section for even input sample */
            Y = SKP_SUB32(in32, S[SOff + 0]);
            X = SKP_SMLAWB(Y, Y, A_fb1_21[0]);
            out_1 = SKP_ADD32(S[SOff + 0], X);
            S[SOff + 0] = SKP_ADD32(in32, X);

            /* Convert to Q10 */
            in32 = SKP_LSHIFT((int)@in[inOff + 2 * k + 1], 10);

            /* All-pass section for odd input sample */
            Y = SKP_SUB32(in32, S[SOff + 1]);
            X = SKP_SMULWB(Y, A_fb1_20[0]);
            out_2 = SKP_ADD32(S[SOff + 1], X);
            S[SOff + 1] = SKP_ADD32(in32, X);

            /* Add/subtract, convert back to int16 and store to output */
            outL[outLOff + k] = (short)SKP_SAT16(SKP_RSHIFT_ROUND(SKP_ADD32(out_2, out_1), 11));
            outH[outHOff + k] = (short)SKP_SAT16(SKP_RSHIFT_ROUND(SKP_SUB32(out_2, out_1), 11));
        }
    }
}
