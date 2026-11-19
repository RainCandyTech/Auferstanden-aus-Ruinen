#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_private_IIR_FIR.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_private_IIR_FIR.c
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
// File Name:	SKP_Silk_resampler_private_IIR_FIR.c  			    *
//																		*
// Description: Hybrid IIR/FIR polyphase implementation of resampling	*
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
// All rights reserved.													*
//                                                                      */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，函数名保持
// C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(委托): 本函数被赋给 resampler_state.resampler_function 字段
// （SKP_Silk_resampler_init 中上采样与 ARMA4 分数比降采样分支）；签名与地基
// 委托 SilkResamplerStateFn 完全一致。
// PORT-NOTE(void* 首参): C 首参 void* SS + 函数内 cast 为状态结构体；此处首参
// 直接是强类型状态对象，cast 行消失。
// PORT-NOTE(跨类型 memcpy): C 源三处 SKP_memcpy 的字节数是
// RESAMPLER_ORDER_FIR_144 * sizeof( SKP_int32 )（= 24 字节），而源/目标是
// int32[] sFIR ↔ int16[] buf——C 按字节跨类型拷贝（本 SDK 原样保留的怪癖）。
// gcc/x86 为小端：int32 的低 16 位落在地址较低的 int16 槽位。以下两个 helper
// 逐字节复现该拷贝，保证状态位等（12 个 int16 ↔ 6 个 int32 往返）。
// 其中后两处拷贝（buf→buf、buf→sFIR）在 nSamplesIn == batchSize 时源尾越界
// 读 6 个元素（C 原版未定义行为，详见主函数内 PORT-NOTE）——越界垃圾经论证
// 不影响任何输出位等，C# 侧按源合法前缀钳制。
// buf → buf 的那次 memcpy 目标与源同为 int16 数组，24 字节 = 12 个元素 =
// RESAMPLER_ORDER_FIR_144 * 2，用地基 SKP_memcpy 按元素个数照抄。
// PORT-NOTE(宏→常量): RESAMPLER_MAX_BATCH_SIZE_IN（480，SKP_Silk_resampler_
// private.h）按类内 const 复现；RESAMPLER_ORDER_FIR_144（6）与
// SKP_Silk_resampler_frac_FIR_144 表来自 SKP_Silk_resampler_rom（using static
// 裸名引用，与 C 调用点形态一致）。
// PORT-NOTE(#if): 默认构建（EMBEDDED_ARM=0）下 `#if EMBEDDED_ARM<5` 为真，
// 仅移植 C 代码体；SKP_INLINE → [MethodImpl(AggressiveInlining)]（§2）。
// PORT-NOTE(指针返回值): C 的 INTERPOL 返回推进后的 out 指针，移植为返回
// 推进后的 outOff（int）。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_resampler_rom;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_private_IIR_FIR_impl
{
    internal const int RESAMPLER_MAX_BATCH_SIZE_IN = 480;    /* SKP_Silk_resampler_private.h */

    /* 跨类型按字节 memcpy（见文件头 PORT-NOTE），nInt32 以 int32 元素计 */
    private static void memcpy_i32_to_i16( short[] dst, int dstOff, int[] src, int srcOff, int nInt32 )
    {
        for( int i = 0; i < nInt32; i++ )
        {
            dst[ dstOff + 2 * i ] = (short)( src[ srcOff + i ] & 0xFFFF );
            dst[ dstOff + 2 * i + 1 ] = (short)( ( (uint)src[ srcOff + i ] ) >> 16 );
        }
    }

    private static void memcpy_i16_to_i32( int[] dst, int dstOff, short[] src, int srcOff, int nInt32 )
    {
        // PORT-NOTE: nInt32 钳制到源内可完整读回的数量（见主函数末尾的越界怪癖说明）；
        // 源不足时不写 dst 对应项（保持原值），位等性见该说明。
        int avail = ( src.Length - srcOff ) / 2;
        if( avail < nInt32 )
        {
            nInt32 = avail;
        }
        for( int i = 0; i < nInt32; i++ )
        {
            dst[ dstOff + i ] = ( (int)(ushort)src[ srcOff + 2 * i ] ) | ( ( (int)(ushort)src[ srcOff + 2 * i + 1 ] ) << 16 );
        }
    }

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static int SKP_Silk_resampler_private_IIR_FIR_INTERPOL(
        short[] @out, int outOff,
        short[] buf, int bufOff,
        int max_index_Q16, int index_increment_Q16 )
    {
        int index_Q16, res_Q15;
        int buf_ptrOff;
        int table_index;

        /* Interpolate upsampled signal and store in output array */
        for( index_Q16 = 0; index_Q16 < max_index_Q16; index_Q16 += index_increment_Q16 )
        {
            table_index = SKP_SMULWB( index_Q16 & 0xFFFF, 144 );
            buf_ptrOff = bufOff + ( index_Q16 >> 16 );

            res_Q15 = SKP_SMULBB( buf[ buf_ptrOff + 0 ], SKP_Silk_resampler_frac_FIR_144[ table_index ][ 0 ] );
            res_Q15 = SKP_SMLABB( res_Q15, buf[ buf_ptrOff + 1 ], SKP_Silk_resampler_frac_FIR_144[ table_index ][ 1 ] );
            res_Q15 = SKP_SMLABB( res_Q15, buf[ buf_ptrOff + 2 ], SKP_Silk_resampler_frac_FIR_144[ table_index ][ 2 ] );
            res_Q15 = SKP_SMLABB( res_Q15, buf[ buf_ptrOff + 3 ], SKP_Silk_resampler_frac_FIR_144[ 143 - table_index ][ 2 ] );
            res_Q15 = SKP_SMLABB( res_Q15, buf[ buf_ptrOff + 4 ], SKP_Silk_resampler_frac_FIR_144[ 143 - table_index ][ 1 ] );
            res_Q15 = SKP_SMLABB( res_Q15, buf[ buf_ptrOff + 5 ], SKP_Silk_resampler_frac_FIR_144[ 143 - table_index ][ 0 ] );
            @out[ outOff++ ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( res_Q15, 15 ) );
        }
        return outOff;
    }

    /* Upsample using a combination of allpass-based 2x upsampling and FIR interpolation */
    public static void SKP_Silk_resampler_private_IIR_FIR(
        SKP_Silk_resampler_state_struct S,  /* I/O: Resampler state 					*/
        short[] @out, int outOff,           /* O:	Output signal 						*/
        short[] @in, int inOff,             /* I:	Input signal						*/
        int inLen                           /* I:	Number of input samples				*/
    )
    {
        int nSamplesIn;
        int max_index_Q16, index_increment_Q16;
        short[] buf = new short[ 2 * RESAMPLER_MAX_BATCH_SIZE_IN + RESAMPLER_ORDER_FIR_144 ];

        /* Copy buffered samples to start of buffer */
        memcpy_i32_to_i16( buf, 0, S.sFIR, 0, RESAMPLER_ORDER_FIR_144 );

        /* Iterate over blocks of frameSizeIn input samples */
        index_increment_Q16 = S.invRatio_Q16;
        while( true )
        {
            nSamplesIn = SKP_min( inLen, S.batchSize );

            if( S.input2x == 1 )
            {
                /* Upsample 2x */
                // PORT-NOTE: 委托调用点按 §3 补偏移实参：sIIR 基址 0、buf 起始
                // RESAMPLER_ORDER_FIR_144（C 传 &buf[ RESAMPLER_ORDER_FIR_144 ]）。
                S.up2_function( S.sIIR, 0, buf, RESAMPLER_ORDER_FIR_144, @in, inOff, nSamplesIn );
            }
            else
            {
                /* Fourth-order ARMA filter */
                SKP_Silk_resampler_private_ARMA4_impl.SKP_Silk_resampler_private_ARMA4(
                    S.sIIR, 0, buf, RESAMPLER_ORDER_FIR_144, @in, inOff, S.Coefs, 0, nSamplesIn );
            }

            max_index_Q16 = SKP_LSHIFT32( nSamplesIn, 16 + S.input2x );         /* +1 if 2x upsampling */
            outOff = SKP_Silk_resampler_private_IIR_FIR_INTERPOL( @out, outOff, buf, 0, max_index_Q16, index_increment_Q16 );
            inOff += nSamplesIn;
            inLen -= nSamplesIn;

            if( inLen > 0 )
            {
                /* More iterations to do; copy last part of filtered signal to beginning of buffer */
                // PORT-NOTE(C 越界怪癖): C 此处 SKP_memcpy 从 &buf[ nSamplesIn << S->input2x ]
                // 拷 RESAMPLER_ORDER_FIR_144 * sizeof( SKP_int32 ) = 24 字节（12 个 int16）。
                // 当本批 nSamplesIn == S.batchSize（如 48000→44100: batchSize=480, input2x=1）
                // 时源尾 960 + 12 = 972 超出 buf 尺寸 966——C 原版越界读 6 个元素（未定义
                // 行为，x86 静默成功）。越界垃圾只落入 buf[6..11]；该区在下批要么被
                // 2x 上采样/ARMA4 输出完全覆盖（写 buf[6..6+2n-1]，而 INTERPOL 只读
                // buf[0..2n+4]，6+2n-1 >= 2n+4 恒成立），要么位于 INTERPOL 读取范围之外，
                // 故不影响输出位等（已由 C vs C# 全路径逐字节比对证实）。C# 数组边界严格，
                // 此处把拷贝长度钳制到源合法前缀（真正的 FIR 历史 buf[src..src+5] 必含在内）。
                int srcOff = nSamplesIn << S.input2x;
                int nCopy = RESAMPLER_ORDER_FIR_144 * 2;
                if( srcOff + nCopy > buf.Length )
                {
                    nCopy = buf.Length - srcOff;
                }
                SKP_memcpy( buf, 0, buf, srcOff, nCopy );
            }
            else
            {
                break;
            }
        }

        /* Copy last part of filtered signal to the state for the next call */
        // PORT-NOTE(C 越界怪癖): 与上一处同理——C 的 SKP_memcpy 以 24 字节把 buf 源尾
        // 跨类型读回 sFIR（6 个 int32），源尾同样可能越界 6 个 int16（落入 sFir 的后半
        // 段）。该段在下次调用恢复进 buf[6..11] 后按上注论证不影响输出位等。C# 侧按源
        // 合法前缀钳制（可完整读回的 int32 不足 RESAMPLER_ORDER_FIR_144 时少写，对应
        // sFIR 后半段保持原值；真正被使用的 FIR 历史必在前 2 个 int32 内）。
        memcpy_i16_to_i32( S.sFIR, 0, buf, nSamplesIn << S.input2x, RESAMPLER_ORDER_FIR_144 );
    }
}
