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
// SKP_Silk_decode_frame.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_decode_frame.c（单帧解码调度）
//
// PORT-NOTE: 本文件主函数与 C 文件同名（SKP_Silk_decode_frame），
//   C# 禁止成员名与其包含类型同名（CS0542），故类名加后缀 _impl，
//   方法名保持 C 原名（本簇统一约定，详见 SKP_Silk_enc_API.cs 文件头）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 地基：错误码
using static AaR.Server.Silk.SKP_Silk_errors;
// 本簇其他文件
using static AaR.Server.Silk.SKP_Silk_decode_parameters_impl;     // SKP_Silk_decode_parameters
using static AaR.Server.Silk.SKP_Silk_decode_core_impl;           // SKP_Silk_decode_core
using static AaR.Server.Silk.SKP_Silk_decoder_set_fs_impl;        // SKP_Silk_decoder_set_fs
// 其他簇（类名 = C 文件名，PORTING.md §1）
// PORT-NOTE: SKP_Silk_CNG.c / SKP_Silk_PLC.c / SKP_Silk_biquad.c 的主函数与其文件
//   同名（CS0542/CS1955 受影响文件），若集成期类名统一加后缀，仅需更新 using static 行。
using static AaR.Server.Silk.SKP_Silk_PLC_impl;                             // SKP_Silk_PLC / SKP_Silk_PLC_glue_frames
using static AaR.Server.Silk.SKP_Silk_CNG_impl;                             // SKP_Silk_CNG
using static AaR.Server.Silk.SKP_Silk_range_coder;                 // SKP_Silk_range_dec_init
using static AaR.Server.Silk.SKP_Silk_biquad_impl;                      // SKP_Silk_biquad

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_decode_frame.c —— 单帧解码调度。
/// </summary>
internal static class SKP_Silk_decode_frame_impl
{
    /****************/
    /* Decode frame */
    /****************/
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）：pOut/pCode 加偏移；
    //   pN / decBytes 为只写标量 → out。
    public static int SKP_Silk_decode_frame(
        SKP_Silk_decoder_state          psDec,               /* I/O  Pointer to Silk decoder state               */
        short[]                         pOut, int pOutOff,    /* O    Pointer to output speech frame              */
        out short                       pN,                  /* O    Pointer to size of output frame             */
        byte[]                          pCode, int pCodeOff, /* I    Pointer to payload                          */
        int                             nBytes,              /* I    Payload length                              */
        int                             action,              /* I    Action from Jitter Buffer                   */
        out int                         decBytes             /* O    Used bytes to decode this frame             */
    )
    {
        // PORT-NOTE: C 为栈上未初始化结构；托管 new() 零初始化，被读字段先写后读。
        SKP_Silk_decoder_control sDecCtrl = new();
        int         L, fs_Khz_old, ret = 0;
        int[]       Pulses = new int[ MAX_FRAME_LENGTH ];


        L = psDec.frame_length;
        sDecCtrl.LTP_scale_Q14 = 0;

        /* Safety checks */
        /* SKP_assert( L > 0 && L <= MAX_FRAME_LENGTH ); —— C 构建中为空，删除 */

        /********************************************/
        /* Decode Frame if packet is not lost  */
        /********************************************/
        decBytes = 0;
        if( action == 0 ) {
            /********************************************/
            /* Initialize arithmetic coder              */
            /********************************************/
            fs_Khz_old    = psDec.fs_kHz;
            if( psDec.nFramesDecoded == 0 ) {
                /* Initialize range decoder state */
                SKP_Silk_range_dec_init( psDec.sRC, pCode, pCodeOff, nBytes );
            }

            /********************************************/
            /* Decode parameters and pulse signal       */
            /********************************************/
            SKP_Silk_decode_parameters( psDec, sDecCtrl, Pulses, 0, 1 );


            if( psDec.sRC.error != 0 ) {
                psDec.nBytesLeft = 0;

                action              = 1; /* PLC operation */
                /* revert fs if changed in decode_parameters */
                SKP_Silk_decoder_set_fs( psDec, fs_Khz_old );

                /* Avoid crashing */
                decBytes = psDec.sRC.bufferLength;

                if( psDec.sRC.error == RANGE_CODER_DEC_PAYLOAD_TOO_LONG ) {
                    ret = SKP_SILK_DEC_PAYLOAD_TOO_LARGE;
                } else {
                    ret = SKP_SILK_DEC_PAYLOAD_ERROR;
                }
            } else {
                decBytes = psDec.sRC.bufferLength - psDec.nBytesLeft;
                psDec.nFramesDecoded++;

                /* Update lengths. Sampling frequency could have changed */
                L = psDec.frame_length;

                /********************************************************/
                /* Run inverse NSQ                                      */
                /********************************************************/
                SKP_Silk_decode_core( psDec, sDecCtrl, pOut, pOutOff, Pulses, 0 );

                /********************************************************/
                /* Update PLC state                                     */
                /********************************************************/
                SKP_Silk_PLC( psDec, sDecCtrl, pOut, pOutOff, L, action );

                psDec.lossCnt = 0;
                psDec.prev_sigtype = sDecCtrl.sigtype;

                /* A frame has been decoded without errors */
                psDec.first_frame_after_reset = 0;
            }
        }
        /*************************************************************/
        /* Generate Concealment frame if packet is lost, or corrupt  */
        /*************************************************************/
        if( action == 1 ) {
            /* Handle packet loss by extrapolation */
            SKP_Silk_PLC( psDec, sDecCtrl, pOut, pOutOff, L, action );
        }

        /*************************/
        /* Update output buffer. */
        /*************************/
        SKP_memcpy( psDec.outBuf, 0, pOut, pOutOff, L );

        /****************************************************************/
        /* Ensure smooth connection of extrapolated and good frames     */
        /****************************************************************/
        SKP_Silk_PLC_glue_frames( psDec, sDecCtrl, pOut, pOutOff, L );

        /************************************************/
        /* Comfort noise generation / estimation        */
        /************************************************/
        SKP_Silk_CNG( psDec, sDecCtrl, pOut, pOutOff, L );

        /********************************************/
        /* HP filter output                            */
        /********************************************/
        /* SKP_assert( ... ) —— C 构建中为空，删除 */
        // PORT-NOTE: C 的 in 与 out 为同一缓冲（pOut），按 PORTING.md §3 两条指针
        //   分别传同一（数组, 偏移），不合并。
        SKP_Silk_biquad( pOut, pOutOff, psDec.HP_B, 0, psDec.HP_A, 0, psDec.HPState, 0, pOut, pOutOff, L );

        /********************************************/
        /* set output frame length                    */
        /********************************************/
        pN = (short)L;

        /* Update some decoder state variables */
        psDec.lagPrev = sDecCtrl.pitchL[ NB_SUBFR - 1 ];


        return ret;
    }
}
