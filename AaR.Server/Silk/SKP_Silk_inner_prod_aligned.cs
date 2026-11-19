#nullable disable
// ***********************************************************************
// SKP_Silk_inner_prod_aligned.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_inner_prod_aligned.c
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
/// SKP_Silk_inner_prod_aligned.c ——（Copyright 2008-2010 (c), Skype Limited，Date: 080601）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件含与文件同名的
// SKP_Silk_inner_prod_aligned 函数。为保持函数名（跨代理咬合点）不变，
// 类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_inner_prod_aligned_impl
{
    /* sum= for(i=0;i<len;i++)inVec1[i]*inVec2[i];      ---        inner product    */
    /* Note for ARM asm:                                                            */
    /*        * inVec1 and inVec2 should be at least 2 byte aligned.    (Or defined as short/int16) */
    /*        * len should be positive 16bit integer.                               */
    /*        * only when len>6, memory access can be reduced by half.              */

    public static int SKP_Silk_inner_prod_aligned(
        short[] inVec1, int inVec1Off,     /*    I input vector 1    */
        short[] inVec2, int inVec2Off,     /*    I input vector 2    */
        int len                             /*    I vector lengths    */
    )
    {
        int i;
        int sum = 0;
        for (i = 0; i < len; i++)
        {
            sum = SKP_SMLABB(sum, inVec1[inVec1Off + i], inVec2[inVec2Off + i]);
        }
        return sum;
    }

    public static long SKP_Silk_inner_prod16_aligned_64(
        short[] inVec1, int inVec1Off,     /*    I input vector 1    */
        short[] inVec2, int inVec2Off,     /*    I input vector 2    */
        int len                             /*    I vector lengths    */
    )
    {
        int i;
        long sum = 0;
        for (i = 0; i < len; i++)
        {
            sum = SKP_SMLALBB(sum, inVec1[inVec1Off + i], inVec2[inVec2Off + i]);
        }
        return sum;
    }
}
