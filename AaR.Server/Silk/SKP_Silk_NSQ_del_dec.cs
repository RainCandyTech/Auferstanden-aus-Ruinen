#nullable disable
// ***********************************************************************
// SKP_Silk_NSQ_del_dec.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NSQ_del_dec.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - 文件内局部类型：NSQ_del_dec_struct（类，含定长数组）；NSQ_sample_struct
//     （struct 值类型——C 以值语义 memcpy 拷贝样本状态，C# 结构体赋值逐位等价）。
//   - MAX_DEL_DEC_STATES=4 的延迟判决循环全部照抄展开（含 4x2 psSampleState）。
//   - 指针步进（x/q/pxq/A_Q12/B_Q14/AR_shp_Q13/shp_lag_ptr/pred_lag_ptr/
//     psLPC_Q14）一律改为（数组 + 局部偏移）。
//   - 小端分支的 a_Q12_tmp 打包技巧同 SKP_Silk_NSQ.cs（见 SKP_pack16to32）。
//   - C 的 `NSQ_sample_struct *psSS = psSampleState[k]` 行指针在 C# 中不存在，
//     以 psSampleState[k][j] 直接访问（值语义与指针写回一致）。
//   - 跨文件引用：SKP_Silk_MA.SKP_Silk_MA_Prediction(...)。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_Inlines;
using static AaR.Server.Silk.SKP_Silk_tables_other;          // SKP_Silk_Quantization_Offsets_Q10

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): C 函数名与所在文件同名（SKP_Silk_NSQ_del_dec），按全工程统一
// 约定：类名加 "_impl" 后缀、方法名保持 C 原名；调用方以
// `using static ...SKP_Silk_NSQ_del_dec_impl;` + 裸名 SKP_Silk_NSQ_del_dec(...) 调用。
internal static class SKP_Silk_NSQ_del_dec_impl
{
    // PORT-NOTE: C 的 SKP_int16_MAX/SKP_int32_MAX（typedef.h，地基未含），值逐位一致。
    private const int SKP_int16_MAX = 32767;
    private const int SKP_int32_MAX = 2147483647; /* 0x7FFFFFFF */

    // PORT-NOTE: 同 SKP_Silk_NSQ.cs —— C 以 SKP_memcpy 把 int16 系数对字节级复制进
    // int32 数组（小端），等价于相邻两系数打包（低半字 = 偶数下标系数）。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SKP_pack16to32(short lo, short hi)
    {
        return (int)(((uint)(ushort)hi << 16) | (uint)(ushort)lo);
    }

    private sealed class NSQ_del_dec_struct
    {
        public int[] RandState = new int[DECISION_DELAY];
        public int[] Q_Q10 = new int[DECISION_DELAY];
        public int[] Xq_Q10 = new int[DECISION_DELAY];
        public int[] Pred_Q16 = new int[DECISION_DELAY];
        public int[] Shape_Q10 = new int[DECISION_DELAY];
        public int[] Gain_Q16 = new int[DECISION_DELAY];
        public int[] sAR2_Q14 = new int[MAX_SHAPE_LPC_ORDER];
        public int[] sLPC_Q14 = new int[MAX_FRAME_LENGTH / NB_SUBFR + NSQ_LPC_BUF_LENGTH];
        public int LF_AR_Q12;
        public int Seed;
        public int SeedInit;
        public int RD_Q10;
    }

    // PORT-NOTE: C 的 NSQ_sample_struct 为值类型 struct；被整结构 memcpy 拷贝，
    // 移植为 C# struct（赋值即逐字段值拷贝），保证与 C 指针写回语义一致。
    private struct NSQ_sample_struct
    {
        public int Q_Q10;
        public int RD_Q10;
        public int xq_Q14;
        public int LF_AR_Q12;
        public int sLTP_shp_Q10;
        public int LPC_exc_Q16;
    }

