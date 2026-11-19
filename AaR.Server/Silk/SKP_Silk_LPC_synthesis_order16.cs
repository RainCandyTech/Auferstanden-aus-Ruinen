#nullable disable
// ***********************************************************************
// SKP_Silk_LPC_synthesis_order16.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_LPC_synthesis_order16.c
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
/// SKP_Silk_LPC_synthesis_order16.c —— Coefficients are in Q12.
/// 16th order AR filter.
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_LPC_synthesis_order16_impl
{
    /* 16th order AR filter */
    // PORT-NOTE: 参数名 in/out 为 C# 上下文关键字，按原名以 @in/@out 转义；指针参数按 §3 改为数组 + 偏移。
    // A_align_Q12 的打包为元素级表达式，不依赖内存布局；
    // _SYSTEM_IS_BIG_ENDIAN 分支按 §7 不移植（默认 gcc 构建为小端路径）。
    public static void SKP_Silk_LPC_synthesis_order16(
        short[] @in, int inOff,            /* I:   excitation signal */
        short[] A_Q12, int A_Q12Off,       /* I:   AR coefficients [16], between -8_Q0 and 8_Q0 */
        int Gain_Q26,                      /* I:   gain */
        int[] S, int SOff,                 /* I/O: state vector [16] */
        short[] @out, int outOff,          /* O:   output signal */
        int len                           /* I:   signal length, must be multiple of 16 */
    )
    {
        int k;
        int SA, SB, out32_Q10, out32;
        int Atmp;
        int[] A_align_Q12 = new int[8];

        /* combine two A_Q12 values and ensure 32-bit alignment */
        for (k = 0; k < 8; k++)
        {
            A_align_Q12[k] = (((int)A_Q12[A_Q12Off + 2 * k]) & 0x0000ffff) | SKP_LSHIFT((int)A_Q12[A_Q12Off + 2 * k + 1], 16);
        }
        /* S[] values are in Q14 */
        /* NOTE: the code below loads two int16 values in an int32, and multiplies each using the   */
        /* SMLAWB and SMLAWT instructions. On a big-endian CPU the two int16 variables would be     */
        /* loaded in reverse order and the code will give the wrong result. In that case swapping   */
        /* the SMLAWB and SMLAWT instructions should solve the problem.                             */
        for (k = 0; k < len; k++)
        {
            /* unrolled loop: prolog */
            /* multiply-add two prediction coefficients per iteration */
            SA = S[SOff + 15];
            Atmp = A_align_Q12[0];
            SB = S[SOff + 14];
            S[SOff + 14] = SA;
            out32_Q10 = SKP_SMULWB(SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);
            SA = S[SOff + 13];
            S[SOff + 13] = SB;

            /* unrolled loop: main loop */
            Atmp = A_align_Q12[1];
            SB = S[SOff + 12];
            S[SOff + 12] = SA;
            out32_Q10 = SKP_SMLAWB_ovflw(out32_Q10, SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);
            SA = S[SOff + 11];
            S[SOff + 11] = SB;

            Atmp = A_align_Q12[2];
            SB = S[SOff + 10];
            S[SOff + 10] = SA;
            out32_Q10 = SKP_SMLAWB_ovflw(out32_Q10, SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);
            SA = S[SOff + 9];
            S[SOff + 9] = SB;

            Atmp = A_align_Q12[3];
            SB = S[SOff + 8];
            S[SOff + 8] = SA;
            out32_Q10 = SKP_SMLAWB_ovflw(out32_Q10, SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);
            SA = S[SOff + 7];
            S[SOff + 7] = SB;

            Atmp = A_align_Q12[4];
            SB = S[SOff + 6];
            S[SOff + 6] = SA;
            out32_Q10 = SKP_SMLAWB_ovflw(out32_Q10, SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);
            SA = S[SOff + 5];
            S[SOff + 5] = SB;

            Atmp = A_align_Q12[5];
            SB = S[SOff + 4];
            S[SOff + 4] = SA;
            out32_Q10 = SKP_SMLAWB_ovflw(out32_Q10, SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);
            SA = S[SOff + 3];
            S[SOff + 3] = SB;

            Atmp = A_align_Q12[6];
            SB = S[SOff + 2];
            S[SOff + 2] = SA;
            out32_Q10 = SKP_SMLAWB_ovflw(out32_Q10, SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);
            SA = S[SOff + 1];
            S[SOff + 1] = SB;

            /* unrolled loop: epilog */
            Atmp = A_align_Q12[7];
            SB = S[SOff + 0];
            S[SOff + 0] = SA;
            out32_Q10 = SKP_SMLAWB_ovflw(out32_Q10, SA, Atmp);
            out32_Q10 = SKP_SMLAWT_ovflw(out32_Q10, SB, Atmp);

            /* unrolled loop: end */
            /* apply gain to excitation signal and add to prediction */
            out32_Q10 = SKP_ADD_SAT32(out32_Q10, SKP_SMULWB(Gain_Q26, @in[inOff + k]));

            /* scale to Q0 */
            out32 = SKP_RSHIFT_ROUND(out32_Q10, 10);

            /* saturate output */
            @out[outOff + k] = (short)SKP_SAT16(out32);

            /* move result into delay line */
            S[SOff + 15] = SKP_LSHIFT_SAT32(out32_Q10, 4);
        }
    }
}
