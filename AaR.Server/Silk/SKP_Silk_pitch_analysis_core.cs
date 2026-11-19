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
// SKP_Silk_pitch_analysis_core.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_pitch_analysis_core.c（函数 SKP_Silk_pitch_analysis_core /
//   SKP_FIX_P_Ana_calc_corr_st3 / SKP_FIX_P_Ana_calc_energy_st3 /
//   SKP_FIX_P_Ana_find_scaling；文件内 static 函数全部保留）
// 移植说明：
//   - 三层基音搜索逐行机械翻译；C 的 target_ptr / basis_ptr / input_signal_ptr
//     指针步进改为「同一数组 + 偏移」表达。
//   - 表 SKP_Silk_CB_lags_stage2 / SKP_Silk_CB_lags_stage3 /
//     SKP_Silk_Lag_range_stage3 / SKP_Silk_cbk_sizes_stage3 /
//     SKP_Silk_cbk_offsets_stage3 为表簇产物（SKP_Silk_pitch_est_tables.c →
//     static readonly 交错数组），裸名引用。
//   - SKP_Silk_resampler_down2 / down2_3 / down3 / inner_prod_aligned /
//     insertion_sort_decreasing_int16 / lin2log / int16_array_maxabs 为 DSP 簇
//     产物，按 SigProc_FIX.h 原型推导签名，裸名调用。
//   - 出参 pitch_out 为数组（+偏移）；lagIndex / contourIndex 仅写 → out；
//     LTPCorr_Q15 先读后写 → ref。
//   - SKP_assert 在 C 构建中为空宏，按移植规约删除。
// PORT-NOTE: 同 SKP_Silk_control_audio_bandwidth.cs 头注——类名加 _impl（CS0542）。
// PORT-NOTE: C 声明 SKP_int32 scratch_mem[ 3 * PITCH_EST_MAX_FRAME_LENGTH ] 并以
//   (SKP_int16*) 视图复作 16 位缓冲（本函数内从未按 32 位读写，仅存缩放后的输入
//   信号副本）；C# 无法对 int[] 做 short 视图，改为同元素数的 short[]。
// PORT-NOTE: C 的二维/三维局部数组（C[4][149]、energies_st3[4][34][5] 等）改为
//   交错数组，访问形态 [k][j][i] 不变；对整个二维数组的 SKP_memset 改为逐行 memset。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_Inlines;
using static AaR.Server.Silk.SKP_Silk_pitch_est_tables;
using static AaR.Server.Silk.SKP_Silk_resampler_down2_impl;
using static AaR.Server.Silk.SKP_Silk_resampler_down2_3_impl;
using static AaR.Server.Silk.SKP_Silk_resampler_down3_impl;
using static AaR.Server.Silk.SKP_Silk_inner_prod_aligned_impl;
using static AaR.Server.Silk.SKP_Silk_sort;
using static AaR.Server.Silk.SKP_Silk_lin2log_impl;
using static AaR.Server.Silk.SKP_Silk_array_maxabs;

namespace AaR.Server.Silk;

/// <summary>src/SKP_Silk_pitch_analysis_core.c —— 定点核心基音分析函数。</summary>
internal static class SKP_Silk_pitch_analysis_core_impl
{
    internal const int SCRATCH_SIZE = 22;

