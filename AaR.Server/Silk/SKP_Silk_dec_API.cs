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
// SKP_Silk_dec_API.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_dec_API.c（解码器顶层 API：Get_Decoder_Size / InitDecoder /
//   Decode / search_for_LBRR / get_TOC / get_version）
//
// PORT-NOTE: C 的 void* 状态改为强类型 SKP_Silk_decoder_state 直传（本簇任务
//   规约第 2 条）。search_for_LBRR / get_TOC 中 C 为栈上未初始化局部解码器状态，
//   托管 new() 零初始化；被读取的字段均在读取前写入（见各处 PORT-NOTE）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
// 地基：错误码
using static AaR.Server.Silk.SKP_Silk_errors;
// 本簇其他文件
using static AaR.Server.Silk.SKP_Silk_decode_frame_impl;          // SKP_Silk_decode_frame
using static AaR.Server.Silk.SKP_Silk_decode_parameters_impl;     // SKP_Silk_decode_parameters
using static AaR.Server.Silk.SKP_Silk_create_init_destroy;         // SKP_Silk_init_decoder
// 其他簇（类名 = C 文件名，PORTING.md §1）
// PORT-NOTE: SKP_Silk_resampler.c 的主函数与其文件同名（CS0542/CS1955 受影响
//   文件），若集成期类名统一加后缀，仅需更新下面一行 using static。
using static AaR.Server.Silk.SKP_Silk_resampler_impl;                   // SKP_Silk_resampler_init / SKP_Silk_resampler
using static AaR.Server.Silk.SKP_Silk_range_coder;                 // SKP_Silk_range_dec_init

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_dec_API.c —— 解码器顶层 API。
/// </summary>
internal static class SKP_Silk_dec_API
{
    /*********************/
    /* Decoder functions */
    /*********************/

    // PORT-NOTE: C 版本向 *decSizeBytes 写入 sizeof( SKP_Silk_decoder_state )。
    // 托管侧状态为类实例，无需查询字节数，移植为无参并返回 0。
    public static int SKP_Silk_SDK_Get_Decoder_Size()
    {
        int ret = 0;

        return ret;
    }

    /* Reset decoder state */
    public static int SKP_Silk_SDK_InitDecoder(
        SKP_Silk_decoder_state decState                /* I/O: State                                          */
    )
    {
        int ret = 0;

        ret  = SKP_Silk_init_decoder( decState );

        return ret;
    }