    public static void SKP_Silk_NSQ_del_dec(
        SKP_Silk_encoder_state psEncC,                                    /* I/O  Encoder State                       */
        SKP_Silk_encoder_control psEncCtrlC,                                /* I    Encoder Control                     */
        SKP_Silk_nsq_state NSQ,                                            /* I/O  NSQ state                           */
        short[] x, int xOff,                                               /* I    Prefiltered input signal            */
        sbyte[] q, int qOff,                                               /* O    Quantized pulse signal              */
        int LSFInterpFactor_Q2,                                            /* I    LSF interpolation factor in Q2      */
        short[][] PredCoef_Q12,                                             /* I    Prediction coefs（两行：C 的平铺 [2*MAX_LPC_ORDER] → 交错；行选择见下） */
        short[] LTPCoef_Q14, int LTPCoef_Q14Off,                           /* I    LT prediction coefs                 */
        short[] AR2_Q13, int AR2_Q13Off,                                   /* I                                        */
        int[] HarmShapeGain_Q14, int HarmShapeGain_Q14Off,                 /* I                                        */
        int[] Tilt_Q14, int Tilt_Q14Off,                                   /* I    Spectral tilt                       */
        int[] LF_shp_Q14, int LF_shp_Q14Off,                               /* I                                        */
        int[] Gains_Q16, int Gains_Q16Off,                                 /* I                                        */
        int Lambda_Q10,                                                     /* I                                        */
        int LTP_scale_Q14                                                   /* I    LTP state scaling                   */
    )
    {
        int i, k, lag, start_idx, LSF_interpolation_flag, Winner_ind, subfr;
        int last_smple_idx, smpl_buf_idx, decisionDelay, subfr_length;
        short[] A_Q12; int A_Q12Off;
        short[] B_Q14; int B_Q14Off;
        short[] AR_shp_Q13; int AR_shp_Q13Off;
        short[] pxqArr; int pxq;                 /* C: SKP_int16 *pxq（指向 NSQ->xq 内部） */
        int[] sLTP_Q16 = new int[2 * MAX_FRAME_LENGTH];
        short[] sLTP = new short[2 * MAX_FRAME_LENGTH];
        int HarmShapeFIRPacked_Q14;
        int offset_Q10;
        int[] FiltState = new int[MAX_LPC_ORDER];
        int RDmin_Q10;
        int[] x_sc_Q10 = new int[MAX_FRAME_LENGTH / NB_SUBFR];
        NSQ_del_dec_struct[] psDelDec = new NSQ_del_dec_struct[MAX_DEL_DEC_STATES];
        NSQ_del_dec_struct psDD;

        subfr_length = psEncC.frame_length / NB_SUBFR;

        /* Set unvoiced lag to the previous one, overwrite later for voiced */
        lag = NSQ.lagPrev;

        /* Initialize delayed decision states */
        // PORT-NOTE: C 为 SKP_memset( psDelDec, 0, n * sizeof( NSQ_del_dec_struct ) )；
        // 数组元素为类，等价于对前 nStates 个元素 new（全字段零初始化；未用到的
        // 尾部元素保持 null，与 C 中未 memset 部分不被访问一致）。
        for (k = 0; k < psEncC.nStatesDelayedDecision; k++)
        {
            psDelDec[k] = new NSQ_del_dec_struct();
        }
        for (k = 0; k < psEncC.nStatesDelayedDecision; k++)
        {
            psDD = psDelDec[k];
            psDD.Seed = (k + psEncCtrlC.Seed) & 3;
            psDD.SeedInit = psDD.Seed;
            psDD.RD_Q10 = 0;
            psDD.LF_AR_Q12 = NSQ.sLF_AR_shp_Q12;
            psDD.Shape_Q10[0] = NSQ.sLTP_shp_Q10[psEncC.frame_length - 1];
            SKP_memcpy(psDD.sLPC_Q14, 0, NSQ.sLPC_Q14, 0, NSQ_LPC_BUF_LENGTH);
            SKP_memcpy(psDD.sAR2_Q14, 0, NSQ.sAR2_Q14, 0, NSQ.sAR2_Q14.Length);
        }

        offset_Q10 = SKP_Silk_Quantization_Offsets_Q10[psEncCtrlC.sigtype][psEncCtrlC.QuantOffsetType];
        smpl_buf_idx = 0; /* index of oldest samples */

        decisionDelay = SKP_min_int(DECISION_DELAY, subfr_length);

        /* For voiced frames limit the decision delay to lower than the pitch lag */
        if (psEncCtrlC.sigtype == SIG_TYPE_VOICED)
        {
            for (k = 0; k < NB_SUBFR; k++)
            {
                decisionDelay = SKP_min_int(decisionDelay,
                    psEncCtrlC.pitchL[k] - LTP_ORDER / 2 - 1);
            }
        }
        else
        {
            if (lag > 0)
            {
                decisionDelay = SKP_min_int(decisionDelay, lag - LTP_ORDER / 2 - 1);
            }
        }

        if (LSFInterpFactor_Q2 == (1 << 2))
        {
            LSF_interpolation_flag = 0;
        }
        else
        {
            LSF_interpolation_flag = 1;
        }

        /* Setup pointers to start of sub frame */
        pxqArr = NSQ.xq;
        pxq = psEncC.frame_length;
        NSQ.sLTP_shp_buf_idx = psEncC.frame_length;
        NSQ.sLTP_buf_idx = psEncC.frame_length;
        subfr = 0;
        for (k = 0; k < NB_SUBFR; k++)
        {
            /* C: A_Q12 = &PredCoef_Q12[ ((k>>1)|(1-LSF_interpolation_flag)) * MAX_LPC_ORDER ]；
               交错数组语义：行索引 = ((k>>1)|(1-LSF_interpolation_flag))，行内偏移 0。 */
            A_Q12 = PredCoef_Q12[((k >> 1) | (1 - LSF_interpolation_flag))];
            A_Q12Off = 0;
            B_Q14 = LTPCoef_Q14;
            B_Q14Off = LTPCoef_Q14Off + k * LTP_ORDER;
            AR_shp_Q13 = AR2_Q13;
            AR_shp_Q13Off = AR2_Q13Off + k * MAX_SHAPE_LPC_ORDER;

            /* Noise shape parameters */
            HarmShapeFIRPacked_Q14 = SKP_RSHIFT(HarmShapeGain_Q14[HarmShapeGain_Q14Off + k], 2);
            HarmShapeFIRPacked_Q14 |= SKP_LSHIFT((int)SKP_RSHIFT(HarmShapeGain_Q14[HarmShapeGain_Q14Off + k], 1), 16);

            NSQ.rewhite_flag = 0;
            if (psEncCtrlC.sigtype == SIG_TYPE_VOICED)
            {
                /* Voiced */
                lag = psEncCtrlC.pitchL[k];

                /* Re-whitening */
                if ((k & (3 - SKP_LSHIFT(LSF_interpolation_flag, 1))) == 0)
                {
                    if (k == 2)
                    {
                        /* RESET DELAYED DECISIONS */
                        /* Find winner */
                        RDmin_Q10 = psDelDec[0].RD_Q10;
                        Winner_ind = 0;
                        for (i = 1; i < psEncC.nStatesDelayedDecision; i++)
                        {
                            if (psDelDec[i].RD_Q10 < RDmin_Q10)
                            {
                                RDmin_Q10 = psDelDec[i].RD_Q10;
                                Winner_ind = i;
                            }
                        }
                        for (i = 0; i < psEncC.nStatesDelayedDecision; i++)
                        {
                            if (i != Winner_ind)
                            {
                                psDelDec[i].RD_Q10 += (SKP_int32_MAX >> 4);
                            }
                        }

                        /* Copy final part of signals from winner state to output and long-term filter states */
                        psDD = psDelDec[Winner_ind];
                        last_smple_idx = smpl_buf_idx + decisionDelay;
                        for (i = 0; i < decisionDelay; i++)
                        {
                            last_smple_idx = (last_smple_idx - 1) & DECISION_DELAY_MASK;
                            q[qOff + i - decisionDelay] = (sbyte)SKP_RSHIFT(psDD.Q_Q10[last_smple_idx], 10);
                            pxqArr[pxq + i - decisionDelay] = (short)SKP_SAT16(SKP_RSHIFT_ROUND(
                                SKP_SMULWW(psDD.Xq_Q10[last_smple_idx],
                                psDD.Gain_Q16[last_smple_idx]), 10));
                            NSQ.sLTP_shp_Q10[NSQ.sLTP_shp_buf_idx - decisionDelay + i] = psDD.Shape_Q10[last_smple_idx];
                        }

                        subfr = 0;
                    }

                    /* Rewhiten with new A coefs */
                    start_idx = psEncC.frame_length - lag - psEncC.predictLPCOrder - LTP_ORDER / 2;

                    SKP_memset(FiltState, 0, 0, psEncC.predictLPCOrder);
                    SKP_Silk_MA.SKP_Silk_MA_Prediction(NSQ.xq,
                        start_idx + k * psEncC.subfr_length,
                        A_Q12, A_Q12Off, FiltState, 0, sLTP, start_idx,
                        psEncC.frame_length - start_idx, psEncC.predictLPCOrder);

                    NSQ.sLTP_buf_idx = psEncC.frame_length;
                    NSQ.rewhite_flag = 1;
                }
            }

            SKP_Silk_nsq_del_dec_scale_states(NSQ, psDelDec, 0, x, xOff, x_sc_Q10, 0,
                subfr_length, sLTP, 0, sLTP_Q16, 0, k, psEncC.nStatesDelayedDecision, smpl_buf_idx,
                LTP_scale_Q14, Gains_Q16, Gains_Q16Off, psEncCtrlC.pitchL, 0);

            SKP_Silk_noise_shape_quantizer_del_dec(NSQ, psDelDec, 0, psEncCtrlC.sigtype, x_sc_Q10, 0,
                q, qOff, pxqArr, pxq, sLTP_Q16, 0,
                A_Q12, A_Q12Off, B_Q14, B_Q14Off,
                AR_shp_Q13, AR_shp_Q13Off, lag, HarmShapeFIRPacked_Q14,
                Tilt_Q14[Tilt_Q14Off + k], LF_shp_Q14[LF_shp_Q14Off + k], Gains_Q16[Gains_Q16Off + k],
                Lambda_Q10, offset_Q10, psEncC.subfr_length, subfr++, psEncC.shapingLPCOrder,
                psEncC.predictLPCOrder, psEncC.warping_Q16, psEncC.nStatesDelayedDecision,
                ref smpl_buf_idx, decisionDelay);

            xOff += psEncC.subfr_length;
            qOff += psEncC.subfr_length;
            pxq += psEncC.subfr_length;
        }

        /* Find winner */
        RDmin_Q10 = psDelDec[0].RD_Q10;
        Winner_ind = 0;
        for (k = 1; k < psEncC.nStatesDelayedDecision; k++)
        {
            if (psDelDec[k].RD_Q10 < RDmin_Q10)
            {
                RDmin_Q10 = psDelDec[k].RD_Q10;
                Winner_ind = k;
            }
        }

        /* Copy final part of signals from winner state to output and long-term filter states */
        psDD = psDelDec[Winner_ind];
        psEncCtrlC.Seed = psDD.SeedInit;
        last_smple_idx = smpl_buf_idx + decisionDelay;
        for (i = 0; i < decisionDelay; i++)
        {
            last_smple_idx = (last_smple_idx - 1) & DECISION_DELAY_MASK;
            q[qOff + i - decisionDelay] = (sbyte)SKP_RSHIFT(psDD.Q_Q10[last_smple_idx], 10);
            pxqArr[pxq + i - decisionDelay] = (short)SKP_SAT16(SKP_RSHIFT_ROUND(
                SKP_SMULWW(psDD.Xq_Q10[last_smple_idx], psDD.Gain_Q16[last_smple_idx]), 10));
            NSQ.sLTP_shp_Q10[NSQ.sLTP_shp_buf_idx - decisionDelay + i] = psDD.Shape_Q10[last_smple_idx];
            sLTP_Q16[NSQ.sLTP_buf_idx - decisionDelay + i] = psDD.Pred_Q16[last_smple_idx];
        }
        SKP_memcpy(NSQ.sLPC_Q14, 0, psDD.sLPC_Q14, psEncC.subfr_length, NSQ_LPC_BUF_LENGTH);
        SKP_memcpy(NSQ.sAR2_Q14, 0, psDD.sAR2_Q14, 0, psDD.sAR2_Q14.Length);

        /* Update states */
        NSQ.sLF_AR_shp_Q12 = psDD.LF_AR_Q12;
        NSQ.lagPrev = psEncCtrlC.pitchL[NB_SUBFR - 1];

        /* Save quantized speech and noise shaping signals */
        SKP_memcpy(NSQ.xq, 0, NSQ.xq, psEncC.frame_length, psEncC.frame_length);
        SKP_memcpy(NSQ.sLTP_shp_Q10, 0, NSQ.sLTP_shp_Q10, psEncC.frame_length, psEncC.frame_length);
    }

