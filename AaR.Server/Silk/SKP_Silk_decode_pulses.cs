#nullable disable
// ***********************************************************************
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
// NO EXPRESS OR IMPLIED LICENSES ARE GRANTED TO ANY PARTY'S PATENT RIGHTS
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
//
// SKP_Silk_decode_pulses.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_decode_pulses.c（激励量化索引解码，shell 解码）
//
// PORT-NOTE: 本文件主函数与 C 文件同名（SKP_Silk_decode_pulses），
//   C# 禁止成员名与其包含类型同名（CS0542），故类名加后缀 _impl，
//   方法名保持 C 原名（本簇统一约定，详见 SKP_Silk_enc_API.cs 文件头）。
//   C 局部指针 pulses_ptr / cdf_ptr 改为（基址数组, 偏移）表示。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 其他簇（类名 = C 文件名，PORTING.md §1；以下三类的主函数均不与其文件同名）
using static AaR.Server.Silk.SKP_Silk_range_coder;                 // SKP_Silk_range_decoder
using static AaR.Server.Silk.SKP_Silk_shell_coder;                 // SKP_Silk_shell_decoder
using static AaR.Server.Silk.SKP_Silk_code_signs;                 // SKP_Silk_decode_signs
// 表（其他簇）
using static AaR.Server.Silk.SKP_Silk_tables_pulses_per_block;    // rate_levels / pulses_per_block CDF
using static AaR.Server.Silk.SKP_Silk_tables_other;                 // SKP_Silk_lsb_CDF（表实际位于 tables_other）

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_decode_pulses.c —— 激励量化索引解码。
/// </summary>
internal static class SKP_Silk_decode_pulses_impl
{
    /*********************************************/
    /* Decode quantization indices of excitation */
    /*********************************************/
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）：q 为输出数组加偏移。
    //   SKP_Silk_range_decoder 的 data 参数按 §3 规则 1 移植为 out int。
    public static void SKP_Silk_decode_pulses(
        SKP_Silk_range_coder_state      psRC,               /* I/O  Range coder state                           */
        SKP_Silk_decoder_control        psDecCtrl,           /* I/O  Decoder control                             */
        int[]                           q, int qOff,         /* O    Excitation signal                           */
        int                             frame_length          /* I    Frame length (preliminary)                  */
    )
    {
        int   i, j, k, iter, abs_q, nLS, bit;
        int[] sum_pulses = new int[ MAX_NB_SHELL_BLOCKS ], nLshifts = new int[ MAX_NB_SHELL_BLOCKS ];
        int   pulses_ptr;
        ushort[] cdf_ptr;

        /*********************/
        /* Decode rate level */
        /*********************/
        SKP_Silk_range_decoder( out psDecCtrl.RateLevelIndex, psRC,
                SKP_Silk_rate_levels_CDF[ psDecCtrl.sigtype ], 0, SKP_Silk_rate_levels_CDF_offset );

        /* Calculate number of shell blocks */
        iter = frame_length / SHELL_CODEC_FRAME_LENGTH;

        /***************************************************/
        /* Sum-Weighted-Pulses Decoding                    */
        /***************************************************/
        cdf_ptr = SKP_Silk_pulses_per_block_CDF[ psDecCtrl.RateLevelIndex ];
        for( i = 0; i < iter; i++ ) {
            nLshifts[ i ] = 0;
            SKP_Silk_range_decoder( out sum_pulses[ i ], psRC, cdf_ptr, 0, SKP_Silk_pulses_per_block_CDF_offset );

            /* LSB indication */
            while( sum_pulses[ i ] == ( MAX_PULSES + 1 ) ) {
                nLshifts[ i ]++;
                SKP_Silk_range_decoder( out sum_pulses[ i ], psRC,
                        SKP_Silk_pulses_per_block_CDF[ N_RATE_LEVELS - 1 ], 0, SKP_Silk_pulses_per_block_CDF_offset );
            }
        }

        /***************************************************/
        /* Shell decoding                                  */
        /***************************************************/
        for( i = 0; i < iter; i++ ) {
            if( sum_pulses[ i ] > 0 ) {
                SKP_Silk_shell_decoder( q, qOff + SKP_SMULBB( i, SHELL_CODEC_FRAME_LENGTH ), psRC, sum_pulses[ i ] );
            } else {
                SKP_memset( q, qOff + SKP_SMULBB( i, SHELL_CODEC_FRAME_LENGTH ), 0, SHELL_CODEC_FRAME_LENGTH );
            }
        }

        /***************************************************/
        /* LSB Decoding                                    */
        /***************************************************/
        for( i = 0; i < iter; i++ ) {
            if( nLshifts[ i ] > 0 ) {
                nLS = nLshifts[ i ];
                // PORT-NOTE: C 局部指针 pulses_ptr = &q[ SKP_SMULBB( i, SHELL_CODEC_FRAME_LENGTH ) ]
                //   → 基址 q 固定 + 偏移局部量 pulses_ptr。
                pulses_ptr = qOff + SKP_SMULBB( i, SHELL_CODEC_FRAME_LENGTH );
                for( k = 0; k < SHELL_CODEC_FRAME_LENGTH; k++ ) {
                    abs_q = q[ pulses_ptr + k ];
                    for( j = 0; j < nLS; j++ ) {
                        abs_q = SKP_LSHIFT( abs_q, 1 );
                        SKP_Silk_range_decoder( out bit, psRC, SKP_Silk_lsb_CDF, 0, 1 );
                        abs_q += bit;
                    }
                    q[ pulses_ptr + k ] = abs_q;
                }
            }
        }

        /****************************************/
        /* Decode and add signs to pulse signal */
        /****************************************/
        SKP_Silk_decode_signs( psRC, q, qOff, frame_length, psDecCtrl.sigtype,
            psDecCtrl.QuantOffsetType, psDecCtrl.RateLevelIndex);
    }
}