    /* Decode a frame */
    // PORT-NOTE: 指针 → 数组 + 偏移（PORTING.md §3）。C 中
    //   pSamplesOutInternal（指向 samplesOut 或局部缓冲的指针）改写为
    //   （数组, 偏移）二元组；*nSamplesOut → ref short。
    public static int SKP_Silk_SDK_Decode(
        SKP_Silk_decoder_state         psDec,               /* I/O: State                                           */
        SKP_SILK_SDK_DecControlStruct  decControl,           /* I/O: Control structure                               */
        int                            lostFlag,             /* I:   0: no loss, 1 loss                              */
        byte[]                         inData, int inDataOff,/* I:   Encoded input vector                            */
        int                            nBytesIn,              /* I:   Number of input Bytes                           */
        short[]                        samplesOut, int samplesOutOff, /* O:   Decoded output speech vector            */
        ref short                      nSamplesOut            /* I/O: Number of samples (vector/decoded)              */
    )
    {
        int ret = 0, used_bytes, prev_fs_kHz;
        short[] samplesOutInternal = new short[ MAX_API_FS_KHZ * FRAME_LENGTH_MS ];
        short[] pSamplesOutInternal;
        int    pSamplesOutInternalOff;

        /* We need this buffer to have room for an internal frame */
        pSamplesOutInternal    = samplesOut;
        pSamplesOutInternalOff = samplesOutOff;
        if( psDec.fs_kHz * 1000 > decControl.API_sampleRate ) {
            pSamplesOutInternal    = samplesOutInternal;
            pSamplesOutInternalOff = 0;
        }

        /**********************************/
        /* Test if first frame in payload */
        /**********************************/
        if( psDec.moreInternalDecoderFrames == 0 ) {
            /* First Frame in Payload */
            psDec.nFramesDecoded = 0;  /* Used to count frames in packet */
        }

        if( psDec.moreInternalDecoderFrames == 0 &&    /* First frame in packet    */
            lostFlag == 0 &&                            /* Not packet loss          */
            nBytesIn > MAX_ARITHM_BYTES ) {             /* Too long payload         */
                /* Avoid trying to decode a too large packet */
                lostFlag = 1;
                ret = SKP_SILK_DEC_PAYLOAD_TOO_LARGE;
        }

        /* Save previous sample frequency */
        prev_fs_kHz = psDec.fs_kHz;

        /* Call decoder for one frame */
        ret += SKP_Silk_decode_frame( psDec, pSamplesOutInternal, pSamplesOutInternalOff, out nSamplesOut,
            inData, inDataOff, nBytesIn, lostFlag, out used_bytes );

        if( used_bytes != 0 ) { /* Only Call if not a packet loss */
            if( psDec.nBytesLeft > 0 && psDec.FrameTermination == SKP_SILK_MORE_FRAMES && psDec.nFramesDecoded < 5 ) {
                /* We have more frames in the Payload */
                psDec.moreInternalDecoderFrames = 1;
            } else {
                /* Last frame in Payload */
                psDec.moreInternalDecoderFrames = 0;
                psDec.nFramesInPacket = psDec.nFramesDecoded;

                /* Track inband FEC usage */
                if( psDec.vadFlag == VOICE_ACTIVITY ) {
                    if( psDec.FrameTermination == SKP_SILK_LAST_FRAME ) {
                        psDec.no_FEC_counter++;
                        if( psDec.no_FEC_counter > NO_LBRR_THRES ) {
                            psDec.inband_FEC_offset = 0;
                        }
                    } else if( psDec.FrameTermination == SKP_SILK_LBRR_VER1 ) {
                        psDec.inband_FEC_offset = 1; /* FEC info with 1 packet delay */
                        psDec.no_FEC_counter    = 0;
                    } else if( psDec.FrameTermination == SKP_SILK_LBRR_VER2 ) {
                        psDec.inband_FEC_offset = 2; /* FEC info with 2 packets delay */
                        psDec.no_FEC_counter    = 0;
                    }
                }
            }
        }

        if( MAX_API_FS_KHZ * 1000 < decControl.API_sampleRate ||
            8000       > decControl.API_sampleRate ) {
            ret = SKP_SILK_DEC_INVALID_SAMPLING_FREQUENCY;
            return( ret );
        }

        /* Resample if needed */
        if( psDec.fs_kHz * 1000 != decControl.API_sampleRate ) {
            short[] samplesOut_tmp = new short[ MAX_API_FS_KHZ * FRAME_LENGTH_MS ];

            /* Copy to a tmp buffer as the resampling writes to samplesOut */
            SKP_memcpy( samplesOut_tmp, 0, pSamplesOutInternal, pSamplesOutInternalOff, nSamplesOut );

            /* (Re-)initialize resampler state when switching internal sampling frequency */
            if( prev_fs_kHz != psDec.fs_kHz || psDec.prev_API_sampleRate != decControl.API_sampleRate ) {
                ret = SKP_Silk_resampler_init( psDec.resampler_state, SKP_SMULBB( psDec.fs_kHz, 1000 ), decControl.API_sampleRate );
            }

            /* Resample the output to API_sampleRate */
            ret += SKP_Silk_resampler( psDec.resampler_state, samplesOut, samplesOutOff, samplesOut_tmp, 0, nSamplesOut );

            /* Update the number of output samples */
            nSamplesOut = (short)SKP_DIV32( (int)nSamplesOut * decControl.API_sampleRate, psDec.fs_kHz * 1000 );
        } else if( prev_fs_kHz * 1000 > decControl.API_sampleRate ) {
            SKP_memcpy( samplesOut, samplesOutOff, pSamplesOutInternal, pSamplesOutInternalOff, nSamplesOut );
        }

        psDec.prev_API_sampleRate = decControl.API_sampleRate;

        /* Copy all parameters that are needed out of internal structure to the control stucture */
        decControl.frameSize                 = (int)(ushort)( decControl.API_sampleRate / 50 ) ;
        decControl.framesPerPacket           = (int)psDec.nFramesInPacket;
        decControl.inBandFECOffset           = (int)psDec.inband_FEC_offset;
        decControl.moreInternalDecoderFrames = (int)psDec.moreInternalDecoderFrames;

        return ret;
    }

