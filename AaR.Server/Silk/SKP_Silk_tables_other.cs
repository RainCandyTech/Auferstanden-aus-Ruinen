#nullable disable
// ***********************************************************************
// SKP_Silk_tables_other.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：src/SKP_Silk_tables_other.c
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
// NO EXPRESS OR IMPLIED LICENSES TO ANY PARTY'S PATENT RIGHTS ARE GRANTED 
// BY THIS LICENSE. THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND 
// CONTRIBUTORS ''AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING,
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND 
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE 
// COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, 
// INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
// NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF 
// USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON 
// ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT 
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ***********************************************************************

// 本文件为常量表文件，逐值照抄（含 C 源中的行内注释与被注释掉的备选系数行）。
// PORT-NOTE: TargetRate_table_* 行内 MAX_TARGET_RATE_BPS、Quantization_Offsets_Q10 行内
//   OFFSET_*_Q10 均为 SKP_Silk_define 常量（using static 后裸名可用；编译期常量隐式收窄合法）。
// PORT-NOTE: SWITCH_TRANSITION_FILTERING 在默认 gcc 构建（SKP_Silk_define.h 中为 1）下生效，
//   故 Transition_LP_B/A_Q28 属于默认基准表，无条件移植（C# 侧无 #if，直接生效）。
using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;

