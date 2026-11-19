#nullable disable
// ***********************************************************************
// SKP_Silk_encode_pulses.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_encode_pulses.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - combine_and_check 为 C 文件内 SKP_INLINE 函数 → AggressiveInlining。
//   - 指针 abs_pulses_ptr / pulses_ptr 改为局部偏移量（步进 +=）。
//   - 引用表（表簇 SKP_Silk_tables_pulses_per_block.cs 产出，裸名）：
//       SKP_Silk_max_pulses_table、SKP_Silk_pulses_per_block_BITS_Q6（short[][]）、
//       SKP_Silk_rate_levels_BITS_Q6（short[][]）、SKP_Silk_pulses_per_block_CDF（ushort[][]）、
//       SKP_Silk_lsb_CDF（tables_other 产出，ushort[]）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tables_pulses_per_block;  // 脉冲/码率级/shell 表（表簇产出，裸名）
using static AaR.Server.Silk.SKP_Silk_tables_other;              // SKP_Silk_lsb_CDF

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): C 函数名与所在文件同名，按全工程统一约定：类名加 "_impl"
// 后缀、方法名保持 C 原名；调用方以 `using static ...SKP_Silk_encode_pulses_impl;`
// + 裸名 SKP_Silk_encode_pulses(...) 调用。
internal static class SKP_Silk_encode_pulses_impl
{
    // PORT-NOTE: C 的 SKP_int32_MAX（interface/SKP_Silk_typedef.h，地基未含），值逐位一致。
    private const int SKP_int32_MAX = 2147483647; /* 0x7FFFFFFF */

    /*********************************************/
    /* Encode quantization indices of excitation */
    /*********************************************/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int combine_and_check(          /* return ok */
        int[] pulses_comb, int pulses_combOff,    /* O */
        int[] pulses_in, int pulses_inOff,        /* I */
        int max_pulses,                           /* I    max value for sum of pulses */
        int len                                   /* I    number of output values */
    )
    {
        int k, sum;

        for (k = 0; k < len; k++)
        {
            sum = pulses_in[pulses_inOff + 2 * k] + pulses_in[pulses_inOff + 2 * k + 1];
            if (sum > max_pulses)
            {
                return 1;
            }
            pulses_comb[pulses_combOff + k] = sum;
        }

        return 0;
    }

