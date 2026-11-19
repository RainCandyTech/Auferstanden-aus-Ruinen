#nullable disable
// ***********************************************************************
// SKP_Silk_macros.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_macros.h + src/SKP_Silk_SigProc_FIX.h 的宏层
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明，见文件尾。）
// C# 移植说明：宏 → AggressiveInlining 静态方法；语义与 C 宏逐表达式对应；
//   有符号溢出按二补回绕（unchecked），与 gcc -O3 实际行为一致。
// ***********************************************************************

using System.Runtime.CompilerServices;

namespace AaR.Server.Silk;

/// <summary>SKP_Silk_macros.h + SigProc_FIX.h 宏层（定点运算基元）。</summary>
internal static class SKP_Silk_macros
{
    // (a32 * (SKP_int32)((SKP_int16)(b32))) >> 16，输出为 32 位 int
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMULWB(int a32, int b32)
    {
        int b = (int)(short)b32;
        return ((a32 >> 16) * b) + (((a32 & 0x0000FFFF) * b) >> 16);
    }

    // a32 + ((b32 * (SKP_int32)((SKP_int16)(c32))) >> 16)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLAWB(int a32, int b32, int c32)
    {
        int c = (int)(short)c32;
        return a32 + (((b32 >> 16) * c) + (((b32 & 0x0000FFFF) * c) >> 16));
    }

    // (a32 * (b32 >> 16)) >> 16
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMULWT(int a32, int b32)
    {
        int bh = b32 >> 16;
        return ((a32 >> 16) * bh) + (((a32 & 0x0000FFFF) * bh) >> 16);
    }

    // a32 + ((b32 * (c32 >> 16)) >> 16)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLAWT(int a32, int b32, int c32)
    {
        int ch = c32 >> 16;
        return a32 + (((b32 >> 16) * ch) + (((b32 & 0x0000FFFF) * ch) >> 16));
    }

    // (SKP_int32)((SKP_int16)(a32)) * (SKP_int32)((SKP_int16)(b32))
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMULBB(int a32, int b32)
    {
        return (int)(short)a32 * (int)(short)b32;
    }

    // a32 + (SKP_int32)((SKP_int16)(b32)) * (SKP_int32)((SKP_int16)(c32))
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLABB(int a32, int b32, int c32)
    {
        return a32 + (int)(short)b32 * (int)(short)c32;
    }

    // (SKP_int32)((SKP_int16)(a32)) * (b32 >> 16)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMULBT(int a32, int b32)
    {
        return (int)(short)a32 * (b32 >> 16);
    }

    // a32 + (SKP_int32)((SKP_int16)(b32)) * (c32 >> 16)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLABT(int a32, int b32, int c32)
    {
        return a32 + (int)(short)b32 * (c32 >> 16);
    }

    // a64 + b32 * c32
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SKP_SMLAL(long a64, int b32, int c32)
    {
        return a64 + (long)b32 * (long)c32;
    }

    // (a32 * b32) >> 16（两级近似）
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMULWW(int a32, int b32)
    {
        return SKP_SMULWB(a32, b32) + a32 * SKP_RSHIFT_ROUND(b32, 16);
    }

    // a32 + ((b32 * c32) >> 16)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLAWW(int a32, int b32, int c32)
    {
        return SKP_SMLAWB(a32, b32, c32) + b32 * SKP_RSHIFT_ROUND(c32, 16);
    }

    // (SKP_int32)(((SKP_int64)a32 * b32) >> 32) —— 高 32 位乘
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMMUL(int a32, int b32)
    {
        return unchecked((int)(((long)a32 * (long)b32) >> 32));
    }