    /*************************************************************/
    /*      FIXED POINT CORE PITCH ANALYSIS FUNCTION             */
    /*************************************************************/
    public static int SKP_Silk_pitch_analysis_core(  /* O    Voicing estimate: 0 voiced, 1 unvoiced                      */
        short[] signal, int signalOff,               /* I    Signal of length PITCH_EST_FRAME_LENGTH_MS*Fs_kHz           */
        int[] pitch_out, int pitch_outOff,           /* O    4 pitch lag values                                          */
        out int lagIndex,                            /* O    Lag Index                                                   */
        out int contourIndex,                        /* O    Pitch contour Index                                         */
        ref int LTPCorr_Q15,                         /* I/O  Normalized correlation; input: value from previous frame    */
        int prevLag,                                 /* I    Last lag of previous frame; set to zero is unvoiced         */
        int search_thres1_Q16,                       /* I    First stage threshold for lag candidates 0 - 1              */
        int search_thres2_Q15,                       /* I    Final threshold for lag candidates 0 - 1                    */
        int Fs_kHz,                                  /* I    Sample frequency (kHz)                                      */
        int complexity,                              /* I   Complexity setting, 0-2, where 2 is highest                 */
        int forLJC                                   /* I    1 if this function is called from LJC code, 0 otherwise.  */
        )
    {
        short[] signal_8kHz = new short[ PITCH_EST_MAX_FRAME_LENGTH_ST_2 ];
        short[] signal_4kHz = new short[ PITCH_EST_MAX_FRAME_LENGTH_ST_1 ];
        short[] scratch_mem = new short[ 3 * PITCH_EST_MAX_FRAME_LENGTH ];   /* 见文件头 PORT-NOTE */
        short[] input_signal_ptr;
        int input_signal_ptrOff;
        int[] filt_state = new int[ PITCH_EST_MAX_DECIMATE_STATE_LENGTH ];
        int i, k, d, j;
        short[][] C = new short[ PITCH_EST_NB_SUBFR ][];
        for( int ci = 0; ci < PITCH_EST_NB_SUBFR; ci++ )
        {
            C[ ci ] = new short[ ( PITCH_EST_MAX_LAG >> 1 ) + 5 ];
        }
        int target_ptrOff = 0, basis_ptrOff = 0;   /* C 的 target_ptr / basis_ptr 指针 → 偏移 */
        int cross_corr, normalizer, energy, shift, energy_basis, energy_target;
        int[] d_srch = new int[ PITCH_EST_D_SRCH_LENGTH ];
        short[] d_comp = new short[ ( PITCH_EST_MAX_LAG >> 1 ) + 5 ];
        int Cmax, length_d_srch, length_d_comp;
        int sum, threshold, temp32;
        int CBimax, CBimax_new, CBimax_old, lag, start_lag, end_lag, lag_new;
        int[] CC = new int[ PITCH_EST_NB_CBKS_STAGE2_EXT ];
        int CCmax, CCmax_b, CCmax_new_b, CCmax_new;
        int[][][] energies_st3 = new int[ PITCH_EST_NB_SUBFR ][][];
        int[][][] crosscorr_st3 = new int[ PITCH_EST_NB_SUBFR ][][];
        for( int e0 = 0; e0 < PITCH_EST_NB_SUBFR; e0++ )
        {
            energies_st3[ e0 ] = new int[ PITCH_EST_NB_CBKS_STAGE3_MAX ][];
            crosscorr_st3[ e0 ] = new int[ PITCH_EST_NB_CBKS_STAGE3_MAX ][];
            for( int e1 = 0; e1 < PITCH_EST_NB_CBKS_STAGE3_MAX; e1++ )
            {
                energies_st3[ e0 ][ e1 ] = new int[ PITCH_EST_NB_STAGE3_LAGS ];
                crosscorr_st3[ e0 ][ e1 ] = new int[ PITCH_EST_NB_STAGE3_LAGS ];
            }
        }
        int lag_counter;
        int frame_length, frame_length_8kHz, frame_length_4kHz, max_sum_sq_length;
        int sf_length, sf_length_8kHz;
        int min_lag, min_lag_8kHz, min_lag_4kHz;
        int max_lag, max_lag_8kHz, max_lag_4kHz;
        int contour_bias, diff;
        int lz, lshift;
        int cbk_offset, cbk_size, nb_cbks_stage2;
        int delta_lag_log2_sqr_Q7, lag_log2_Q7, prevLag_log2_Q7, prev_lag_bias_Q15, corr_thres_Q15;

        /* Check for valid sampling frequency */
        // SKP_assert( Fs_kHz == 8 || Fs_kHz == 12 || Fs_kHz == 16 || Fs_kHz == 24 );（删除）

        /* Check for valid complexity setting */
        // SKP_assert( complexity >= SKP_Silk_PITCH_EST_MIN_COMPLEX );（删除）
        // SKP_assert( complexity <= SKP_Silk_PITCH_EST_MAX_COMPLEX );（删除）

        // SKP_assert( search_thres1_Q16 >= 0 && search_thres1_Q16 <= (1<<16) );（删除）
        // SKP_assert( search_thres2_Q15 >= 0 && search_thres2_Q15 <= (1<<15) );（删除）

        /* Setup frame lengths max / min lag for the sampling frequency */
        frame_length      = PITCH_EST_FRAME_LENGTH_MS * Fs_kHz;
        frame_length_4kHz = PITCH_EST_FRAME_LENGTH_MS * 4;
        frame_length_8kHz = PITCH_EST_FRAME_LENGTH_MS * 8;
        sf_length         = SKP_RSHIFT( frame_length,      3 );
        sf_length_8kHz    = SKP_RSHIFT( frame_length_8kHz, 3 );
        min_lag           = PITCH_EST_MIN_LAG_MS * Fs_kHz;
        min_lag_4kHz      = PITCH_EST_MIN_LAG_MS * 4;
        min_lag_8kHz      = PITCH_EST_MIN_LAG_MS * 8;
        max_lag           = PITCH_EST_MAX_LAG_MS * Fs_kHz;
        max_lag_4kHz      = PITCH_EST_MAX_LAG_MS * 4;
        max_lag_8kHz      = PITCH_EST_MAX_LAG_MS * 8;

        /* 对应 C：SKP_memset( C, 0, sizeof( SKP_int16 ) * PITCH_EST_NB_SUBFR * ( ( PITCH_EST_MAX_LAG >> 1 ) + 5) ) */
        for( int ci = 0; ci < PITCH_EST_NB_SUBFR; ci++ )
        {
            SKP_memset( C[ ci ], 0, (short)0, ( PITCH_EST_MAX_LAG >> 1 ) + 5 );
        }

        /* Resample from input sampled at Fs_kHz to 8 kHz */
        if( Fs_kHz == 16 )
        {
            SKP_memset( filt_state, 0, 0, 2 );
            SKP_Silk_resampler_down2( filt_state, 0, signal_8kHz, 0, signal, signalOff, frame_length );
        }
        else if ( Fs_kHz == 12 )
        {
            int[] R23 = new int[ 6 ];
            SKP_memset( R23, 0, 0, 6 );
            SKP_Silk_resampler_down2_3( R23, 0, signal_8kHz, 0, signal, signalOff, PITCH_EST_FRAME_LENGTH_MS * 12 );
        }
        else if( Fs_kHz == 24 )
        {
            int[] filt_state_fix = new int[ 8 ];
            SKP_memset( filt_state_fix, 0, 0, 8 );
            SKP_Silk_resampler_down3( filt_state_fix, 0, signal_8kHz, 0, signal, signalOff, 24 * PITCH_EST_FRAME_LENGTH_MS );
        }
        else
        {
            // SKP_assert( Fs_kHz == 8 );（删除）
            SKP_memcpy( signal_8kHz, 0, signal, signalOff, frame_length_8kHz );
        }
        /* Decimate again to 4 kHz */
        SKP_memset( filt_state, 0, 0, 2 );/* Set state to zero */
        SKP_Silk_resampler_down2( filt_state, 0, signal_4kHz, 0, signal_8kHz, 0, frame_length_8kHz );

        /* Low-pass filter */
        for( i = frame_length_4kHz - 1; i > 0; i-- )
        {
            signal_4kHz[ i ] = SKP_ADD_SAT16( signal_4kHz[ i ], signal_4kHz[ i - 1 ] );
        }

        /*******************************************************************************
        ** Scale 4 kHz signal down to prevent correlations measures from overflowing
        ** find scaling as max scaling for each 8kHz(?) subframe
        *******************************************************************************/

        /* Inner product is calculated with different lengths, so scale for the worst case */
        max_sum_sq_length = SKP_max_32( sf_length_8kHz, SKP_RSHIFT( frame_length_4kHz, 1 ) );
        shift = SKP_FIX_P_Ana_find_scaling( signal_4kHz, 0, frame_length_4kHz, max_sum_sq_length );
        if( shift > 0 )
        {
            for( i = 0; i < frame_length_4kHz; i++ )
            {
                signal_4kHz[ i ] = (short)SKP_RSHIFT( signal_4kHz[ i ], shift );
            }
        }

        /******************************************************************************
        * FIRST STAGE, operating in 4 khz
        ******************************************************************************/
        target_ptrOff = SKP_RSHIFT( frame_length_4kHz, 1 );
        for( k = 0; k < 2; k++ )
        {
            /* Check that we are within range of the array */
            // SKP_assert( target_ptr >= signal_4kHz );（删除）
            // SKP_assert( target_ptr + sf_length_8kHz <= signal_4kHz + frame_length_4kHz );（删除）

            basis_ptrOff = target_ptrOff - min_lag_4kHz;

            /* Check that we are within range of the array */
            // SKP_assert( basis_ptr >= signal_4kHz );（删除）
            // SKP_assert( basis_ptr + sf_length_8kHz <= signal_4kHz + frame_length_4kHz );（删除）

            normalizer = 0;
            cross_corr = 0;
            /* Calculate first vector products before loop */
            cross_corr = SKP_Silk_inner_prod_aligned( signal_4kHz, target_ptrOff, signal_4kHz, basis_ptrOff, sf_length_8kHz );
            normalizer = SKP_Silk_inner_prod_aligned( signal_4kHz, basis_ptrOff, signal_4kHz, basis_ptrOff, sf_length_8kHz );
            normalizer = SKP_ADD_SAT32( normalizer, SKP_SMULBB( sf_length_8kHz, 4000 ) );

            temp32 = SKP_DIV32( cross_corr, SKP_Silk_SQRT_APPROX( normalizer ) + 1 );
            C[ k ][ min_lag_4kHz ] = (short)SKP_SAT16( temp32 );        /* Q0 */

            /* From now on normalizer is computed recursively */
            for( d = min_lag_4kHz + 1; d <= max_lag_4kHz; d++ )
            {
                basis_ptrOff--;

                /* Check that we are within range of the array */
                // SKP_assert( basis_ptr >= signal_4kHz );（删除）
                // SKP_assert( basis_ptr + sf_length_8kHz <= signal_4kHz + frame_length_4kHz );（删除）

                cross_corr = SKP_Silk_inner_prod_aligned( signal_4kHz, target_ptrOff, signal_4kHz, basis_ptrOff, sf_length_8kHz );

                /* Add contribution of new sample and remove contribution from oldest sample */
                normalizer +=
                    SKP_SMULBB( signal_4kHz[ basis_ptrOff ], signal_4kHz[ basis_ptrOff ] ) -
                    SKP_SMULBB( signal_4kHz[ basis_ptrOff + sf_length_8kHz ], signal_4kHz[ basis_ptrOff + sf_length_8kHz ] );

                temp32 = SKP_DIV32( cross_corr, SKP_Silk_SQRT_APPROX( normalizer ) + 1 );
                C[ k ][ d ] = (short)SKP_SAT16( temp32 );                        /* Q0 */
            }
            /* Update target pointer */
            target_ptrOff += sf_length_8kHz;
        }

        /* Combine two subframes into single correlation measure and apply short-lag bias */
        for( i = max_lag_4kHz; i >= min_lag_4kHz; i-- )
        {
            sum = (int)C[ 0 ][ i ] + (int)C[ 1 ][ i ];                /* Q0 */
            // SKP_assert( SKP_RSHIFT( sum, 1 ) == SKP_SAT16( SKP_RSHIFT( sum, 1 ) ) );（删除）
            sum = SKP_RSHIFT( sum, 1 );                                           /* Q-1 */
            // SKP_assert( SKP_LSHIFT( (SKP_int32)-i, 4 ) == SKP_SAT16( SKP_LSHIFT( (SKP_int32)-i, 4 ) ) );（删除）
            sum = SKP_SMLAWB( sum, sum, SKP_LSHIFT( -i, 4 ) );                    /* Q-1 */
            // SKP_assert( sum == SKP_SAT16( sum ) );（删除）
            C[ 0 ][ i ] = (short)sum;                                         /* Q-1 */
        }

        /* Sort */
        length_d_srch = 4 + 2 * complexity;
        // SKP_assert( 3 * length_d_srch <= PITCH_EST_D_SRCH_LENGTH );（删除）
        SKP_Silk_insertion_sort_decreasing_int16( C[ 0 ], min_lag_4kHz, d_srch, 0, max_lag_4kHz - min_lag_4kHz + 1, length_d_srch );

        /* Escape if correlation is very low already here */
        target_ptrOff = SKP_RSHIFT( frame_length_4kHz, 1 );
        energy = SKP_Silk_inner_prod_aligned( signal_4kHz, target_ptrOff, signal_4kHz, target_ptrOff, SKP_RSHIFT( frame_length_4kHz, 1 ) );
        energy = SKP_ADD_POS_SAT32( energy, 1000 );                              /* Q0 */
        Cmax = (int)C[ 0 ][ min_lag_4kHz ];                                  /* Q-1 */
        threshold = SKP_SMULBB( Cmax, Cmax );                                    /* Q-2 */
        /* Compare in Q-2 domain */
        if( SKP_RSHIFT( energy, 4 + 2 ) > threshold )
        {
            SKP_memset( pitch_out, pitch_outOff, 0, PITCH_EST_NB_SUBFR );
            LTPCorr_Q15  = 0;
            lagIndex     = 0;
            contourIndex = 0;
            return 1;
        }

        threshold = SKP_SMULWB( search_thres1_Q16, Cmax );
        for( i = 0; i < length_d_srch; i++ )
        {
            /* Convert to 8 kHz indices for the sorted correlation that exceeds the threshold */
            if( C[ 0 ][ min_lag_4kHz + i ] > threshold )
            {
                d_srch[ i ] = ( d_srch[ i ] + min_lag_4kHz ) << 1;
            }
            else
            {
                length_d_srch = i;
                break;
            }
        }
        // SKP_assert( length_d_srch > 0 );（删除）

        for( i = min_lag_8kHz - 5; i < max_lag_8kHz + 5; i++ )
        {
            d_comp[ i ] = 0;
        }
        for( i = 0; i < length_d_srch; i++ )
        {
            d_comp[ d_srch[ i ] ] = 1;
        }

        /* Convolution */
        for( i = max_lag_8kHz + 3; i >= min_lag_8kHz; i-- )
        {
            // PORT-NOTE: C 为 d_comp[i] += (int 运算后隐式截断)；C# 复合赋值不接受
            //   short += int，改为显式 (short) 截断，二补回绕语义逐位一致。
            d_comp[ i ] = (short)( d_comp[ i ] + d_comp[ i - 1 ] + d_comp[ i - 2 ] );
        }

        length_d_srch = 0;
        for( i = min_lag_8kHz; i < max_lag_8kHz + 1; i++ )
        {
            if( d_comp[ i + 1 ] > 0 )
            {
                d_srch[ length_d_srch ] = i;
                length_d_srch++;
            }
        }

        /* Convolution */
        for( i = max_lag_8kHz + 3; i >= min_lag_8kHz; i-- )
        {
            // PORT-NOTE: 同上——short += int 的显式截断写法。
            d_comp[ i ] = (short)( d_comp[ i ] + d_comp[ i - 1 ] + d_comp[ i - 2 ] + d_comp[ i - 3 ] );
        }

        length_d_comp = 0;
        for( i = min_lag_8kHz; i < max_lag_8kHz + 4; i++ )
        {
            if( d_comp[ i ] > 0 )
            {
                d_comp[ length_d_comp ] = (short)( i - 2 );
                length_d_comp++;
            }
        }

        /**********************************************************************************
        ** SECOND STAGE, operating at 8 kHz, on lag sections with high correlation
        *************************************************************************************/

        /******************************************************************************
        ** Scale signal down to avoid correlations measures from overflowing
        *******************************************************************************/
        /* find scaling as max scaling for each subframe */
        shift = SKP_FIX_P_Ana_find_scaling( signal_8kHz, 0, frame_length_8kHz, sf_length_8kHz );
        if( shift > 0 )
        {
            for( i = 0; i < frame_length_8kHz; i++ )
            {
                signal_8kHz[ i ] = (short)SKP_RSHIFT( signal_8kHz[ i ], shift );
            }
        }

        /*********************************************************************************
        * Find energy of each subframe projected onto its history, for a range of delays
        *********************************************************************************/
        /* 对应 C：SKP_memset( C, 0, PITCH_EST_NB_SUBFR * ( ( PITCH_EST_MAX_LAG >> 1 ) + 5 ) * sizeof( SKP_int16 ) ) */
        for( int ci = 0; ci < PITCH_EST_NB_SUBFR; ci++ )
        {
            SKP_memset( C[ ci ], 0, (short)0, ( PITCH_EST_MAX_LAG >> 1 ) + 5 );
        }

        target_ptrOff = frame_length_4kHz; /* point to middle of frame */
        for( k = 0; k < PITCH_EST_NB_SUBFR; k++ )
        {
            /* Check that we are within range of the array */
            // SKP_assert( target_ptr >= signal_8kHz );（删除）
            // SKP_assert( target_ptr + sf_length_8kHz <= signal_8kHz + frame_length_8kHz );（删除）

            energy_target = SKP_Silk_inner_prod_aligned( signal_8kHz, target_ptrOff, signal_8kHz, target_ptrOff, sf_length_8kHz );
            // ToDo: Calculate 1 / energy_target here and save one division inside next for loop
            for( j = 0; j < length_d_comp; j++ )
            {
                d = d_comp[ j ];
                basis_ptrOff = target_ptrOff - d;

                /* Check that we are within range of the array */
                // SKP_assert( basis_ptr >= signal_8kHz );（删除）
                // SKP_assert( basis_ptr + sf_length_8kHz <= signal_8kHz + frame_length_8kHz );（删除）

                cross_corr   = SKP_Silk_inner_prod_aligned( signal_8kHz, target_ptrOff, signal_8kHz, basis_ptrOff, sf_length_8kHz );
                energy_basis = SKP_Silk_inner_prod_aligned( signal_8kHz, basis_ptrOff, signal_8kHz, basis_ptrOff, sf_length_8kHz );
                if( cross_corr > 0 )
                {
                    energy = SKP_max( energy_target, energy_basis ); /* Find max to make sure first division < 1.0 */
                    lz = SKP_Silk_CLZ32( cross_corr );
                    lshift = SKP_LIMIT_32( lz - 1, 0, 15 );
                    temp32 = SKP_DIV32( SKP_LSHIFT( cross_corr, lshift ), SKP_RSHIFT( energy, 15 - lshift ) + 1 ); /* Q15 */
                    // SKP_assert( temp32 == SKP_SAT16( temp32 ) );（删除）
                    temp32 = SKP_SMULWB( cross_corr, temp32 ); /* Q(-1), cc * ( cc / max(b, t) ) */
                    temp32 = SKP_ADD_SAT32( temp32, temp32 );  /* Q(0) */
                    lz = SKP_Silk_CLZ32( temp32 );
                    lshift = SKP_LIMIT_32( lz - 1, 0, 15 );
                    energy = SKP_min( energy_target, energy_basis );
                    C[ k ][ d ] = (short)SKP_DIV32( SKP_LSHIFT( temp32, lshift ), SKP_RSHIFT( energy, 15 - lshift ) + 1 ); // Q15
                }
                else
                {
                    C[ k ][ d ] = 0;
                }
            }
            target_ptrOff += sf_length_8kHz;
        }

        /* search over lag range and lags codebook */
        /* scale factor for lag codebook, as a function of center lag */

        CCmax   = int.MinValue;   /* SKP_int32_MIN */
        CCmax_b = int.MinValue;   /* SKP_int32_MIN */

        CBimax = 0; /* To avoid returning undefined lag values */
        lag = -1;   /* To check if lag with strong enough correlation has been found */

        if( prevLag > 0 )
        {
            if( Fs_kHz == 12 )
            {
                prevLag = SKP_DIV32_16( SKP_LSHIFT( prevLag, 1 ), 3 );
            }
            else if( Fs_kHz == 16 )
            {
                prevLag = SKP_RSHIFT( prevLag, 1 );
            }
            else if( Fs_kHz == 24 )
            {
                prevLag = SKP_DIV32_16( prevLag, 3 );
            }
            prevLag_log2_Q7 = SKP_Silk_lin2log( (int)prevLag );
        }
        else
        {
            prevLag_log2_Q7 = 0;
        }
        // SKP_assert( search_thres2_Q15 == SKP_SAT16( search_thres2_Q15 ) );（删除）
        corr_thres_Q15 = SKP_RSHIFT( SKP_SMULBB( search_thres2_Q15, search_thres2_Q15 ), 13 );

        /* If input is 8 khz use a larger codebook here because it is last stage */
        if( Fs_kHz == 8 && complexity > SKP_Silk_PITCH_EST_MIN_COMPLEX )
        {
            nb_cbks_stage2 = PITCH_EST_NB_CBKS_STAGE2_EXT;
        }
        else
        {
            nb_cbks_stage2 = PITCH_EST_NB_CBKS_STAGE2;
        }

        for( k = 0; k < length_d_srch; k++ )
        {
            d = d_srch[ k ];
            for( j = 0; j < nb_cbks_stage2; j++ )
            {
                CC[ j ] = 0;
                for( i = 0; i < PITCH_EST_NB_SUBFR; i++ )
                {
                    /* Try all codebooks */
                    CC[ j ] = CC[ j ] + (int)C[ i ][ d + SKP_Silk_CB_lags_stage2[ i ][ j ] ];
                }
            }
            /* Find best codebook */
            CCmax_new = int.MinValue;   /* SKP_int32_MIN */
            CBimax_new = 0;
            for( i = 0; i < nb_cbks_stage2; i++ )
            {
                if( CC[ i ] > CCmax_new )
                {
                    CCmax_new = CC[ i ];
                    CBimax_new = i;
                }
            }

            /* Bias towards shorter lags */
            lag_log2_Q7 = SKP_Silk_lin2log( (int)d ); /* Q7 */
            // SKP_assert( lag_log2_Q7 == SKP_SAT16( lag_log2_Q7 ) );（删除）
            // SKP_assert( PITCH_EST_NB_SUBFR * PITCH_EST_SHORTLAG_BIAS_Q15 == SKP_SAT16( PITCH_EST_NB_SUBFR * PITCH_EST_SHORTLAG_BIAS_Q15 ) );（删除）

            if ( forLJC != 0 )
            {
                CCmax_new_b = CCmax_new;
            }
            else
            {
                CCmax_new_b = CCmax_new - SKP_RSHIFT( SKP_SMULBB( PITCH_EST_NB_SUBFR * PITCH_EST_SHORTLAG_BIAS_Q15, lag_log2_Q7 ), 7 ); /* Q15 */
            }

            /* Bias towards previous lag */
            // SKP_assert( PITCH_EST_NB_SUBFR * PITCH_EST_PREVLAG_BIAS_Q15 == SKP_SAT16( PITCH_EST_NB_SUBFR * PITCH_EST_PREVLAG_BIAS_Q15 ) );（删除）
            if( prevLag > 0 )
            {
                delta_lag_log2_sqr_Q7 = lag_log2_Q7 - prevLag_log2_Q7;
                // SKP_assert( delta_lag_log2_sqr_Q7 == SKP_SAT16( delta_lag_log2_sqr_Q7 ) );（删除）
                delta_lag_log2_sqr_Q7 = SKP_RSHIFT( SKP_SMULBB( delta_lag_log2_sqr_Q7, delta_lag_log2_sqr_Q7 ), 7 );
                prev_lag_bias_Q15 = SKP_RSHIFT( SKP_SMULBB( PITCH_EST_NB_SUBFR * PITCH_EST_PREVLAG_BIAS_Q15, ( LTPCorr_Q15 ) ), 15 ); /* Q15 */
                prev_lag_bias_Q15 = SKP_DIV32( SKP_MUL( prev_lag_bias_Q15, delta_lag_log2_sqr_Q7 ), delta_lag_log2_sqr_Q7 + ( 1 << 6 ) );
                CCmax_new_b -= prev_lag_bias_Q15; /* Q15 */
            }

            if ( CCmax_new_b > CCmax_b                                          &&              /* Find maximum biased correlation                  */
                  CCmax_new > corr_thres_Q15                                    &&              /* Correlation needs to be high enough to be voiced */
                 SKP_Silk_CB_lags_stage2[ 0 ][ CBimax_new ] <= min_lag_8kHz                   /* Lag must be in range                             */
                )
            {
                CCmax_b = CCmax_new_b;
                CCmax   = CCmax_new;
                lag     = d;
                CBimax  = CBimax_new;
            }
        }

        if( lag == -1 )
        {
            /* No suitable candidate found */
            SKP_memset( pitch_out, pitch_outOff, 0, PITCH_EST_NB_SUBFR );
            LTPCorr_Q15  = 0;
            lagIndex     = 0;
            contourIndex = 0;
            return 1;
        }

        if( Fs_kHz > 8 )
        {
            /******************************************************************************
            ** Scale input signal down to avoid correlations measures from overflowing
            *******************************************************************************/
            /* find scaling as max scaling for each subframe */
            shift = SKP_FIX_P_Ana_find_scaling( signal, signalOff, frame_length, sf_length );
            if( shift > 0 )
            {
                /* Move signal to scratch mem because the input signal should be unchanged */
                /* Reuse the 32 bit scratch mem vector, use a 16 bit pointer from now */
                input_signal_ptr = scratch_mem;
                input_signal_ptrOff = 0;
                for( i = 0; i < frame_length; i++ )
                {
                    input_signal_ptr[ i ] = (short)SKP_RSHIFT( signal[ signalOff + i ], shift );
                }
            }
            else
            {
                input_signal_ptr = signal;
                input_signal_ptrOff = signalOff;
            }
            /*********************************************************************************/

            /* Search in original signal */

            CBimax_old = CBimax;
            /* Compensate for decimation */
            // SKP_assert( lag == SKP_SAT16( lag ) );（删除）
            if( Fs_kHz == 12 )
            {
                lag = SKP_RSHIFT( SKP_SMULBB( lag, 3 ), 1 );
            }
            else if( Fs_kHz == 16 )
            {
                lag = SKP_LSHIFT( lag, 1 );
            }
            else
            {
                lag = SKP_SMULBB( lag, 3 );
            }

            lag = SKP_LIMIT_int( lag, min_lag, max_lag );
            start_lag = SKP_max_int( lag - 2, min_lag );
            end_lag   = SKP_min_int( lag + 2, max_lag );
            lag_new   = lag;                                    /* to avoid undefined lag */
            CBimax    = 0;                                        /* to avoid undefined lag */
            // SKP_assert( SKP_LSHIFT( CCmax, 13 ) >= 0 );（删除）
            LTPCorr_Q15 = (int)SKP_Silk_SQRT_APPROX( SKP_LSHIFT( CCmax, 13 ) ); /* Output normalized correlation */

            CCmax = int.MinValue;   /* SKP_int32_MIN */
            /* pitch lags according to second stage */
            for( k = 0; k < PITCH_EST_NB_SUBFR; k++ )
            {
                pitch_out[ pitch_outOff + k ] = lag + 2 * SKP_Silk_CB_lags_stage2[ k ][ CBimax_old ];
            }
            /* Calculate the correlations and energies needed in stage 3 */
            SKP_FIX_P_Ana_calc_corr_st3(  crosscorr_st3, input_signal_ptr, input_signal_ptrOff, start_lag, sf_length, complexity );
            SKP_FIX_P_Ana_calc_energy_st3( energies_st3, input_signal_ptr, input_signal_ptrOff, start_lag, sf_length, complexity );

            lag_counter = 0;
            // SKP_assert( lag == SKP_SAT16( lag ) );（删除）
            contour_bias = SKP_DIV32_16( PITCH_EST_FLATCONTOUR_BIAS_Q20, lag );

            /* Setup cbk parameters acording to complexity setting */
            cbk_size   = (int)SKP_Silk_cbk_sizes_stage3[   complexity ];
            cbk_offset = (int)SKP_Silk_cbk_offsets_stage3[ complexity ];

            for( d = start_lag; d <= end_lag; d++ )
            {
                for( j = cbk_offset; j < ( cbk_offset + cbk_size ); j++ )
                {
                    cross_corr = 0;
                    energy     = 0;
                    for( k = 0; k < PITCH_EST_NB_SUBFR; k++ )
                    {
                        // SKP_assert( PITCH_EST_NB_SUBFR == 4 );（删除）
                        energy     += SKP_RSHIFT( energies_st3[  k ][ j ][ lag_counter ], 2 ); /* use mean, to avoid overflow */
                        // SKP_assert( energy >= 0 );（删除）
                        cross_corr += SKP_RSHIFT( crosscorr_st3[ k ][ j ][ lag_counter ], 2 ); /* use mean, to avoid overflow */
                    }
                    if( cross_corr > 0 )
                    {
                        /* Divide cross_corr / energy and get result in Q15 */
                        lz = SKP_Silk_CLZ32( cross_corr );
                        /* Divide with result in Q13, cross_corr could be larger than energy */
                        lshift = SKP_LIMIT_32( lz - 1, 0, 13 );
                        CCmax_new = SKP_DIV32( SKP_LSHIFT( cross_corr, lshift ), SKP_RSHIFT( energy, 13 - lshift ) + 1 );
                        CCmax_new = SKP_SAT16( CCmax_new );
                        CCmax_new = SKP_SMULWB( cross_corr, CCmax_new );
                        /* Saturate */
                        if( CCmax_new > SKP_RSHIFT( int.MaxValue /* SKP_int32_MAX */, 3 ) )
                        {
                            CCmax_new = int.MaxValue;   /* SKP_int32_MAX */
                        }
                        else
                        {
                            CCmax_new = SKP_LSHIFT( CCmax_new, 3 );
                        }
                        /* Reduce depending on flatness of contour */
                        diff = j - SKP_RSHIFT( PITCH_EST_NB_CBKS_STAGE3_MAX, 1 );
                        diff = SKP_MUL( diff, diff );
                        diff = short.MaxValue /* SKP_int16_MAX */ - SKP_RSHIFT( SKP_MUL( contour_bias, diff ), 5 ); /* Q20 -> Q15 */
                        // SKP_assert( diff == SKP_SAT16( diff ) );（删除）
                        CCmax_new = SKP_LSHIFT( SKP_SMULWB( CCmax_new, diff ), 1 );
                    }
                    else
                    {
                        CCmax_new = 0;
                    }

                    if( CCmax_new > CCmax                                               &&
                       ( d + (int)SKP_Silk_CB_lags_stage3[ 0 ][ j ] ) <= max_lag
                       )
                    {
                        CCmax   = CCmax_new;
                        lag_new = d;
                        CBimax  = j;
                    }
                }
                lag_counter++;
            }

            for( k = 0; k < PITCH_EST_NB_SUBFR; k++ )
            {
                pitch_out[ pitch_outOff + k ] = lag_new + SKP_Silk_CB_lags_stage3[ k ][ CBimax ];
            }
            lagIndex = lag_new - min_lag;
            contourIndex = CBimax;
        }
        else
        {
            /* Save Lags and correlation */
            CCmax = SKP_max( CCmax, 0 );
            LTPCorr_Q15 = (int)SKP_Silk_SQRT_APPROX( SKP_LSHIFT( CCmax, 13 ) ); /* Output normalized correlation */
            for( k = 0; k < PITCH_EST_NB_SUBFR; k++ )
            {
                pitch_out[ pitch_outOff + k ] = lag + SKP_Silk_CB_lags_stage2[ k ][ CBimax ];
            }
            lagIndex = lag - min_lag_8kHz;
            contourIndex = CBimax;
        }
        // SKP_assert( *lagIndex >= 0 );（删除）
        /* return as voiced */
        return 0;
    }