    /* Encode quantization indices of excitation */
    public static void SKP_Silk_encode_pulses(
        SKP_Silk_range_coder_state psRC,           /* I/O  Range coder state               */
        int sigtype,                              /* I    Sigtype                         */
        int QuantOffsetType,                      /* I    QuantOffsetType                 */
        sbyte[] q, int qOff,                      /* I    quantization indices            */
        int frame_length                          /* I    Frame length                    */
    )
    {
        int i, k, j, iter, bit, nLS, scale_down, RateLevelIndex = 0;
        int abs_q, minSumBits_Q6, sumBits_Q6;
        int[] abs_pulses = new int[MAX_FRAME_LENGTH];
        int[] sum_pulses = new int[MAX_NB_SHELL_BLOCKS];
        int[] nRshifts = new int[MAX_NB_SHELL_BLOCKS];
        int[] pulses_comb = new int[8];
        int abs_pulses_ptr;                        /* C 指针 → 偏移 */
        int pulses_ptr;                            /* C 指针 → 偏移 */
        ushort[] cdf_ptr;
        short[] nBits_ptr;

        SKP_memset(pulses_comb, 0, 0, 8); // Fixing Valgrind reported problem

        /****************************/
        /* Prepare for shell coding */
        /****************************/
        /* Calculate number of shell blocks */
        iter = frame_length / SHELL_CODEC_FRAME_LENGTH;

        /* Take the absolute value of the pulses */
        for (i = 0; i < frame_length; i += 4)
        {
            abs_pulses[i + 0] = (int)SKP_abs(q[qOff + i + 0]);
            abs_pulses[i + 1] = (int)SKP_abs(q[qOff + i + 1]);
            abs_pulses[i + 2] = (int)SKP_abs(q[qOff + i + 2]);
            abs_pulses[i + 3] = (int)SKP_abs(q[qOff + i + 3]);
        }

        /* Calc sum pulses per shell code frame */
        abs_pulses_ptr = 0;
        for (i = 0; i < iter; i++)
        {
            nRshifts[i] = 0;

            while (true)
            {
                /* 1+1 -> 2 */
                scale_down = combine_and_check(pulses_comb, 0, abs_pulses, abs_pulses_ptr,
                    SKP_Silk_max_pulses_table[0], 8);

                /* 2+2 -> 4 */
                scale_down += combine_and_check(pulses_comb, 0, pulses_comb, 0,
                    SKP_Silk_max_pulses_table[1], 4);

                /* 4+4 -> 8 */
                scale_down += combine_and_check(pulses_comb, 0, pulses_comb, 0,
                    SKP_Silk_max_pulses_table[2], 2);

                /* 8+8 -> 16 */
                sum_pulses[i] = pulses_comb[0] + pulses_comb[1];
                if (sum_pulses[i] > SKP_Silk_max_pulses_table[3])
                {
                    scale_down++;
                }

                if (scale_down != 0)
                {
                    /* We need to down scale the quantization signal */
                    nRshifts[i]++;
                    for (k = 0; k < SHELL_CODEC_FRAME_LENGTH; k++)
                    {
                        abs_pulses[abs_pulses_ptr + k] = SKP_RSHIFT(abs_pulses[abs_pulses_ptr + k], 1);
                    }
                }
                else
                {
                    /* Jump out of while(1) loop and go to next shell coding frame */
                    break;
                }
            }
            abs_pulses_ptr += SHELL_CODEC_FRAME_LENGTH;
        }

        /**************/
        /* Rate level */
        /**************/
        /* find rate level that leads to fewest bits for coding of pulses per block info */
        minSumBits_Q6 = SKP_int32_MAX;
        for (k = 0; k < N_RATE_LEVELS - 1; k++)
        {
            nBits_ptr = SKP_Silk_pulses_per_block_BITS_Q6[k];
            sumBits_Q6 = SKP_Silk_rate_levels_BITS_Q6[sigtype][k];
            for (i = 0; i < iter; i++)
            {
                if (nRshifts[i] > 0)
                {
                    sumBits_Q6 += nBits_ptr[MAX_PULSES + 1];
                }
                else
                {
                    sumBits_Q6 += nBits_ptr[sum_pulses[i]];
                }
            }
            if (sumBits_Q6 < minSumBits_Q6)
            {
                minSumBits_Q6 = sumBits_Q6;
                RateLevelIndex = k;
            }
        }
        SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, RateLevelIndex,
            SKP_Silk_rate_levels_CDF[sigtype], 0);

        /***************************************************/
        /* Sum-Weighted-Pulses Encoding                    */
        /***************************************************/
        cdf_ptr = SKP_Silk_pulses_per_block_CDF[RateLevelIndex];
        for (i = 0; i < iter; i++)
        {
            if (nRshifts[i] == 0)
            {
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, sum_pulses[i], cdf_ptr, 0);
            }
            else
            {
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, MAX_PULSES + 1, cdf_ptr, 0);
                for (k = 0; k < nRshifts[i] - 1; k++)
                {
                    SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, MAX_PULSES + 1,
                        SKP_Silk_pulses_per_block_CDF[N_RATE_LEVELS - 1], 0);
                }
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, sum_pulses[i],
                    SKP_Silk_pulses_per_block_CDF[N_RATE_LEVELS - 1], 0);
            }
        }

        /******************/
        /* Shell Encoding */
        /******************/
        for (i = 0; i < iter; i++)
        {
            if (sum_pulses[i] > 0)
            {
                SKP_Silk_shell_coder.SKP_Silk_shell_encoder(psRC, abs_pulses,
                    i * SHELL_CODEC_FRAME_LENGTH);
            }
        }

        /****************/
        /* LSB Encoding */
        /****************/
        for (i = 0; i < iter; i++)
        {
            if (nRshifts[i] > 0)
            {
                pulses_ptr = qOff + i * SHELL_CODEC_FRAME_LENGTH;
                nLS = nRshifts[i] - 1;
                for (k = 0; k < SHELL_CODEC_FRAME_LENGTH; k++)
                {
                    abs_q = (sbyte)SKP_abs(q[pulses_ptr + k]);
                    for (j = nLS; j > 0; j--)
                    {
                        bit = SKP_RSHIFT(abs_q, j) & 1;
                        SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, bit, SKP_Silk_lsb_CDF, 0);
                    }
                    bit = abs_q & 1;
                    SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, bit, SKP_Silk_lsb_CDF, 0);
                }
            }
        }

        /****************/
        /* Encode signs */
        /****************/
        SKP_Silk_code_signs.SKP_Silk_encode_signs(psRC, q, qOff, frame_length, sigtype,
            QuantOffsetType, RateLevelIndex);
    }
}
