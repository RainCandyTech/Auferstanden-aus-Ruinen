#nullable disable
// ***********************************************************************
// SKP_Silk_NSQ.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NSQ.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - 指针步进（x/q/pxq/A_Q12/B_Q14/AR_shp_Q13/shp_lag_ptr/pred_lag_ptr/
//     psLPC_Q14）一律改为（数组引用 + 局部偏移），步进 += 照抄。
//   - 小端分支（!_SYSTEM_IS_BIG_ENDIAN）的 a_Q12_tmp 打包技巧：C 用
//     SKP_memcpy 把 int16 系数对复制进 int32 数组再以 Atmp 配对取
//     SMLAWB/SMLAWT；C# 无联合体/字节级 memcpy，改为 SKP_pack16to32
//     逐位打包（低半字 = 偶数系数），见 PORT-NOTE。
//   - 跨文件引用：SKP_Silk_MA.SKP_Silk_MA_Prediction(...)（形态由
//     PORTING.md 规则推出）；SKP_Silk_Inlines（using static，裸名）。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_Inlines;
using static AaR.Server.Silk.SKP_Silk_tables_other;          // SKP_Silk_Quantization_Offsets_Q10

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): C 函数名与所在文件同名（SKP_Silk_NSQ），按全工程统一约定：
// 类名加 "_impl" 后缀、方法名保持 C 原名；调用方以
// `using static ...SKP_Silk_NSQ_impl;` + 裸名 SKP_Silk_NSQ(...) 调用。
internal static class SKP_Silk_NSQ_impl
{
    // PORT-NOTE: C 的 SKP_int16_MAX（interface/SKP_Silk_typedef.h，地基未含），值逐位一致。
    private const int SKP_int16_MAX = 32767;

    // PORT-NOTE: C 用 SKP_memcpy( a_Q12_tmp, a_Q12, predictLPCOrder * sizeof( SKP_int16 ) )
    // 把 int16 系数对字节级复制进 int32 数组（小端）；等价于把相邻两系数打包进
    // int32（低半字 = 偶数下标系数），供 Atmp 配对取 SMLAWB/SMLAWT。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SKP_pack16to32(short lo, short hi)
    {
        return (int)(((uint)(ushort)hi << 16) | (uint)(ushort)lo);
    }