    /*************************************************************************/
    /* Calculates the correlations used in stage 3 search. In order to cover */
    /* the whole lag codebook for all the searched offset lags (lag +- 2),   */
    /*************************************************************************/
    public static void SKP_FIX_P_Ana_calc_corr_st3(
        int[][][] cross_corr_st3,                /* (O) 3 DIM correlation array */
        short[] signal, int signalOff,           /* I vector to correlate         */
        int start_lag,                            /* I lag offset to search around */
        int sf_length,                           /* I length of a 5 ms subframe   */
        int complexity                          /* I Complexity setting          */
        )
    {
        int target_ptrOff, basis_ptrOff;   /* C 的 target_ptr / basis_ptr 指针 → 偏移 */
        int cross_corr;
        int i, j, k, lag_counter;
        int cbk_offset, cbk_size, delta, idx;
        int[] scratch_mem = new int[ SCRATCH_SIZE ];

        // SKP_assert( complexity >= SKP_Silk_PITCH_EST_MIN_COMPLEX );（删除）
        // SKP_assert( complexity <= SKP_Silk_PITCH_EST_MAX_COMPLEX );（删除）

        cbk_offset = SKP_Silk_cbk_offsets_stage3[ complexity ];
        cbk_size   = SKP_Silk_cbk_sizes_stage3[   complexity ];

        target_ptrOff = signalOff + SKP_LSHIFT( sf_length, 2 ); /* Pointer to middle of frame */
        for( k = 0; k < PITCH_EST_NB_SUBFR; k++ )
        {
            lag_counter = 0;

            /* Calculate the correlations for each subframe */
            for( j = SKP_Silk_Lag_range_stage3[ complexity ][ k ][ 0 ]; j <= SKP_Silk_Lag_range_stage3[ complexity ][ k ][ 1 ]; j++ )
            {
                basis_ptrOff = target_ptrOff - ( start_lag + j );
                cross_corr = SKP_Silk_inner_prod_aligned( signal, target_ptrOff, signal, basis_ptrOff, sf_length );
                // SKP_assert( lag_counter < SCRATCH_SIZE );（删除）
                scratch_mem[ lag_counter ] = cross_corr;
                lag_counter++;
            }

            delta = SKP_Silk_Lag_range_stage3[ complexity ][ k ][ 0 ];
            for( i = cbk_offset; i < ( cbk_offset + cbk_size ); i++ )
            {
                /* Fill out the 3 dim array that stores the correlations for */
                /* each code_book vector for each start lag */
                idx = SKP_Silk_CB_lags_stage3[ k ][ i ] - delta;
                for( j = 0; j < PITCH_EST_NB_STAGE3_LAGS; j++ )
                {
                    // SKP_assert( idx + j < SCRATCH_SIZE );（删除）
                    // SKP_assert( idx + j < lag_counter );（删除）
                    cross_corr_st3[ k ][ i ][ j ] = scratch_mem[ idx + j ];
                }
            }
            target_ptrOff += sf_length;
        }
    }

