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
// SKP_Silk_prefilter_FIX.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_prefilter_FIX.c（函数 SKP_Silk_warped_LPC_analysis_filter_FIX /
//   SKP_Silk_prefilter_FIX / SKP_Silk_prefilt_FIX）
// 移植说明：
//   - #if EMBEDDED_ARM<6 在默认构建下为真，C 版
//     SKP_Silk_warped_LPC_analysis_filter_FIX（本文件内定义）照常移植。
//   - #if !defined(_SYSTEM_IS_BIG_ENDIAN) 取激活分支：B_Q12 打包为单 int32 的
//     SMLABB/SMLABT 版本；大端分支不移植。
//   - C 中 px / pxw / AR1_shp_Q13 指针步进改为「同一数组 + 偏移」表达。
//   - SKP_prefilt_FIX 中 LTP_shp_buf = P->sLTP_shp 保留为局部数组别名（引用同一
//     数组对象，写回状态语义一致）。
//   - SKP_Silk_prefilt_FIX 为 SKP_INLINE → AggressiveInlining。
//   - SKP_assert 在 C 构建中为空宏，按移植规约删除。
// PORT-NOTE: 同 SKP_Silk_control_audio_bandwidth.cs 头注——类名加 _impl（CS0542）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;

namespace AaR.Server.Silk;

/// <summary>src/SKP_Silk_prefilter_FIX.c —— 量化器输入信号的预滤波。</summary>
internal static class SKP_Silk_prefilter_FIX_impl
{
    public static void SKP_Silk_warped_LPC_analysis_filter_FIX(
        int[] state, int stateOff,                 /* I/O  State [order + 1]                       */
        short[] res, int resOff,                   /* O    Residual signal [length]                */
        short[] coef_Q13, int coef_Q13Off,         /* I    Coefficients [order]                    */
        short[] input, int inputOff,               /* I    Input signal [length]                   */
        short lambda_Q16,                         /* I    Warping factor                          */
        int length,                                /* I    Length of input signal                  */
        int order                                  /* I    Filter order (even)                     */
        )
    {
        int n, i;
        int acc_Q11, tmp1, tmp2;

        /* Order must be even */
        // SKP_assert( ( order & 1 ) == 0 );（删除）

        for( n = 0; n < length; n++ )
        {
            /* Output of lowpass section */
            tmp2 = SKP_SMLAWB( state[ stateOff ], state[ stateOff + 1 ], lambda_Q16 );
            state[ stateOff ] = SKP_LSHIFT( input[ inputOff + n ], 14 );
            /* Output of allpass section */
            tmp1 = SKP_SMLAWB( state[ stateOff + 1 ], state[ stateOff + 2 ] - tmp2, lambda_Q16 );
            state[ stateOff + 1 ] = tmp2;
            acc_Q11 = SKP_SMULWB( tmp2, coef_Q13[ coef_Q13Off ] );
            /* Loop over allpass sections */
            for( i = 2; i < order; i += 2 )
            {
                /* Output of allpass section */
                tmp2 = SKP_SMLAWB( state[ stateOff + i ], state[ stateOff + i + 1 ] - tmp1, lambda_Q16 );
                state[ stateOff + i ] = tmp1;
                acc_Q11 = SKP_SMLAWB( acc_Q11, tmp1, coef_Q13[ coef_Q13Off + i - 1 ] );
                /* Output of allpass section */
                tmp1 = SKP_SMLAWB( state[ stateOff + i + 1 ], state[ stateOff + i + 2 ] - tmp2, lambda_Q16 );
                state[ stateOff + i + 1 ] = tmp2;
                acc_Q11 = SKP_SMLAWB( acc_Q11, tmp2, coef_Q13[ coef_Q13Off + i ] );
            }
            state[ stateOff + order ] = tmp1;
            acc_Q11 = SKP_SMLAWB( acc_Q11, tmp1, coef_Q13[ coef_Q13Off + order - 1 ] );
            res[ resOff + n ] = (short)SKP_SAT16( (int)input[ inputOff + n ] - SKP_RSHIFT_ROUND( acc_Q11, 11 ) );
        }
    }

