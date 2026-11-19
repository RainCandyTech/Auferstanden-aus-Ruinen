#nullable disable
// ***********************************************************************
// SKP_Silk_MA.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_MA.c
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
/// SKP_Silk_MA.c —— Variable order MA filter.
///（Copyright 2006 (c), Skype Limited，Date: 060221）
/// </summary>
internal static class SKP_Silk_MA
{
    /* Variable order MA prediction error filter */
    // PORT-NOTE: 参数名 in/out 为 C# 上下文关键字，按原名以 @in/@out 转义；指针参数按 §3 改为数组 + 偏移
    public static void SKP_Silk_MA_Prediction(
        short[] @in, int inOff,        /* I:   Input signal                                */
        short[] B, int BOff,           /* I:   MA prediction coefficients, Q12 [order]     */
        int[] S, int SOff,             /* I/O: State vector [order]                        */
        short[] @out, int outOff,      /* O:   Output signal                               */
        int len,                       /* I:   Signal length                               */
        int order                      /* I:   Filter order                                */
    )
    {
        int k, d, in16;
        int out32;

        for (k = 0; k < len; k++)
        {
            in16 = @in[inOff + k];
            out32 = SKP_LSHIFT(in16, 12) - S[SOff + 0];
            out32 = SKP_RSHIFT_ROUND(out32, 12);

            for (d = 0; d < order - 1; d++)
            {
                S[SOff + d] = SKP_SMLABB_ovflw(S[SOff + d + 1], in16, B[BOff + d]);
            }
            S[SOff + order - 1] = SKP_SMULBB(in16, B[BOff + order - 1]);

            /* Limit */
            @out[outOff + k] = (short)SKP_SAT16(out32);
        }
    }

    // PORT-NOTE: 参数名 in/out 为 C# 上下文关键字，按原名以 @in/@out 转义；指针参数按 §3 改为数组 + 偏移
    public static void SKP_Silk_LPC_analysis_filter(
        short[] @in, int inOff,        /* I:   Input signal                                */
        short[] B, int BOff,           /* I:   MA prediction coefficients, Q12 [order]     */
        short[] S, int SOff,           /* I/O: State vector [order]                        */
        short[] @out, int outOff,      /* O:   Output signal                               */
        int len,                       /* I:   Signal length                               */
        int Order                      /* I:   Filter order                                */
    )
    {
        int k, j, idx, Order_half = SKP_RSHIFT(Order, 1);
        int out32_Q12, out32;
        short SA, SB;

        /* S[] values are in Q0 */
        for (k = 0; k < len; k++)
        {
            SA = S[SOff + 0];
            out32_Q12 = 0;
            for (j = 0; j < (Order_half - 1); j++)
            {
                idx = SKP_SMULBB(2, j) + 1;
                /* Multiply-add two prediction coefficients for each loop */
                SB = S[SOff + idx];
                S[SOff + idx] = SA;
                out32_Q12 = SKP_SMLABB(out32_Q12, SA, B[BOff + idx - 1]);
                out32_Q12 = SKP_SMLABB(out32_Q12, SB, B[BOff + idx]);
                SA = S[SOff + idx + 1];
                S[SOff + idx + 1] = SB;
            }

            /* Unrolled loop: epilog */
            SB = S[SOff + Order - 1];
            S[SOff + Order - 1] = SA;
            out32_Q12 = SKP_SMLABB(out32_Q12, SA, B[BOff + Order - 2]);
            out32_Q12 = SKP_SMLABB(out32_Q12, SB, B[BOff + Order - 1]);

            /* Subtract prediction */
            out32_Q12 = SKP_SUB_SAT32(SKP_LSHIFT((int)@in[inOff + k], 12), out32_Q12);

            /* Scale to Q0 */
            out32 = SKP_RSHIFT_ROUND(out32_Q12, 12);

            /* Saturate output */
            @out[outOff + k] = (short)SKP_SAT16(out32);

            /* Move input line */
            S[SOff + 0] = @in[inOff + k];
        }
    }
}
