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
// SKP_Silk_LP_variable_cutoff.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_LP_variable_cutoff.c（函数 SKP_Silk_LP_interpolate_filter_taps /
//   SKP_Silk_LP_variable_cutoff）
// 移植说明：
//   - SWITCH_TRANSITION_FILTERING = 1（默认 gcc 构建），只移植其激活分支。
//   - TRANSITION_INT_STEPS_DOWN == 32 与 TRANSITION_INT_STEPS_UP == 64 在默认
//     构建下均成立，故移植 #if 的激活分支（移位实现），#else 分支不移植。
//   - 表 SKP_Silk_Transition_LP_B_Q28 / SKP_Silk_Transition_LP_A_Q28 为表簇产物
//     （static readonly 交错数组 int[][]），裸名引用。
//   - SKP_Silk_biquad_alt 为 DSP 簇产物，按 SigProc_FIX.h 原型推导签名，裸名调用。
//   - SKP_assert 在 C 构建中为空宏，按移植规约删除。
// PORT-NOTE: 同 SKP_Silk_control_audio_bandwidth.cs 头注——类名加 _impl（CS0542）。
//   参数名 out / in 为 C# 关键字/上下文关键字，写作 @out / @in（标识符不变）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_tables_other;
using static AaR.Server.Silk.SKP_Silk_biquad_alt_impl;

namespace AaR.Server.Silk;

/// <summary>src/SKP_Silk_LP_variable_cutoff.cs —— 基于椭圆滤波器分段线性插值的可变截止频率低通滤波。</summary>
internal static class SKP_Silk_LP_variable_cutoff_impl
{
    /*

        Elliptic/Cauer filters designed with 0.1 dB passband ripple,
            80 dB minimum stopband attenuation, and
            [0.95 : 0.15 : 0.35] normalized cut off frequencies.

    */

    /* Helper function, that interpolates the filter taps */
    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    public static void SKP_Silk_LP_interpolate_filter_taps(
        int[] B_Q28, int b_Q28Off,                  /* I/O  B_Q28[ TRANSITION_NB ] */
        int[] A_Q28, int a_Q28Off,                  /* I/O  A_Q28[ TRANSITION_NA ] */
        int ind,                                   /* I    */
        int fac_Q16                                 /* I    */
        )
    {
        int nb, na;

        if( ind < TRANSITION_INT_NUM - 1 )
        {
            if( fac_Q16 > 0 )
            {
                if( fac_Q16 == SKP_SAT16( fac_Q16 ) )     /* fac_Q16 is in range of a 16-bit int */
                {
                    /* Piece-wise linear interpolation of B and A */
                    for( nb = 0; nb < TRANSITION_NB; nb++ )
                    {
                        B_Q28[ b_Q28Off + nb ] = SKP_SMLAWB(
                            SKP_Silk_Transition_LP_B_Q28[ ind     ][ nb ],
                            SKP_Silk_Transition_LP_B_Q28[ ind + 1 ][ nb ] -
                            SKP_Silk_Transition_LP_B_Q28[ ind     ][ nb ],
                            fac_Q16 );
                    }
                    for( na = 0; na < TRANSITION_NA; na++ )
                    {
                        A_Q28[ a_Q28Off + na ] = SKP_SMLAWB(
                            SKP_Silk_Transition_LP_A_Q28[ ind     ][ na ],
                            SKP_Silk_Transition_LP_A_Q28[ ind + 1 ][ na ] -
                            SKP_Silk_Transition_LP_A_Q28[ ind     ][ na ],
                            fac_Q16 );
                    }
                }
                else if( fac_Q16 == ( 1 << 15 ) )         /* Neither fac_Q16 nor ( ( 1 << 16 ) - fac_Q16 ) is in range of a 16-bit int */
                {
                    /* Piece-wise linear interpolation of B and A */
                    for( nb = 0; nb < TRANSITION_NB; nb++ )
                    {
                        B_Q28[ b_Q28Off + nb ] = SKP_RSHIFT(
                            SKP_Silk_Transition_LP_B_Q28[ ind     ][ nb ] +
                            SKP_Silk_Transition_LP_B_Q28[ ind + 1 ][ nb ],
                            1 );
                    }
                    for( na = 0; na < TRANSITION_NA; na++ )
                    {
                        A_Q28[ a_Q28Off + na ] = SKP_RSHIFT(
                            SKP_Silk_Transition_LP_A_Q28[ ind     ][ na ] +
                            SKP_Silk_Transition_LP_A_Q28[ ind + 1 ][ na ],
                            1 );
                    }
                }
                else                                      /* ( ( 1 << 16 ) - fac_Q16 ) is in range of a 16-bit int */
                {
                    /* Piece-wise linear interpolation of B and A */
                    for( nb = 0; nb < TRANSITION_NB; nb++ )
                    {
                        B_Q28[ b_Q28Off + nb ] = SKP_SMLAWB(
                            SKP_Silk_Transition_LP_B_Q28[ ind + 1 ][ nb ],
                            SKP_Silk_Transition_LP_B_Q28[ ind     ][ nb ] -
                            SKP_Silk_Transition_LP_B_Q28[ ind + 1 ][ nb ],
                            ( 1 << 16 ) - fac_Q16 );
                    }
                    for( na = 0; na < TRANSITION_NA; na++ )
                    {
                        A_Q28[ a_Q28Off + na ] = SKP_SMLAWB(
                            SKP_Silk_Transition_LP_A_Q28[ ind + 1 ][ na ],
                            SKP_Silk_Transition_LP_A_Q28[ ind     ][ na ] -
                            SKP_Silk_Transition_LP_A_Q28[ ind + 1 ][ na ],
                            ( 1 << 16 ) - fac_Q16 );
                    }
                }
            }
            else
            {
                SKP_memcpy( B_Q28, b_Q28Off, SKP_Silk_Transition_LP_B_Q28[ ind ], 0, TRANSITION_NB );
                SKP_memcpy( A_Q28, a_Q28Off, SKP_Silk_Transition_LP_A_Q28[ ind ], 0, TRANSITION_NA );
            }
        }
        else
        {
            SKP_memcpy( B_Q28, b_Q28Off, SKP_Silk_Transition_LP_B_Q28[ TRANSITION_INT_NUM - 1 ], 0, TRANSITION_NB );
            SKP_memcpy( A_Q28, a_Q28Off, SKP_Silk_Transition_LP_A_Q28[ TRANSITION_INT_NUM - 1 ], 0, TRANSITION_NA );
        }
    }

