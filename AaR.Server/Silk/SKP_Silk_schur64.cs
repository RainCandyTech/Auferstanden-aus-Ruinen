#nullable disable
// ***********************************************************************
// SKP_Silk_schur64.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_schur64.c
// Copyright (c) 2006-2012, Skype Limited.（BSD 风格许可，保留原始版权声明）
//
// SKP_Silk_schur64.c —— 从相关序列计算反射系数（额外精度版）。
// Copyright 2008 (c), Skype Limited。Date: 080103。
// 主函数与文件同名（CS0542）→ 类名加 _impl 后缀，函数名保持。
// C 侧 `#if EMBEDDED_ARM<6`（默认构建恒真）只保留真分支。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_Inlines;

namespace AaR.Server.Silk;

internal static class SKP_Silk_schur64_impl
{
    // 比 schur() 慢，但更精确。使用 SMULL()。
    // 返回残差能量。
    public static int SKP_Silk_schur64(
        int[] rc_Q16,                  /* O: 反射系数 [order]，Q16 */
        int rc_Q16Off,
        int[] c,                       /* I: 相关 [order+1] */
        int cOff,
        int order)                     /* I: 预测阶数 */
    {
        int k, n;
        int[][] C = new int[SKP_Silk_MAX_ORDER_LPC + 1][];
        for (int i = 0; i < C.Length; i++) C[i] = new int[2];
        int Ctmp1_Q30, Ctmp2_Q30, rc_tmp_Q31;

        /* 非法输入检查 */
        if (c[cOff] <= 0)
        {
            SKP_memset(rc_Q16, rc_Q16Off, 0, order);
            return 0;
        }

        for (k = 0; k < order + 1; k++)
        {
            C[k][0] = C[k][1] = c[cOff + k];
        }

        for (k = 0; k < order; k++)
        {
            /* 反射系数：两个 Q30 值相除，结果 Q31 */
            rc_tmp_Q31 = SKP_DIV32_varQ(-C[k + 1][0], C[0][1], 31);

            /* 保存输出 */
            rc_Q16[rc_Q16Off + k] = SKP_RSHIFT_ROUND(rc_tmp_Q31, 15);

            /* 更新相关 */
            for (n = 0; n < order - k; n++)
            {
                Ctmp1_Q30 = C[n + k + 1][0];
                Ctmp2_Q30 = C[n][1];

                /* 乘加最高 int32 */
                C[n + k + 1][0] = Ctmp1_Q30 + SKP_SMMUL(SKP_LSHIFT(Ctmp2_Q30, 1), rc_tmp_Q31);
                C[n][1] = Ctmp2_Q30 + SKP_SMMUL(SKP_LSHIFT(Ctmp1_Q30, 1), rc_tmp_Q31);
            }
        }

        return C[0][1];
    }
}