namespace AaR.Server.Silk;
/// <summary>SKP_Silk_tables_other.c —— 码率/SNR 映射、检测/解码滤波系数等杂表。</summary>
internal static class SKP_Silk_tables_other
{
    /* Piece-wise linear mapping from bitrate in kbps to coding quality in dB SNR */
/* C: const SKP_int32 TargetRate_table_NB[ TARGET_RATE_TAB_SZ ] = */
    internal static readonly int[] TargetRate_table_NB = new int[]
    {
    0,      8000,   9000,   11000,  13000,  16000,  22000,  MAX_TARGET_RATE_BPS
    };
/* C: const SKP_int32 TargetRate_table_MB[ TARGET_RATE_TAB_SZ ] = */
    internal static readonly int[] TargetRate_table_MB = new int[]
    {
    0,      10000,  12000,  14000,  17000,  21000,  28000,  MAX_TARGET_RATE_BPS
    };
/* C: const SKP_int32 TargetRate_table_WB[ TARGET_RATE_TAB_SZ ] = */
    internal static readonly int[] TargetRate_table_WB = new int[]
    {
    0,      11000,  14000,  17000,  21000,  26000,  36000,  MAX_TARGET_RATE_BPS
    };
/* C: const SKP_int32 TargetRate_table_SWB[ TARGET_RATE_TAB_SZ ] = */
    internal static readonly int[] TargetRate_table_SWB = new int[]
    {
    0,      13000,  16000,  19000,  25000,  32000,  46000,  MAX_TARGET_RATE_BPS
    };
/* C: const SKP_int32 SNR_table_Q1[ TARGET_RATE_TAB_SZ ] = */
    internal static readonly int[] SNR_table_Q1 = new int[]
    {
    19,     31,     35,     39,     43,     47,     54,     64
    };
/* C: const SKP_int32 SNR_table_one_bit_per_sample_Q7[ 4 ] = */
    internal static readonly int[] SNR_table_one_bit_per_sample_Q7 = new int[]
    {
    1984,   2240,   2408,   2708
    };
/* C: const SKP_int16 SKP_Silk_SWB_detect_B_HP_Q13[ NB_SOS ][ 3 ] = */
    internal static readonly short[][] SKP_Silk_SWB_detect_B_HP_Q13 = new short[][]
    {
    //{400, -550, 400}, {400, 130, 400}, {400, 390, 400}
    new short[]{575, -948, 575}, new short[]{575, -221, 575}, new short[]{575, 104, 575} 
    };
/* C: const SKP_int16 SKP_Silk_SWB_detect_A_HP_Q13[ NB_SOS ][ 2 ] = */
    internal static readonly short[][] SKP_Silk_SWB_detect_A_HP_Q13 = new short[][]
    {
    new short[]{14613, 6868}, new short[]{12883, 7337}, new short[]{11586, 7911}
    //{14880, 6900}, {14400, 7300}, {13700, 7800}
    };
/* C: const SKP_int16 SKP_Silk_Dec_A_HP_24[ DEC_HP_ORDER ] = */
    internal static readonly short[] SKP_Silk_Dec_A_HP_24 = new short[]
    {
-16220, 8030
    };              // second order AR coefs, Q13
/* C: const SKP_int16 SKP_Silk_Dec_B_HP_24[ DEC_HP_ORDER + 1 ] = */
    internal static readonly short[] SKP_Silk_Dec_B_HP_24 = new short[]
    {
8000, -16000, 8000
    };        // second order MA coefs, Q13
/* C: const SKP_int16 SKP_Silk_Dec_A_HP_16[ DEC_HP_ORDER ] = */
    internal static readonly short[] SKP_Silk_Dec_A_HP_16 = new short[]
    {
-16127, 7940
    };              // second order AR coefs, Q13
/* C: const SKP_int16 SKP_Silk_Dec_B_HP_16[ DEC_HP_ORDER + 1 ] = */
    internal static readonly short[] SKP_Silk_Dec_B_HP_16 = new short[]
    {
8000, -16000, 8000
    };        // second order MA coefs, Q13
/* C: const SKP_int16 SKP_Silk_Dec_A_HP_12[ DEC_HP_ORDER ] = */
    internal static readonly short[] SKP_Silk_Dec_A_HP_12 = new short[]
    {
-16043, 7859
    };              // second order AR coefs, Q13
/* C: const SKP_int16 SKP_Silk_Dec_B_HP_12[ DEC_HP_ORDER + 1 ] = */
    internal static readonly short[] SKP_Silk_Dec_B_HP_12 = new short[]
    {
8000, -16000, 8000
    };        // second order MA coefs, Q13
/* C: const SKP_int16 SKP_Silk_Dec_A_HP_8[ DEC_HP_ORDER ] = */
    internal static readonly short[] SKP_Silk_Dec_A_HP_8 = new short[]
    {
-15885, 7710
    };               // second order AR coefs, Q13
/* C: const SKP_int16 SKP_Silk_Dec_B_HP_8[ DEC_HP_ORDER + 1 ] = */
    internal static readonly short[] SKP_Silk_Dec_B_HP_8 = new short[]
    {
8000, -16000, 8000
    };         // second order MA coefs, Q13
/* C: const SKP_uint16 SKP_Silk_lsb_CDF[ 3 ] = */
    internal static readonly ushort[] SKP_Silk_lsb_CDF = new ushort[]
    {
0,  40000,  65535
    };
/* C: const SKP_uint16 SKP_Silk_LTPscale_CDF[ 4 ] = */
    internal static readonly ushort[] SKP_Silk_LTPscale_CDF = new ushort[]
    {
0,  32000,  48000,  65535
    };
    /* C: const SKP_int SKP_Silk_LTPscale_offset = 2; */
    internal static readonly int SKP_Silk_LTPscale_offset = 2;
/* C: const SKP_uint16 SKP_Silk_vadflag_CDF[ 3 ] = */
    internal static readonly ushort[] SKP_Silk_vadflag_CDF = new ushort[]
    {
0,  22000,  65535
    }; // 66% for speech, 33% for no speech
    /* C: const SKP_int SKP_Silk_vadflag_offset = 1; */
    internal static readonly int SKP_Silk_vadflag_offset = 1;
/* C: const SKP_int SKP_Silk_SamplingRates_table[ 4 ] = */
    internal static readonly int[] SKP_Silk_SamplingRates_table = new int[]
    {
8, 12, 16, 24
    };
/* C: const SKP_uint16 SKP_Silk_SamplingRates_CDF[ 5 ] = */
    internal static readonly ushort[] SKP_Silk_SamplingRates_CDF = new ushort[]
    {
0,  16000,  32000,  48000,  65535
    };
    /* C: const SKP_int SKP_Silk_SamplingRates_offset = 2; */
    internal static readonly int SKP_Silk_SamplingRates_offset = 2;
/* C: const SKP_uint16 SKP_Silk_NLSF_interpolation_factor_CDF[ 6 ] = */
    internal static readonly ushort[] SKP_Silk_NLSF_interpolation_factor_CDF = new ushort[]
    {
0,   3706,   8703,  19226,  30926,  65535
    };
    /* C: const SKP_int SKP_Silk_NLSF_interpolation_factor_offset = 4; */
    internal static readonly int SKP_Silk_NLSF_interpolation_factor_offset = 4;
/* C: const SKP_uint16 SKP_Silk_FrameTermination_CDF[ 5 ] = */
    internal static readonly ushort[] SKP_Silk_FrameTermination_CDF = new ushort[]
    {
0, 20000, 45000, 56000, 65535
    };
    /* C: const SKP_int SKP_Silk_FrameTermination_offset = 2; */
    internal static readonly int SKP_Silk_FrameTermination_offset = 2;
/* C: const SKP_uint16 SKP_Silk_Seed_CDF[ 5 ] = */
    internal static readonly ushort[] SKP_Silk_Seed_CDF = new ushort[]
    {
0, 16384, 32768, 49152, 65535
    };
    /* C: const SKP_int SKP_Silk_Seed_offset = 2; */
    internal static readonly int SKP_Silk_Seed_offset = 2;
/* C: const SKP_int16 SKP_Silk_Quantization_Offsets_Q10[ 2 ][ 2 ] = */
    internal static readonly short[][] SKP_Silk_Quantization_Offsets_Q10 = new short[][]
    {
    new short[]{ OFFSET_VL_Q10, OFFSET_VH_Q10 }, new short[]{ OFFSET_UVL_Q10, OFFSET_UVH_Q10 }
    };
/* C: const SKP_int16 SKP_Silk_LTPScales_table_Q14[ 3 ] = */
    internal static readonly short[] SKP_Silk_LTPScales_table_Q14 = new short[]
    {
 15565, 11469, 8192 
    };
/* C: const SKP_int32 SKP_Silk_Transition_LP_B_Q28[ TRANSITION_INT_NUM ][ TRANSITION_NB ] = */
    internal static readonly int[][] SKP_Silk_Transition_LP_B_Q28 = new int[][]
    {
new int[]{    250767114,  501534038,  250767114  },
new int[]{    209867381,  419732057,  209867381  },
new int[]{    170987846,  341967853,  170987846  },
new int[]{    131531482,  263046905,  131531482  },
new int[]{     89306658,  178584282,   89306658  }
    };
/* C: const SKP_int32 SKP_Silk_Transition_LP_A_Q28[ TRANSITION_INT_NUM ][ TRANSITION_NA ] = */
    internal static readonly int[][] SKP_Silk_Transition_LP_A_Q28 = new int[][]
    {
new int[]{    506393414,  239854379  },
new int[]{    411067935,  169683996  },
new int[]{    306733530,  116694253  },
new int[]{    185807084,   77959395  },
new int[]{     35497197,   57401098  }
    };
}
