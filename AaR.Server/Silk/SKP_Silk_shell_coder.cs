#nullable disable
// ***********************************************************************
// SKP_Silk_shell_coder.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_shell_coder.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - combine_pulses / encode_split / decode_split 为 C 文件内 SKP_INLINE
//     函数 → AggressiveInlining。
//   - encode_split/decode_split 的 C 局部指针 cdf = &shell_table[offsets[p]]
//     不再单独取指针，直接以（shell_table, shell_tableOff + offsets[p]）
//     传入 range coder——与 C 指针语义逐位一致。
//   - decode_split 的 p_child1/p_child2 仅解引用（p_child1[0]）→ out int；
//     调用点 out pulses0[pulses0Off + k]（数组元素为合法 out 实参）。
//   - 引用表（表簇 SKP_Silk_tables_pulses_per_block.cs 产出，裸名）：
//       SKP_Silk_shell_code_table0/1/2/3（ushort[]）、
//       SKP_Silk_shell_code_table_offsets（int[19]）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tables_pulses_per_block;  // shell code 表（表簇产出，裸名）

namespace AaR.Server.Silk;

internal static class SKP_Silk_shell_coder
{
    /* shell coder; pulse-subframe length is hardcoded */

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void combine_pulses(
        int[] outBuf, int outOff,                  /* O:   combined pulses vector [len] */
        int[] inBuf, int inOff,                    /* I:   input vector       [2 * len] */
        int len                                    /* I:   number of OUTPUT samples     */
    )
    {
        int k;
        for (k = 0; k < len; k++)
        {
            outBuf[outOff + k] = inBuf[inOff + 2 * k] + inBuf[inOff + 2 * k + 1];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void encode_split(
        SKP_Silk_range_coder_state sRC,            /* I/O: compressor data structure                   */
        int p_child1,                              /* I:   pulse amplitude of first child subframe     */
        int p,                                     /* I:   pulse amplitude of current subframe         */
        ushort[] shell_table, int shell_tableOff   /* I:   table of shell cdfs                         */
    )
    {
        if (p > 0)
        {
            // PORT-NOTE: C 为 cdf = &shell_table[ SKP_Silk_shell_code_table_offsets[ p ] ]，
            // 再传 cdf；此处等价地传（shell_table, 偏移）。
            SKP_Silk_range_coder.SKP_Silk_range_encoder(sRC, p_child1, shell_table,
                shell_tableOff + SKP_Silk_shell_code_table_offsets[p]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void decode_split(
        out int p_child1,                          /* O:   pulse amplitude of first child subframe     */
        out int p_child2,                          /* O:   pulse amplitude of second child subframe    */
        SKP_Silk_range_coder_state sRC,            /* I/O: compressor data structure                   */
        int p,                                     /* I:   pulse amplitude of current subframe         */
        ushort[] shell_table, int shell_tableOff   /* I:   table of shell cdfs                         */
    )
    {
        int cdf_middle;

        if (p > 0)
        {
            cdf_middle = SKP_RSHIFT(p, 1);
            // PORT-NOTE: 同 encode_split 的表内偏移传递。
            SKP_Silk_range_coder.SKP_Silk_range_decoder(out p_child1, sRC, shell_table,
                shell_tableOff + SKP_Silk_shell_code_table_offsets[p], cdf_middle);
            p_child2 = p - p_child1;
        }
        else
        {
            p_child1 = 0;
            p_child2 = 0;
        }
    }

    /* Shell encoder, operates on one shell code frame of 16 pulses */
    public static void SKP_Silk_shell_encoder(
        SKP_Silk_range_coder_state sRC,            /* I/O  compressor data structure                   */
        int[] pulses0, int pulses0Off              /* I    data: nonnegative pulse amplitudes          */
    )
    {
        int[] pulses1 = new int[8], pulses2 = new int[4], pulses3 = new int[2], pulses4 = new int[1];

        /* this function operates on one shell code frame of 16 pulses */

        /* tree representation per pulse-subframe */
        combine_pulses(pulses1, 0, pulses0, pulses0Off, 8);
        combine_pulses(pulses2, 0, pulses1, 0, 4);
        combine_pulses(pulses3, 0, pulses2, 0, 2);
        combine_pulses(pulses4, 0, pulses3, 0, 1);

        encode_split(sRC, pulses3[0], pulses4[0], SKP_Silk_shell_code_table3, 0);

        encode_split(sRC, pulses2[0], pulses3[0], SKP_Silk_shell_code_table2, 0);

        encode_split(sRC, pulses1[0], pulses2[0], SKP_Silk_shell_code_table1, 0);
        encode_split(sRC, pulses0[pulses0Off + 0], pulses1[0], SKP_Silk_shell_code_table0, 0);
        encode_split(sRC, pulses0[pulses0Off + 2], pulses1[1], SKP_Silk_shell_code_table0, 0);

        encode_split(sRC, pulses1[2], pulses2[1], SKP_Silk_shell_code_table1, 0);
        encode_split(sRC, pulses0[pulses0Off + 4], pulses1[2], SKP_Silk_shell_code_table0, 0);
        encode_split(sRC, pulses0[pulses0Off + 6], pulses1[3], SKP_Silk_shell_code_table0, 0);

        encode_split(sRC, pulses2[2], pulses3[1], SKP_Silk_shell_code_table2, 0);

        encode_split(sRC, pulses1[4], pulses2[2], SKP_Silk_shell_code_table1, 0);
        encode_split(sRC, pulses0[pulses0Off + 8], pulses1[4], SKP_Silk_shell_code_table0, 0);
        encode_split(sRC, pulses0[pulses0Off + 10], pulses1[5], SKP_Silk_shell_code_table0, 0);

        encode_split(sRC, pulses1[6], pulses2[3], SKP_Silk_shell_code_table1, 0);
        encode_split(sRC, pulses0[pulses0Off + 12], pulses1[6], SKP_Silk_shell_code_table0, 0);
        encode_split(sRC, pulses0[pulses0Off + 14], pulses1[7], SKP_Silk_shell_code_table0, 0);
    }


    /* Shell decoder, operates on one shell code frame of 16 pulses */
    public static void SKP_Silk_shell_decoder(
        int[] pulses0, int pulses0Off,              /* O    data: nonnegative pulse amplitudes          */
        SKP_Silk_range_coder_state sRC,            /* I/O  compressor data structure                   */
        int pulses4                                /* I    number of pulses per pulse-subframe         */
    )
    {
        int[] pulses3 = new int[2], pulses2 = new int[4], pulses1 = new int[8];

        /* this function operates on one shell code frame of 16 pulses */

        decode_split(out pulses3[0], out pulses3[1], sRC, pulses4, SKP_Silk_shell_code_table3, 0);

        decode_split(out pulses2[0], out pulses2[1], sRC, pulses3[0], SKP_Silk_shell_code_table2, 0);

        decode_split(out pulses1[0], out pulses1[1], sRC, pulses2[0], SKP_Silk_shell_code_table1, 0);
        decode_split(out pulses0[pulses0Off + 0], out pulses0[pulses0Off + 1], sRC, pulses1[0], SKP_Silk_shell_code_table0, 0);
        decode_split(out pulses0[pulses0Off + 2], out pulses0[pulses0Off + 3], sRC, pulses1[1], SKP_Silk_shell_code_table0, 0);

        decode_split(out pulses1[2], out pulses1[3], sRC, pulses2[1], SKP_Silk_shell_code_table1, 0);
        decode_split(out pulses0[pulses0Off + 4], out pulses0[pulses0Off + 5], sRC, pulses1[2], SKP_Silk_shell_code_table0, 0);
        decode_split(out pulses0[pulses0Off + 6], out pulses0[pulses0Off + 7], sRC, pulses1[3], SKP_Silk_shell_code_table0, 0);

        decode_split(out pulses2[2], out pulses2[3], sRC, pulses3[1], SKP_Silk_shell_code_table2, 0);

        decode_split(out pulses1[4], out pulses1[5], sRC, pulses2[2], SKP_Silk_shell_code_table1, 0);
        decode_split(out pulses0[pulses0Off + 8], out pulses0[pulses0Off + 9], sRC, pulses1[4], SKP_Silk_shell_code_table0, 0);
        decode_split(out pulses0[pulses0Off + 10], out pulses0[pulses0Off + 11], sRC, pulses1[5], SKP_Silk_shell_code_table0, 0);

        decode_split(out pulses1[6], out pulses1[7], sRC, pulses2[3], SKP_Silk_shell_code_table1, 0);
        decode_split(out pulses0[pulses0Off + 12], out pulses0[pulses0Off + 13], sRC, pulses1[6], SKP_Silk_shell_code_table0, 0);
        decode_split(out pulses0[pulses0Off + 14], out pulses0[pulses0Off + 15], sRC, pulses1[7], SKP_Silk_shell_code_table0, 0);
    }
}
