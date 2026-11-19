#nullable disable
// ***********************************************************************
// SKP_Silk_bwexpander.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_bwexpander.c
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

/// <summary>SKP_Silk_bwexpander.c —— Chirp (bandwidth expand) LP AR filter.</summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_bwexpander_impl
{
    /* Chirp (bandwidth expand) LP AR filter */
    public static void SKP_Silk_bwexpander(
        short[] ar, int arOff,   /* I/O  AR filter to be expanded (without leading 1)    */
        int d,                   /* I    Length of ar                                    */
        int chirp_Q16            /* I    Chirp factor (typically in the range 0 to 1)    */
    )
    {
        int i;
        int chirp_minus_one_Q16;

        chirp_minus_one_Q16 = chirp_Q16 - 65536;

        /* NB: Dont use SKP_SMULWB, instead of SKP_RSHIFT_ROUND( SKP_MUL() , 16 ), below. */
        /* Bias in SKP_SMULWB can lead to unstable filters                                */
        for (i = 0; i < d - 1; i++)
        {
            ar[arOff + i] = (short)SKP_RSHIFT_ROUND(SKP_MUL(chirp_Q16, ar[arOff + i]), 16);
            chirp_Q16 += SKP_RSHIFT_ROUND(SKP_MUL(chirp_Q16, chirp_minus_one_Q16), 16);
        }
        ar[arOff + d - 1] = (short)SKP_RSHIFT_ROUND(SKP_MUL(chirp_Q16, ar[arOff + d - 1]), 16);
    }
}
