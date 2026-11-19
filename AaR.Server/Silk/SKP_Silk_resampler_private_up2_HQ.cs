#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_private_up2_HQ.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_private_up2_HQ.c
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
// SKP_Silk_resampler_private_up2_HQ.c                                *
//                                                                      *
// Upsample by a factor 2, high quality                                 *
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
//                                                                      */

/* Upsample by a factor 2, high quality */
/* Uses 2nd order allpass filters for the 2x upsampling, followed by a      */
/* notch filter just above Nyquist.                                         */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，函数名保持
// C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(委托): SKP_Silk_resampler_private_up2_HQ 被赋给 resampler_state.
// up2_function 字段（高质量分支）；SKP_Silk_resampler_private_up2_HQ_wrapper
// 被赋给 resampler_state.resampler_function 字段（Fs_out = 2 * Fs_in 特例）。
// 两者签名分别与地基委托 SilkResamplerIntStateFn / SilkResamplerStateFn
// 完全一致。
// PORT-NOTE(#if): 默认构建（EMBEDDED_ARM=0）下 `#if (EMBEDDED_ARM<5)` 为真，
// 仅移植 C 代码体。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_resampler_rom;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_private_up2_HQ_impl
{
    /* Upsample by a factor 2, high quality */
    public static void SKP_Silk_resampler_private_up2_HQ(
        int[] S, int SOff,            /* I/O: Resampler state [ 6 ]				*/
        short[] @out, int outOff,     /* O:   Output signal [ 2 * len ]           */
        short[] @in, int inOff,       /* I:   Input signal [ len ]                 */
        int len                       /* I:   Number of INPUT samples              */
    )
    {
        int k;
        int in32, out32_1, out32_2, Y, X;

        /* Internal variables and state are in Q10 format */
        for( k = 0; k < len; k++ )
        {
            /* Convert to Q10 */
            in32 = SKP_LSHIFT( (int)@in[ inOff + k ], 10 );

            /* First all-pass section for even output sample */
            Y = SKP_SUB32( in32, S[ SOff + 0 ] );
            X = SKP_SMULWB( Y, SKP_Silk_resampler_up2_hq_0[ 0 ] );
            out32_1 = SKP_ADD32( S[ SOff + 0 ], X );
            S[ SOff + 0 ] = SKP_ADD32( in32, X );

            /* Second all-pass section for even output sample */
            Y = SKP_SUB32( out32_1, S[ SOff + 1 ] );
            X = SKP_SMLAWB( Y, Y, SKP_Silk_resampler_up2_hq_0[ 1 ] );
            out32_2 = SKP_ADD32( S[ SOff + 1 ], X );
            S[ SOff + 1 ] = SKP_ADD32( out32_1, X );

            /* Biquad notch filter */
            out32_2 = SKP_SMLAWB( out32_2, S[ SOff + 5 ], SKP_Silk_resampler_up2_hq_notch[ 2 ] );
            out32_2 = SKP_SMLAWB( out32_2, S[ SOff + 4 ], SKP_Silk_resampler_up2_hq_notch[ 1 ] );
            out32_1 = SKP_SMLAWB( out32_2, S[ SOff + 4 ], SKP_Silk_resampler_up2_hq_notch[ 0 ] );
            S[ SOff + 5 ] = SKP_SUB32( out32_2, S[ SOff + 5 ] );

            /* Apply gain in Q15, convert back to int16 and store to output */
            @out[ outOff + 2 * k ] = (short)SKP_SAT16( SKP_RSHIFT32(
                SKP_SMLAWB( 256, out32_1, SKP_Silk_resampler_up2_hq_notch[ 3 ] ), 9 ) );

            /* First all-pass section for odd output sample */
            Y = SKP_SUB32( in32, S[ SOff + 2 ] );
            X = SKP_SMULWB( Y, SKP_Silk_resampler_up2_hq_1[ 0 ] );
            out32_1 = SKP_ADD32( S[ SOff + 2 ], X );
            S[ SOff + 2 ] = SKP_ADD32( in32, X );

            /* Second all-pass section for odd output sample */
            Y = SKP_SUB32( out32_1, S[ SOff + 3 ] );
            X = SKP_SMLAWB( Y, Y, SKP_Silk_resampler_up2_hq_1[ 1 ] );
            out32_2 = SKP_ADD32( S[ SOff + 3 ], X );
            S[ SOff + 3 ] = SKP_ADD32( out32_1, X );

            /* Biquad notch filter */
            out32_2 = SKP_SMLAWB( out32_2, S[ SOff + 4 ], SKP_Silk_resampler_up2_hq_notch[ 2 ] );
            out32_2 = SKP_SMLAWB( out32_2, S[ SOff + 5 ], SKP_Silk_resampler_up2_hq_notch[ 1 ] );
            out32_1 = SKP_SMLAWB( out32_2, S[ SOff + 5 ], SKP_Silk_resampler_up2_hq_notch[ 0 ] );
            S[ SOff + 4 ] = SKP_SUB32( out32_2, S[ SOff + 4 ] );

            /* Apply gain in Q15, convert back to int16 and store to output */
            @out[ outOff + 2 * k + 1 ] = (short)SKP_SAT16( SKP_RSHIFT32(
                SKP_SMLAWB( 256, out32_1, SKP_Silk_resampler_up2_hq_notch[ 3 ] ), 9 ) );
        }
    }


    public static void SKP_Silk_resampler_private_up2_HQ_wrapper(
        SKP_Silk_resampler_state_struct S,  /* I/O: Resampler state (unused)	*/
        short[] @out, int outOff,           /* O:   Output signal [ 2 * len ]   */
        short[] @in, int inOff,             /* I:   Input signal [ len ]        */
        int len                             /* I:   Number of input samples     */
    )
    {
        // PORT-NOTE: C 首参为 void* SS，函数体第一行 `SKP_Silk_resampler_state_struct *S =
        // (SKP_Silk_resampler_state_struct *)SS;`——此处首参已是强类型状态对象
        // （按 SilkResamplerStateFn 首参类型移植），该 cast 行随之消失。
        SKP_Silk_resampler_private_up2_HQ( S.sIIR, 0, @out, outOff, @in, inOff, len );
    }
}
