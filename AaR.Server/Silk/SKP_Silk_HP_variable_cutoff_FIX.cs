#nullable disable
// ***********************************************************************
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.
// Redistribution and use in source and binary forms, with or without
// modification, (subject to the limitations in the disclaimer below)
// are permitted provided that the following conditions are met:
// - Redistributions of source code must retain the above copyright notice,
//   this list of conditions and the following disclaimer.
// - Redistributions in binary form must reproduce the above copyright
//   notice, this list of conditions and the following disclaimer in the
//   documentation and/or other materials provided with the distribution.
// - Neither the name of Skype Limited, nor the names of specific
//   contributors, may be used to endorse or promote products derived from
//   this software without specific prior written permission.
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
// SKP_Silk_HP_variable_cutoff_FIX.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_HP_variable_cutoff_FIX.c（函数 SKP_Silk_HP_variable_cutoff_FIX）
// 移植说明：
//   - HIGH_PASS_INPUT = 1（默认 gcc 构建），整个文件位于其激活分支内。
//   - SKP_Silk_biquad_alt / SKP_Silk_lin2log / SKP_Silk_log2lin 为 DSP 簇产物，
//     按 SigProc_FIX.h 原型推导签名，裸名调用。
//   - SKP_FIX_CONST 的裸双精度字面量按地基方法（float 参数）写作 0.6f 等形式；
//     本文件内全部字面量已实测 float 语义 == gcc 双精度语义（位等）。
//   - SKP_assert 在 C 构建中为空宏，按移植规约删除。
// PORT-NOTE: 同 SKP_Silk_control_audio_bandwidth.cs 头注——类名加 _impl（CS0542）。
//   参数名 out / in 为 C# 关键字/上下文关键字，写作 @out / @in（标识符不变）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_biquad_alt_impl;
using static AaR.Server.Silk.SKP_Silk_lin2log_impl;
using static AaR.Server.Silk.SKP_Silk_log2lin_impl;

namespace AaR.Server.Silk;

/// <summary>src/SKP_Silk_HP_variable_cutoff_FIX.c —— 基于基音滞后统计自适应截止频率的高通滤波。</summary>
internal static class SKP_Silk_HP_variable_cutoff_FIX_impl
{
    public const int SKP_RADIANS_CONSTANT_Q19 = 1482;        // 0.45f * 2.0f * 3.14159265359 / 1000
    public const int SKP_LOG2_VARIABLE_HP_MIN_FREQ_Q7 = 809; // log(80) in Q7