    /* Function to find LBRR information in a packet */
    // PORT-NOTE: 指针 → 数组 + 偏移：inData/LBRRData 加偏移；nLBRRBytes 为只写 → out。
    public static void SKP_Silk_SDK_search_for_LBRR(
        byte[]  inData, int inDataOff,                     /* I:   Encoded input vector                            */
        int     nBytesIn,                                  /* I:   Number of input Bytes                           */
        int     lost_offset,                                /* I:   Offset from lost packet                         */
        byte[]  LBRRData, int LBRRDataOff,                  /* O:   LBRR payload                                    */
        out short nLBRRBytes                                /* O:   Number of LBRR Bytes                            */
    )
    {
        // PORT-NOTE: C 为栈上局部解码器状态（未初始化）。以下字段在读取前均有写入：
        //   nFramesDecoded / fs_kHz / lossCnt 显式置零，prevNLSF_Q15 memset，sRC 由
        //   range_dec_init 初始化，其余字段由 decode_parameters 写入后再读。
        //   托管 new() 零初始化与该路径语义一致（nFramesInPacket 等未读字段不参与）。
        SKP_Silk_decoder_state   sDec = new(); // Local decoder state to avoid interfering with running decoder */
        SKP_Silk_decoder_control sDecCtrl = new();
        int[] TempQ = new int[ MAX_FRAME_LENGTH ];

        if( lost_offset < 1 || lost_offset > MAX_LBRR_DELAY ) {
            /* No useful FEC in this packet */
            nLBRRBytes = 0;
            return;
        }

        sDec.nFramesDecoded = 0;
        sDec.fs_kHz         = 0; /* Force update parameters LPC_order etc */
        sDec.lossCnt        = 0; /* Avoid running bw expansion of the LPC parameters when searching for LBRR data */
        SKP_memset( sDec.prevNLSF_Q15, 0, 0, MAX_LPC_ORDER );
        SKP_Silk_range_dec_init( sDec.sRC, inData, inDataOff, nBytesIn );

        while(true) {
            SKP_Silk_decode_parameters( sDec, sDecCtrl, TempQ, 0, 0 );

            if( sDec.sRC.error != 0 ) {
                /* Corrupt stream */
                nLBRRBytes = 0;
                return;
            }
            if( ( ( sDec.FrameTermination - 1 ) & lost_offset ) != 0 && sDec.FrameTermination > 0 && sDec.nBytesLeft >= 0 ) {
                /* The wanted FEC is present in the packet */
                nLBRRBytes = (short)sDec.nBytesLeft;
                SKP_memcpy( LBRRData, LBRRDataOff, inData, inDataOff + ( nBytesIn - sDec.nBytesLeft ), sDec.nBytesLeft );
                break;
            }
            if( sDec.nBytesLeft > 0 && sDec.FrameTermination == SKP_SILK_MORE_FRAMES ) {
                sDec.nFramesDecoded++;
            } else {
                /* PORT-NOTE: C 的 LBRRData = NULL 为死存储（置空后立即 break，
                   对调用方无可见效果），托管侧数组参数无法置空，不予移植。 */
                nLBRRBytes = 0;
                break;
            }
        }
    }

