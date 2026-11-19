#nullable disable
// ***********************************************************************
// SKP_Silk_sum_sqr_shift.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_sum_sqr_shift.c
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
/// SKP_Silk_sum_sqr_shift.c —— compute number of bits to right shift the
/// sum of squares of a vector of int16s to make it fit in an int32.
///（Copyright 2006-2008 (c), Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀（与工作区其他簇对该文件的引用约定一致）。
internal static class SKP_Silk_sum_sqr_shift_impl
{
    /* Compute number of bits to right shift the sum of squares of a vector */
    /* of int16s to make it fit in an int32                                 */
    public static void SKP_Silk_sum_sqr_shift(
        out int energy,                 /* O    Energy of x, after shifting to the right            */
        out int shift,                  /* O    Number of bits right shift applied to energy        */
        short[] x, int xOff,            /* I    Input vector                                        */
        int len                         /* I    Length of input vector                              */
    )
    {
        int i, shft;
        int in32, nrg_tmp, nrg;

        // PORT-NOTE: 原 C 代码为 if( (SKP_int32)( (SKP_int_ptr_size)x & 2 ) != 0 )，
        // 即根据指针原始地址是否 4 字节对齐决定是否先单独处理一个样本。托管数组下无法
        // 表达原始地址；参考 gcc x86-64 构建中所有调用点传入的都是 4 字节对齐缓冲
        // （偏移均为偶数元素），该未对齐分支为死代码，故固定走对齐路径：nrg = 0; i = 0;
        nrg = 0;
        i = 0;

        shft = 0;
        len--;
        while (i < len)
        {
            /* Load two values at once */
            // PORT-NOTE: C 代码 in32 = *( (SKP_int32 *)&x[ i ] ) 一次读两个 int16。
            // 小端内存布局下低 16 位 = x[i]、高 16 位 = x[i+1]，展开为等价表达式；
            // SKP_SMULBB/SKP_SMLABB_ovflw 取其低 16 位、SKP_SMLATT_ovflw 取其高 16 位，与 C 逐位一致。
            in32 = ((int)x[xOff + i + 1] << 16) | (int)(ushort)x[xOff + i];
            nrg = SKP_SMLABB_ovflw(nrg, in32, in32);
            nrg = SKP_SMLATT_ovflw(nrg, in32, in32);
            i += 2;
            if (nrg < 0)
            {
                /* Scale down */
                nrg = SKP_RSHIFT_uint(nrg, 2);
                shft = 2;
                break;
            }
        }
        for (; i < len; i += 2)
        {
            /* Load two values at once */
            in32 = ((int)x[xOff + i + 1] << 16) | (int)(ushort)x[xOff + i];
            nrg_tmp = SKP_SMULBB(in32, in32);
            nrg_tmp = SKP_SMLATT_ovflw(nrg_tmp, in32, in32);
            nrg = SKP_ADD_RSHIFT_uint(nrg, nrg_tmp, shft);
            if (nrg < 0)
            {
                /* Scale down */
                nrg = SKP_RSHIFT_uint(nrg, 2);
                shft += 2;
            }
        }
        if (i == len)
        {
            /* One sample left to process */
            nrg_tmp = SKP_SMULBB(x[xOff + i], x[xOff + i]);
            nrg = SKP_ADD_RSHIFT_uint(nrg, nrg_tmp, shft);
        }

        /* Make sure to have at least one extra leading zero (two leading zeros in total) */
        if ((nrg & unchecked((int)0xC0000000)) != 0)
        {
            nrg = SKP_RSHIFT_uint(nrg, 2);
            shft += 2;
        }

        /* Output arguments */
        shift = shft;
        energy = nrg;
    }
}