    /* High-pass filter with cutoff frequency adaptation based on pitch lag statistics */
    public static void SKP_Silk_HP_variable_cutoff_FIX(
        SKP_Silk_encoder_state_FIX psEnc,         /* I/O  Encoder state FIX                           */
        SKP_Silk_encoder_control_FIX psEncCtrl,    /* I/O  Encoder control FIX                         */
        short[] @out, int outOff,                  /* O    high-pass filtered output signal            */
        short[] @in, int inOff                     /* I    input signal                                */
        )
    {
        int quality_Q15;
        int[] B_Q28 = new int[ 3 ], A_Q28 = new int[ 2 ];
        int Fc_Q19, r_Q28, r_Q22;
        int pitch_freq_Hz_Q16, pitch_freq_log_Q7, delta_freq_Q7;

        /*********************************************/
        /* Estimate Low End of Pitch Frequency Range */
        /*********************************************/
        if( psEnc.sCmn.prev_sigtype == SIG_TYPE_VOICED )
        {
            /* difference, in log domain */
            pitch_freq_Hz_Q16 = SKP_DIV32_16( SKP_LSHIFT( SKP_MUL( psEnc.sCmn.fs_kHz, 1000 ), 16 ), psEnc.sCmn.prevLag );
            pitch_freq_log_Q7 = SKP_Silk_lin2log( pitch_freq_Hz_Q16 ) - ( 16 << 7 ); //0x70

            /* adjustment based on quality */
            quality_Q15 = psEncCtrl.input_quality_bands_Q15[ 0 ];
            pitch_freq_log_Q7 = SKP_SUB32( pitch_freq_log_Q7, SKP_SMULWB( SKP_SMULWB( SKP_LSHIFT( quality_Q15, 2 ), quality_Q15 ),
                pitch_freq_log_Q7 - SKP_LOG2_VARIABLE_HP_MIN_FREQ_Q7 ) );
            pitch_freq_log_Q7 = SKP_ADD32( pitch_freq_log_Q7, SKP_RSHIFT( SKP_FIX_CONST( 0.6f, 15 ) - quality_Q15, 9 ) );

            //delta_freq = pitch_freq_log - psEnc->variable_HP_smth1;
            delta_freq_Q7 = pitch_freq_log_Q7 - SKP_RSHIFT( psEnc.variable_HP_smth1_Q15, 8 );
            if( delta_freq_Q7 < 0 )
            {
                /* less smoothing for decreasing pitch frequency, to track something close to the minimum */
                delta_freq_Q7 = SKP_MUL( delta_freq_Q7, 3 );
            }

            /* limit delta, to reduce impact of outliers */
            delta_freq_Q7 = SKP_LIMIT_32( delta_freq_Q7, -SKP_FIX_CONST( VARIABLE_HP_MAX_DELTA_FREQ, 7 ), SKP_FIX_CONST( VARIABLE_HP_MAX_DELTA_FREQ, 7 ) );

            /* update smoother */
            psEnc.variable_HP_smth1_Q15 = SKP_SMLAWB( psEnc.variable_HP_smth1_Q15,
                SKP_MUL( SKP_LSHIFT( psEnc.speech_activity_Q8, 1 ), delta_freq_Q7 ), SKP_FIX_CONST( VARIABLE_HP_SMTH_COEF1, 16 ) );
        }
        /* second smoother */
        psEnc.variable_HP_smth2_Q15 = SKP_SMLAWB( psEnc.variable_HP_smth2_Q15,
            psEnc.variable_HP_smth1_Q15 - psEnc.variable_HP_smth2_Q15, SKP_FIX_CONST( VARIABLE_HP_SMTH_COEF2, 16 ) );

        /* convert from log scale to Hertz */
        psEncCtrl.pitch_freq_low_Hz = SKP_Silk_log2lin( SKP_RSHIFT( psEnc.variable_HP_smth2_Q15, 8 ) );

        /* limit frequency range */
        psEncCtrl.pitch_freq_low_Hz = SKP_LIMIT_32( psEncCtrl.pitch_freq_low_Hz,
            SKP_FIX_CONST( VARIABLE_HP_MIN_FREQ, 0 ), SKP_FIX_CONST( VARIABLE_HP_MAX_FREQ, 0 ) );

        /********************************/
        /* Compute Filter Coefficients  */
        /********************************/
        /* compute cut-off frequency, in radians */
        //Fc_num   = (SKP_float)( 0.45f * 2.0f * 3.14159265359 * psEncCtrl->pitch_freq_low_Hz );
        //Fc_denom = (SKP_float)( 1e3f * psEnc->sCmn.fs_kHz );
        // SKP_assert( psEncCtrl->pitch_freq_low_Hz <= SKP_int32_MAX / SKP_RADIANS_CONSTANT_Q19 );（删除）
        Fc_Q19 = SKP_DIV32_16( SKP_SMULBB( SKP_RADIANS_CONSTANT_Q19, psEncCtrl.pitch_freq_low_Hz ), psEnc.sCmn.fs_kHz ); // range: 3704 - 27787, 11-15 bits
        // SKP_assert( Fc_Q19 >=  3704 );（删除）
        // SKP_assert( Fc_Q19 <= 27787 );（删除）

        r_Q28 = SKP_FIX_CONST( 1.0f, 28 ) - SKP_MUL( SKP_FIX_CONST( 0.92f, 9 ), Fc_Q19 );
        // SKP_assert( r_Q28 >= 255347779 );（删除）
        // SKP_assert( r_Q28 <= 266690872 );（删除）

        /* b = r * [ 1; -2; 1 ]; */
        /* a = [ 1; -2 * r * ( 1 - 0.5 * Fc^2 ); r^2 ]; */
        B_Q28[ 0 ] = r_Q28;
        B_Q28[ 1 ] = SKP_LSHIFT( -r_Q28, 1 );
        B_Q28[ 2 ] = r_Q28;

        // -r * ( 2 - Fc * Fc );
        r_Q22  = SKP_RSHIFT( r_Q28, 6 );
        A_Q28[ 0 ] = SKP_SMULWW( r_Q22, SKP_SMULWW( Fc_Q19, Fc_Q19 ) - SKP_FIX_CONST( 2.0f, 22 ) );
        A_Q28[ 1 ] = SKP_SMULWW( r_Q22, r_Q22 );

        /********************************/
        /* High-Pass Filter             */
        /********************************/
        SKP_Silk_biquad_alt( @in, inOff, B_Q28, 0, A_Q28, 0, psEnc.sCmn.In_HP_State, 0, @out, outOff, psEnc.sCmn.frame_length );
    }
}