    // 带饱和加减法（SKP_Silk_macros.h）
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD_SAT32(int a, int b)
    {
        if (((a + b) & unchecked((int)0x80000000)) == 0)
        {
            return ((a & b) & unchecked((int)0x80000000)) != 0 ? int.MinValue : a + b;
        }
        else
        {
            return ((a | b) & unchecked((int)0x80000000)) == 0 ? int.MaxValue : a + b;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SUB_SAT32(int a, int b)
    {
        if (((a - b) & unchecked((int)0x80000000)) == 0)
        {
            return (a & (b ^ unchecked((int)0x80000000)) & unchecked((int)0x80000000)) != 0
                ? int.MinValue : a - b;
        }
        else
        {
            return ((a ^ unchecked((int)0x80000000)) & b & unchecked((int)0x80000000)) != 0
                ? int.MaxValue : a - b;
        }
    }

    // 带饱和的 16 位加法
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short SKP_ADD_SAT16(short a, int b)
    {
        int t = (int)a + b;
        if (t > short.MaxValue) return short.MaxValue;
        if (t < short.MinValue) return short.MinValue;
        return (short)t;
    }

    // ======================= CLZ（macros.h 中的内联函数） =======================

    public static int SKP_Silk_CLZ16(short in16v)
    {
        int x = in16v;
        int out32 = 0;
        if (x == 0)
        {
            return 16;
        }
        /* test nibbles */
        if ((x & 0xFF00) != 0)
        {
            if ((x & 0xF000) != 0)
            {
                x >>= 12;
            }
            else
            {
                out32 += 4;
                x >>= 8;
            }
        }
        else
        {
            if ((x & 0xFFF0) != 0)
            {
                out32 += 8;
                x >>= 4;
            }
            else
            {
                out32 += 12;
            }
        }
        /* test bits and return */
        if ((x & 0xC) != 0)
        {
            if ((x & 0x8) != 0)
                return out32 + 0;
            else
                return out32 + 1;
        }
        else
        {
            if ((x & 0xE) != 0)
                return out32 + 2;
            else
                return out32 + 3;
        }
    }

    public static int SKP_Silk_CLZ32(int in32)
    {
        /* test highest 16 bits and convert to SKP_int16 */
        if ((in32 & 0xFFFF0000) != 0)
        {
            return SKP_Silk_CLZ16((short)(in32 >> 16));
        }
        else
        {
            return SKP_Silk_CLZ16((short)in32) + 16;
        }
    }

    // ======================= SigProc_FIX.h 宏层 =======================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_MUL(int a32, int b32) { return a32 * b32; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_MUL_uint(int a32, int b32) { return a32 * b32; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_MLA(int a32, int b32, int c32) { return a32 + b32 * c32; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMULTT(int a32, int b32) { return (a32 >> 16) * (b32 >> 16); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLATT(int a32, int b32, int c32) { return a32 + (b32 >> 16) * (c32 >> 16); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SKP_SMLALBB(long a64, short b16, short c16)
    {
        return a64 + (long)(int)b16 * (long)(int)c16;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SKP_SMULL(int a32, int b32) { return (long)a32 * (long)b32; }

    // 允许溢出的加减（二补回绕）
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD32_ovflw(int a, int b) { return a + b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SUB32_ovflw(int a, int b) { return a - b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_MLA_ovflw(int a32, int b32, int c32) { return a32 + b32 * c32; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLABB_ovflw(int a32, int b32, int c32) { return a32 + SKP_SMULBB(b32, c32); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLATT_ovflw(int a32, int b32, int c32) { return a32 + SKP_SMULTT(b32, c32); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLAWB_ovflw(int a32, int b32, int c32) { return a32 + SKP_SMULWB(b32, c32); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SMLAWT_ovflw(int a32, int b32, int c32) { return a32 + SKP_SMULWT(b32, c32); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_DIV32_16(int a32, int b16) { return a32 / b16; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_DIV32(int a32, int b32) { return a32 / b32; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD32(int a, int b) { return a + b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SKP_ADD64(long a, long b) { return a + b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SUB32(int a, int b) { return a - b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short SKP_SAT16(int a)
    {
        return (short)(a > short.MaxValue ? short.MaxValue : (a < short.MinValue ? short.MinValue : a));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SAT32(int a)
    {
        return a > int.MaxValue ? int.MaxValue : (a < int.MinValue ? int.MinValue : a);
    }

    // 正输入带饱和加法
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD_POS_SAT32(int a, int b)
    {
        return ((a + b) & unchecked((int)0x80000000)) != 0 ? int.MaxValue : a + b;
    }

    // 移位
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LSHIFT32(int a, int shift) { return a << shift; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SKP_LSHIFT64(long a, int shift) { return a << shift; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LSHIFT(int a, int shift) { return a << shift; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_RSHIFT32(int a, int shift) { return a >> shift; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SKP_RSHIFT64(long a, int shift) { return a >> shift; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_RSHIFT(int a, int shift) { return a >> shift; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LSHIFT_ovflw(int a, int shift) { return a << shift; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LSHIFT_uint(int a, int shift) { return (int)((uint)a << shift); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_RSHIFT_uint(int a, int shift) { return (int)((uint)a >> shift); }

    // 先饱和再左移
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LSHIFT_SAT32(int a, int shift)
    {
        return SKP_LIMIT_32(a, int.MinValue >> shift, int.MaxValue >> shift) << shift;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD_LSHIFT(int a, int b, int shift) { return a + (b << shift); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD_LSHIFT32(int a, int b, int shift) { return a + (b << shift); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD_RSHIFT(int a, int b, int shift) { return a + (b >> shift); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD_RSHIFT32(int a, int b, int shift) { return a + (b >> shift); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ADD_RSHIFT_uint(int a, int b, int shift) { return (int)((uint)a + ((uint)b >> shift)); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SUB_LSHIFT32(int a, int b, int shift) { return a - (b << shift); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_SUB_RSHIFT32(int a, int b, int shift) { return a - (b >> shift); }

    // 舍入右移（shift > 0）
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_RSHIFT_ROUND(int a, int shift)
    {
        return shift == 1 ? (a >> 1) + (a & 1) : (((a >> (shift - 1)) + 1) >> 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long SKP_RSHIFT_ROUND64(long a, int shift)
    {
        return shift == 1 ? (a >> 1) + (a & 1) : (((a >> (shift - 1)) + 1) >> 1);
    }

    // 32x32 乘积需要右移的位数（宏，用下述方法代替）
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_NSHIFT_MUL_32_32(int a, int b)
    {
        return -(31 - (32 - SKP_Silk_CLZ32(SKP_abs(a)) + (32 - SKP_Silk_CLZ32(SKP_abs(b)))));
    }

    // min/max
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_min(int a, int b) { return a < b ? a : b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_max(int a, int b) { return a > b ? a : b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_min_int(int a, int b) { return a < b ? a : b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_min_32(int a, int b) { return a < b ? a : b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_max_int(int a, int b) { return a > b ? a : b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short SKP_max_16(short a, short b) { return a > b ? a : b; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_max_32(int a, int b) { return a > b ? a : b; }

    // 三段限幅（limit1 与 limit2 无序）
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LIMIT(int a, int limit1, int limit2)
    {
        return limit1 > limit2
            ? (a > limit1 ? limit1 : (a < limit2 ? limit2 : a))
            : (a > limit2 ? limit2 : (a < limit1 ? limit1 : a));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LIMIT_int(int a, int limit1, int limit2) { return SKP_LIMIT(a, limit1, limit2); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_LIMIT_32(int a, int limit1, int limit2) { return SKP_LIMIT(a, limit1, limit2); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_abs(int a) { return a > 0 ? a : -a; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_abs_int32(int a) { return (a ^ (a >> 31)) - (a >> 31); }

    // 伪随机数生成器
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_RAND(int seed)
    {
        return (int)(907633515u + (uint)seed * 196314165u);
    }

    // 浮点常量 → 定点常量：C 语义为 float 乘（int64→float 精确）再与 double 0.5 相加后截断
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_FIX_CONST(float c, int q)
    {
        float prod = c * (float)(1L << q);
        return (int)((double)prod + 0.5);
    }

    // ======================= ROR（SigProc_FIX.h） =======================

    // 右环移；rot 为负时左移。SDK 只以非零 rot 调用。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SKP_ROR32(int a32, int rot)
    {
        uint x = (uint)a32;
        if (rot <= 0)
        {
            uint m = (uint)(-rot);
            return (int)((x << (int)m) | (x >> (int)(32 - m)));
        }
        else
        {
            uint r = (uint)rot;
            return (int)((x << (int)(32 - r)) | (x >> (int)r));
        }
    }

    // ======================= memcpy / memset 助手（保持调用点贴近 C） =======================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memcpy(short[] d, int di, short[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memcpy(int[] d, int di, int[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memcpy(byte[] d, int di, byte[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memcpy(sbyte[] d, int di, sbyte[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memcpy(long[] d, int di, long[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memcpy(ushort[] d, int di, ushort[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memmove(short[] d, int di, short[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memmove(int[] d, int di, int[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memmove(byte[] d, int di, byte[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memmove(sbyte[] d, int di, sbyte[] s, int si, int n) { Array.Copy(s, si, d, di, n); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memset(short[] d, int di, short value, int n)
    {
        for (int i = 0; i < n; i++) d[di + i] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memset(int[] d, int di, int value, int n)
    {
        for (int i = 0; i < n; i++) d[di + i] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memset(byte[] d, int di, byte value, int n)
    {
        for (int i = 0; i < n; i++) d[di + i] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_memset(sbyte[] d, int di, sbyte value, int n)
    {
        for (int i = 0; i < n; i++) d[di + i] = value;
    }
}

// ***********************************************************************
// 原始版权声明（Skype Limited，BSD 风格）：
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.
// Redistribution and use in source and binary forms, with or without
// modification, (subject to the limitations in the disclaimer below)
// are permitted provided that the following conditions are met:
// - Redistributions of source code must retain the above copyright notice,
//   this list of conditions and the following disclaimer.
// - Redistributions in binary form must reproduce the above copyright
//   notice, this list of conditions and the following disclaimer in the
//   documentation and/or other materials provided with the distribution.
// - Neither the name of Skype Limited, nor the names of specific
//   contributors, may be used to endorse or promote products derived from
//   this software without specific prior written permission.
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
