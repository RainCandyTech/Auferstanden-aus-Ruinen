#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_down2.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_down2.c
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

//                                                                      *
// SKP_Silk_resampler_down2.c                                         *
//                                                                      *
// Downsample by a factor 2, mediocre quality                           *
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
//                                                                      */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，函数名保持
// C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(委托): 本函数被赋给 resampler_state.down_pre_function 字段
// （SKP_Silk_resampler_init 中）；签名（参数类型与顺序）与地基委托
// SilkResamplerIntStateFn 完全一致。
// PORT-NOTE(#if): 默认构建（EMBEDDED_ARM=0）下 `#if (EMBEDDED_ARM<5)` 为真，
// 仅移植 C 代码体。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_resampler_rom;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_down2_impl
{
    /* Downsample by a factor 2, mediocre quality */
    public static void SKP_Silk_resampler_down2(
        int[] S, int SOff,            /* I/O: State vector [ 2 ]                  */
        short[] @out, int outOff,     /* O:   Output signal [ len ]               */
        short[] @in, int inOff,       /* I:   Input signal [ floor(len/2) ]       */
        int inLen                     /* I:   Number of input samples             */
    )
    {
        int k, len2 = SKP_RSHIFT32( inLen, 1 );
        int in32, out32, Y, X;

        /* Internal variables and state are in Q10 format */
        for( k = 0; k < len2; k++ )
        {
            /* Convert to Q10 */
            in32 = SKP_LSHIFT( (int)@in[ inOff + 2 * k ], 10 );

            /* All-pass section for even input sample */
            Y = SKP_SUB32( in32, S[ SOff + 0 ] );
            X = SKP_SMLAWB( Y, Y, SKP_Silk_resampler_down2_1 );
            out32 = SKP_ADD32( S[ SOff + 0 ], X );
            S[ SOff + 0 ] = SKP_ADD32( in32, X );

            /* Convert to Q10 */
            in32 = SKP_LSHIFT( (int)@in[ inOff + 2 * k + 1 ], 10 );

            /* All-pass section for odd input sample, and add to output of previous section */
            Y = SKP_SUB32( in32, S[ SOff + 1 ] );
            X = SKP_SMULWB( Y, SKP_Silk_resampler_down2_0 );
            out32 = SKP_ADD32( out32, S[ SOff + 1 ] );
            out32 = SKP_ADD32( out32, X );
            S[ SOff + 1 ] = SKP_ADD32( in32, X );

            /* Add, convert back to int16 and store to output */
            @out[ outOff + k ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( out32, 11 ) );
        }
    }
}
