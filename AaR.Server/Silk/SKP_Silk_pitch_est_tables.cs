#nullable disable
// ***********************************************************************
// SKP_Silk_pitch_est_tables.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：src/SKP_Silk_pitch_est_tables.c
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

// 本文件为纯常量表文件（自动生成自 generate_pitch_est_tables.m 的原始输出），逐值照抄。
// PORT-NOTE: 多维表 → 交错数组，访问点保持 tbl[i][j] / tbl[i][j][k] 形态不变。
// PORT-NOTE: cbk_sizes_stage3 / cbk_offsets_stage3 表值为裸名常量与表达式，
//   来自 SKP_Silk_define（using static 后原样可用；均为编译期常量，隐式收窄到 short 合法）。
using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;

namespace AaR.Server.Silk;
/// <summary>SKP_Silk_pitch_est_tables.c —— 基音估计 CB lag / 搜索范围表。</summary>
internal static class SKP_Silk_pitch_est_tables
{
    /********************************************************/
    /* Auto Generated File from generate_pitch_est_tables.m */
    /********************************************************/

/* C: const SKP_int16 SKP_Silk_CB_lags_stage2[PITCH_EST_NB_SUBFR][PITCH_EST_NB_CBKS_STAGE2_EXT] = */
    internal static readonly short[][] SKP_Silk_CB_lags_stage2 = new short[][]
    {
    new short[]{0, 2,-1,-1,-1, 0, 0, 1, 1, 0, 1},
    new short[]{0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0},
    new short[]{0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0},
    new short[]{0,-1, 2, 1, 0, 1, 1, 0, 0,-1,-1} 
    };
/* C: const SKP_int16 SKP_Silk_CB_lags_stage3[PITCH_EST_NB_SUBFR][PITCH_EST_NB_CBKS_STAGE3_MAX] = */
    internal static readonly short[][] SKP_Silk_CB_lags_stage3 = new short[][]
    {
    new short[]{-9,-7,-6,-5,-5,-4,-4,-3,-3,-2,-2,-2,-1,-1,-1, 0, 0, 0, 1, 1, 0, 1, 2, 2, 2, 3, 3, 4, 4, 5, 6, 5, 6, 8},
    new short[]{-3,-2,-2,-2,-1,-1,-1,-1,-1, 0, 0,-1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 1, 2, 1, 2, 2, 2, 2, 3},
    new short[]{ 3, 3, 2, 2, 2, 2, 1, 2, 1, 1, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0,-1, 0, 0,-1,-1,-1,-1,-1,-2,-2,-2},
    new short[]{ 9, 8, 6, 5, 6, 5, 4, 4, 3, 3, 2, 2, 2, 1, 0, 1, 1, 0, 0, 0,-1,-1,-1,-2,-2,-2,-3,-3,-4,-4,-5,-5,-6,-7}
 
    };
/* C: const SKP_int16 SKP_Silk_Lag_range_stage3[ SKP_Silk_PITCH_EST_MAX_COMPLEX + 1 ] [ PITCH_EST_NB_SUBFR ][ 2 ] = */
    internal static readonly short[][][] SKP_Silk_Lag_range_stage3 = new short[][][]
    {
    /* Lags to search for low number of stage3 cbks */
    new short[][]{
        new short[]{-2,6},
        new short[]{-1,5},
        new short[]{-1,5},
        new short[]{-2,7}
    },
    /* Lags to search for middle number of stage3 cbks */
    new short[][]{
        new short[]{-4,8},
        new short[]{-1,6},
        new short[]{-1,6},
        new short[]{-4,9}
    },
    /* Lags to search for max number of stage3 cbks */
    new short[][]{
        new short[]{-9,12},
        new short[]{-3,7},
        new short[]{-2,7},
        new short[]{-7,13}
    }
    };
/* C: const SKP_int16 SKP_Silk_cbk_sizes_stage3[SKP_Silk_PITCH_EST_MAX_COMPLEX + 1] = */
    internal static readonly short[] SKP_Silk_cbk_sizes_stage3 = new short[]
    {
    PITCH_EST_NB_CBKS_STAGE3_MIN,
    PITCH_EST_NB_CBKS_STAGE3_MID,
    PITCH_EST_NB_CBKS_STAGE3_MAX
    };
/* C: const SKP_int16 SKP_Silk_cbk_offsets_stage3[SKP_Silk_PITCH_EST_MAX_COMPLEX + 1] = */
    internal static readonly short[] SKP_Silk_cbk_offsets_stage3 = new short[]
    {
    ((PITCH_EST_NB_CBKS_STAGE3_MAX - PITCH_EST_NB_CBKS_STAGE3_MIN) >> 1),
    ((PITCH_EST_NB_CBKS_STAGE3_MAX - PITCH_EST_NB_CBKS_STAGE3_MID) >> 1),
    0
    };
}
