#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_private_AR2.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_private_AR2.c
//
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
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
// THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ***********************************************************************

//																		*
// SKP_Silk_resampler_private_AR2. c                                  *
//																		*
// Second order AR filter with single delay elements                	*
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
//                                                                      */

// PORT-NOTE(类名 CS0542): C# 规定类成员名不得与类名相同；本文件主函数名与其
// .c 文件名（=按 §1 应取的类名）相同，二者必居其一改名。函数名是跨簇调用与
// 对应关系的锚点，故保持 C 原名不变，类名加后缀 `_impl`（与 enc 簇既有先例
// SKP_Silk_encode_frame_FIX_impl 一致）。跨文件调用一律写
// `SKP_Silk_resampler_private_AR2_impl.SKP_Silk_resampler_private_AR2(...)`。
// 跨簇引用者（如 SKP_Silk_enc_API）用 `using static` 裸名调用，仅 using 行
// 受后缀影响。建议主会话全工程统一 `_impl` 规则。
// PORT-NOTE(#if): C 源 `#if (EMBEDDED_ARM<5)` 在默认构建（EMBEDDED_ARM=0）下
// 为真，仅移植该分支的 C 代码体；ARM 分支按 §7 不移植。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_private_AR2_impl
{
    /* Second order AR filter with single delay elements */
    public static void SKP_Silk_resampler_private_AR2(
        int[] S, int SOff,            /* I/O: State vector [ 2 ]			        */
        int[] out_Q8, int out_Q8Off,  /* O:	Output signal				    	*/
        short[] @in, int inOff,       /* I:	Input signal				    	*/
        short[] A_Q14, int A_Q14Off,  /* I:	AR coefficients, Q14 	            */
        int len                       /* I:	Signal length				        */
    )
    {
        int k;
        int out32;

        for( k = 0; k < len; k++ )
        {
            out32 = SKP_ADD_LSHIFT32( S[ SOff + 0 ], (int)@in[ inOff + k ], 8 );
            out_Q8[ out_Q8Off + k ] = out32;
            out32 = SKP_LSHIFT( out32, 2 );
            S[ SOff + 0 ] = SKP_SMLAWB( S[ SOff + 1 ], out32, A_Q14[ A_Q14Off + 0 ] );
            S[ SOff + 1 ] = SKP_SMULWB( out32, A_Q14[ A_Q14Off + 1 ] );
        }
    }
}
