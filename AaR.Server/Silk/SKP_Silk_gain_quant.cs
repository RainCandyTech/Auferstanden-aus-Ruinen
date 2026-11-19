#nullable disable
// ***********************************************************************
// SKP_Silk_gain_quant.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_gain_quant.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - 文件头宏 OFFSET / SCALE_Q16 / INV_SCALE_Q16 为预处理常量表达式，
//     移植为 const（引用 SKP_Silk_define 的 MIN_QGAIN_DB/MAX_QGAIN_DB/
//     N_LEVELS_QGAIN，求值结果与 C 编译期常量逐位一致）：
//       OFFSET        = 2176
//       SCALE_Q16     = 2420
//       INV_SCALE_Q16 = 1774269
//   - 引用 SKP_Silk_lin2log / SKP_Silk_log2lin（表簇之外的标量函数簇产出，
//     类名 = 文件名）。本文件不引用 SKP_Silk_TargetRate_table（C 源中无此引用）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): lin2log/log2lin 为对端簇"文件名=函数名"冲突符号：其类名带
// "_impl" 后缀、方法名保持 C 原名。
internal static class SKP_Silk_gain_quant
{
    private const int OFFSET = ((MIN_QGAIN_DB * 128) / 6 + 16 * 128);
    private const int SCALE_Q16 = (65536 * (N_LEVELS_QGAIN - 1)) / (((MAX_QGAIN_DB - MIN_QGAIN_DB) * 128) / 6);
    private const int INV_SCALE_Q16 = (65536 * (((MAX_QGAIN_DB - MIN_QGAIN_DB) * 128) / 6)) / (N_LEVELS_QGAIN - 1);

    /* Gain scalar quantization with hysteresis, uniform on log scale */
    public static void SKP_Silk_gains_quant(
        int[] ind, int indOff,                     /* O    gain indices                            */
        int[] gain_Q16, int gain_Q16Off,           /* I/O  gains (quantized out)                   */
        ref int prev_ind,                          /* I/O  last index in previous frame            */
        int conditional                            /* I    first gain is delta coded if 1          */
    )
    {
        int k;

        for (k = 0; k < NB_SUBFR; k++)
        {
            /* Add half of previous quantization error, convert to log scale, scale, floor() */
            // PORT-NOTE: 对端簇 CS0542 冲突符号（类名 _impl 后缀，方法名保持 C 原名）。
            // 按工程约定调用点同样加 "_impl"。
            ind[indOff + k] = SKP_SMULWB(SCALE_Q16,
                SKP_Silk_lin2log_impl.SKP_Silk_lin2log(gain_Q16[gain_Q16Off + k]) - OFFSET);

            /* Round towards previous quantized gain (hysteresis) */
            if (ind[indOff + k] < prev_ind)
            {
                ind[indOff + k]++;
            }

            /* Compute delta indices and limit */
            if (k == 0 && conditional == 0)
            {
                /* Full index */
                ind[indOff + k] = SKP_LIMIT_int(ind[indOff + k], 0, N_LEVELS_QGAIN - 1);
                ind[indOff + k] = SKP_max_int(ind[indOff + k], prev_ind + MIN_DELTA_GAIN_QUANT);
                prev_ind = ind[indOff + k];
            }
            else
            {
                /* Delta index */
                ind[indOff + k] = SKP_LIMIT_int(ind[indOff + k] - prev_ind,
                    MIN_DELTA_GAIN_QUANT, MAX_DELTA_GAIN_QUANT);
                /* Accumulate deltas */
                prev_ind += ind[indOff + k];
                /* Shift to make non-negative */
                ind[indOff + k] -= MIN_DELTA_GAIN_QUANT;
            }

            /* Convert to linear scale and scale */
            gain_Q16[gain_Q16Off + k] = SKP_Silk_log2lin_impl.SKP_Silk_log2lin(
                SKP_min_32(SKP_SMULWB(INV_SCALE_Q16, prev_ind) + OFFSET, 3967)); /* 3968 = 31 in Q7 */
        }
    }

    /* Gains scalar dequantization, uniform on log scale */
    public static void SKP_Silk_gains_dequant(
        int[] gain_Q16, int gain_Q16Off,           /* O    quantized gains                         */
        int[] ind, int indOff,                     /* I    gain indices                            */
        ref int prev_ind,                          /* I/O  last index in previous frame            */
        int conditional                            /* I    first gain is delta coded if 1          */
    )
    {
        int k;

        for (k = 0; k < NB_SUBFR; k++)
        {
            if (k == 0 && conditional == 0)
            {
                prev_ind = ind[indOff + k];
            }
            else
            {
                /* Delta index */
                prev_ind += ind[indOff + k] + MIN_DELTA_GAIN_QUANT;
            }

            /* Convert to linear scale and scale */
            gain_Q16[gain_Q16Off + k] = SKP_Silk_log2lin_impl.SKP_Silk_log2lin(
                SKP_min_32(SKP_SMULWB(INV_SCALE_Q16, prev_ind) + OFFSET, 3967)); /* 3968 = 31 in Q7 */
        }
    }
}
