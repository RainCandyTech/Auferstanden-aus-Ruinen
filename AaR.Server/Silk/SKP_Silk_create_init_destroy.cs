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
// SKP_Silk_create_init_destroy.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_create_init_destroy.c（解码器状态初始化）
//
// PORT-NOTE: 本 SDK v1.0.9 的 create_init_destroy.c 仅含 SKP_Silk_init_decoder。
//   Get_Encoder_Size / Get_Decoder_Size 分别位于 enc_API.cs 与 dec_API.cs（按 C
//   源实际归属），SKP_Silk_SDK_get_version 位于 dec_API.c（非本文件），均按
//   PORTING.md §1 的"文件对应"规则就近移植，未在本文件重复。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 本簇其他文件
using static AaR.Server.Silk.SKP_Silk_decoder_set_fs_impl;        // SKP_Silk_decoder_set_fs（本簇）
// 其他簇（类名 = C 文件名，PORTING.md §1）
// PORT-NOTE: SKP_Silk_CNG.c / SKP_Silk_PLC.c 的主函数与其文件同名（CS0542/CS1955
//   受影响文件），若集成期类名统一加后缀，仅需更新下面两行 using static。
using static AaR.Server.Silk.SKP_Silk_CNG_impl;                             // SKP_Silk_CNG_Reset
using static AaR.Server.Silk.SKP_Silk_PLC_impl;                             // SKP_Silk_PLC_Reset

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_create_init_destroy.c —— 解码器状态初始化。
/// </summary>
internal static class SKP_Silk_create_init_destroy
{
    /************************/
    /* Init Decoder State   */
    /************************/
    // PORT-NOTE: C 的整结构体 SKP_memset( psDec, 0, sizeof( SKP_Silk_decoder_state ) )
    //   → 地基已提供的 psDec.Clear()。
    public static int SKP_Silk_init_decoder(
        SKP_Silk_decoder_state      psDec                 /* I/O  Decoder state pointer                       */
    )
    {
        psDec.Clear();
        /* Set sampling rate to 24 kHz, and init non-zero values */
        SKP_Silk_decoder_set_fs( psDec, 24 );

        /* Used to deactivate e.g. LSF interpolation and fluctuation reduction */
        psDec.first_frame_after_reset = 1;
        psDec.prev_inv_gain_Q16 = 65536;

        /* Reset CNG state */
        SKP_Silk_CNG_Reset( psDec );

        SKP_Silk_PLC_Reset( psDec );

        return(0);
    }
}
