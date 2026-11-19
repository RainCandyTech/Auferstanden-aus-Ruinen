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
// SKP_Silk_decode_core.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_decode_core.c（核心解码器：逆 NSQ，LTP + LPC 合成）
//
// PORT-NOTE: 本文件主函数与 C 文件同名（SKP_Silk_decode_core），
//   C# 禁止成员名与其包含类型同名（CS0542），故类名加后缀 _impl，
//   方法名保持 C 原名（本簇统一约定，详见 SKP_Silk_enc_API.cs 文件头）。
//   C 条件编译：默认 gcc 构建 EMBEDDED_ARM 未定义（按 0 处理），#if EMBEDDED_ARM<5
//   成立，故移植小端通用分支（decode_short_term_prediction 的 Atmp 打包路径）；
//   _SYSTEM_IS_BIG_ENDIAN 分支按 PORTING.md §7 不移植。
//   C 局部指针（pxq / pexc_Q10 / pres_Q10 / pred_lag_ptr / A_Q12 / B_Q14）改写为
//   （基址数组, 偏移）二元组或偏移局部量，见各处 PORT-NOTE。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 地基：定点除法/倒数内联函数
using static AaR.Server.Silk.SKP_Silk_Inlines;
// 其他簇（类名 = C 文件名，PORTING.md §1；SKP_Silk_MA.c 的主函数不与其文件同名）
using static AaR.Server.Silk.SKP_Silk_MA;                           // SKP_Silk_MA_Prediction
// 表（其他簇）
using static AaR.Server.Silk.SKP_Silk_tables_other;           // SKP_Silk_Quantization_Offsets_Q10（表实际位于 tables_other，非 type_offset）

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_decode_core.c —— 核心解码器。
/// </summary>
internal static class SKP_Silk_decode_core_impl
{
    /**********************************************************/
    /* Core decoder. Performs inverse NSQ operation LTP + LPC */
    /**********************************************************/
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）：xq / q 加偏移。
    public static void SKP_Silk_decode_core(
        SKP_Silk_decoder_state      psDec,                          /* I/O  Decoder state               */
        SKP_Silk_decoder_control    psDecCtrl,                      /* I    Decoder control             */
        short[]                     xq, int xqOff,                  /* O    Decoded speech              */
        int[]                       q, int qOff                     /* I    Pulse signal                */
    )
    {
        int   i, k, lag = 0, start_idx, sLTP_buf_idx, NLSF_interpolation_flag, sigtype;
        short[] A_Q12, B_Q14, A_Q12_tmp = new short[ MAX_LPC_ORDER ];
        int   B_Q14_off;
        short[] sLTP = new short[ MAX_FRAME_LENGTH ];
        int   LTP_pred_Q14, Gain_Q16, inv_gain_Q16, inv_gain_Q32, gain_adj_Q16, rand_seed, offset_Q10, dither;
        int   pred_lag_ptr, pexc_Q10, pres_Q10, pxq;
        int[] vec_Q10 = new int[ MAX_FRAME_LENGTH / NB_SUBFR ];
        int[] FiltState = new int[ MAX_LPC_ORDER ];

        /* SKP_assert( psDec->prev_inv_gain_Q16 != 0 ); —— C 构建中为空，删除 */

        offset_Q10 = SKP_Silk_Quantization_Offsets_Q10[ psDecCtrl.sigtype ][ psDecCtrl.QuantOffsetType ];

        if( psDecCtrl.NLSFInterpCoef_Q2 < ( 1 << 2 ) ) {
            NLSF_interpolation_flag = 1;
        } else {
            NLSF_interpolation_flag = 0;
        }


        /* Decode excitation */
        rand_seed = psDecCtrl.Seed;
        for( i = 0; i < psDec.frame_length; i++ ) {
            rand_seed = SKP_RAND( rand_seed );
            /* dither = rand_seed < 0 ? 0xFFFFFFFF : 0; */
            dither = SKP_RSHIFT( rand_seed, 31 );

            psDec.exc_Q10[ i ] = SKP_LSHIFT( (int)q[ qOff + i ], 10 ) + offset_Q10;
            psDec.exc_Q10[ i ] = ( psDec.exc_Q10[ i ] ^ dither ) - dither;

            rand_seed += q[ qOff + i ];
        }


        // PORT-NOTE: C 局部指针 pexc_Q10 / pres_Q10 / pxq 分别指向
        //   psDec->exc_Q10 / psDec->res_Q10 / &psDec->outBuf[ frame_length ]，
        //   此处改写为偏移局部量（基址数组固定）。
        pexc_Q10 = 0;
        pres_Q10 = 0;
        pxq      = psDec.frame_length;
        sLTP_buf_idx = psDec.frame_length;
        /* Loop over subframes */
        for( k = 0; k < NB_SUBFR; k++ ) {
            A_Q12 = psDecCtrl.PredCoef_Q12[ k >> 1 ];

            /* Preload LPC coeficients to array on stack. Gives small performance gain */
            SKP_memcpy( A_Q12_tmp, 0, A_Q12, 0, psDec.LPC_order );
            B_Q14      = psDecCtrl.LTPCoef_Q14;
            B_Q14_off  = k * LTP_ORDER;
            Gain_Q16   = psDecCtrl.Gains_Q16[ k ];
            sigtype    = psDecCtrl.sigtype;

            // PORT-NOTE: C 宏 SKP_int16_MAX = 0x7FFF
            inv_gain_Q16 = SKP_INVERSE32_varQ( SKP_max( Gain_Q16, 1 ), 32 );
            inv_gain_Q16 = SKP_min( inv_gain_Q16, short.MaxValue );

            /* Calculate Gain adjustment factor */
            gain_adj_Q16 = (int)1 << 16;
            if( inv_gain_Q16 != psDec.prev_inv_gain_Q16 ) {
                gain_adj_Q16 =  SKP_DIV32_varQ( inv_gain_Q16, psDec.prev_inv_gain_Q16, 16 );
            }

            /* Avoid abrupt transition from voiced PLC to unvoiced normal decoding */
            if( psDec.lossCnt != 0 && psDec.prev_sigtype == SIG_TYPE_VOICED &&
                psDecCtrl.sigtype == SIG_TYPE_UNVOICED && k < ( NB_SUBFR >> 1 ) ) {

                SKP_memset( B_Q14, B_Q14_off, (short)0, LTP_ORDER );
                B_Q14[ B_Q14_off + LTP_ORDER/2 ] = (short)( 1 << 12 ); /* 0.25 */

                sigtype = SIG_TYPE_VOICED;
                psDecCtrl.pitchL[ k ] = psDec.lagPrev;
            }

            if( sigtype == SIG_TYPE_VOICED ) {
                /* Voiced */

                lag = psDecCtrl.pitchL[ k ];
                /* Re-whitening */
                if( ( k & ( 3 - SKP_LSHIFT( NLSF_interpolation_flag, 1 ) ) ) == 0 ) {
                    /* Rewhiten with new A coefs */
                    start_idx = psDec.frame_length - lag - psDec.LPC_order - LTP_ORDER / 2;
                    /* SKP_assert( start_idx >= 0 ); —— 删除 */
                    /* SKP_assert( start_idx <= psDec->frame_length - psDec->LPC_order ); —— 删除 */

                    SKP_memset( FiltState, 0, 0, psDec.LPC_order ); /* Not really necessary, but Valgrind and Coverity will complain otherwise */
                    SKP_Silk_MA_Prediction( psDec.outBuf, start_idx + k * ( psDec.frame_length >> 2 ),
                        A_Q12, 0, FiltState, 0, sLTP, start_idx, psDec.frame_length - start_idx, psDec.LPC_order );

                    /* After rewhitening the LTP state is unscaled */
                    inv_gain_Q32 = SKP_LSHIFT( inv_gain_Q16, 16 );
                    if( k == 0 ) {
                        /* Do LTP downscaling */
                        inv_gain_Q32 = SKP_LSHIFT( SKP_SMULWB( inv_gain_Q32, psDecCtrl.LTP_scale_Q14 ), 2 );
                    }
                    for( i = 0; i < (lag + LTP_ORDER/2); i++ ) {
                        psDec.sLTP_Q16[ sLTP_buf_idx - i - 1 ] = SKP_SMULWB( inv_gain_Q32, sLTP[ psDec.frame_length - i - 1 ] );
                    }
                } else {
                    /* Update LTP state when Gain changes */
                    if( gain_adj_Q16 != (int)1 << 16 ) {
                        for( i = 0; i < ( lag + LTP_ORDER / 2 ); i++ ) {
                            psDec.sLTP_Q16[ sLTP_buf_idx - i - 1 ] = SKP_SMULWW( gain_adj_Q16, psDec.sLTP_Q16[ sLTP_buf_idx - i - 1 ] );
                        }
                    }
                }
            }

            /* Scale short term state */
            for( i = 0; i < MAX_LPC_ORDER; i++ ) {
                psDec.sLPC_Q14[ i ] = SKP_SMULWW( gain_adj_Q16, psDec.sLPC_Q14[ i ] );
            }

            /* Save inv_gain */
            /* SKP_assert( inv_gain_Q16 != 0 ); —— C 构建中为空，删除 */
            psDec.prev_inv_gain_Q16 = inv_gain_Q16;

            /* Long-term prediction */
            if( sigtype == SIG_TYPE_VOICED ) {
                /* Setup pointer */
                // PORT-NOTE: C 局部指针 pred_lag_ptr = &psDec->sLTP_Q16[ sLTP_buf_idx - lag + LTP_ORDER / 2 ]
                //   → 偏移局部量（基址 psDec.sLTP_Q16 固定）。
                pred_lag_ptr = sLTP_buf_idx - lag + LTP_ORDER / 2;
                for( i = 0; i < psDec.subfr_length; i++ ) {
                    /* Unrolled loop */
                    LTP_pred_Q14 = SKP_SMULWB(               psDec.sLTP_Q16[ pred_lag_ptr      ], B_Q14[ B_Q14_off      ] );
                    LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, psDec.sLTP_Q16[ pred_lag_ptr -  1 ], B_Q14[ B_Q14_off + 1 ] );
                    LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, psDec.sLTP_Q16[ pred_lag_ptr -  2 ], B_Q14[ B_Q14_off + 2 ] );
                    LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, psDec.sLTP_Q16[ pred_lag_ptr -  3 ], B_Q14[ B_Q14_off + 3 ] );
                    LTP_pred_Q14 = SKP_SMLAWB( LTP_pred_Q14, psDec.sLTP_Q16[ pred_lag_ptr -  4 ], B_Q14[ B_Q14_off + 4 ] );
                    pred_lag_ptr++;

                    /* Generate LPC residual */
                    psDec.res_Q10[ pres_Q10 + i ] = SKP_ADD32( psDec.exc_Q10[ pexc_Q10 + i ], SKP_RSHIFT_ROUND( LTP_pred_Q14, 4 ) );

                    /* Update states */
                    psDec.sLTP_Q16[ sLTP_buf_idx ] = SKP_LSHIFT( psDec.res_Q10[ pres_Q10 + i ], 6 );
                    sLTP_buf_idx++;
                }
            } else {
                SKP_memcpy( psDec.res_Q10, pres_Q10, psDec.exc_Q10, pexc_Q10, psDec.subfr_length );
            }

            SKP_Silk_decode_short_term_prediction(vec_Q10, 0, psDec.res_Q10, pres_Q10, psDec.sLPC_Q14, 0, A_Q12_tmp, 0, psDec.LPC_order, psDec.subfr_length);

            /* Scale with Gain */
            for( i = 0; i < psDec.subfr_length; i++ ) {
                psDec.outBuf[ pxq + i ] = (short)SKP_SAT16( SKP_RSHIFT_ROUND( SKP_SMULWW( vec_Q10[ i ], Gain_Q16 ), 10 ) );
            }

            /* Update LPC filter state */
            // PORT-NOTE: C 此处 SKP_memcpy 的源/目的重叠（同数组左移 subfr_length），
            //   地基助手基于 Array.Copy，语义等价 memmove，与该搬移意图一致。
            SKP_memcpy( psDec.sLPC_Q14, 0, psDec.sLPC_Q14, psDec.subfr_length, MAX_LPC_ORDER );
            pexc_Q10 += psDec.subfr_length;
            pres_Q10 += psDec.subfr_length;
            pxq      += psDec.subfr_length;
        }

        /* Copy to output */
        SKP_memcpy( xq, xqOff, psDec.outBuf, psDec.frame_length, psDec.frame_length );

    }

    /* PORT-NOTE: C 条件编译 #if EMBEDDED_ARM<5 —— 默认构建成立，保留；
       其中 #if !defined(_SYSTEM_IS_BIG_ENDIAN) 的小端分支为默认分支。 */
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）。
    public static void SKP_Silk_decode_short_term_prediction(
        int[]      vec_Q10, int vec_Q10Off,
        int[]      pres_Q10, int pres_Q10Off,
        int[]      sLPC_Q14, int sLPC_Q14Off,
        short[]    A_Q12_tmp, int A_Q12_tmpOff,
        int        LPC_order,
        int        subfr_length
    )
    {
        int   i;
        int   LPC_pred_Q10;
        {
            int Atmp;
            /* Short term prediction */
            /* NOTE: the code below loads two int16 values in an int32, and multiplies each using the   */
            /* SMLAWB and SMLAWT instructions. On a big-endian CPU the two int16 variables would be     */
            /* loaded in reverse order and the code will give the wrong result. In that case swapping   */
            /* the SMLAWB and SMLAWT instructions should solve the problem.                             */
            // PORT-NOTE: C 为 *( (SKP_int32*)&A_Q12_tmp[ n ] ) 小端打包读 —— 两个 int16
            //   拼为一个 int32（低 16 位为 A_Q12_tmp[n]，高 16 位为 A_Q12_tmp[n+1]）。
            if( LPC_order == 16 ) {
                for( i = 0; i < subfr_length; i++ ) {
                    /* unrolled */
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 0 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 1 ] << 16 ));    /* read two coefficients at once */
                    LPC_pred_Q10 = SKP_SMULWB(               sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  1 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  2 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 2 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 3 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  3 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  4 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 4 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 5 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  5 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  6 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 6 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 7 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  7 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  8 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 8 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 9 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  9 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 10 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 10 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 11 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 11 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 12 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 12 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 13 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 13 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 14 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 14 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 15 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 15 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 16 ], Atmp );

                    /* Add prediction to LPC residual */
                    vec_Q10[ vec_Q10Off + i ] = SKP_ADD32( pres_Q10[ pres_Q10Off + i ], LPC_pred_Q10 );

                    /* Update states */
                    sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i ] = SKP_LSHIFT_ovflw( vec_Q10[ vec_Q10Off + i ], 4 );
                }
            } else {
                /* SKP_assert( LPC_order == 10 ); —— C 构建中为空，删除 */
                for( i = 0; i < subfr_length; i++ ) {
                    /* unrolled */
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 0 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 1 ] << 16 ));    /* read two coefficients at once */
                    LPC_pred_Q10 = SKP_SMULWB(               sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  1 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  2 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 2 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 3 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  3 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  4 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 4 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 5 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  5 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  6 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 6 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 7 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  7 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  8 ], Atmp );
                    Atmp = (int)((uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 8 ] | ( (uint)(ushort)A_Q12_tmp[ A_Q12_tmpOff + 9 ] << 16 ));
                    LPC_pred_Q10 = SKP_SMLAWB( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i -  9 ], Atmp );
                    LPC_pred_Q10 = SKP_SMLAWT( LPC_pred_Q10, sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i - 10 ], Atmp );

                    /* Add prediction to LPC residual */
                    vec_Q10[ vec_Q10Off + i ] = SKP_ADD32( pres_Q10[ pres_Q10Off + i ], LPC_pred_Q10 );

                    /* Update states */
                    sLPC_Q14[ sLPC_Q14Off + MAX_LPC_ORDER + i ] = SKP_LSHIFT_ovflw( vec_Q10[ vec_Q10Off + i ], 4 );
                }
            }
        }
    }
}
