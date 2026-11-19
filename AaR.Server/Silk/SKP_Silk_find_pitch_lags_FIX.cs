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
// SKP_Silk_find_pitch_lags_FIX.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_find_pitch_lags_FIX.c（函数 SKP_Silk_find_pitch_lags_FIX）
// 移植说明：
//   - C 中 x_buf = x - psEnc->sCmn.frame_length 为负偏移指针（指向 x 数组之前的
//     历史样本，由调用方保证缓冲含历史段）；移植为 ( x, x_bufOff ) 偏移对，
//     x_buf_ptr / Wsig_ptr 同理全部改为偏移量。
//   - SKP_Silk_apply_sine_window / autocorr / schur / k2a / bwexpander / MA_Prediction
//     为 DSP 簇产物，按原型推导签名（数组 + 偏移），裸名调用。
//   - SKP_Silk_pitch_analysis_core 为本簇 SKP_Silk_pitch_analysis_core.cs 的产物。
//   - SKP_FIX_CONST 的裸双精度字面量按地基方法（float 参数）写作 0.45f 等形式；
//     本文件内全部字面量已实测 float 语义 == gcc 双精度语义（位等）。
//   - SKP_assert 在 C 构建中为空宏，按移植规约删除。
// PORT-NOTE: 同 SKP_Silk_control_audio_bandwidth.cs 头注——类名加 _impl（CS0542）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_Inlines;
using static AaR.Server.Silk.SKP_Silk_apply_sine_window_impl;
using static AaR.Server.Silk.SKP_Silk_autocorr_impl;
using static AaR.Server.Silk.SKP_Silk_schur_impl;
using static AaR.Server.Silk.SKP_Silk_k2a_impl;
using static AaR.Server.Silk.SKP_Silk_bwexpander_impl;
using static AaR.Server.Silk.SKP_Silk_MA;
using static AaR.Server.Silk.SKP_Silk_pitch_analysis_core_impl;

namespace AaR.Server.Silk;

