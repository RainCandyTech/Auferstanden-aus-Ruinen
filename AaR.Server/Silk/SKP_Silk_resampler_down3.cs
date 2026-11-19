#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_down3.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_down3.c
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
// SKP_Silk_resampler_down3.c                                         *
//                                                                      *
// Downsample by a factor 3, low quality                                *
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
//                                                                      */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，函数名保持
// C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(宏→常量): `#define ORDER_FIR 6`（文件内宏）→ 类内 const；
// RESAMPLER_MAX_BATCH_SIZE_IN（480，定义于 SKP_Silk_resampler_private.h）随
// 使用它的每个编译单元展开，此处按类内 const 复现（调用方为
// SKP_Silk_pitch_analysis_core.c 的移植文件）。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_resampler_rom;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_down3_impl
{
    internal const int ORDER_FIR = 6;                        /* C 文件内 #define ORDER_FIR 6 */
    internal const int RESAMPLER_MAX_BATCH_SIZE_IN = 480;    /* SKP_Silk_resampler_private.h */

    /* Downsample by a factor 3, low quality */
    public static void SKP_Silk_resampler_down3(
        int[] S, int SOff,            /* I/O: State vector [ 8 ]                  */
        short[] @out, int outOff,     /* O:   Output signal [ floor(inLen/3) ]    */
        short[] @in, int inOff,       /* I:   Input signal [ inLen ]              */
        int inLen                     /* I:   Number of input samples             */
    )
    {
        int nSamplesIn, counter, res_Q6;
        int[] buf = new int[ RESAMPLER_MAX_BATCH_SIZE_IN + ORDER_FIR ];
        int buf_ptrOff;   /* C: SKP_int32 *buf_ptr —— 指针 → buf 内偏移 */

        /* Copy buffered samples to start of buffer */
        SKP_memcpy( buf, 0, S, SOff, ORDER_FIR );

        /* Iterate over blocks of frameSizeIn input samples */
        while( true )
        {
            nSamplesIn = SKP_min( inLen, RESAMPLER_MAX_BATCH_SIZE_IN );

            /* Second-order AR filter (output in Q8) */
            SKP_Silk_resampler_private_AR2_impl.SKP_Silk_resampler_private_AR2(
                S, SOff + ORDER_FIR, buf, ORDER_FIR, @in, inOff,
                SKP_Silk_Resampler_1_3_COEFS_LQ, 0, nSamplesIn );

            /* Interpolate filtered signal */
            buf_ptrOff = 0;
            counter = nSamplesIn;
            while( counter > 2 )
            {
                /* Inner product */
                res_Q6 = SKP_SMULWB( SKP_ADD32( buf[ buf_ptrOff + 0 ], buf[ buf_ptrOff + 5 ] ), SKP_Silk_Resampler_1_3_COEFS_LQ[ 2 ] );
                res_Q6 = SKP_SMLAWB( res_Q6, SKP_ADD32( buf[ buf_ptrOff + 1 ], buf[ buf_ptrOff + 4 ] ), SKP_Silk_Resampler_1_3_COEFS_LQ[ 3 ] );
                res_Q6 = SKP_SMLAWB( res_Q6, SKP_ADD32( buf[ buf_ptrOff + 2 ], buf[ buf_ptrOff + 3 ] ), SKP_Silk_Resampler_1_3_COEFS_LQ[ 4 ] );

                /* Scale down, saturate and store in output array */
                @out[ outOff++ ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( res_Q6, 6 ) );

                buf_ptrOff += 3;
                counter -= 3;
            }

            inOff += nSamplesIn;
            inLen -= nSamplesIn;

            if( inLen > 0 )
            {
                /* More iterations to do; copy last part of filtered signal to beginning of buffer */
                SKP_memcpy( buf, 0, buf, nSamplesIn, ORDER_FIR );
            }
            else
            {
                break;
            }
        }

        /* Copy last part of filtered signal to the state for the next call */
        SKP_memcpy( S, SOff, buf, nSamplesIn, ORDER_FIR );
    }
}
