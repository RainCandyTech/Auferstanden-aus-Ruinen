#nullable disable
// ***********************************************************************
// SKP_Silk_Inlines.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_Inlines.h
// Copyright (c) 2006-2012, Skype Limited.（BSD 风格许可，保留原始版权声明）
// ***********************************************************************

using System.Runtime.CompilerServices;

namespace AaR.Server.Silk;

/// <summary>SKP_Silk_Inlines.h 内联定点函数。</summary>
internal static class SKP_Silk_Inlines
{
    // 统计 64 位值的前导零
    public static int SKP_Silk_CLZ64(long @in)
    {
        int in_upper = (int)(@in >> 32);
        if (in_upper == 0)
        {
            /* 在低 32 位中查找 */
            return 32 + SKP_Silk_macros.SKP_Silk_CLZ32((int)@in);
        }
        else
        {
            /* 在高 32 位中查找 */
            return SKP_Silk_macros.SKP_Silk_CLZ32(in_upper);
        }
    }

    // 前导零个数 + 前导 1 后面的 7 位
    public static void SKP_Silk_CLZ_FRAC(int @in, out int lz, out int frac_Q7)
    {
        int lzeros = SKP_Silk_macros.SKP_Silk_CLZ32(@in);

        lz = lzeros;
        frac_Q7 = SKP_Silk_macros.SKP_ROR32(@in, 24 - lzeros) & 0x7f;
    }

    // 平方根近似
    // 精度：输出 > 15 时 < ±10%；输出 > 120 时 < ±2.5%
    public static int SKP_Silk_SQRT_APPROX(int x)
    {
        int y, lz, frac_Q7;

        if (x <= 0)
        {
            return 0;
        }

        SKP_Silk_CLZ_FRAC(x, out lz, out frac_Q7);

        if ((lz & 1) != 0)
        {
            y = 32768;
        }
        else
        {
            y = 46214;        /* 46214 = sqrt(2) * 32768 */
        }

        /* 修正尺度 */
        y >>= (lz >> 1);

        /* 用输入的分数部分增量 */
        y = SKP_Silk_macros.SKP_SMLAWB(y, y, SKP_Silk_macros.SKP_SMULBB(213, frac_Q7));

        return y;
    }

    // 16 位值溢出前可左移的位数（ITU 定义，norm(0)=0）
    public static int SKP_Silk_norm16(short a)
    {
        int a32;

        /* if ((a == 0) || (a == SKP_int16_MIN)) return(0); */
        if ((a << 1) == 0) return 0;

        a32 = a;
        /* if (a32 < 0) a32 = -a32 - 1; */
        a32 ^= a32 >> 31;

        return SKP_Silk_macros.SKP_Silk_CLZ32(a32) - 17;
    }

    // 32 位值溢出前可左移的位数（ITU 定义，norm(0)=0）
    public static int SKP_Silk_norm32(int a)
    {
        /* if ((a == 0) || (a == SKP_int32_MIN)) return(0); */
        if ((a << 1) == 0) return 0;

        /* if (a < 0) a = -a - 1; */
        a ^= a >> 31;

        return SKP_Silk_macros.SKP_Silk_CLZ32(a) - 1;
    }

    // 两 int32 相除，结果放指定 Q 域
    public static int SKP_DIV32_varQ(
        int a32,
        int b32,
        int Qres)
    {
        int a_headrm, b_headrm, lshift;
        int b32_inv, a32_nrm, b32_nrm, result;

        /* 计算头部空余位数并归一化输入 */
        a_headrm = SKP_Silk_macros.SKP_Silk_CLZ32(SKP_Silk_macros.SKP_abs(a32)) - 1;
        a32_nrm = SKP_Silk_macros.SKP_LSHIFT(a32, a_headrm);                    /* Q: a_headrm */
        b_headrm = SKP_Silk_macros.SKP_Silk_CLZ32(SKP_Silk_macros.SKP_abs(b32)) - 1;
        b32_nrm = SKP_Silk_macros.SKP_LSHIFT(b32, b_headrm);                    /* Q: b_headrm */

        /* b32 的倒数，14 位精度 */
        b32_inv = SKP_Silk_macros.SKP_DIV32_16(int.MaxValue >> 2, b32_nrm >> 16);  /* Q: 29 + 16 - b_headrm */

        /* 一阶近似 */
        result = SKP_Silk_macros.SKP_SMULWB(a32_nrm, b32_inv);                   /* Q: 29 + a_headrm - b_headrm */

        /* 减去分母与一阶近似之积得到残差 */
        a32_nrm -= SKP_Silk_macros.SKP_LSHIFT_ovflw(SKP_Silk_macros.SKP_SMMUL(b32_nrm, result), 3);  /* Q: a_headrm */

        /* 精化 */
        result = SKP_Silk_macros.SKP_SMLAWB(result, a32_nrm, b32_inv);           /* Q: 29 + a_headrm - b_headrm */

        /* 转换到 Qres 域 */
        lshift = 29 + a_headrm - b_headrm - Qres;
        if (lshift <= 0)
        {
            return SKP_Silk_macros.SKP_LSHIFT_SAT32(result, -lshift);
        }
        else
        {
            if (lshift < 32)
            {
                return SKP_Silk_macros.SKP_RSHIFT(result, lshift);
            }
            else
            {
                /* 避免未定义结果 */
                return 0;
            }
        }
    }