/// <summary>src/SKP_Silk_find_pitch_lags_FIX.c —— 寻找基音滞后。</summary>
internal static class SKP_Silk_find_pitch_lags_FIX_impl
{
    /* Find pitch lags */
    public static void SKP_Silk_find_pitch_lags_FIX(
        SKP_Silk_encoder_state_FIX psEnc,           /* I/O  encoder state                               */
        SKP_Silk_encoder_control_FIX psEncCtrl,     /* I/O  encoder control                             */
        short[] res, int resOff,                     /* O    residual                                    */
        short[] x, int xOff                          /* I    Speech signal                               */
        )
    {
        SKP_Silk_predict_state_FIX psPredSt = psEnc.sPred;
        int buf_len, i, scale;
        int thrhld_Q15, res_nrg;
        // PORT-NOTE: C 的 const SKP_int16 *x_buf / *x_buf_ptr / *Wsig_ptr 为指针步进，
        //   这里改为「同一数组 + 偏移」表达（x_bufOff / x_buf_ptrOff / Wsig_ptrOff）。
        int x_bufOff, x_buf_ptrOff;
        short[] Wsig = new short[ FIND_PITCH_LPC_WIN_MAX ];
        int Wsig_ptrOff;
        int[] auto_corr = new int[ MAX_FIND_PITCH_LPC_ORDER + 1 ];
        short[] rc_Q15 = new short[ MAX_FIND_PITCH_LPC_ORDER ];
        int[] A_Q24 = new int[ MAX_FIND_PITCH_LPC_ORDER ];
        int[] FiltState = new int[ MAX_FIND_PITCH_LPC_ORDER ];
        short[] A_Q12 = new short[ MAX_FIND_PITCH_LPC_ORDER ];

        /******************************************/
        /* Setup buffer lengths etc based on Fs   */
        /******************************************/
        buf_len = SKP_ADD_LSHIFT( psEnc.sCmn.la_pitch, psEnc.sCmn.frame_length, 1 );

        /* Safty check */
        // SKP_assert( buf_len >= psPredSt->pitch_LPC_win_length );（删除）

        x_bufOff = xOff - psEnc.sCmn.frame_length;

        /*************************************/
        /* Estimate LPC AR coefficients      */
        /*************************************/

        /* Calculate windowed signal */

        /* First LA_LTP samples */
        x_buf_ptrOff = x_bufOff + buf_len - psPredSt.pitch_LPC_win_length;
        Wsig_ptrOff = 0;
        SKP_Silk_apply_sine_window( Wsig, Wsig_ptrOff, x, x_buf_ptrOff, 1, psEnc.sCmn.la_pitch );

        /* Middle un - windowed samples */
        Wsig_ptrOff += psEnc.sCmn.la_pitch;
        x_buf_ptrOff += psEnc.sCmn.la_pitch;
        SKP_memcpy( Wsig, Wsig_ptrOff, x, x_buf_ptrOff, psPredSt.pitch_LPC_win_length - SKP_LSHIFT( psEnc.sCmn.la_pitch, 1 ) );

        /* Last LA_LTP samples */
        Wsig_ptrOff += psPredSt.pitch_LPC_win_length - SKP_LSHIFT( psEnc.sCmn.la_pitch, 1 );
        x_buf_ptrOff += psPredSt.pitch_LPC_win_length - SKP_LSHIFT( psEnc.sCmn.la_pitch, 1 );
        SKP_Silk_apply_sine_window( Wsig, Wsig_ptrOff, x, x_buf_ptrOff, 2, psEnc.sCmn.la_pitch );

        /* Calculate autocorrelation sequence */
        SKP_Silk_autocorr( auto_corr, 0, out scale, Wsig, 0, psPredSt.pitch_LPC_win_length, psEnc.sCmn.pitchEstimationLPCOrder + 1 );

        /* Add white noise, as fraction of energy */
        auto_corr[ 0 ] = SKP_SMLAWB( auto_corr[ 0 ], auto_corr[ 0 ], SKP_FIX_CONST( FIND_PITCH_WHITE_NOISE_FRACTION, 16 ) );

        /* Calculate the reflection coefficients using schur */
        res_nrg = SKP_Silk_schur( rc_Q15, 0, auto_corr, 0, psEnc.sCmn.pitchEstimationLPCOrder );

        /* Prediction gain */
        psEncCtrl.predGain_Q16 = SKP_DIV32_varQ( auto_corr[ 0 ], SKP_max_int( res_nrg, 1 ), 16 );

        /* Convert reflection coefficients to prediction coefficients */
        SKP_Silk_k2a( A_Q24, 0, rc_Q15, 0, psEnc.sCmn.pitchEstimationLPCOrder );

        /* Convert From 32 bit Q24 to 16 bit Q12 coefs */
        for( i = 0; i < psEnc.sCmn.pitchEstimationLPCOrder; i++ )
        {
            A_Q12[ i ] = (short)SKP_SAT16( SKP_RSHIFT( A_Q24[ i ], 12 ) );
        }

        /* Do BWE */
        SKP_Silk_bwexpander( A_Q12, 0, psEnc.sCmn.pitchEstimationLPCOrder, SKP_FIX_CONST( FIND_PITCH_BANDWITH_EXPANSION, 16 ) );

        /*****************************************/
        /* LPC analysis filtering                */
        /*****************************************/
        SKP_memset( FiltState, 0, 0, psEnc.sCmn.pitchEstimationLPCOrder ); /* Not really necessary, but Valgrind will complain otherwise */
        SKP_Silk_MA_Prediction( x, x_bufOff, A_Q12, 0, FiltState, 0, res, resOff, buf_len, psEnc.sCmn.pitchEstimationLPCOrder );
        SKP_memset( res, resOff, (short)0, psEnc.sCmn.pitchEstimationLPCOrder );

        /* Threshold for pitch estimator */
        thrhld_Q15 = SKP_FIX_CONST( 0.45f, 15 );
        thrhld_Q15 = SKP_SMLABB( thrhld_Q15, SKP_FIX_CONST( -0.004f, 15 ), psEnc.sCmn.pitchEstimationLPCOrder );
        thrhld_Q15 = SKP_SMLABB( thrhld_Q15, SKP_FIX_CONST( -0.1f,   7  ), psEnc.speech_activity_Q8 );
        thrhld_Q15 = SKP_SMLABB( thrhld_Q15, SKP_FIX_CONST( 0.15f,  15 ), psEnc.sCmn.prev_sigtype );
        thrhld_Q15 = SKP_SMLAWB( thrhld_Q15, SKP_FIX_CONST( -0.1f,   16 ), psEncCtrl.input_tilt_Q15 );
        thrhld_Q15 = SKP_SAT16(  thrhld_Q15 );

        /*****************************************/
        /* Call pitch estimator                  */
        /*****************************************/
        psEncCtrl.sCmn.sigtype = SKP_Silk_pitch_analysis_core( res, resOff, psEncCtrl.sCmn.pitchL, 0, out psEncCtrl.sCmn.lagIndex,
            out psEncCtrl.sCmn.contourIndex, ref psEnc.LTPCorr_Q15, psEnc.sCmn.prevLag, psEnc.sCmn.pitchEstimationThreshold_Q16,
            (short)thrhld_Q15, psEnc.sCmn.fs_kHz, psEnc.sCmn.pitchEstimationComplexity, 0 /* SKP_FALSE */ );
    }
}
