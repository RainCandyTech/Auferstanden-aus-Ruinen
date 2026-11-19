#nullable disable
// ***********************************************************************
// SKP_Silk_encode_parameters.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_encode_parameters.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - 引用 psEncC 状态 + range coder + NLSF 码本（psNLSF_CB.StartPtr 为
//     ushort[][]，经 SKP_Silk_range_encoder_multi 以行内 offset 0 编码）。
//   - sigtype 分支逻辑（VOICED 时编码 lag/contour/LTP/LTPscale）逐行照抄。
//   - 引用表（表簇产出，裸名）：
//       SKP_Silk_tables_other：SamplingRates_table/CDF、NLSF_interpolation_factor_CDF、
//                              Seed_CDF、vadflag_CDF
//       SKP_Silk_tables_type_offset：type_offset_CDF、type_offset_joint_CDF
//       SKP_Silk_tables_gain：gain_CDF、delta_gain_CDF
//       SKP_Silk_tables_pitch_lag：pitch_lag_NB/MB/WB/SWB_CDF、pitch_contour(_NB)_CDF
//       SKP_Silk_tables_LTP：LTP_per_index_CDF、LTP_gain_CDF_ptrs、LTPscale_CDF
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tables_other;          // SamplingRates/CDF、Seed、vadflag、NLSF 插值 CDF
using static AaR.Server.Silk.SKP_Silk_tables_type_offset;    // type_offset CDF
using static AaR.Server.Silk.SKP_Silk_tables_gain;           // gain CDF
using static AaR.Server.Silk.SKP_Silk_tables_pitch_lag;      // pitch lag/contour CDF
using static AaR.Server.Silk.SKP_Silk_tables_LTP;            // LTP CDF

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): C 函数名与所在文件同名，按全工程统一约定：类名加 "_impl"
// 后缀、方法名保持 C 原名；调用方以 `using static ...SKP_Silk_encode_parameters_impl;`
// + 裸名 SKP_Silk_encode_parameters(...) 调用。
internal static class SKP_Silk_encode_parameters_impl
{
    /*******************************************/
    /* Encode parameters to create the payload */
    /*******************************************/
    public static void SKP_Silk_encode_parameters(
        SKP_Silk_encoder_state psEncC,             /* I/O  Encoder state                   */
        SKP_Silk_encoder_control psEncCtrlC,       /* I/O  Encoder control                 */
        SKP_Silk_range_coder_state psRC,           /* I/O  Range encoder state             */
        sbyte[] q, int qOff                        /* I    Quantization indices            */
    )
    {
        int i, k, typeOffset;
        SKP_Silk_NLSF_CB_struct psNLSF_CB;


        /************************/
        /* Encode sampling rate */
        /************************/
        /* only done for first frame in packet */
        if (psEncC.nFramesInPayloadBuf == 0)
        {
            /* get sampling rate index */
            for (i = 0; i < 3; i++)
            {
                if (SKP_Silk_SamplingRates_table[i] == psEncC.fs_kHz)
                {
                    break;
                }
            }
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, i, SKP_Silk_SamplingRates_CDF, 0);
        }