    /* Low-pass filter with variable cutoff frequency based on  */
    /* piece-wise linear interpolation between elliptic filters */
    /* Start by setting psEncC->transition_frame_no = 1;            */
    /* Deactivate by setting psEncC->transition_frame_no = 0;   */
    public static void SKP_Silk_LP_variable_cutoff(
        SKP_Silk_LP_state psLP,                  /* I/O  LP filter state                     */
        short[] @out, int outOff,                /* O    Low-pass filtered output signal     */
        short[] @in, int inOff,                  /* I    Input signal                        */
        int frame_length                         /* I    Frame length                        */
        )
    {
        int[] B_Q28 = new int[ TRANSITION_NB ], A_Q28 = new int[ TRANSITION_NA ];
        int fac_Q16 = 0;
        int ind = 0;

        /* Interpolate filter coefficients if needed */
        if( psLP.transition_frame_no > 0 )
        {
            if( psLP.mode == 0 )
            {
                if( psLP.transition_frame_no < TRANSITION_FRAMES_DOWN )
                {
                    /* Calculate index and interpolation factor for interpolation */
                    // C 源为 #if( TRANSITION_INT_STEPS_DOWN == 32 ) 的激活分支（默认构建下成立）
                    fac_Q16 = SKP_LSHIFT( psLP.transition_frame_no, 16 - 5 );
                    ind      = SKP_RSHIFT( fac_Q16, 16 );
                    fac_Q16 -= SKP_LSHIFT( ind, 16 );

                    /* Interpolate filter coefficients */
                    SKP_Silk_LP_interpolate_filter_taps( B_Q28, 0, A_Q28, 0, ind, fac_Q16 );

                    /* Increment transition frame number for next frame */
                    psLP.transition_frame_no++;

                }
                else
                {
                    // SKP_assert( psLP->transition_frame_no == TRANSITION_FRAMES_DOWN );（删除）
                    /* End of transition phase */
                    SKP_Silk_LP_interpolate_filter_taps( B_Q28, 0, A_Q28, 0, TRANSITION_INT_NUM - 1, 0 );
                }
            }
            else
            {
                // SKP_assert( psLP->mode == 1 );（删除）
                if( psLP.transition_frame_no < TRANSITION_FRAMES_UP )
                {
                    /* Calculate index and interpolation factor for interpolation */
                    // C 源为 #if( TRANSITION_INT_STEPS_UP == 64 ) 的激活分支（默认构建下成立）
                    fac_Q16 = SKP_LSHIFT( TRANSITION_FRAMES_UP - psLP.transition_frame_no, 16 - 6 );
                    ind      = SKP_RSHIFT( fac_Q16, 16 );
                    fac_Q16 -= SKP_LSHIFT( ind, 16 );

                    /* Interpolate filter coefficients */
                    SKP_Silk_LP_interpolate_filter_taps( B_Q28, 0, A_Q28, 0, ind, fac_Q16 );

                    /* Increment transition frame number for next frame */
                    psLP.transition_frame_no++;

                }
                else
                {
                    // SKP_assert( psLP->transition_frame_no == TRANSITION_FRAMES_UP );（删除）
                    /* End of transition phase */
                    SKP_Silk_LP_interpolate_filter_taps( B_Q28, 0, A_Q28, 0, 0, 0 );
                }
            }
        }

        if( psLP.transition_frame_no > 0 )
        {
            /* ARMA low-pass filtering */
            // SKP_assert( TRANSITION_NB == 3 && TRANSITION_NA == 2 );（删除）
            SKP_Silk_biquad_alt( @in, inOff, B_Q28, 0, A_Q28, 0, psLP.In_LP_State, 0, @out, outOff, frame_length );
        }
        else
        {
            /* Instead of using the filter, copy input directly to output */
            SKP_memcpy( @out, outOff, @in, inOff, frame_length );
        }
    }
}
