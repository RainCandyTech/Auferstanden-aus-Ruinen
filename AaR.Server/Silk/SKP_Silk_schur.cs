#nullable disable
// ***********************************************************************
// SKP_Silk_schur.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_schur.c
// Copyright (c) 2006-2012, Skype Limited.（BSD 风格许可，保留原始版权声明）
//
// SKP_Silk_schur.c —— 从相关序列计算反射系数。
// Copyright 2008 (c), Skype Limited。Date: 080103。
// 主函数与文件同名（CS0542）→ 类名加 _impl 后缀，函数名保持。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;

namespace AaR.Server.Silk;

internal static class SKP_Silk_schur_impl
{
    // 比 schur64() 快，但精度低得多。使用 SMLAWB()（armv5E+）。
    // 返回残差能量。
    public static int SKP_Silk_schur(
        short[] rc_Q15,                 /* O: 反射系数 [order]，Q15 */
        int rc_Q15Off,
        int[] c,                       /* I: 相关 [order+1] */
        int cOff,
        int order)                     /* I: 预测阶数 */
    {
        int k, n, lz;
        int[][] C = new int[SKP_Silk_MAX_ORDER_LPC + 1][];
        for (int i = 0; i < C.Length; i++) C[i] = new int[2];
        int Ctmp1, Ctmp2, rc_tmp_Q15;

        /* 前导零个数 */
        lz = SKP_Silk_macros.SKP_Silk_CLZ32(c[cOff]);

        /* 复制相关值并把电平调到 Q30 */
        if (lz < 2)
        {
            /* lz 必为 1，右移一位 */
            for (k = 0; k < order + 1; k++)
            {
                C[k][0] = C[k][1] = SKP_RSHIFT(c[cOff + k], 1);
            }
        }
        else if (lz > 2)
        {
            /* 左移 */
            lz -= 2;
            for (k = 0; k < order + 1; k++)
            {
                C[k][0] = C[k][1] = SKP_LSHIFT(c[cOff + k], lz);
            }
        }
        else
        {
            /* 无需移位 */
            for (k = 0; k < order + 1; k++)
            {
                C[k][0] = C[k][1] = c[cOff + k];
            }
        }

        for (k = 0; k < order; k++)
        {
            /* 反射系数 */
            rc_tmp_Q15 = -SKP_DIV32_16(C[k + 1][0], SKP_max_32(SKP_RSHIFT(C[0][1], 15), 1));

            /* 截断（对条件良好的输入不应发生） */
            rc_tmp_Q15 = SKP_SAT16(rc_tmp_Q15);

            /* 存储 */
            rc_Q15[rc_Q15Off + k] = (short)rc_tmp_Q15;

            /* 更新相关 */
            for (n = 0; n < order - k; n++)
            {
                Ctmp1 = C[n + k + 1][0];
                Ctmp2 = C[n][1];
                C[n + k + 1][0] = SKP_SMLAWB(Ctmp1, SKP_LSHIFT(Ctmp2, 1), rc_tmp_Q15);
                C[n][1] = SKP_SMLAWB(Ctmp2, SKP_LSHIFT(Ctmp1, 1), rc_tmp_Q15);
            }
        }

        /* 返回残差能量 */
        return C[0][1];
    }
}