    /* Getting type of content for a packet */
    public static void SKP_Silk_SDK_get_TOC(
        byte[]                  inData, int inDataOff,     /* I:   Encoded input vector                            */
        int                     nBytesIn,                  /* I:   Number of input bytes                           */
        SKP_Silk_TOC_struct     Silk_TOC                    /* O:   Type of content                                 */
    )
    {
        // PORT-NOTE: C 为栈上局部解码器状态（未初始化）；托管 new() 零初始化。
        //   C 中 sDec.nFramesInPacket 为栈上垃圾但仅用于末尾 corrupt 判定，
        //   零初始化给出确定性行为（0 恒 <= SILK_MAX_FRAMES_PER_PACKET）。
        SKP_Silk_decoder_state   sDec = new(); // Local Decoder state to avoid interfering with running decoder */
        SKP_Silk_decoder_control sDecCtrl = new();
        int[] TempQ = new int[ MAX_FRAME_LENGTH ];

        sDec.nFramesDecoded = 0;
        sDec.fs_kHz         = 0; /* Force update parameters LPC_order etc */
        SKP_Silk_range_dec_init( sDec.sRC, inData, inDataOff, nBytesIn );

        Silk_TOC.corrupt = 0;
        while( true ) {
            SKP_Silk_decode_parameters( sDec, sDecCtrl, TempQ, 0, 0 );

            Silk_TOC.vadFlags[     sDec.nFramesDecoded ] = sDec.vadFlag;
            Silk_TOC.sigtypeFlags[ sDec.nFramesDecoded ] = sDecCtrl.sigtype;

            if( sDec.sRC.error != 0 ) {
                /* Corrupt stream */
                Silk_TOC.corrupt = 1;
                break;
            }

            if( sDec.nBytesLeft > 0 && sDec.FrameTermination == SKP_SILK_MORE_FRAMES ) {
                sDec.nFramesDecoded++;
            } else {
                break;
            }
        }
        if( Silk_TOC.corrupt != 0 || sDec.FrameTermination == SKP_SILK_MORE_FRAMES ||
            sDec.nFramesInPacket > SKP_Silk_TOC_struct.SILK_MAX_FRAMES_PER_PACKET ) {
            /* Corrupt packet */
            // PORT-NOTE: C 的 SKP_memset( Silk_TOC, 0, sizeof( SKP_Silk_TOC_struct ) )，
            //   地基 TOC 结构未提供 Clear()，手工逐字段清零。
            Silk_TOC.framesInPacket = 0;
            Silk_TOC.fs_kHz         = 0;
            Silk_TOC.inbandLBRR     = 0;
            Silk_TOC.corrupt        = 0;
            SKP_memset( Silk_TOC.vadFlags,     0, 0, SKP_Silk_TOC_struct.SILK_MAX_FRAMES_PER_PACKET );
            SKP_memset( Silk_TOC.sigtypeFlags, 0, 0, SKP_Silk_TOC_struct.SILK_MAX_FRAMES_PER_PACKET );
            Silk_TOC.corrupt = 1;
        } else {
            Silk_TOC.framesInPacket = sDec.nFramesDecoded + 1;
            Silk_TOC.fs_kHz         = sDec.fs_kHz;
            if( sDec.FrameTermination == SKP_SILK_LAST_FRAME ) {
                Silk_TOC.inbandLBRR = sDec.FrameTermination;
            } else {
                Silk_TOC.inbandLBRR = sDec.FrameTermination - 1;
            }
        }
    }

    /**************************/
    /* Get the version number */
    /**************************/
    /* Return a pointer to string specifying the version */
    // PORT-NOTE: C 源（SKP_Silk_dec_API.c:277）实际字符串为 "1.0.9.6"，
    //   PORTING.md §7 概述写作 "1.0.9"；以 C 源为准逐字照抄。
    public static string SKP_Silk_SDK_get_version()
    {
        return "1.0.9.6";
    }
}
