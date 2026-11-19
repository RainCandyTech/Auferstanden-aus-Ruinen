#nullable disable
// ***********************************************************************
// SKP_Silk_biquad_alt.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_biquad_alt.c
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
/// SKP_Silk_biquad_alt.c —— Second order ARMA filter.
/// Can handle slowly varying filter coefficients.
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_biquad_alt_impl
{
    /* Second order ARMA filter, alternative implementation */
    // PORT-NOTE: 参数名 in/out 为 C# 上下文关键字，按原名以 @in/@out 转义；指针参数按 §3 改为数组 + 偏移
    public static void SKP_Silk_biquad_alt(
        short[] @in, int inOff,        /* I:    Input signal                   */
        int[] B_Q28, int B_Q28Off,     /* I:    MA coefficients [3]            */
        int[] A_Q28, int A_Q28Off,     /* I:    AR coefficients [2]            */
        int[] S, int SOff,             /* I/O: State vector [2]                */
        short[] @out, int outOff,      /* O:    Output signal                  */
        int len                        /* I:    Signal length (must be even)   */
    )
    {
        /* DIRECT FORM II TRANSPOSED (uses 2 element state vector) */
        int k;
        int inval, A0_U_Q28, A0_L_Q28, A1_U_Q28, A1_L_Q28, out32_Q14;

        /* Negate A_Q28 values and split in two parts */
        A0_L_Q28 = (-A_Q28[A_Q28Off + 0]) & 0x00003FFF;        /* lower part */
        A0_U_Q28 = SKP_RSHIFT(-A_Q28[A_Q28Off + 0], 14);       /* upper part */
        A1_L_Q28 = (-A_Q28[A_Q28Off + 1]) & 0x00003FFF;        /* lower part */
        A1_U_Q28 = SKP_RSHIFT(-A_Q28[A_Q28Off + 1], 14);       /* upper part */

        for (k = 0; k < len; k++)
        {
            /* S[ 0 ], S[ 1 ]: Q12 */
            inval = @in[inOff + k];
            out32_Q14 = SKP_LSHIFT(SKP_SMLAWB(S[SOff + 0], B_Q28[B_Q28Off + 0], inval), 2);

            S[SOff + 0] = S[SOff + 1] + SKP_RSHIFT_ROUND(SKP_SMULWB(out32_Q14, A0_L_Q28), 14);
            S[SOff + 0] = SKP_SMLAWB(S[SOff + 0], out32_Q14, A0_U_Q28);
            S[SOff + 0] = SKP_SMLAWB(S[SOff + 0], B_Q28[B_Q28Off + 1], inval);

            S[SOff + 1] = SKP_RSHIFT_ROUND(SKP_SMULWB(out32_Q14, A1_L_Q28), 14);
            S[SOff + 1] = SKP_SMLAWB(S[SOff + 1], out32_Q14, A1_U_Q28);
            S[SOff + 1] = SKP_SMLAWB(S[SOff + 1], B_Q28[B_Q28Off + 2], inval);

            /* Scale back to Q0 and saturate */
            @out[outOff + k] = (short)SKP_SAT16(SKP_RSHIFT(out32_Q14 + (1 << 14) - 1, 14));
        }
    }
}
