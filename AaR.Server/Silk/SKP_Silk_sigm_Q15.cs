#nullable disable
// ***********************************************************************
// SKP_Silk_sigm_Q15.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：src/SKP_Silk_sigm_Q15.c
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

// PORT-NOTE（任务描述与实源不符）: 本 SDK v1.0.9 的 SKP_Silk_sigm_Q15.c 并非 256 项 Q15
//   sigmoid 表，而是 3 张 6 项 LUT（slope_Q10 / pos_Q15 / neg_Q15）+ 线性插值函数
//   （256 项整表是后续 SILK 版本的形态）。按 C 实源逐值移植。
// PORT-NOTE: #if EMBEDDED_ARM<4 为默认构建分支（EMBEDDED_ARM 未定义按 0 处理），照此移植；
//   ARM 汇编分支（SKP_Silk_sigm_Q15_arm.S）按 PORTING.md §7 不移植。
// PORT-NOTE（CS0542）: C# 不允许成员名与封闭类型同名，而本文件函数名与文件名同为
//   SKP_Silk_sigm_Q15。为保住函数名（调用点 using static 本类后保持 C 的裸名调用形态），
//   类名加 _impl 后缀 → SKP_Silk_sigm_Q15_impl（与本移植工程 schur_impl 等同名冲突文件的
//   既有约定一致）；文件名仍为 SKP_Silk_sigm_Q15.cs。
//   【注意】SKP_Silk_LTP_scale_ctrl_FIX.cs:34 现写的是 using static ...SKP_Silk_sigm_Q15_c，
//   与本类名不一致（其余 VAD/process_gains/noise_shape_analysis 均已按 _impl 引用），需该文件方修正。】
using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;

namespace AaR.Server.Silk;
/*                                                                      *
 * SKP_sigm_Q15.c                                                       *
 *                                                                      *
 * Approximate sigmoid function                                         *
 *                                                                      *
 * Copyright 2006 (c), Skype Limited                                    *
 * Date: 060221                                                         *
 *                                                                      */

/// <summary>SKP_Silk_sigm_Q15.c —— 近似 sigmoid 函数（定点，Q15 输出）。</summary>
internal static class SKP_Silk_sigm_Q15_impl
{
    /********************************/
    /* approximate sigmoid function */
    /********************************/
    /* fprintf(1, '%d, ', round(1024 * ([1 ./ (1 + exp(-(1:5))), 1] - 1 ./ (1 + exp(-(0:5)))))); */
    internal static readonly int[] sigm_LUT_slope_Q10 = new int[] { 237, 153, 73, 30, 12, 7 };
    /* fprintf(1, '%d, ', round(32767 * 1 ./ (1 + exp(-(0:5))))); */
    internal static readonly int[] sigm_LUT_pos_Q15 = new int[] { 16384, 23955, 28861, 31213, 32178, 32548 };
    /* fprintf(1, '%d, ', round(32767 * 1 ./ (1 + exp((0:5))))); */
    internal static readonly int[] sigm_LUT_neg_Q15 = new int[] { 16384, 8812, 3906, 1554, 589, 219 };

    public static int SKP_Silk_sigm_Q15( int in_Q5 )
    {
        int ind;

        if( in_Q5 < 0 ) {
            /* Negative input */
            in_Q5 = -in_Q5;
            if( in_Q5 >= 6 * 32 ) {
                return 0;        /* Clip */
            } else {
                /* Linear interpolation of look up table */
                ind = SKP_RSHIFT( in_Q5, 5 );
                return( sigm_LUT_neg_Q15[ ind ] - SKP_SMULBB( sigm_LUT_slope_Q10[ ind ], in_Q5 & 0x1F ) );
            }
        } else {
            /* Positive input */
            if( in_Q5 >= 6 * 32 ) {
                return 32767;        /* clip */
            } else {
                /* Linear interpolation of look up table */
                ind = SKP_RSHIFT( in_Q5, 5 );
                return( sigm_LUT_pos_Q15[ ind ] + SKP_SMULBB( sigm_LUT_slope_Q10[ ind ], in_Q5 & 0x1F ) );
            }
        }
    }
}
