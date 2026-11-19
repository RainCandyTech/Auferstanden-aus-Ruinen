#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_private_up4.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_private_up4.c
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
// SKP_Silk_resampler_private_up4.c                                   *
//                                                                      *
// Upsample by a factor 4, low quality                                  *
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
//                                                                      */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，函数名保持
// C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(委托): 本函数被赋给 resampler_state.up_post_function 字段
// （SKP_Silk_resampler_init 中 Fs_Hz_out > 96000 分支）；签名与地基委托
// SilkResamplerIntStateFn 完全一致。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_resampler_rom;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_private_up4_impl
{
    /* Upsample by a factor 4, Note: very low quality, only use with output sampling rates above 96 kHz. */
    public static void SKP_Silk_resampler_private_up4(
        int[] S, int SOff,            /* I/O: State vector [ 2 ]                      */
        short[] @out, int outOff,     /* O:   Output signal [ 4 * len ]               */
        short[] @in, int inOff,       /* I:   Input signal [ len ]                    */
        int len                       /* I:   Number of INPUT samples                 */
    )
    {
        int k;
        int in32, out32, Y, X;
        short out16;

        /* Internal variables and state are in Q10 format */
        for( k = 0; k < len; k++ )
        {
            /* Convert to Q10 */
            in32 = SKP_LSHIFT( (int)@in[ inOff + k ], 10 );

            /* All-pass section for even output sample */
            Y = SKP_SUB32( in32, S[ SOff + 0 ] );
            X = SKP_SMULWB( Y, SKP_Silk_resampler_up2_lq_0 );
            out32 = SKP_ADD32( S[ SOff + 0 ], X );
            S[ SOff + 0 ] = SKP_ADD32( in32, X );

            /* Convert back to int16 and store to output */
            out16 = (short)SKP_SAT16( SKP_RSHIFT_ROUND( out32, 10 ) );
            @out[ outOff + 4 * k ] = out16;
            @out[ outOff + 4 * k + 1 ] = out16;

            /* All-pass section for odd output sample */
            Y = SKP_SUB32( in32, S[ SOff + 1 ] );
            X = SKP_SMLAWB( Y, Y, SKP_Silk_resampler_up2_lq_1 );
            out32 = SKP_ADD32( S[ SOff + 1 ], X );
            S[ SOff + 1 ] = SKP_ADD32( in32, X );

            /* Convert back to int16 and store to output */
            out16 = (short)SKP_SAT16( SKP_RSHIFT_ROUND( out32, 10 ) );
            @out[ outOff + 4 * k + 2 ] = out16;
            @out[ outOff + 4 * k + 3 ] = out16;
        }
    }
}