    /********************************************************************/
    /* Calculate the energies for first two subframes. The energies are */
    /* calculated recursively.                                          */
    /********************************************************************/
    public static void SKP_FIX_P_Ana_calc_energy_st3(
        int[][][] energies_st3,                  /* (O) 3 DIM energy array */
        short[] signal, int signalOff,           /* I vector to calc energy in    */
        int start_lag,                           /* I lag offset to search around */
        int sf_length,                          /* I length of one 5 ms subframe */
        int complexity                           /* I Complexity setting          */
        )
    {
        int target_ptrOff, basis_ptrOff;   /* C 的 target_ptr / basis_ptr 指针 → 偏移 */
        int energy;
        int k, i, j, lag_counter;
        int cbk_offset, cbk_size, delta, idx;
        int[] scratch_mem = new int[ SCRATCH_SIZE ];

        // SKP_assert( complexity >= SKP_Silk_PITCH_EST_MIN_COMPLEX );（删除）
        // SKP_assert( complexity <= SKP_Silk_PITCH_EST_MAX_COMPLEX );（删除）

        cbk_offset = SKP_Silk_cbk_offsets_stage3[ complexity ];
        cbk_size   = SKP_Silk_cbk_sizes_stage3[   complexity ];

        target_ptrOff = signalOff + SKP_LSHIFT( sf_length, 2 );
        for( k = 0; k < PITCH_EST_NB_SUBFR; k++ )
        {
            lag_counter = 0;

            /* Calculate the energy for first lag */
            basis_ptrOff = target_ptrOff - ( start_lag + SKP_Silk_Lag_range_stage3[ complexity ][ k ][ 0 ] );
            energy = SKP_Silk_inner_prod_aligned( signal, basis_ptrOff, signal, basis_ptrOff, sf_length );
            // SKP_assert( energy >= 0 );（删除）
            scratch_mem[ lag_counter ] = energy;
            lag_counter++;

            for( i = 1; i < ( SKP_Silk_Lag_range_stage3[ complexity ][ k ][ 1 ] - SKP_Silk_Lag_range_stage3[ complexity ][ k ][ 0 ] + 1 ); i++ )
            {
                /* remove part outside new window */
                energy -= SKP_SMULBB( signal[ basis_ptrOff + sf_length - i ], signal[ basis_ptrOff + sf_length - i ] );
                // SKP_assert( energy >= 0 );（删除）

                /* add part that comes into window */
                energy = SKP_ADD_SAT32( energy, SKP_SMULBB( signal[ basis_ptrOff - i ], signal[ basis_ptrOff - i ] ) );
                // SKP_assert( energy >= 0 );（删除）
                // SKP_assert( lag_counter < SCRATCH_SIZE );（删除）
                scratch_mem[ lag_counter ] = energy;
                lag_counter++;
            }

            delta = SKP_Silk_Lag_range_stage3[ complexity ][ k ][ 0 ];
            for( i = cbk_offset; i < ( cbk_offset + cbk_size ); i++ )
            {
                /* Fill out the 3 dim array that stores the correlations for    */
                /* each code_book vector for each start lag                        */
                idx = SKP_Silk_CB_lags_stage3[ k ][ i ] - delta;
                for( j = 0; j < PITCH_EST_NB_STAGE3_LAGS; j++ )
                {
                    // SKP_assert( idx + j < SCRATCH_SIZE );（删除）
                    // SKP_assert( idx + j < lag_counter );（删除）
                    energies_st3[ k ][ i ][ j ] = scratch_mem[ idx + j ];
                    // SKP_assert( energies_st3[ k ][ i ][ j ] >= 0.0f );（删除）
                }
            }
            target_ptrOff += sf_length;
        }
    }

    public static int SKP_FIX_P_Ana_find_scaling(
        short[] signal, int signalOff,
        int signal_length,
        int sum_sqr_len
        )
    {
        int nbits, x_max;

        x_max = SKP_Silk_int16_array_maxabs( signal, signalOff, signal_length );

        if( x_max < short.MaxValue /* SKP_int16_MAX */ )
        {
            /* Number of bits needed for the sum of the squares */
            nbits = 32 - SKP_Silk_CLZ32( SKP_SMULBB( x_max, x_max ) );
        }
        else
        {
            /* Here we don't know if x_max should have been SKP_int16_MAX + 1, so we expect the worst case */
            nbits = 30;
        }
        nbits += 17 - SKP_Silk_CLZ16( (short)sum_sqr_len );

        /* Without a guarantee of saturation, we need to keep the 31st bit free */
        if( nbits < 31 )
        {
            return 0;
        }
        else
        {
            return( nbits - 30 );
        }
    }
}