    public static void SKP_Silk_NSQ(
        SKP_Silk_encoder_state psEncC,                                    /* I/O  Encoder State                       */
        SKP_Silk_encoder_control psEncCtrlC,                                /* I    Encoder Control                     */
        SKP_Silk_nsq_state NSQ,                                            /* I/O  NSQ state                           */
        short[] x, int xOff,                                               /* I    prefiltered input signal            */
        sbyte[] q, int qOff,                                               /* O    quantized qulse signal              */
        int LSFInterpFactor_Q2,                                            /* I    LSF interpolation factor in Q2      */
        short[][] PredCoef_Q12,                                             /* I    Short term prediction coefficients（两行：C 的平铺 [2*MAX_LPC_ORDER] → 交错；行选择见下） */
        short[] LTPCoef_Q14, int LTPCoef_Q14Off,                           /* I    Long term prediction coefficients   */
        short[] AR2_Q13, int AR2_Q13Off,                                   /* I                                        */
        int[] HarmShapeGain_Q14, int HarmShapeGain_Q14Off,                 /* I                                        */
        int[] Tilt_Q14, int Tilt_Q14Off,                                   /* I    Spectral tilt                       */
        int[] LF_shp_Q14, int LF_shp_Q14Off,                               /* I                                        */
        int[] Gains_Q16, int Gains_Q16Off,                                 /* I                                        */
        int Lambda_Q10,                                                     /* I                                        */
        int LTP_scale_Q14                                                   /* I    LTP state scaling                   */
    )
    {
        int k, lag, start_idx, LSF_interpolation_flag;
        short[] A_Q12; int A_Q12Off;
        short[] B_Q14; int B_Q14Off;
        short[] AR_shp_Q13; int AR_shp_Q13Off;
        short[] pxqArr; int pxq;                 /* C: SKP_int16 *pxq（指向 NSQ->xq 内部） */
        int[] sLTP_Q16 = new int[2 * MAX_FRAME_LENGTH];
        short[] sLTP = new short[2 * MAX_FRAME_LENGTH];
        int HarmShapeFIRPacked_Q14;
        int offset_Q10;
        int[] FiltState = new int[MAX_LPC_ORDER];
        int[] x_sc_Q10 = new int[MAX_FRAME_LENGTH / NB_SUBFR];

        NSQ.rand_seed = psEncCtrlC.Seed;
        /* Set unvoiced lag to the previous one, overwrite later for voiced */
        lag = NSQ.lagPrev;

        offset_Q10 = SKP_Silk_Quantization_Offsets_Q10[psEncCtrlC.sigtype][psEncCtrlC.QuantOffsetType];

        if (LSFInterpFactor_Q2 == (1 << 2))
        {
            LSF_interpolation_flag = 0;
        }
        else
        {
            LSF_interpolation_flag = 1;
        }

        /* Setup pointers to start of sub frame */
        NSQ.sLTP_shp_buf_idx = psEncC.frame_length;
        NSQ.sLTP_buf_idx = psEncC.frame_length;
        pxqArr = NSQ.xq;
        pxq = psEncC.frame_length;
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

                    /* Rewhiten with new A coefs */
                    start_idx = psEncC.frame_length - lag - psEncC.predictLPCOrder - LTP_ORDER / 2;

                    SKP_memset(FiltState, 0, 0, psEncC.predictLPCOrder);
                    SKP_Silk_MA.SKP_Silk_MA_Prediction(NSQ.xq,
                        start_idx + k * (psEncC.frame_length >> 2),
                        A_Q12, A_Q12Off, FiltState, 0, sLTP, start_idx,
                        psEncC.frame_length - start_idx, psEncC.predictLPCOrder);

                    NSQ.rewhite_flag = 1;
                    NSQ.sLTP_buf_idx = psEncC.frame_length;
                }
            }

            SKP_Silk_nsq_scale_states(NSQ, x, xOff, x_sc_Q10, 0, psEncC.subfr_length, sLTP, 0,
                sLTP_Q16, 0, k, LTP_scale_Q14, Gains_Q16, Gains_Q16Off, psEncCtrlC.pitchL, 0);

            SKP_Silk_noise_shape_quantizer(NSQ, psEncCtrlC.sigtype, x_sc_Q10, 0, q, qOff,
                pxqArr, pxq, sLTP_Q16, 0, A_Q12, A_Q12Off, B_Q14, B_Q14Off,
                AR_shp_Q13, AR_shp_Q13Off, lag, HarmShapeFIRPacked_Q14,
                Tilt_Q14[Tilt_Q14Off + k], LF_shp_Q14[LF_shp_Q14Off + k], Gains_Q16[Gains_Q16Off + k],
                Lambda_Q10, offset_Q10, psEncC.subfr_length, psEncC.shapingLPCOrder, psEncC.predictLPCOrder
            );

            xOff += psEncC.subfr_length;
            qOff += psEncC.subfr_length;
            pxq += psEncC.subfr_length;
        }

        /* Update lagPrev for next frame */
        NSQ.lagPrev = psEncCtrlC.pitchL[NB_SUBFR - 1];

        /* Save quantized speech and noise shaping signals */
        SKP_memcpy(NSQ.xq, 0, NSQ.xq, psEncC.frame_length, psEncC.frame_length);
        SKP_memcpy(NSQ.sLTP_shp_Q10, 0, NSQ.sLTP_shp_Q10, psEncC.frame_length, psEncC.frame_length);
    }

    /***********************************/
    /* SKP_Silk_noise_shape_quantizer  */
    /***********************************/
    private static void SKP_Silk_noise_shape_quantizer(
        SKP_Silk_nsq_state NSQ,                    /* I/O  NSQ state                       */
        int sigtype,                               /* I    Signal type                     */
        int[] x_sc_Q10, int x_sc_Q10Off,           /* I                                    */
        sbyte[] q, int qOff,                       /* O                                    */
        short[] xq, int xqOff,                     /* O                                    */
        int[] sLTP_Q16, int sLTP_Q16Off,           /* I/O  LTP state                       */
        short[] a_Q12, int a_Q12Off,               /* I    Short term prediction coefs     */
        short[] b_Q14, int b_Q14Off,               /* I    Long term prediction coefs      */
        short[] AR_shp_Q13, int AR_shp_Q13Off,     /* I    Noise shaping AR coefs          */
        int lag,                                    /* I    Pitch lag                       */
        int HarmShapeFIRPacked_Q14,                /* I                                    */
        int Tilt_Q14,                              /* I    Spectral tilt                   */
        int LF_shp_Q14,                            /* I                                    */
        int Gain_Q16,                              /* I                                    */
        int Lambda_Q10,                            /* I                                    */
        int offset_Q10,                            /* I                                    */
        int length,                                /* I    Input length                    */
        int shapingLPCOrder,                       /* I    Noise shaping AR filter order   */
        int predictLPCOrder                        /* I    Prediction filter order         */
    )
    {
        int i, j;
        int LTP_pred_Q14, LPC_pred_Q10, n_AR_Q10, n_LTP_Q14;
        int n_LF_Q10, r_Q10, q_Q0, q_Q10;
        int thr1_Q10, thr2_Q10, thr3_Q10;
        int dither, exc_Q10, LPC_exc_Q10, xq_Q10;
        int tmp1, tmp2, sLF_AR_shp_Q10;
        int shp_lag_ptr, pred_lag_ptr, psLPC_Q14;   /* C 指针 → 偏移 */
        int[] a_Q12_tmp = new int[MAX_LPC_ORDER / 2];
        int Atmp;

        /* Preload LPC coefficients to array on stack. Gives small performance gain */
        // PORT-NOTE: C 为 SKP_memcpy( a_Q12_tmp, a_Q12, predictLPCOrder * sizeof( SKP_int16 ) )
        // ——字节级复制即按小端把相邻两 int16 打包进一个 int32。
        for (j = 0; j < (predictLPCOrder >> 1); j++)
        {
            a_Q12_tmp[j] = SKP_pack16to32(a_Q12[a_Q12Off + 2 * j], a_Q12[a_Q12Off + 2 * j + 1]);
        }

        shp_lag_ptr = NSQ.sLTP_shp_buf_idx - lag + HARM_SHAPE_FIR_TAPS / 2;
        pred_lag_ptr = NSQ.sLTP_buf_idx - lag + LTP_ORDER / 2;

        /* Setup short term AR state */
        psLPC_Q14 = NSQ_LPC_BUF_LENGTH - 1;

        /* Quantization thresholds */
        thr1_Q10 = SKP_SUB_RSHIFT32(-1536, Lambda_Q10, 1);
        thr2_Q10 = SKP_SUB_RSHIFT32(-512, Lambda_Q10, 1);
        thr2_Q10 = SKP_ADD_RSHIFT32(thr2_Q10, SKP_SMULBB(offset_Q10, Lambda_Q10), 10);
        thr3_Q10 = SKP_ADD_RSHIFT32(512, Lambda_Q10, 1);

        for (i = 0; i < length; i++)
        {
            /* Generate dither */
            NSQ.rand_seed = SKP_RAND(NSQ.rand_seed);

            /* dither = rand_seed < 0 ? 0xFFFFFFFF : 0; */
            dither = SKP_RSHIFT(NSQ.rand_seed, 31);

            /* Short-term prediction */
            /* NOTE: the code below loads two int16 values in an int32, and multiplies each using the   */
            /* SMLAWB and SMLAWT instructions. On a big-endian CPU the two int16 variables would be   */
            /* loaded in reverse order and the code will give the wrong result. In that case swapping */
            /* the SMLAWB and SMLAWT instructions should solve the problem.                             */
            /* Partially unrolled */
            Atmp = a_Q12_tmp[0];      /* read two coefficients at once */
            LPC_pred_Q10 = SKP_SMULWB(NSQ.sLPC_Q14[psLPC_Q14 + 0], Atmp);
            LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 1], Atmp);
            Atmp = a_Q12_tmp[1];
            LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 2], Atmp);
            LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 3], Atmp);
            Atmp = a_Q12_tmp[2];
            LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 4], Atmp);
            LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 5], Atmp);
            Atmp = a_Q12_tmp[3];
            LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 6], Atmp);
            LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 7], Atmp);
            Atmp = a_Q12_tmp[4];
            LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 8], Atmp);
            LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - 9], Atmp);
            for (j = 10; j < predictLPCOrder; j += 2)
            {
                Atmp = a_Q12_tmp[j >> 1];     /* read two coefficients at once */
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - j], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, NSQ.sLPC_Q14[psLPC_Q14 - j - 1], Atmp);
            }

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

            /* Noise shape feedback */
            tmp2 = NSQ.sLPC_Q14[psLPC_Q14 + 0];
            tmp1 = NSQ.sAR2_Q14[0];
            NSQ.sAR2_Q14[0] = tmp2;
            n_AR_Q10 = SKP_SMULWB(tmp2, AR_shp_Q13[AR_shp_Q13Off + 0]);
            for (j = 2; j < shapingLPCOrder; j += 2)
            {
                tmp2 = NSQ.sAR2_Q14[j - 1];
                NSQ.sAR2_Q14[j - 1] = tmp1;
                n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, tmp1, AR_shp_Q13[AR_shp_Q13Off + j - 1]);
                tmp1 = NSQ.sAR2_Q14[j + 0];
                NSQ.sAR2_Q14[j + 0] = tmp2;
                n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, tmp2, AR_shp_Q13[AR_shp_Q13Off + j]);
            }
            NSQ.sAR2_Q14[shapingLPCOrder - 1] = tmp1;
            n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, tmp1, AR_shp_Q13[AR_shp_Q13Off + shapingLPCOrder - 1]);

            n_AR_Q10 = SKP_RSHIFT(n_AR_Q10, 1);   /* Q11 -> Q10 */
            n_AR_Q10 = SKP_SMLAWB(n_AR_Q10, NSQ.sLF_AR_shp_Q12, Tilt_Q14);

            n_LF_Q10 = SKP_LSHIFT(SKP_SMULWB(NSQ.sLTP_shp_Q10[NSQ.sLTP_shp_buf_idx - 1], LF_shp_Q14), 2);
            n_LF_Q10 = SKP_SMLAWT(n_LF_Q10, NSQ.sLF_AR_shp_Q12, LF_shp_Q14);

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

            /* Input minus prediction plus noise feedback  */
            //r = x[ i ] - LTP_pred - LPC_pred + n_AR + n_Tilt + n_LF + n_LTP;
            tmp1 = SKP_SUB32(LTP_pred_Q14, n_LTP_Q14);                       /* Add Q14 stuff */
            tmp1 = SKP_RSHIFT(tmp1, 4);                                      /* convert to Q10  */
            tmp1 = SKP_ADD32(tmp1, LPC_pred_Q10);                            /* add Q10 stuff */
            tmp1 = SKP_SUB32(tmp1, n_AR_Q10);                                /* subtract Q10 stuff */
            tmp1 = SKP_SUB32(tmp1, n_LF_Q10);                                /* subtract Q10 stuff */
            r_Q10 = SKP_SUB32(x_sc_Q10[x_sc_Q10Off + i], tmp1);

            /* Flip sign depending on dither */
            r_Q10 = (r_Q10 ^ dither) - dither;
            r_Q10 = SKP_SUB32(r_Q10, offset_Q10);
            r_Q10 = SKP_LIMIT_32(r_Q10, -(64 << 10), 64 << 10);

            /* Quantize */
            q_Q0 = 0;
            q_Q10 = 0;
            if (r_Q10 < thr2_Q10)
            {
                if (r_Q10 < thr1_Q10)
                {
                    q_Q0 = SKP_RSHIFT_ROUND(SKP_ADD_RSHIFT32(r_Q10, Lambda_Q10, 1), 10);
                    q_Q10 = SKP_LSHIFT(q_Q0, 10);
                }
                else
                {
                    q_Q0 = -1;
                    q_Q10 = -1024;
                }
            }
            else
            {
                if (r_Q10 > thr3_Q10)
                {
                    q_Q0 = SKP_RSHIFT_ROUND(SKP_SUB_RSHIFT32(r_Q10, Lambda_Q10, 1), 10);
                    q_Q10 = SKP_LSHIFT(q_Q0, 10);
                }
            }
            q[qOff + i] = (sbyte)q_Q0; /* No saturation needed because max is 64 */

            /* Excitation */
            exc_Q10 = SKP_ADD32(q_Q10, offset_Q10);
            exc_Q10 = (exc_Q10 ^ dither) - dither;

            /* Add predictions */
            LPC_exc_Q10 = SKP_ADD32(exc_Q10, SKP_RSHIFT_ROUND(LTP_pred_Q14, 4));
            xq_Q10 = SKP_ADD32(LPC_exc_Q10, LPC_pred_Q10);

            /* Scale XQ back to normal level before saving */
            xq[xqOff + i] = (short)SKP_SAT16(SKP_RSHIFT_ROUND(SKP_SMULWW(xq_Q10, Gain_Q16), 10));


            /* Update states */
            psLPC_Q14++;
            NSQ.sLPC_Q14[psLPC_Q14] = SKP_LSHIFT(xq_Q10, 4);
            sLF_AR_shp_Q10 = SKP_SUB32(xq_Q10, n_AR_Q10);
            NSQ.sLF_AR_shp_Q12 = SKP_LSHIFT(sLF_AR_shp_Q10, 2);

            NSQ.sLTP_shp_Q10[NSQ.sLTP_shp_buf_idx] = SKP_SUB32(sLF_AR_shp_Q10, n_LF_Q10);
            sLTP_Q16[sLTP_Q16Off + NSQ.sLTP_buf_idx] = SKP_LSHIFT(LPC_exc_Q10, 6);
            NSQ.sLTP_shp_buf_idx++;
            NSQ.sLTP_buf_idx++;

            /* Make dither dependent on quantized signal */
            NSQ.rand_seed += q[qOff + i];
        }

        /* Update LPC synth buffer */
        SKP_memcpy(NSQ.sLPC_Q14, 0, NSQ.sLPC_Q14, length, NSQ_LPC_BUF_LENGTH);
    }

    private static void SKP_Silk_nsq_scale_states(
        SKP_Silk_nsq_state NSQ,                    /* I/O NSQ state                        */
        short[] x, int xOff,                       /* I input in Q0                        */
        int[] x_sc_Q10, int x_sc_Q10Off,           /* O input scaled with 1/Gain           */
        int subfr_length,                          /* I length of input                    */
        short[] sLTP, int sLTPOff,                 /* I re-whitened LTP state in Q0        */
        int[] sLTP_Q16, int sLTP_Q16Off,           /* O LTP state matching scaled input    */
        int subfr,                                  /* I subframe number                    */
        int LTP_scale_Q14,                         /* I                                    */
        int[] Gains_Q16, int Gains_Q16Off,          /* I                                    */
        int[] pitchL, int pitchLOff                 /* I                                    */
    )
    {
        int i, lag;
        int inv_gain_Q16, gain_adj_Q16, inv_gain_Q32;

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

            NSQ.sLF_AR_shp_Q12 = SKP_SMULWW(gain_adj_Q16, NSQ.sLF_AR_shp_Q12);

            /* Scale short-term prediction and shaping states */
            for (i = 0; i < NSQ_LPC_BUF_LENGTH; i++)
            {
                NSQ.sLPC_Q14[i] = SKP_SMULWW(gain_adj_Q16, NSQ.sLPC_Q14[i]);
            }
            for (i = 0; i < MAX_SHAPE_LPC_ORDER; i++)
            {
                NSQ.sAR2_Q14[i] = SKP_SMULWW(gain_adj_Q16, NSQ.sAR2_Q14[i]);
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
}