    // 求 int32 的倒数，结果放指定 Q 域
    public static int SKP_INVERSE32_varQ(
        int b32,
        int Qres)
    {
        int b_headrm, lshift;
        int b32_inv, b32_nrm, err_Q32, result;

        /* 计算头部空余位数并归一化输入 */
        b_headrm = SKP_Silk_macros.SKP_Silk_CLZ32(SKP_Silk_macros.SKP_abs(b32)) - 1;
        b32_nrm = SKP_Silk_macros.SKP_LSHIFT(b32, b_headrm);                    /* Q: b_headrm */

        /* b32 的倒数，14 位精度 */
        b32_inv = SKP_Silk_macros.SKP_DIV32_16(int.MaxValue >> 2, b32_nrm >> 16);  /* Q: 29 + 16 - b_headrm */

        /* 一阶近似 */
        result = SKP_Silk_macros.SKP_LSHIFT(b32_inv, 16);                       /* Q: 61 - b_headrm */

        /* 从 1 中减去分母与一阶近似之积得到残差 */
        err_Q32 = SKP_Silk_macros.SKP_LSHIFT_ovflw(-SKP_Silk_macros.SKP_SMULWB(b32_nrm, b32_inv), 3);   /* Q32 */

        /* 精化 */
        result = SKP_Silk_macros.SKP_SMLAWW(result, err_Q32, b32_inv);          /* Q: 61 - b_headrm */

        /* 转换到 Qres 域 */
        lshift = 61 - b_headrm - Qres;
        if (lshift <= 0)
        {
            return SKP_Silk_macros.SKP_LSHIFT_SAT32(result, -lshift);
        }
        else
        {
            if (lshift < 32)
            {
                return SKP_Silk_macros.SKP_RSHIFT(result, lshift);
            }
            else
            {
                /* 避免未定义结果 */
                return 0;
            }
        }
    }

    public const int SKP_SIN_APPROX_CONST0 = 1073735400;
    public const int SKP_SIN_APPROX_CONST1 = -82778932;
    public const int SKP_SIN_APPROX_CONST2 = 1059577;
    public const int SKP_SIN_APPROX_CONST3 = -5013;

    // 正弦近似；输入 65536 对应 2 * pi
    // 用输入的 0、2、4、6 次幂多项式展开，相对误差 < 1e-5
    public static int SKP_Silk_SIN_APPROX_Q24(int x)
    {
        int y_Q30;

        /* 只保留低 16 位（函数以 65536 为周期） */
        x &= 65535;

        /* 分四象限 */
        if (x <= 32768)
        {
            if (x < 16384)
            {
                /* 返回 cos(pi/2 - x) */
                x = 16384 - x;
            }
            else
            {
                /* 返回 cos(x - pi/2) */
                x -= 16384;
            }
            if (x < 1100)
            {
                /* 特例：高精度 */
                return SKP_Silk_macros.SKP_SMLAWB(1 << 24, SKP_Silk_macros.SKP_MUL(x, x), -5053);
            }
            x = SKP_Silk_macros.SKP_SMULWB(SKP_Silk_macros.SKP_LSHIFT(x, 8), x);       /* 含 x^2，Q20 */
            y_Q30 = SKP_Silk_macros.SKP_SMLAWB(SKP_SIN_APPROX_CONST2, x, SKP_SIN_APPROX_CONST3);
            y_Q30 = SKP_Silk_macros.SKP_SMLAWW(SKP_SIN_APPROX_CONST1, x, y_Q30);
            y_Q30 = SKP_Silk_macros.SKP_SMLAWW(SKP_SIN_APPROX_CONST0 + 66, x, y_Q30);
        }
        else
        {
            if (x < 49152)
            {
                /* 返回 -cos(3*pi/2 - x) */
                x = 49152 - x;
            }
            else
            {
                /* 返回 -cos(x - 3*pi/2) */
                x -= 49152;
            }
            if (x < 1100)
            {
                /* 特例：高精度 */
                return SKP_Silk_macros.SKP_SMLAWB(-(1 << 24), SKP_Silk_macros.SKP_MUL(x, x), 5053);
            }
            x = SKP_Silk_macros.SKP_SMULWB(SKP_Silk_macros.SKP_LSHIFT(x, 8), x);       /* 含 x^2，Q20 */
            y_Q30 = SKP_Silk_macros.SKP_SMLAWB(-SKP_SIN_APPROX_CONST2, x, -SKP_SIN_APPROX_CONST3);
            y_Q30 = SKP_Silk_macros.SKP_SMLAWW(-SKP_SIN_APPROX_CONST1, x, y_Q30);
            y_Q30 = SKP_Silk_macros.SKP_SMLAWW(-SKP_SIN_APPROX_CONST0, x, y_Q30);
        }
        return SKP_Silk_macros.SKP_RSHIFT_ROUND(y_Q30, 6);
    }

    // 余弦近似；输入 65536 对应 2 * pi，相对误差 < 1e-5
    public static int SKP_Silk_COS_APPROX_Q24(int x)
    {
        return SKP_Silk_SIN_APPROX_Q24(x + 16384);
    }
}