    public static void SKP_Silk_prefilter_FIX(
        SKP_Silk_encoder_state_FIX psEnc,             /* I/O  Encoder state FIX                           */
        SKP_Silk_encoder_control_FIX psEncCtrl,       /* I    Encoder control FIX                         */
        short[] xw, int xwOff,                         /* O    Weighted signal                             */
        short[] x, int xOff                            /* I    Speech signal                               */
        )
    {
        SKP_Silk_prefilter_state_FIX P = psEnc.sPrefilt;
        int j, k, lag;
        int tmp_32;
        // PORT-NOTE: C 的 const SKP_int16 *AR1_shp_Q13 / *px / *pxw 指针步进
        //   改为「同一数组 + 偏移」（ar1_shp_Q13Off / pxOff / pxwOff）。
        int ar1_shp_Q13Off;
        int pxOff, pxwOff;
        int HarmShapeGain_Q12, Tilt_Q14;
        int HarmShapeFIRPacked_Q12, LF_shp_Q14;
        int[] x_filt_Q12 = new int[ MAX_FRAME_LENGTH / NB_SUBFR ];
        short[] st_res = new short[ ( MAX_FRAME_LENGTH / NB_SUBFR ) + MAX_SHAPE_LPC_ORDER ];
        int B_Q12;   /* 对应 C 的 #if !defined(_SYSTEM_IS_BIG_ENDIAN) 分支 */

        /* Setup pointers */
        pxOff  = xOff;
        pxwOff = xwOff;
        lag = P.lagPrev;
        for( k = 0; k < NB_SUBFR; k++ )
        {
            /* Update Variables that change per sub frame */
            if( psEncCtrl.sCmn.sigtype == SIG_TYPE_VOICED )
            {
                lag = psEncCtrl.sCmn.pitchL[ k ];
            }

            /* Noise shape parameters */
            HarmShapeGain_Q12 = SKP_SMULWB( psEncCtrl.HarmShapeGain_Q14[ k ], 16384 - psEncCtrl.HarmBoost_Q14[ k ] );
            // SKP_assert( HarmShapeGain_Q12 >= 0 );（删除）
            HarmShapeFIRPacked_Q12  =                          SKP_RSHIFT( HarmShapeGain_Q12, 2 );
            HarmShapeFIRPacked_Q12 |= SKP_LSHIFT( (int)SKP_RSHIFT( HarmShapeGain_Q12, 1 ), 16 );
            Tilt_Q14    = psEncCtrl.Tilt_Q14[   k ];
            LF_shp_Q14  = psEncCtrl.LF_shp_Q14[ k ];
            ar1_shp_Q13Off = k * MAX_SHAPE_LPC_ORDER;

            /* Short term FIR filtering*/
            SKP_Silk_warped_LPC_analysis_filter_FIX( P.sAR_shp, 0, st_res, 0, psEncCtrl.AR1_Q13, ar1_shp_Q13Off, x, pxOff,
                (short)psEnc.sCmn.warping_Q16, psEnc.sCmn.subfr_length, psEnc.sCmn.shapingLPCOrder );

            /* reduce (mainly) low frequencies during harmonic emphasis */
            /* NOTE: the code below loads two int16 values in an int32, and multiplies each using the   */
            /* SMLABB and SMLABT instructions. On a big-endian CPU the two int16 variables would be     */
            /* loaded in reverse order and the code will give the wrong result. In that case swapping   */
            /* the SMLABB and SMLABT instructions should solve the problem.                             */
            B_Q12 = SKP_RSHIFT_ROUND( psEncCtrl.GainsPre_Q14[ k ], 2 );
            tmp_32 = SKP_SMLABB( SKP_FIX_CONST( INPUT_TILT, 26 ), psEncCtrl.HarmBoost_Q14[ k ], HarmShapeGain_Q12 );   /* Q26 */
            tmp_32 = SKP_SMLABB( tmp_32, psEncCtrl.coding_quality_Q14, SKP_FIX_CONST( HIGH_RATE_INPUT_TILT, 12 ) );    /* Q26 */
            tmp_32 = SKP_SMULWB( tmp_32, -psEncCtrl.GainsPre_Q14[ k ] );                                               /* Q24 */
            tmp_32 = SKP_RSHIFT_ROUND( tmp_32, 12 );                                                                    /* Q12 */
            B_Q12 |= SKP_LSHIFT( (int)SKP_SAT16( tmp_32 ), 16 );

            x_filt_Q12[ 0 ] = SKP_SMLABT( SKP_SMULBB( st_res[ 0 ], B_Q12 ), P.sHarmHP, B_Q12 );
            for( j = 1; j < psEnc.sCmn.subfr_length; j++ )
            {
                x_filt_Q12[ j ] = SKP_SMLABT( SKP_SMULBB( st_res[ j ], B_Q12 ), st_res[ j - 1 ], B_Q12 );
            }

            P.sHarmHP = st_res[ psEnc.sCmn.subfr_length - 1 ];

            SKP_Silk_prefilt_FIX( P, x_filt_Q12, 0, xw, pxwOff, HarmShapeFIRPacked_Q12, Tilt_Q14,
                LF_shp_Q14, lag, psEnc.sCmn.subfr_length );

            pxOff  += psEnc.sCmn.subfr_length;
            pxwOff += psEnc.sCmn.subfr_length;
        }

        P.lagPrev = psEncCtrl.sCmn.pitchL[ NB_SUBFR - 1 ];
    }

