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
// SKP_Silk_decode_pitch.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_decode_pitch.c（基音滞后解码）
//
// PORT-NOTE: 本文件主函数与 C 文件同名（SKP_Silk_decode_pitch），
//   C# 禁止成员名与其包含类型同名（CS0542），故类名加后缀 _impl，
//   方法名保持 C 原名（本簇统一约定，详见 SKP_Silk_enc_API.cs 文件头）。
//   SKP_Silk_CB_lags_stage2/3 为二维表 → 交错数组（PORTING.md §5），
//   访问点 tbl[ i ][ contourIndex ] 形态不变。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 表（其他簇）
using static AaR.Server.Silk.SKP_Silk_pitch_est_tables;           // SKP_Silk_CB_lags_stage2 / _stage3

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_decode_pitch.c —— 基音滞后解码。
/// </summary>
internal static class SKP_Silk_decode_pitch_impl
{
    /***********************************************************
    * Pitch analyser function
    ********************************************************** */
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）：pitch_lags 加偏移。
    public static void SKP_Silk_decode_pitch(
        int          lagIndex,                        /* I                             */
        int          contourIndex,                    /* O                             */
        int[]        pitch_lags, int pitch_lagsOff,   /* O 4 pitch values              */
        int          Fs_kHz                           /* I sampling frequency (kHz)    */
    )
    {
        int lag, i, min_lag;

        min_lag = SKP_SMULBB( PITCH_EST_MIN_LAG_MS, Fs_kHz );

        /* Only for 24 / 16 kHz version for now */
        lag = min_lag + lagIndex;
        if( Fs_kHz == 8 ) {
            /* Only a small codebook for 8 khz */
            for( i = 0; i < PITCH_EST_NB_SUBFR; i++ ) {
                pitch_lags[ pitch_lagsOff + i ] = lag + SKP_Silk_CB_lags_stage2[ i ][ contourIndex ];
            }
        } else {
            for( i = 0; i < PITCH_EST_NB_SUBFR; i++ ) {
                pitch_lags[ pitch_lagsOff + i ] = lag + SKP_Silk_CB_lags_stage3[ i ][ contourIndex ];
            }
        }
    }
}
