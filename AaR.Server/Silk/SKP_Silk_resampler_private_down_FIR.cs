#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_private_down_FIR.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_private_down_FIR.c
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
// File Name:	SKP_Silk_resampler_private_down_FIR.c                 *
//																		*
// Description: Hybrid IIR/FIR polyphase implementation of resampling	*
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
// All rights reserved.													*
//                                                                      */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，函数名保持
// C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(委托): 本函数被赋给 resampler_state.resampler_function 字段
// （SKP_Silk_resampler_init 中各整数比降采样分支）；签名与地基委托
// SilkResamplerStateFn 完全一致。
// PORT-NOTE(void* 首参): C 首参 void* SS + 函数内 cast 为状态结构体；此处首参
// 直接是强类型状态对象，cast 行消失。
// PORT-NOTE(宏→常量): RESAMPLER_MAX_BATCH_SIZE_IN（480，SKP_Silk_resampler_
// private.h）按类内 const 复现；RESAMPLER_DOWN_ORDER_FIR（12）与 FIR 系数表
// 来自 SKP_Silk_resampler_rom（using static 裸名引用，与 C 调用点形态一致）。
// PORT-NOTE(#if): 默认构建（EMBEDDED_ARM=0）下 `#if EMBEDDED_ARM<5` 为真，
// 仅移植 C 代码体；SKP_INLINE → [MethodImpl(AggressiveInlining)]（§2）。
// PORT-NOTE(指针返回值): C 的 INTERPOL0/1 返回推进后的 out 指针，移植为返回
// 推进后的 outOff（int）。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_resampler_rom;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_private_down_FIR_impl
{
    internal const int RESAMPLER_MAX_BATCH_SIZE_IN = 480;    /* SKP_Silk_resampler_private.h */

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static int SKP_Silk_resampler_private_down_FIR_INTERPOL0(
        short[] @out, int outOff,
        int[] buf2, int buf2Off,
        short[] FIR_Coefs, int FIR_CoefsOff,
        int max_index_Q16, int index_increment_Q16 )
    {
        int index_Q16, res_Q6;
        int buf_ptrOff;

        for( index_Q16 = 0; index_Q16 < max_index_Q16; index_Q16 += index_increment_Q16 )
        {
            /* Integer part gives pointer to buffered input */
            buf_ptrOff = buf2Off + SKP_RSHIFT( index_Q16, 16 );

            /* Inner product */
            res_Q6 = SKP_SMULWB( SKP_ADD32( buf2[ buf_ptrOff + 0 ], buf2[ buf_ptrOff + 11 ] ), FIR_Coefs[ FIR_CoefsOff + 0 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, SKP_ADD32( buf2[ buf_ptrOff + 1 ], buf2[ buf_ptrOff + 10 ] ), FIR_Coefs[ FIR_CoefsOff + 1 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, SKP_ADD32( buf2[ buf_ptrOff + 2 ], buf2[ buf_ptrOff + 9 ] ), FIR_Coefs[ FIR_CoefsOff + 2 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, SKP_ADD32( buf2[ buf_ptrOff + 3 ], buf2[ buf_ptrOff + 8 ] ), FIR_Coefs[ FIR_CoefsOff + 3 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, SKP_ADD32( buf2[ buf_ptrOff + 4 ], buf2[ buf_ptrOff + 7 ] ), FIR_Coefs[ FIR_CoefsOff + 4 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, SKP_ADD32( buf2[ buf_ptrOff + 5 ], buf2[ buf_ptrOff + 6 ] ), FIR_Coefs[ FIR_CoefsOff + 5 ] );

            /* Scale down, saturate and store in output array */
            @out[ outOff++ ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( res_Q6, 6 ) );
        }
        return outOff;
    }

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static int SKP_Silk_resampler_private_down_FIR_INTERPOL1(
        short[] @out, int outOff,
        int[] buf2, int buf2Off,
        short[] FIR_Coefs, int FIR_CoefsOff,
        int max_index_Q16, int index_increment_Q16,
        int FIR_Fracs )
    {
        int index_Q16, res_Q6;
        int buf_ptrOff;
        int interpol_ind;
        int interpolOff;   /* C: const SKP_int16 *interpol_ptr —— 指针 → FIR_Coefs 内偏移 */

        for( index_Q16 = 0; index_Q16 < max_index_Q16; index_Q16 += index_increment_Q16 )
        {
            /* Integer part gives pointer to buffered input */
            buf_ptrOff = buf2Off + SKP_RSHIFT( index_Q16, 16 );

            /* Fractional part gives interpolation coefficients */
            interpol_ind = SKP_SMULWB( index_Q16 & 0xFFFF, FIR_Fracs );

            /* Inner product */
            interpolOff = FIR_CoefsOff + RESAMPLER_DOWN_ORDER_FIR / 2 * interpol_ind;
            res_Q6 = SKP_SMULWB( buf2[ buf_ptrOff + 0 ], FIR_Coefs[ interpolOff + 0 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 1 ], FIR_Coefs[ interpolOff + 1 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 2 ], FIR_Coefs[ interpolOff + 2 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 3 ], FIR_Coefs[ interpolOff + 3 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 4 ], FIR_Coefs[ interpolOff + 4 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 5 ], FIR_Coefs[ interpolOff + 5 ] );
            interpolOff = FIR_CoefsOff + RESAMPLER_DOWN_ORDER_FIR / 2 * ( FIR_Fracs - 1 - interpol_ind );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 11 ], FIR_Coefs[ interpolOff + 0 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 10 ], FIR_Coefs[ interpolOff + 1 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 9 ], FIR_Coefs[ interpolOff + 2 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 8 ], FIR_Coefs[ interpolOff + 3 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 7 ], FIR_Coefs[ interpolOff + 4 ] );
            res_Q6 = SKP_SMLAWB( res_Q6, buf2[ buf_ptrOff + 6 ], FIR_Coefs[ interpolOff + 5 ] );

            /* Scale down, saturate and store in output array */
            @out[ outOff++ ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( res_Q6, 6 ) );
        }
        return outOff;
    }

    /* Resample with a 2x downsampler (optional), a 2nd order AR filter followed by FIR interpolation */
    public static void SKP_Silk_resampler_private_down_FIR(
        SKP_Silk_resampler_state_struct S,  /* I/O: Resampler state 					*/
        short[] @out, int outOff,           /* O:	Output signal 						*/
        short[] @in, int inOff,             /* I:	Input signal						*/
        int inLen                           /* I:	Number of input samples				*/
    )
    {
        int nSamplesIn;
        int max_index_Q16, index_increment_Q16;
        short[] buf1 = new short[ RESAMPLER_MAX_BATCH_SIZE_IN / 2 ];
        int[] buf2 = new int[ RESAMPLER_MAX_BATCH_SIZE_IN + RESAMPLER_DOWN_ORDER_FIR ];
        short[] FIR_Coefs; int FIR_CoefsOff;   /* C: const SKP_int16 *FIR_Coefs —— 指针 → 数组 + 偏移 */

        /* Copy buffered samples to start of buffer */
        SKP_memcpy( buf2, 0, S.sFIR, 0, RESAMPLER_DOWN_ORDER_FIR );

        FIR_Coefs = S.Coefs;
        FIR_CoefsOff = 2;   /* C: FIR_Coefs = &S->Coefs[ 2 ]; */

        /* Iterate over blocks of frameSizeIn input samples */
        index_increment_Q16 = S.invRatio_Q16;
        while( true )
        {
            nSamplesIn = SKP_min( inLen, S.batchSize );

            if( S.input2x == 1 )
            {
                /* Downsample 2x */
                SKP_Silk_resampler_down2_impl.SKP_Silk_resampler_down2( S.sDown2, 0, buf1, 0, @in, inOff, nSamplesIn );

                nSamplesIn = SKP_RSHIFT32( nSamplesIn, 1 );

                /* Second-order AR filter (output in Q8) */
                SKP_Silk_resampler_private_AR2_impl.SKP_Silk_resampler_private_AR2(
                    S.sIIR, 0, buf2, RESAMPLER_DOWN_ORDER_FIR, buf1, 0, S.Coefs, 0, nSamplesIn );
            }
            else
            {
                /* Second-order AR filter (output in Q8) */
                SKP_Silk_resampler_private_AR2_impl.SKP_Silk_resampler_private_AR2(
                    S.sIIR, 0, buf2, RESAMPLER_DOWN_ORDER_FIR, @in, inOff, S.Coefs, 0, nSamplesIn );
            }

            max_index_Q16 = SKP_LSHIFT32( nSamplesIn, 16 );

            /* Interpolate filtered signal */
            if( S.FIR_Fracs == 1 )
            {
                outOff = SKP_Silk_resampler_private_down_FIR_INTERPOL0(
                    @out, outOff, buf2, 0, FIR_Coefs, FIR_CoefsOff, max_index_Q16, index_increment_Q16 );
            }
            else
            {
                outOff = SKP_Silk_resampler_private_down_FIR_INTERPOL1(
                    @out, outOff, buf2, 0, FIR_Coefs, FIR_CoefsOff, max_index_Q16, index_increment_Q16, S.FIR_Fracs );
            }

            inOff += nSamplesIn << S.input2x;
            inLen -= nSamplesIn << S.input2x;

            if( inLen > S.input2x )
            {
                /* More iterations to do; copy last part of filtered signal to beginning of buffer */
                SKP_memcpy( buf2, 0, buf2, nSamplesIn, RESAMPLER_DOWN_ORDER_FIR );
            }
            else
            {
                break;
            }
        }

        /* Copy last part of filtered signal to the state for the next call */
        SKP_memcpy( S.sFIR, 0, buf2, nSamplesIn, RESAMPLER_DOWN_ORDER_FIR );
    }
}