    /******************************************/
    /* Noise shape quantizer for one subframe */
    /******************************************/
    private static void SKP_Silk_noise_shape_quantizer_del_dec(
        SKP_Silk_nsq_state NSQ,                    /* I/O  NSQ state                           */
        NSQ_del_dec_struct[] psDelDec, int psDelDecOff, /* I/O  Delayed decision states             */
        int sigtype,                               /* I    Signal type                         */
        int[] x_Q10, int x_Q10Off,                 /* I                                        */
        sbyte[] q, int qOff,                       /* O                                        */
        short[] xq, int xqOff,                     /* O                                        */
        int[] sLTP_Q16, int sLTP_Q16Off,           /* I/O  LTP filter state                    */
        short[] a_Q12, int a_Q12Off,               /* I    Short term prediction coefs         */
        short[] b_Q14, int b_Q14Off,               /* I    Long term prediction coefs          */
        short[] AR_shp_Q13, int AR_shp_Q13Off,     /* I    Noise shaping coefs                 */
        int lag,                                    /* I    Pitch lag                           */
        int HarmShapeFIRPacked_Q14,                /* I                                        */
        int Tilt_Q14,                              /* I    Spectral tilt                       */
        int LF_shp_Q14,                            /* I                                        */
        int Gain_Q16,                              /* I                                        */
        int Lambda_Q10,                            /* I                                        */
        int offset_Q10,                            /* I                                        */
        int length,                                /* I    Input length                        */
        int subfr,                                  /* I    Subframe number                     */
        int shapingLPCOrder,                       /* I    Shaping LPC filter order            */
        int predictLPCOrder,                       /* I    Prediction filter order             */
        int warping_Q16,                           /* I                                        */
        int nStatesDelayedDecision,                /* I    Number of states in decision tree   */
        ref int smpl_buf_idx,                      /* I    Index to newest samples in buffers  */
        int decisionDelay                           /* I                                        */
    )
    {
        int i, j, k, Winner_ind, RDmin_ind, RDmax_ind, last_smple_idx;
        int Winner_rand_state;
        int LTP_pred_Q14, LPC_pred_Q10, n_AR_Q10, n_LTP_Q14;
        int n_LF_Q10, r_Q10, rr_Q20, rd1_Q10, rd2_Q10, RDmin_Q10, RDmax_Q10;
        int q1_Q10, q2_Q10, dither, exc_Q10, LPC_exc_Q10, xq_Q10;
        int tmp1, tmp2, sLF_AR_shp_Q10;
        int pred_lag_ptr, shp_lag_ptr, psLPC_Q14;  /* C 指针 → 偏移 */
        // PORT-NOTE: C 声明 NSQ_sample_struct psSampleState[ MAX_DEL_DEC_STATES ][ 2 ]
        // 及行指针 psSS = psSampleState[ k ]；C# 为交错 struct 数组，psSS 以
        // psSampleState[ k ][ j ] 直接访问替代（值语义与指针写回一致）。
        NSQ_sample_struct[][] psSampleState = new NSQ_sample_struct[MAX_DEL_DEC_STATES][]
        {
            new NSQ_sample_struct[2], new NSQ_sample_struct[2],
            new NSQ_sample_struct[2], new NSQ_sample_struct[2],
        };
        NSQ_del_dec_struct psDD;
        int[] a_Q12_tmp = new int[MAX_LPC_ORDER / 2];
        int Atmp;

        /* Preload LPC coeficients to array on stack. Gives small performance gain */
        // PORT-NOTE: C 为 SKP_memcpy( a_Q12_tmp, a_Q12, predictLPCOrder * sizeof( SKP_int16 ) )
        // ——字节级复制即按小端把相邻两 int16 打包进一个 int32。
        for (j = 0; j < (predictLPCOrder >> 1); j++)
        {
            a_Q12_tmp[j] = SKP_pack16to32(a_Q12[a_Q12Off + 2 * j], a_Q12[a_Q12Off + 2 * j + 1]);
        }

        shp_lag_ptr = NSQ.sLTP_shp_buf_idx - lag + HARM_SHAPE_FIR_TAPS / 2;
        pred_lag_ptr = NSQ.sLTP_buf_idx - lag + LTP_ORDER / 2;

        for (i = 0; i < length; i++)
        {
            /* Perform common calculations used in all states */

            /* Long-term prediction */
            if (sigtype == SIG_TYPE_VOICED)
            {
                /* Unrolled loop */
                LTP_pred_Q14 = SKP_SMULWB(sLTP_Q16[sLTP_Q16Off + pred_lag_ptr + 0], b_Q14[b_Q14Off + 0]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, sLTP_Q16[sLTP_Q16Off + pred_lag_ptr - 1], b_Q14[b_Q14Off + 1]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, sLTP_Q16[sLTP_Q16Off + pred_lag_ptr - 2], b_Q14[b_Q14Off + 2]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, sLTP_Q16[sLTP_Q16Off + pred_lag_ptr - 3], b_Q14[b_Q14Off + 3]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, sLTP_Q16[sLTP_Q16Off + pred_lag_ptr - 4], b_Q14[b_Q14Off + 4]);
                pred_lag_ptr++;
            }
            else
            {
                LTP_pred_Q14 = 0;
            }

            /* Long-term shaping */
            if (lag > 0)
            {
                /* Symmetric, packed FIR coefficients */
                n_LTP_Q14 = SKP_SMULWB(SKP_ADD32(NSQ.sLTP_shp_Q10[shp_lag_ptr + 0],
                    NSQ.sLTP_shp_Q10[shp_lag_ptr - 2]), HarmShapeFIRPacked_Q14);
                n_LTP_Q14 = SKP_SMLAWT(n_LTP_Q14, NSQ.sLTP_shp_Q10[shp_lag_ptr - 1],
                    HarmShapeFIRPacked_Q14);
                n_LTP_Q14 = SKP_LSHIFT(n_LTP_Q14, 6);
                shp_lag_ptr++;
            }
            else
            {
                n_LTP_Q14 = 0;
            }

            for (k = 0; k < nStatesDelayedDecision; k++)
            {
                /* Delayed decision state */
                psDD = psDelDec[psDelDecOff + k];

                /* Generate dither */
                psDD.Seed = SKP_RAND(psDD.Seed);

                /* dither = rand_seed < 0 ? 0xFFFFFFFF : 0; */
                dither = SKP_RSHIFT(psDD.Seed, 31);

                /* Pointer used in short term prediction and shaping */
                psLPC_Q14 = NSQ_LPC_BUF_LENGTH - 1 + i;
                /* Short-term prediction */
                /* Partially unrolled */
                Atmp = a_Q12_tmp[0];          /* read two coefficients at once */
                LPC_pred_Q10 = SKP_SMULWB(psDD.sLPC_Q14[psLPC_Q14 + 0], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 1], Atmp);
                Atmp = a_Q12_tmp[1];
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 2], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 3], Atmp);
                Atmp = a_Q12_tmp[2];
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 4], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 5], Atmp);
                Atmp = a_Q12_tmp[3];
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 6], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 7], Atmp);
                Atmp = a_Q12_tmp[4];
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 8], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - 9], Atmp);
                for (j = 10; j < predictLPCOrder; j += 2)
                {
                    Atmp = a_Q12_tmp[j >> 1]; /* read two coefficients at once */
                    LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - j], Atmp);
                    LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDD.sLPC_Q14[psLPC_Q14 - j - 1], Atmp);
                }

                /* Noise shape feedback */
                /* Output of lowpass section */
                tmp2 = SKP_SMLAWB(psDD.sLPC_Q14[psLPC_Q14 + 0], psDD.sAR2_Q14[0], warping_Q16);
                /* Output of allpass section */
                tmp1 = SKP_SMLAWB(psDD.sAR2_Q14[0], psDD.sAR2_Q14[1] - tmp2, warping_Q16);
                psDD.sAR2_Q14[0] = tmp2;
                n_AR_Q10 = SKP_SMULWB(tmp2, AR_shp_Q13[AR_shp_Q13Off + 0]);
                /* Loop over allpass sections */
                for (j = 2; j < shapingLPCOrder; j += 2)
                {
                    /* Output of allpass section */
                    tmp2 = SKP_SMLAWB(psDD.sAR2_Q14[j - 1], psDD.sAR2_Q14[j + 0] - tmp1, warping_Q16);
                    psDD.sAR2_Q14[j - 1] = tmp1;
                    n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, tmp1, AR_shp_Q13[AR_shp_Q13Off + j - 1]);
                    /* Output of allpass section */
                    tmp1 = SKP_SMLAWB(psDD.sAR2_Q14[j + 0], psDD.sAR2_Q14[j + 1] - tmp2, warping_Q16);
                    psDD.sAR2_Q14[j + 0] = tmp2;
                    n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, tmp2, AR_shp_Q13[AR_shp_Q13Off + j]);
                }
                psDD.sAR2_Q14[shapingLPCOrder - 1] = tmp1;
                n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, tmp1, AR_shp_Q13[AR_shp_Q13Off + shapingLPCOrder - 1]);

                n_AR_Q10 = SKP_RSHIFT(n_AR_Q10, 1);           /* Q11 -> Q10 */
                n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, psDD.LF_AR_Q12, Tilt_Q14);

                n_LF_Q10 = SKP_LSHIFT(SKP_SMULWB(psDD.Shape_Q10[smpl_buf_idx], LF_shp_Q14), 2);
                n_LF_Q10 = SKP_SMLAWT(n_LF_Q10, psDD.LF_AR_Q12, LF_shp_Q14);

                /* Input minus prediction plus noise feedback                       */
                /* r = x[ i ] - LTP_pred - LPC_pred + n_AR + n_Tilt + n_LF + n_LTP  */
                tmp1 = SKP_SUB32(LTP_pred_Q14, n_LTP_Q14);                       /* Add Q14 stuff */
                tmp1 = SKP_RSHIFT(tmp1, 4);                                      /* convert to Q10 */
                tmp1 = SKP_ADD32(tmp1, LPC_pred_Q10);                            /* add Q10 stuff */
                tmp1 = SKP_SUB32(tmp1, n_AR_Q10);                                /* subtract Q10 stuff */
                tmp1 = SKP_SUB32(tmp1, n_LF_Q10);                                /* subtract Q10 stuff */
                r_Q10 = SKP_SUB32(x_Q10[x_Q10Off + i], tmp1);                     /* residual error Q10 */

                /* Flip sign depending on dither */
                r_Q10 = (r_Q10 ^ dither) - dither;
                r_Q10 = SKP_SUB32(r_Q10, offset_Q10);
                r_Q10 = SKP_LIMIT_32(r_Q10, -(64 << 10), 64 << 10);

                /* Find two quantization level candidates and measure their rate-distortion */
                if (r_Q10 < -1536)
                {
                    q1_Q10 = SKP_LSHIFT(SKP_RSHIFT_ROUND(r_Q10, 10), 10);
                    r_Q10 = SKP_SUB32(r_Q10, q1_Q10);
                    rd1_Q10 = SKP_RSHIFT(SKP_SMLABB(SKP_MUL(-SKP_ADD32(q1_Q10, offset_Q10), Lambda_Q10), r_Q10, r_Q10), 10);
                    rd2_Q10 = SKP_ADD32(rd1_Q10, 1024);
                    rd2_Q10 = SKP_SUB32(rd2_Q10, SKP_ADD_LSHIFT32(Lambda_Q10, r_Q10, 1));
                    q2_Q10 = SKP_ADD32(q1_Q10, 1024);
                }
                else if (r_Q10 > 512)
                {
                    q1_Q10 = SKP_LSHIFT(SKP_RSHIFT_ROUND(r_Q10, 10), 10);
                    r_Q10 = SKP_SUB32(r_Q10, q1_Q10);
                    rd1_Q10 = SKP_RSHIFT(SKP_SMLABB(SKP_MUL(SKP_ADD32(q1_Q10, offset_Q10), Lambda_Q10), r_Q10, r_Q10), 10);
                    rd2_Q10 = SKP_ADD32(rd1_Q10, 1024);
                    rd2_Q10 = SKP_SUB32(rd2_Q10, SKP_SUB_LSHIFT32(Lambda_Q10, r_Q10, 1));
                    q2_Q10 = SKP_SUB32(q1_Q10, 1024);
                }
                else
                {            /* r_Q10 >= -1536 && q1_Q10 <= 512 */
                    rr_Q20 = SKP_SMULBB(offset_Q10, Lambda_Q10);
                    rd2_Q10 = SKP_RSHIFT(SKP_SMLABB(rr_Q20, r_Q10, r_Q10), 10);
                    rd1_Q10 = SKP_ADD32(rd2_Q10, 1024);
                    rd1_Q10 = SKP_ADD32(rd1_Q10, SKP_SUB_RSHIFT32(SKP_ADD_LSHIFT32(Lambda_Q10, r_Q10, 1), rr_Q20, 9));
                    q1_Q10 = -1024;
                    q2_Q10 = 0;
                }

                if (rd1_Q10 < rd2_Q10)
                {
                    psSampleState[k][0].RD_Q10 = SKP_ADD32(psDD.RD_Q10, rd1_Q10);
                    psSampleState[k][1].RD_Q10 = SKP_ADD32(psDD.RD_Q10, rd2_Q10);
                    psSampleState[k][0].Q_Q10 = q1_Q10;
                    psSampleState[k][1].Q_Q10 = q2_Q10;
                }
                else
                {
                    psSampleState[k][0].RD_Q10 = SKP_ADD32(psDD.RD_Q10, rd2_Q10);
                    psSampleState[k][1].RD_Q10 = SKP_ADD32(psDD.RD_Q10, rd1_Q10);
                    psSampleState[k][0].Q_Q10 = q2_Q10;
                    psSampleState[k][1].Q_Q10 = q1_Q10;
                }

                /* Update states for best quantization */

                /* Quantized excitation */
                exc_Q10 = SKP_ADD32(offset_Q10, psSampleState[k][0].Q_Q10);
                exc_Q10 = (exc_Q10 ^ dither) - dither;

                /* Add predictions */
                LPC_exc_Q10 = exc_Q10 + SKP_RSHIFT_ROUND(LTP_pred_Q14, 4);
                xq_Q10 = SKP_ADD32(LPC_exc_Q10, LPC_pred_Q10);

                /* Update states */
                sLF_AR_shp_Q10 = SKP_SUB32(xq_Q10, n_AR_Q10);
                psSampleState[k][0].sLTP_shp_Q10 = SKP_SUB32(sLF_AR_shp_Q10, n_LF_Q10);
                psSampleState[k][0].LF_AR_Q12 = SKP_LSHIFT(sLF_AR_shp_Q10, 2);
                psSampleState[k][0].xq_Q14 = SKP_LSHIFT(xq_Q10, 4);
                psSampleState[k][0].LPC_exc_Q16 = SKP_LSHIFT(LPC_exc_Q10, 6);

                /* Update states for second best quantization */

                /* Quantized excitation */
                exc_Q10 = SKP_ADD32(offset_Q10, psSampleState[k][1].Q_Q10);
                exc_Q10 = (exc_Q10 ^ dither) - dither;

                /* Add predictions */
                LPC_exc_Q10 = exc_Q10 + SKP_RSHIFT_ROUND(LTP_pred_Q14, 4);
                xq_Q10 = SKP_ADD32(LPC_exc_Q10, LPC_pred_Q10);

                /* Update states */
                sLF_AR_shp_Q10 = SKP_SUB32(xq_Q10, n_AR_Q10);
                psSampleState[k][1].sLTP_shp_Q10 = SKP_SUB32(sLF_AR_shp_Q10, n_LF_Q10);
                psSampleState[k][1].LF_AR_Q12 = SKP_LSHIFT(sLF_AR_shp_Q10, 2);
                psSampleState[k][1].xq_Q14 = SKP_LSHIFT(xq_Q10, 4);
                psSampleState[k][1].LPC_exc_Q16 = SKP_LSHIFT(LPC_exc_Q10, 6);
            }

            smpl_buf_idx = (smpl_buf_idx - 1) & DECISION_DELAY_MASK;                   /* Index to newest samples              */
            last_smple_idx = (smpl_buf_idx + decisionDelay) & DECISION_DELAY_MASK;     /* Index to decisionDelay old samples   */

            /* Find winner */
            RDmin_Q10 = psSampleState[0][0].RD_Q10;
            Winner_ind = 0;
            for (k = 1; k < nStatesDelayedDecision; k++)
            {
                if (psSampleState[k][0].RD_Q10 < RDmin_Q10)
                {
                    RDmin_Q10 = psSampleState[k][0].RD_Q10;
                    Winner_ind = k;
                }
            }

            /* Increase RD values of expired states */
            Winner_rand_state = psDelDec[psDelDecOff + Winner_ind].RandState[last_smple_idx];
            for (k = 0; k < nStatesDelayedDecision; k++)
            {
                if (psDelDec[psDelDecOff + k].RandState[last_smple_idx] != Winner_rand_state)
                {
                    psSampleState[k][0].RD_Q10 = SKP_ADD32(psSampleState[k][0].RD_Q10, (SKP_int32_MAX >> 4));
                    psSampleState[k][1].RD_Q10 = SKP_ADD32(psSampleState[k][1].RD_Q10, (SKP_int32_MAX >> 4));
                }
            }

            /* Find worst in first set and best in second set */
            RDmax_Q10 = psSampleState[0][0].RD_Q10;
            RDmin_Q10 = psSampleState[0][1].RD_Q10;
            RDmax_ind = 0;
            RDmin_ind = 0;
            for (k = 1; k < nStatesDelayedDecision; k++)
            {
                /* find worst in first set */
                if (psSampleState[k][0].RD_Q10 > RDmax_Q10)
                {
                    RDmax_Q10 = psSampleState[k][0].RD_Q10;
                    RDmax_ind = k;
                }
                /* find best in second set */
                if (psSampleState[k][1].RD_Q10 < RDmin_Q10)
                {
                    RDmin_Q10 = psSampleState[k][1].RD_Q10;
                    RDmin_ind = k;
                }
            }

            /* Replace a state if best from second set outperforms worst in first set */
            if (RDmin_Q10 < RDmax_Q10)
            {
                SKP_Silk_copy_del_dec_state(psDelDec[psDelDecOff + RDmax_ind], psDelDec[psDelDecOff + RDmin_ind], i);
                // PORT-NOTE: C 为 SKP_memcpy( &psSampleState[ RDmax_ind ][ 0 ],
                // &psSampleState[ RDmin_ind ][ 1 ], sizeof( NSQ_sample_struct ) )；
                // C# struct 赋值即整结构值拷贝，逐位等价。
                psSampleState[RDmax_ind][0] = psSampleState[RDmin_ind][1];
            }

            /* Write samples from winner to output and long-term filter states */
            psDD = psDelDec[psDelDecOff + Winner_ind];
            if (subfr > 0 || i >= decisionDelay)
            {
                q[qOff + i - decisionDelay] = (sbyte)SKP_RSHIFT(psDD.Q_Q10[last_smple_idx], 10);
                xq[xqOff + i - decisionDelay] = (short)SKP_SAT16(SKP_RSHIFT_ROUND(
                    SKP_SMULWW(psDD.Xq_Q10[last_smple_idx], psDD.Gain_Q16[last_smple_idx]), 10));
                NSQ.sLTP_shp_Q10[NSQ.sLTP_shp_buf_idx - decisionDelay] = psDD.Shape_Q10[last_smple_idx];
                sLTP_Q16[sLTP_Q16Off + NSQ.sLTP_buf_idx - decisionDelay] = psDD.Pred_Q16[last_smple_idx];
            }
            NSQ.sLTP_shp_buf_idx++;
            NSQ.sLTP_buf_idx++;

            /* Update states */
            for (k = 0; k < nStatesDelayedDecision; k++)
            {
                psDD = psDelDec[psDelDecOff + k];
                psDD.LF_AR_Q12 = psSampleState[k][0].LF_AR_Q12;
                psDD.sLPC_Q14[NSQ_LPC_BUF_LENGTH + i] = psSampleState[k][0].xq_Q14;
                psDD.Xq_Q10[smpl_buf_idx] = SKP_RSHIFT(psSampleState[k][0].xq_Q14, 4);
                psDD.Q_Q10[smpl_buf_idx] = psSampleState[k][0].Q_Q10;
                psDD.Pred_Q16[smpl_buf_idx] = psSampleState[k][0].LPC_exc_Q16;
                psDD.Shape_Q10[smpl_buf_idx] = psSampleState[k][0].sLTP_shp_Q10;
                psDD.Seed = SKP_ADD_RSHIFT32(psDD.Seed, psSampleState[k][0].Q_Q10, 10);
                psDD.RandState[smpl_buf_idx] = psDD.Seed;
                psDD.RD_Q10 = psSampleState[k][0].RD_Q10;
                psDD.Gain_Q16[smpl_buf_idx] = Gain_Q16;
            }
        }
        /* Update LPC states */
        for (k = 0; k < nStatesDelayedDecision; k++)
        {
            psDD = psDelDec[psDelDecOff + k];
            SKP_memcpy(psDD.sLPC_Q14, 0, psDD.sLPC_Q14, length, NSQ_LPC_BUF_LENGTH);
        }
    }

    private static void SKP_Silk_nsq_del_dec_scale_states(
        SKP_Silk_nsq_state NSQ,                    /* I/O  NSQ state                           */
        NSQ_del_dec_struct[] psDelDec, int psDelDecOff, /* I/O  Delayed decision states             */
        short[] x, int xOff,                       /* I    Input in Q0                         */
        int[] x_sc_Q10, int x_sc_Q10Off,           /* O    Input scaled with 1/Gain in Q10     */
        int subfr_length,                          /* I    Length of input                     */
        short[] sLTP, int sLTPOff,                 /* I    Re-whitened LTP state in Q0         */
        int[] sLTP_Q16, int sLTP_Q16Off,           /* O    LTP state matching scaled input     */
        int subfr,                                  /* I    Subframe number                     */
        int nStatesDelayedDecision,                /* I    Number of del dec states            */
        int smpl_buf_idx,                          /* I    Index to newest samples in buffers  */
        int LTP_scale_Q14,                         /* I    LTP state scaling                   */
        int[] Gains_Q16, int Gains_Q16Off,          /* I                                        */
        int[] pitchL, int pitchLOff                 /* I    Pitch lag                           */
    )
    {
        int i, k, lag;
        int inv_gain_Q16, gain_adj_Q16, inv_gain_Q32;
        NSQ_del_dec_struct psDD;

        inv_gain_Q16 = SKP_INVERSE32_varQ(SKP_max(Gains_Q16[Gains_Q16Off + subfr], 1), 32);
        inv_gain_Q16 = SKP_min(inv_gain_Q16, SKP_int16_MAX);
        lag = pitchL[pitchLOff + subfr];

        /* After rewhitening the LTP state is un-scaled, so scale with inv_gain_Q16 */
        if (NSQ.rewhite_flag != 0)
        {
            inv_gain_Q32 = SKP_LSHIFT(inv_gain_Q16, 16);
            if (subfr == 0)
            {
                /* Do LTP downscaling */
                inv_gain_Q32 = SKP_LSHIFT(SKP_SMULWB(inv_gain_Q32, LTP_scale_Q14), 2);
            }
            for (i = NSQ.sLTP_buf_idx - lag - LTP_ORDER / 2; i < NSQ.sLTP_buf_idx; i++)
            {
                sLTP_Q16[sLTP_Q16Off + i] = SKP_SMULWB(inv_gain_Q32, sLTP[sLTPOff + i]);
            }
        }

        /* Adjust for changing gain */
        if (inv_gain_Q16 != NSQ.prev_inv_gain_Q16)
        {
            gain_adj_Q16 = SKP_DIV32_varQ(inv_gain_Q16, NSQ.prev_inv_gain_Q16, 16);

            /* Scale long-term shaping state */
            for (i = NSQ.sLTP_shp_buf_idx - subfr_length * NB_SUBFR; i < NSQ.sLTP_shp_buf_idx; i++)
            {
                NSQ.sLTP_shp_Q10[i] = SKP_SMULWW(gain_adj_Q16, NSQ.sLTP_shp_Q10[i]);
            }

            /* Scale long-term prediction state */
            if (NSQ.rewhite_flag == 0)
            {
                for (i = NSQ.sLTP_buf_idx - lag - LTP_ORDER / 2; i < NSQ.sLTP_buf_idx; i++)
                {
                    sLTP_Q16[sLTP_Q16Off + i] = SKP_SMULWW(gain_adj_Q16, sLTP_Q16[sLTP_Q16Off + i]);
                }
            }

            for (k = 0; k < nStatesDelayedDecision; k++)
            {
                psDD = psDelDec[psDelDecOff + k];

                /* Scale scalar states */
                psDD.LF_AR_Q12 = SKP_SMULWW(gain_adj_Q16, psDD.LF_AR_Q12);

                /* Scale short-term prediction and shaping states */
                for (i = 0; i < NSQ_LPC_BUF_LENGTH; i++)
                {
                    psDD.sLPC_Q14[i] = SKP_SMULWW(gain_adj_Q16, psDD.sLPC_Q14[i]);
                }
                for (i = 0; i < MAX_SHAPE_LPC_ORDER; i++)
                {
                    psDD.sAR2_Q14[i] = SKP_SMULWW(gain_adj_Q16, psDD.sAR2_Q14[i]);
                }
                for (i = 0; i < DECISION_DELAY; i++)
                {
                    psDD.Pred_Q16[i] = SKP_SMULWW(gain_adj_Q16, psDD.Pred_Q16[i]);
                    psDD.Shape_Q10[i] = SKP_SMULWW(gain_adj_Q16, psDD.Shape_Q10[i]);
                }
            }
        }

        /* Scale input */
        for (i = 0; i < subfr_length; i++)
        {
            x_sc_Q10[x_sc_Q10Off + i] = SKP_RSHIFT(SKP_SMULBB(x[xOff + i], (short)inv_gain_Q16), 6);
        }

        /* save inv_gain */
        NSQ.prev_inv_gain_Q16 = inv_gain_Q16;
    }

    private static void SKP_Silk_copy_del_dec_state(
        NSQ_del_dec_struct DD_dst,                 /* I    Dst del dec state                   */
        NSQ_del_dec_struct DD_src,                 /* I    Src del dec state                   */
        int LPC_state_idx                          /* I    Index to LPC buffer                 */
    )
    {
        // PORT-NOTE: C 的 sizeof(...) 字节数换算为元素个数；Gain_Q16 在 C 中亦不拷贝。
        SKP_memcpy(DD_dst.RandState, 0, DD_src.RandState, 0, DD_src.RandState.Length);
        SKP_memcpy(DD_dst.Q_Q10, 0, DD_src.Q_Q10, 0, DD_src.Q_Q10.Length);
        SKP_memcpy(DD_dst.Pred_Q16, 0, DD_src.Pred_Q16, 0, DD_src.Pred_Q16.Length);
        SKP_memcpy(DD_dst.Shape_Q10, 0, DD_src.Shape_Q10, 0, DD_src.Shape_Q10.Length);
        SKP_memcpy(DD_dst.Xq_Q10, 0, DD_src.Xq_Q10, 0, DD_src.Xq_Q10.Length);
        SKP_memcpy(DD_dst.sAR2_Q14, 0, DD_src.sAR2_Q14, 0, DD_src.sAR2_Q14.Length);
        SKP_memcpy(DD_dst.sLPC_Q14, LPC_state_idx, DD_src.sLPC_Q14, LPC_state_idx, NSQ_LPC_BUF_LENGTH);
        DD_dst.LF_AR_Q12 = DD_src.LF_AR_Q12;
        DD_dst.Seed = DD_src.Seed;
        DD_dst.SeedInit = DD_src.SeedInit;
        DD_dst.RD_Q10 = DD_src.RD_Q10;
    }
}
