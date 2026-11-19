#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_private_ARMA4.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_private_ARMA4.c
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
// SKP_Silk_resampler_private_ARMA4.c                                 *
//																		*
// Fourth order ARMA filter, applies 64x gain                           *
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
//                                                                      */

/* Fourth order ARMA filter                                             */
/* Internally operates as two biquad filters in sequence.               */

/* Coeffients are stored in a packed format:                                                        */
/*    { B1_Q14[1], B2_Q14[1], -A1_Q14[1], -A1_Q14[2], -A2_Q14[1], -A2_Q14[2], gain_Q16 }            */
/* where it is assumed that B*_Q14[0], B*_Q14[2], A*_Q14[0] are all 16384                           */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，C# 禁止
// 成员与类同名；函数名保持 C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(#if): 默认构建（EMBEDDED_ARM=0）下 `#if (EMBEDDED_ARM<5)` 为真，
// 仅移植 C 代码体。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_private_ARMA4_impl
{
    public static void SKP_Silk_resampler_private_ARMA4(
        int[] S, int SOff,            /* I/O: State vector [ 4 ]			        */
        short[] @out, int outOff,     /* O:	Output signal				    	*/
        short[] @in, int inOff,       /* I:	Input signal				    	*/
        short[] Coef, int CoefOff,    /* I:	ARMA coefficients [ 7 ]             */
        int len                       /* I:	Signal length				        */
    )
    {
        int k;
        int in_Q8, out1_Q8, out2_Q8, X;

        for( k = 0; k < len; k++ )
        {
            in_Q8 = SKP_LSHIFT32( (int)@in[ inOff + k ], 8 );

            /* Outputs of first and second biquad */
            out1_Q8 = SKP_ADD_LSHIFT32( in_Q8, S[ SOff + 0 ], 2 );
            out2_Q8 = SKP_ADD_LSHIFT32( out1_Q8, S[ SOff + 2 ], 2 );

            /* Update states, which are stored in Q6. Coefficients are in Q14 here */
            X = SKP_SMLAWB( S[ SOff + 1 ], in_Q8, Coef[ CoefOff + 0 ] );
            S[ SOff + 0 ] = SKP_SMLAWB( X, out1_Q8, Coef[ CoefOff + 2 ] );

            X = SKP_SMLAWB( S[ SOff + 3 ], out1_Q8, Coef[ CoefOff + 1 ] );
            S[ SOff + 2 ] = SKP_SMLAWB( X, out2_Q8, Coef[ CoefOff + 4 ] );

            S[ SOff + 1 ] = SKP_SMLAWB( SKP_RSHIFT32( in_Q8, 2 ), out1_Q8, Coef[ CoefOff + 3 ] );
            S[ SOff + 3 ] = SKP_SMLAWB( SKP_RSHIFT32( out1_Q8, 2 ), out2_Q8, Coef[ CoefOff + 5 ] );

            /* Apply gain and store to output. The coefficient is in Q16 */
            @out[ outOff + k ] = (short)SKP_SAT16( SKP_RSHIFT32( SKP_SMLAWB( 128, out2_Q8, Coef[ CoefOff + 6 ] ), 8 ) );
        }
    }
}