    /* SKP_Silk_prefilter. Prefilter for finding Quantizer input signal                           */
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static void SKP_Silk_prefilt_FIX(
        SKP_Silk_prefilter_state_FIX P,            /* I/O state                          */
        int[] st_res_Q12, int st_res_Q12Off,       /* I short term residual signal       */
        short[] xw, int xwOff,                     /* O prefiltered signal               */
        int HarmShapeFIRPacked_Q12,               /* I Harmonic shaping coeficients     */
        int Tilt_Q14,                             /* I Tilt shaping coeficient          */
        int LF_shp_Q14,                           /* I Low-frequancy shaping coeficients*/
        int lag,                                  /* I Lag for harmonic shaping         */
        int length                                /* I Length of signals                */
        )
    {
        int i, idx, LTP_shp_buf_idx;
        int n_LTP_Q12, n_Tilt_Q10, n_LF_Q10;
        int sLF_AR_shp_Q12, sLF_MA_shp_Q12;
        short[] LTP_shp_buf;

        /* To speed up use temp variables instead of using the struct */
        LTP_shp_buf     = P.sLTP_shp;
        LTP_shp_buf_idx = P.sLTP_shp_buf_idx;
        sLF_AR_shp_Q12  = P.sLF_AR_shp_Q12;
        sLF_MA_shp_Q12  = P.sLF_MA_shp_Q12;

        for( i = 0; i < length; i++ )
        {
            if( lag > 0 )
            {
                /* unrolled loop */
                // SKP_assert( HARM_SHAPE_FIR_TAPS == 3 );（删除）
                idx = lag + LTP_shp_buf_idx;
                n_LTP_Q12 = SKP_SMULBB(            LTP_shp_buf[ ( idx - HARM_SHAPE_FIR_TAPS / 2 - 1) & LTP_MASK ], HarmShapeFIRPacked_Q12 );
                n_LTP_Q12 = SKP_SMLABT( n_LTP_Q12, LTP_shp_buf[ ( idx - HARM_SHAPE_FIR_TAPS / 2    ) & LTP_MASK ], HarmShapeFIRPacked_Q12 );
                n_LTP_Q12 = SKP_SMLABB( n_LTP_Q12, LTP_shp_buf[ ( idx - HARM_SHAPE_FIR_TAPS / 2 + 1) & LTP_MASK ], HarmShapeFIRPacked_Q12 );
            }
            else
            {
                n_LTP_Q12 = 0;
            }

            n_Tilt_Q10 = SKP_SMULWB( sLF_AR_shp_Q12, Tilt_Q14 );
            n_LF_Q10   = SKP_SMLAWB( SKP_SMULWT( sLF_AR_shp_Q12, LF_shp_Q14 ), sLF_MA_shp_Q12, LF_shp_Q14 );

            sLF_AR_shp_Q12 = SKP_SUB32( st_res_Q12[ st_res_Q12Off + i ], SKP_LSHIFT( n_Tilt_Q10, 2 ) );
            sLF_MA_shp_Q12 = SKP_SUB32( sLF_AR_shp_Q12,  SKP_LSHIFT( n_LF_Q10,   2 ) );

            LTP_shp_buf_idx = ( LTP_shp_buf_idx - 1 ) & LTP_MASK;
            LTP_shp_buf[ LTP_shp_buf_idx ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( sLF_MA_shp_Q12, 12 ) );

            xw[ xwOff + i ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( SKP_SUB32( sLF_MA_shp_Q12, n_LTP_Q12 ), 12 ) );
        }

        /* Copy temp variable back to state */
        P.sLF_AR_shp_Q12   = sLF_AR_shp_Q12;
        P.sLF_MA_shp_Q12   = sLF_MA_shp_Q12;
        P.sLTP_shp_buf_idx = LTP_shp_buf_idx;
    }
}
