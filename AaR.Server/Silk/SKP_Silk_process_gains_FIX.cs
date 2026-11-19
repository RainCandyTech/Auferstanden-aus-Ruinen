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
// SKP_Silk_process_gains_FIX.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_process_gains_FIX.c（函数 SKP_Silk_process_gains_FIX）
// 移植说明：
//   - SKP_Silk_sigm_Q15 / SKP_Silk_log2lin / SKP_Silk_gains_quant 为 DSP 簇产物，
//     按原型推导签名，裸名调用；SKP_Silk_gains_quant 的 &psShapeSt->LastGainIndex
//     为标量先读后写 → ref。
//   - 表 SKP_Silk_Quantization_Offsets_Q10 为表簇产物（static readonly 交错数组
//     short[][]），裸名引用。
//   - SKP_FIX_CONST 的裸双精度字面量按地基方法（float 参数）写作 0.33f 等形式；
//     本文件内全部字面量已实测 float 语义 == gcc 双精度语义（位等）。
//   - SKP_assert 在 C 构建中为空宏，按移植规约删除。
// PORT-NOTE: 同 SKP_Silk_control_audio_bandwidth.cs 头注——类名加 _impl（CS0542）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_Inlines;
using static AaR.Server.Silk.SKP_Silk_tables_other;
using static AaR.Server.Silk.SKP_Silk_sigm_Q15_impl;
using static AaR.Server.Silk.SKP_Silk_log2lin_impl;
using static AaR.Server.Silk.SKP_Silk_gain_quant;

namespace AaR.Server.Silk;