        /*******************************************/
        /* Encode signal type and quantizer offset */
        /*******************************************/
        typeOffset = 2 * psEncCtrlC.sigtype + psEncCtrlC.QuantOffsetType;
        if (psEncC.nFramesInPayloadBuf == 0)
        {
            /* first frame in packet: independent coding */
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, typeOffset, SKP_Silk_type_offset_CDF, 0);
        }
        else
        {
            /* condidtional coding */
            // PORT-NOTE: C 传 SKP_Silk_type_offset_joint_CDF[ typeOffsetPrev ]（指向行的指针）；
            // 表簇产出 ushort[][]，此处取行、行内 offset 0。
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, typeOffset,
                SKP_Silk_type_offset_joint_CDF[psEncC.typeOffsetPrev], 0);
        }
        psEncC.typeOffsetPrev = typeOffset;

        /****************/
        /* Encode gains */
        /****************/
        /* first subframe */
        if (psEncC.nFramesInPayloadBuf == 0)
        {
            /* first frame in packet: independent coding */
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.GainsIndices[0],
                SKP_Silk_gain_CDF[psEncCtrlC.sigtype], 0);
        }
        else
        {
            /* condidtional coding */
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.GainsIndices[0],
                SKP_Silk_delta_gain_CDF, 0);
        }

        /* remaining subframes */
        for (i = 1; i < NB_SUBFR; i++)
        {
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.GainsIndices[i],
                SKP_Silk_delta_gain_CDF, 0);
        }


        /****************/
        /* Encode NLSFs */
        /****************/
        /* Range encoding of the NLSF path */
        psNLSF_CB = psEncC.psNLSF_CB[psEncCtrlC.sigtype];
        SKP_Silk_range_coder.SKP_Silk_range_encoder_multi(psRC, psEncCtrlC.NLSFIndices, 0,
            psNLSF_CB.StartPtr, 0, psNLSF_CB.nStages);

        /* Encode NLSF interpolation factor */
        SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.NLSFInterpCoef_Q2,
            SKP_Silk_NLSF_interpolation_factor_CDF, 0);


        if (psEncCtrlC.sigtype == SIG_TYPE_VOICED)
        {
            /*********************/
            /* Encode pitch lags */
            /*********************/


            /* lag index */
            if (psEncC.fs_kHz == 8)
            {
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.lagIndex,
                    SKP_Silk_pitch_lag_NB_CDF, 0);
            }
            else if (psEncC.fs_kHz == 12)
            {
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.lagIndex,
                    SKP_Silk_pitch_lag_MB_CDF, 0);
            }
            else if (psEncC.fs_kHz == 16)
            {
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.lagIndex,
                    SKP_Silk_pitch_lag_WB_CDF, 0);
            }
            else
            {
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.lagIndex,
                    SKP_Silk_pitch_lag_SWB_CDF, 0);
            }


            /* countour index */
            if (psEncC.fs_kHz == 8)
            {
                /* Less codevectors used in 8 khz mode */
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.contourIndex,
                    SKP_Silk_pitch_contour_NB_CDF, 0);
            }
            else
            {
                /* Joint for 12, 16, 24 khz */
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.contourIndex,
                    SKP_Silk_pitch_contour_CDF, 0);
            }

            /********************/
            /* Encode LTP gains */
            /********************/

            /* PERIndex value */
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.PERIndex,
                SKP_Silk_LTP_per_index_CDF, 0);

            /* Codebook Indices */
            for (k = 0; k < NB_SUBFR; k++)
            {
                // PORT-NOTE: C 传 SKP_Silk_LTP_gain_CDF_ptrs[ PERIndex ]（指向行的指针）；
                // 表簇产出 ushort[][]（三行各自独立数组），此处取行、行内 offset 0。
                SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.LTPIndex[k],
                    SKP_Silk_LTP_gain_CDF_ptrs[psEncCtrlC.PERIndex], 0);
            }

            /**********************/
            /* Encode LTP scaling */
            /**********************/
            SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.LTP_scaleIndex,
                SKP_Silk_LTPscale_CDF, 0);
        }


        /***************/
        /* Encode seed */
        /***************/
        SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncCtrlC.Seed, SKP_Silk_Seed_CDF, 0);

        /*********************************************/
        /* Encode quantization indices of excitation */
        /*********************************************/
        // PORT-NOTE: encode_pulses 同为 CS0542 冲突符号，其类名带 "_impl"、方法名保持
        // C 原名（见 SKP_Silk_encode_pulses.cs）。
        SKP_Silk_encode_pulses_impl.SKP_Silk_encode_pulses(psRC, psEncCtrlC.sigtype,
            psEncCtrlC.QuantOffsetType, q, qOff, psEncC.frame_length);


        /*********************************************/
        /* Encode VAD flag                           */
        /*********************************************/
        SKP_Silk_range_coder.SKP_Silk_range_encoder(psRC, psEncC.vadFlag, SKP_Silk_vadflag_CDF, 0);
    }
}