/// <summary>src/SKP_Silk_process_gains_FIX.c —— 增益处理。</summary>
internal static class SKP_Silk_process_gains_FIX_impl
{
    /* Processing of gains */
    public static void SKP_Silk_process_gains_FIX(
        SKP_Silk_encoder_state_FIX psEnc,        /* I/O  Encoder state_FIX                           */
        SKP_Silk_encoder_control_FIX psEncCtrl   /* I/O  Encoder control_FIX                         */
        )
    {
        SKP_Silk_shape_state_FIX psShapeSt = psEnc.sShape;
        int k;
        int s_Q16, InvMaxSqrVal_Q16, gain, gain_squared, ResNrg, ResNrgPart, quant_offset_Q10;

        /* Gain reduction when LTP coding gain is high */
        if( psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED )
        {
            /*s = -0.5f * SKP_sigmoid( 0.25f * ( psEncCtrl->LTPredCodGain - 12.0f ) ); */
            s_Q16 = -SKP_Silk_sigm_Q15( SKP_RSHIFT_ROUND( psEncCtrl.LTPredCodGain_Q7 - SKP_FIX_CONST( 12.0f, 7 ), 4 ) );
            for( k = 0; k < NB_SUBFR; k++ )
            {
                psEncCtrl.Gains_Q16[ k ] = SKP_SMLAWB( psEncCtrl.Gains_Q16[ k ], psEncCtrl.Gains_Q16[ k ], s_Q16 );
            }
        }

        /* Limit the quantized signal */
        InvMaxSqrVal_Q16 = SKP_DIV32_16( SKP_Silk_log2lin(
            SKP_SMULWB( SKP_FIX_CONST( 70.0f, 7 ) - psEncCtrl.current_SNR_dB_Q7, SKP_FIX_CONST( 0.33f, 16 ) ) ), psEnc.sCmn.subfr_length );

        for( k = 0; k < NB_SUBFR; k++ )
        {
            /* Soft limit on ratio residual energy and squared gains */
            ResNrg     = psEncCtrl.ResNrg[ k ];
            ResNrgPart = SKP_SMULWW( ResNrg, InvMaxSqrVal_Q16 );
            if( psEncCtrl.ResNrgQ[ k ] > 0 )
            {
                if( psEncCtrl.ResNrgQ[ k ] < 32 )
                {
                    ResNrgPart = SKP_RSHIFT_ROUND( ResNrgPart, psEncCtrl.ResNrgQ[ k ] );
                }
                else
                {
                    ResNrgPart = 0;
                }
            }
            else if( psEncCtrl.ResNrgQ[ k ] != 0 )
            {
                if( ResNrgPart > SKP_RSHIFT( int.MaxValue /* SKP_int32_MAX */, -psEncCtrl.ResNrgQ[ k ] ) )
                {
                    ResNrgPart = int.MaxValue;   /* SKP_int32_MAX */
                }
                else
                {
                    ResNrgPart = SKP_LSHIFT( ResNrgPart, -psEncCtrl.ResNrgQ[ k ] );
                }
            }
            gain = psEncCtrl.Gains_Q16[ k ];
            gain_squared = SKP_ADD_SAT32( ResNrgPart, SKP_SMMUL( gain, gain ) );
            if( gain_squared < short.MaxValue /* SKP_int16_MAX */ )
            {
                /* recalculate with higher precision */
                gain_squared = SKP_SMLAWW( SKP_LSHIFT( ResNrgPart, 16 ), gain, gain );
                // SKP_assert( gain_squared > 0 );（删除）
                gain = SKP_Silk_SQRT_APPROX( gain_squared );                  /* Q8   */
                psEncCtrl.Gains_Q16[ k ] = SKP_LSHIFT_SAT32( gain, 8 );        /* Q16  */
            }
            else
            {
                gain = SKP_Silk_SQRT_APPROX( gain_squared );                  /* Q0   */
                psEncCtrl.Gains_Q16[ k ] = SKP_LSHIFT_SAT32( gain, 16 );      /* Q16  */
            }
        }

        /* Noise shaping quantization */
        SKP_Silk_gains_quant( psEncCtrl.sCmn.GainsIndices, 0, psEncCtrl.Gains_Q16, 0,
            ref psShapeSt.LastGainIndex, psEnc.sCmn.nFramesInPayloadBuf );
        /* Set quantizer offset for voiced signals. Larger offset when LTP coding gain is low or tilt is high (ie low-pass) */
        if( psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED )
        {
            if( psEncCtrl.LTPredCodGain_Q7 + SKP_RSHIFT( psEncCtrl.input_tilt_Q15, 8 ) > SKP_FIX_CONST( 1.0f, 7 ) )
            {
                psEncCtrl.sCmn.QuantOffsetType = 0;
            }
            else
            {
                psEncCtrl.sCmn.QuantOffsetType = 1;
            }
        }

        /* Quantizer boundary adjustment */
        quant_offset_Q10 = SKP_Silk_Quantization_Offsets_Q10[ psEncCtrl.sCmn.sigtype ][ psEncCtrl.sCmn.QuantOffsetType ];
        psEncCtrl.Lambda_Q10 = SKP_FIX_CONST( LAMBDA_OFFSET, 10 )
                              + SKP_SMULBB( SKP_FIX_CONST( LAMBDA_DELAYED_DECISIONS, 10 ), psEnc.sCmn.nStatesDelayedDecision )
                              + SKP_SMULWB( SKP_FIX_CONST( LAMBDA_SPEECH_ACT,        18 ), psEnc.speech_activity_Q8          )
                              + SKP_SMULWB( SKP_FIX_CONST( LAMBDA_INPUT_QUALITY,     12 ), psEncCtrl.input_quality_Q14       )
                              + SKP_SMULWB( SKP_FIX_CONST( LAMBDA_CODING_QUALITY,    12 ), psEncCtrl.coding_quality_Q14      )
                              + SKP_SMULWB( SKP_FIX_CONST( LAMBDA_QUANT_OFFSET,      16 ), quant_offset_Q10                   );

        // SKP_assert( psEncCtrl->Lambda_Q10 > 0 );（删除）
        // SKP_assert( psEncCtrl->Lambda_Q10 < SKP_FIX_CONST( 2, 10 ) );（删除）
    }
}
