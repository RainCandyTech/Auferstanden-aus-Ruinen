#nullable disable
// ***********************************************************************
// SKP_Silk_PLC.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_PLC.c +
//   src/SKP_Silk_PLC.h（PLC.h 的宏定义一并移植；该头文件中无内联函数，
//   亦无 SKP_Silk_PLC_boost —— 1.0.9 源码中不存在该符号）
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - PLC.h 宏（BWE_COEF_Q16 等）与 PLC.c 的 NB_ATT/三张衰减表原样移植。
//   - C 的 union { as_int16[16]; as_int32[8]; } A_Q12_tmp：小端下
//     as_int32[k] 逐位等价于 union_as_int32(as_int16, k) =
//     ((uint)(ushort)as_int16[2k+1] << 16) | (ushort)as_int16[2k]，
//     见函数内 PORT-NOTE；只移植 !defined(_SYSTEM_IS_BIG_ENDIAN) 分支。
//   - 指针 B_Q14/rand_ptr/pred_lag_ptr/sig_Q10_ptr 改为数组引用 + 局部偏移。
//   - 跨文件引用（形态由 PORTING.md 规则推出）：
//       SKP_Silk_bwexpander.SKP_Silk_bwexpander(short[], off, int, int)
//       SKP_Silk_sum_sqr_shift.SKP_Silk_sum_sqr_shift(out, out, short[], off, int)
//       SKP_Silk_LPC_inv_pred_gain.SKP_Silk_LPC_inverse_pred_gain(out, short[], off, int)
//   - 本文件无 goto；C 源控制流（if/else 与 for）直译，无需改写。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_Inlines;

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): C 函数名与所在文件同名（SKP_Silk_PLC），按全工程统一约定：
// 类名加 "_impl" 后缀、方法名保持 C 原名；调用方以 `using static ...SKP_Silk_PLC_impl;`
// + 裸名调用。PLC.c 其余四个函数（PLC_Reset/update/conceal/glue_frames）无冲突，原名不变。
internal static class SKP_Silk_PLC_impl
{
    /* ============ SKP_Silk_PLC.h 的宏定义 ============ */
    public const int BWE_COEF_Q16 = 64880;                    /* 0.99 in Q16                      */
    public const int V_PITCH_GAIN_START_MIN_Q14 = 11469;      /* 0.7 in Q14                       */
    public const int V_PITCH_GAIN_START_MAX_Q14 = 15565;      /* 0.95 in Q14                      */
    public const int MAX_PITCH_LAG_MS = 18;
    public const int SA_THRES_Q8 = 50;
    public const int USE_SINGLE_TAP = 1;
    public const int RAND_BUF_SIZE = 128;
    public const int RAND_BUF_MASK = RAND_BUF_SIZE - 1;
    public const int LOG2_INV_LPC_GAIN_HIGH_THRES = 3;        /* 2^3 = 8 dB LPC gain              */
    public const int LOG2_INV_LPC_GAIN_LOW_THRES = 8;         /* 2^8 = 24 dB LPC gain             */
    public const int PITCH_DRIFT_FAC_Q16 = 655;                /* 0.01 in Q16                      */

    /* ============ SKP_Silk_PLC.c 文件级常量表 ============ */
    private const int NB_ATT = 2;
    internal static readonly short[] HARM_ATT_Q15 = new short[NB_ATT] { 32440, 31130 };              /* 0.99, 0.95 */
    internal static readonly short[] PLC_RAND_ATTENUATE_V_Q15 = new short[NB_ATT] { 31130, 26214 }; /* 0.95, 0.8 */
    internal static readonly short[] PLC_RAND_ATTENUATE_UV_Q15 = new short[NB_ATT] { 32440, 29491 }; /* 0.99, 0.9 */

    // PORT-NOTE: C 的 union { SKP_int16 as_int16[16]; SKP_int32 as_int32[8]; }：
    // 小端下 as_int32[k] 的 32 位 = as_int16[2k]（低半字）| as_int16[2k+1]（高半字）。
    // A_Q12_tmp 仅经 as_int16 写入、as_int32 读出，此读取函数逐位等价。
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int union_as_int32(short[] as_int16, int k)
    {
        return (int)(((uint)(ushort)as_int16[2 * k + 1] << 16) | (uint)(ushort)as_int16[2 * k]);
    }

    public static void SKP_Silk_PLC_Reset(
        SKP_Silk_decoder_state psDec              /* I/O Decoder state        */
    )
    {
        psDec.sPLC.pitchL_Q8 = SKP_RSHIFT(psDec.frame_length, 1);
    }

    public static void SKP_Silk_PLC(
        SKP_Silk_decoder_state psDec,             /* I Decoder state          */
        SKP_Silk_decoder_control psDecCtrl,       /* I Decoder control        */
        short[] signal, int signalOff,            /* O Concealed signal       */
        int length,                               /* I length of residual     */
        int lost                                  /* I Loss flag              */
    )
    {
        /* PLC control function */
        if (psDec.fs_kHz != psDec.sPLC.fs_kHz)
        {
            SKP_Silk_PLC_Reset(psDec);
            psDec.sPLC.fs_kHz = psDec.fs_kHz;
        }

        if (lost != 0)
        {
            /****************************/
            /* Generate Signal          */
            /****************************/
            SKP_Silk_PLC_conceal(psDec, psDecCtrl, signal, signalOff, length);

            psDec.lossCnt++;
        }
        else
        {
            /****************************/
            /* Update state             */
            /****************************/
            SKP_Silk_PLC_update(psDec, psDecCtrl, signal, signalOff, length);
        }
    }

    /**************************************************/
    /* Update state of PLC                            */
    /**************************************************/
    public static void SKP_Silk_PLC_update(
        SKP_Silk_decoder_state psDec,             /* (I/O) Decoder state          */
        SKP_Silk_decoder_control psDecCtrl,       /* (I/O) Decoder control        */
        short[] signal, int signalOff,            /* (I/O) signal                 */
        int length                                 /* (I) length of residual       */
    )
    {
        int LTP_Gain_Q14, temp_LTP_Gain_Q14;
        int i, j;
        SKP_Silk_PLC_struct psPLC;

        psPLC = psDec.sPLC;

        /* Update parameters used in case of packet loss */
        psDec.prev_sigtype = psDecCtrl.sigtype;
        LTP_Gain_Q14 = 0;
        if (psDecCtrl.sigtype == SIG_TYPE_VOICED)
        {
            /* Find the parameters for the last subframe which contains a pitch pulse */
            for (j = 0; j * psDec.subfr_length < psDecCtrl.pitchL[NB_SUBFR - 1]; j++)
            {
                temp_LTP_Gain_Q14 = 0;
                for (i = 0; i < LTP_ORDER; i++)
                {
                    temp_LTP_Gain_Q14 += psDecCtrl.LTPCoef_Q14[(NB_SUBFR - 1 - j) * LTP_ORDER + i];
                }
                if (temp_LTP_Gain_Q14 > LTP_Gain_Q14)
                {
                    LTP_Gain_Q14 = temp_LTP_Gain_Q14;
                    SKP_memcpy(psPLC.LTPCoef_Q14, 0, psDecCtrl.LTPCoef_Q14,
                        SKP_SMULBB(NB_SUBFR - 1 - j, LTP_ORDER), LTP_ORDER);

                    psPLC.pitchL_Q8 = SKP_LSHIFT(psDecCtrl.pitchL[NB_SUBFR - 1 - j], 8);
                }
            }

            // PORT-NOTE: C 为 #if USE_SINGLE_TAP（PLC.h 中恒为 1，默认构建恒编译本块）；
            // C# 的 #if 作用于构建符号而非常量，故去除指令、保留代码，语义与 C 默认构建一致。
            SKP_memset(psPLC.LTPCoef_Q14, 0, (short)0, LTP_ORDER);
            // PORT-NOTE: C 为 int32 赋给 int16 元素（隐式截断）；C# 需显式 (short)。
            psPLC.LTPCoef_Q14[LTP_ORDER / 2] = (short)LTP_Gain_Q14;

            /* Limit LT coefs */
            if (LTP_Gain_Q14 < V_PITCH_GAIN_START_MIN_Q14)
            {
                int scale_Q10;
                int tmp;

                tmp = SKP_LSHIFT(V_PITCH_GAIN_START_MIN_Q14, 10);
                scale_Q10 = SKP_DIV32(tmp, SKP_max(LTP_Gain_Q14, 1));
                for (i = 0; i < LTP_ORDER; i++)
                {
                    // PORT-NOTE: 同上，int32 结果截断为 int16 需显式 (short)。
                    psPLC.LTPCoef_Q14[i] = (short)SKP_RSHIFT(SKP_SMULBB(psPLC.LTPCoef_Q14[i], scale_Q10), 10);
                }
            }
            else if (LTP_Gain_Q14 > V_PITCH_GAIN_START_MAX_Q14)
            {
                int scale_Q14;
                int tmp;

                tmp = SKP_LSHIFT(V_PITCH_GAIN_START_MAX_Q14, 14);
                scale_Q14 = SKP_DIV32(tmp, SKP_max(LTP_Gain_Q14, 1));
                for (i = 0; i < LTP_ORDER; i++)
                {
                    psPLC.LTPCoef_Q14[i] = (short)SKP_RSHIFT(SKP_SMULBB(psPLC.LTPCoef_Q14[i], scale_Q14), 14);
                }
            }
        }
        else
        {
            psPLC.pitchL_Q8 = SKP_LSHIFT(SKP_SMULBB(psDec.fs_kHz, 18), 8);
            SKP_memset(psPLC.LTPCoef_Q14, 0, (short)0, LTP_ORDER);
        }

        /* Save LPC coeficients */
        SKP_memcpy(psPLC.prevLPC_Q12, 0, psDecCtrl.PredCoef_Q12[1], 0, psDec.LPC_order);
        // PORT-NOTE: C 为 int32 赋给 int16 字段（隐式截断）；C# 需显式 (short)。
        psPLC.prevLTP_scale_Q14 = (short)psDecCtrl.LTP_scale_Q14;

        /* Save Gains */
        SKP_memcpy(psPLC.prevGain_Q16, 0, psDecCtrl.Gains_Q16, 0, NB_SUBFR);
    }

    public static void SKP_Silk_PLC_conceal(
        SKP_Silk_decoder_state psDec,             /* I/O Decoder state */
        SKP_Silk_decoder_control psDecCtrl,       /* I/O Decoder control */
        short[] signal, int signalOff,            /* O concealed signal */
        int length                                /* I length of residual */
    )
    {
        int i, j, k;
        short[] B_Q14;
        short[] exc_buf = new short[MAX_FRAME_LENGTH];
        int exc_buf_ptr;                           /* C 指针 → 偏移 */
        short rand_scale_Q14;
        // PORT-NOTE: C 的 union{ as_int16[16]; as_int32[8]; } A_Q12_tmp —— 见 union_as_int32。
        short[] A_Q12_tmp = new short[MAX_LPC_ORDER];
        int rand_seed, harm_Gain_Q15, rand_Gain_Q15;
        int lag, idx, sLTP_buf_idx, shift1, shift2;
        int energy1, energy2;
        int rand_ptr;                              /* C 指针（psDec->exc_Q10 内偏移） */
        int pred_lag_ptr;                          /* C 指针（psDec->sLTP_Q16 内偏移） */
        int[] sig_Q10 = new int[MAX_FRAME_LENGTH];
        int sig_Q10_ptr;                           /* C 指针 → 偏移 */
        int LPC_exc_Q10, LPC_pred_Q10, LTP_pred_Q14;
        int Atmp;
        SKP_Silk_PLC_struct psPLC;

        psPLC = psDec.sPLC;

        /* Update LTP buffer */
        SKP_memcpy(psDec.sLTP_Q16, 0, psDec.sLTP_Q16, psDec.frame_length, psDec.frame_length);

        /* LPC concealment. Apply BWE to previous LPC */
        // PORT-NOTE: SKP_Silk_bwexpander 为对端簇 CS0542 冲突符号：其类名带 "_impl"
        // 后缀、方法名保持 C 原名。
        SKP_Silk_bwexpander_impl.SKP_Silk_bwexpander(psPLC.prevLPC_Q12, 0, psDec.LPC_order, BWE_COEF_Q16);

        /* Find random noise component */
        /* Scale previous excitation signal */
        exc_buf_ptr = 0;
        for (k = (NB_SUBFR >> 1); k < NB_SUBFR; k++)
        {
            for (i = 0; i < psDec.subfr_length; i++)
            {
                exc_buf[exc_buf_ptr + i] = (short)SKP_RSHIFT(
                    SKP_SMULWW(psDec.exc_Q10[i + k * psDec.subfr_length], psPLC.prevGain_Q16[k]), 10);
            }
            exc_buf_ptr += psDec.subfr_length;
        }
        /* Find the subframe with lowest energy of the last two and use that as random noise generator */
        // PORT-NOTE: SKP_Silk_sum_sqr_shift 为对端簇 CS0542 冲突符号（类名 _impl 后缀）。
        // 调用点加 "_impl"。
        SKP_Silk_sum_sqr_shift_impl.SKP_Silk_sum_sqr_shift(out energy1, out shift1, exc_buf, 0, psDec.subfr_length);
        SKP_Silk_sum_sqr_shift_impl.SKP_Silk_sum_sqr_shift(out energy2, out shift2, exc_buf, psDec.subfr_length, psDec.subfr_length);

        if (SKP_RSHIFT(energy1, shift2) < SKP_RSHIFT(energy2, shift1))
        {
            /* First sub-frame has lowest energy */
            rand_ptr = SKP_max_int(0, 3 * psDec.subfr_length - RAND_BUF_SIZE);
        }
        else
        {
            /* Second sub-frame has lowest energy */
            rand_ptr = SKP_max_int(0, psDec.frame_length - RAND_BUF_SIZE);
        }

        /* Setup Gain to random noise component */
        B_Q14 = psPLC.LTPCoef_Q14;
        rand_scale_Q14 = psPLC.randScale_Q14;

        /* Setup attenuation gains */
        harm_Gain_Q15 = HARM_ATT_Q15[SKP_min_int(NB_ATT - 1, psDec.lossCnt)];
        if (psDec.prev_sigtype == SIG_TYPE_VOICED)
        {
            rand_Gain_Q15 = PLC_RAND_ATTENUATE_V_Q15[SKP_min_int(NB_ATT - 1, psDec.lossCnt)];
        }
        else
        {
            rand_Gain_Q15 = PLC_RAND_ATTENUATE_UV_Q15[SKP_min_int(NB_ATT - 1, psDec.lossCnt)];
        }

        /* First Lost frame */
        if (psDec.lossCnt == 0)
        {
            rand_scale_Q14 = (1 << 14);

            /* Reduce random noise Gain for voiced frames */
            if (psDec.prev_sigtype == SIG_TYPE_VOICED)
            {
                for (i = 0; i < LTP_ORDER; i++)
                {
                    rand_scale_Q14 -= B_Q14[i];
                }
                rand_scale_Q14 = SKP_max_16(3277, rand_scale_Q14); /* 0.2 */
                rand_scale_Q14 = (short)SKP_RSHIFT(SKP_SMULBB(rand_scale_Q14, psPLC.prevLTP_scale_Q14), 14);
            }

            /* Reduce random noise for unvoiced frames with high LPC gain */
            if (psDec.prev_sigtype == SIG_TYPE_UNVOICED)
            {
                int invGain_Q30, down_scale_Q30;

                SKP_Silk_LPC_inv_pred_gain.SKP_Silk_LPC_inverse_pred_gain(out invGain_Q30,
                    psPLC.prevLPC_Q12, 0, psDec.LPC_order);

                down_scale_Q30 = SKP_min_32(SKP_RSHIFT((1 << 30), LOG2_INV_LPC_GAIN_HIGH_THRES), invGain_Q30);
                down_scale_Q30 = SKP_max_32(SKP_RSHIFT((1 << 30), LOG2_INV_LPC_GAIN_LOW_THRES), down_scale_Q30);
                down_scale_Q30 = SKP_LSHIFT(down_scale_Q30, LOG2_INV_LPC_GAIN_HIGH_THRES);

                rand_Gain_Q15 = SKP_RSHIFT(SKP_SMULWB(down_scale_Q30, rand_Gain_Q15), 14);
            }
        }

        rand_seed = psPLC.rand_seed;
        lag = SKP_RSHIFT_ROUND(psPLC.pitchL_Q8, 8);
        sLTP_buf_idx = psDec.frame_length;

        /***************************/
        /* LTP synthesis filtering */
        /***************************/
        sig_Q10_ptr = 0;
        for (k = 0; k < NB_SUBFR; k++)
        {
            /* Setup pointer */
            pred_lag_ptr = sLTP_buf_idx - lag + LTP_ORDER / 2;
            for (i = 0; i < psDec.subfr_length; i++)
            {
                rand_seed = SKP_RAND(rand_seed);
                idx = SKP_RSHIFT(rand_seed, 25) & RAND_BUF_MASK;

                /* Unrolled loop */
                LTP_pred_Q14 = SKP_SMULWB(psDec.sLTP_Q16[pred_lag_ptr + 0], B_Q14[0]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, psDec.sLTP_Q16[pred_lag_ptr - 1], B_Q14[1]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, psDec.sLTP_Q16[pred_lag_ptr - 2], B_Q14[2]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, psDec.sLTP_Q16[pred_lag_ptr - 3], B_Q14[3]);
                LTP_pred_Q14 = SKP_SMLAWB(LTP_pred_Q14, psDec.sLTP_Q16[pred_lag_ptr - 4], B_Q14[4]);
                pred_lag_ptr++;

                /* Generate LPC residual */
                LPC_exc_Q10 = SKP_LSHIFT(SKP_SMULWB(psDec.exc_Q10[rand_ptr + idx], rand_scale_Q14), 2); /* Random noise part */
                LPC_exc_Q10 = SKP_ADD32(LPC_exc_Q10, SKP_RSHIFT_ROUND(LTP_pred_Q14, 4));  /* Harmonic part */

                /* Update states */
                psDec.sLTP_Q16[sLTP_buf_idx] = SKP_LSHIFT(LPC_exc_Q10, 6);
                sLTP_buf_idx++;

                /* Save LPC residual */
                sig_Q10[sig_Q10_ptr + i] = LPC_exc_Q10;
            }
            sig_Q10_ptr += psDec.subfr_length;
            /* Gradually reduce LTP gain */
            for (j = 0; j < LTP_ORDER; j++)
            {
                // PORT-NOTE: C 为 int32 赋给 int16 元素（隐式截断）；C# 显式 (short)。
                B_Q14[j] = (short)SKP_RSHIFT(SKP_SMULBB(harm_Gain_Q15, B_Q14[j]), 15);
            }
            /* Gradually reduce excitation gain */
            rand_scale_Q14 = (short)SKP_RSHIFT(SKP_SMULBB(rand_scale_Q14, rand_Gain_Q15), 15);

            /* Slowly increase pitch lag */
            psPLC.pitchL_Q8 += SKP_SMULWB(psPLC.pitchL_Q8, PITCH_DRIFT_FAC_Q16);
            psPLC.pitchL_Q8 = SKP_min_32(psPLC.pitchL_Q8,
                SKP_LSHIFT(SKP_SMULBB(MAX_PITCH_LAG_MS, psDec.fs_kHz), 8));
            lag = SKP_RSHIFT_ROUND(psPLC.pitchL_Q8, 8);
        }

        /***************************/
        /* LPC synthesis filtering */
        /***************************/
        sig_Q10_ptr = 0;
        /* Preload LPC coeficients to array on stack. Gives small performance gain */
        SKP_memcpy(A_Q12_tmp, 0, psPLC.prevLPC_Q12, 0, psDec.LPC_order);
        for (k = 0; k < NB_SUBFR; k++)
        {
            for (i = 0; i < psDec.subfr_length; i++)
            {
                /* partly unrolled */
                /* NOTE: the code below loads two int16 values in an int32, and multiplies each using the   */
                /* SMLAWB and SMLAWT instructions. On a big-endian CPU the two int16 variables would be     */
                /* loaded in reverse order and the code will give the wrong result. In that case swapping   */
                /* the SMLAWB and SMLAWT instructions should solve the problem.                             */
                Atmp = union_as_int32(A_Q12_tmp, 0);    /* read two coefficients at once */
                LPC_pred_Q10 = SKP_SMULWB(psDec.sLPC_Q14[MAX_LPC_ORDER + i - 1], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 2], Atmp);
                Atmp = union_as_int32(A_Q12_tmp, 1);
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 3], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 4], Atmp);
                Atmp = union_as_int32(A_Q12_tmp, 2);
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 5], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 6], Atmp);
                Atmp = union_as_int32(A_Q12_tmp, 3);
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 7], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 8], Atmp);
                Atmp = union_as_int32(A_Q12_tmp, 4);
                LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 9], Atmp);
                LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 10], Atmp);
                for (j = 10; j < psDec.LPC_order; j += 2)
                {
                    Atmp = union_as_int32(A_Q12_tmp, j / 2);
                    LPC_pred_Q10 = SKP_SMLAWB(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 1 - j], Atmp);
                    LPC_pred_Q10 = SKP_SMLAWT(LPC_pred_Q10, psDec.sLPC_Q14[MAX_LPC_ORDER + i - 2 - j], Atmp);
                }
                /* Add prediction to LPC residual */
                sig_Q10[sig_Q10_ptr + i] = SKP_ADD32(sig_Q10[sig_Q10_ptr + i], LPC_pred_Q10);

                /* Update states */
                psDec.sLPC_Q14[MAX_LPC_ORDER + i] = SKP_LSHIFT(sig_Q10[sig_Q10_ptr + i], 4);
            }
            sig_Q10_ptr += psDec.subfr_length;
            /* Update LPC filter state */
            SKP_memcpy(psDec.sLPC_Q14, 0, psDec.sLPC_Q14, psDec.subfr_length, MAX_LPC_ORDER);
        }

        /* Scale with Gain */
        for (i = 0; i < psDec.frame_length; i++)
        {
            signal[signalOff + i] = (short)SKP_SAT16(
                SKP_RSHIFT_ROUND(SKP_SMULWW(sig_Q10[i], psPLC.prevGain_Q16[NB_SUBFR - 1]), 10));
        }

        /**************************************/
        /* Update states                      */
        /**************************************/
        psPLC.rand_seed = rand_seed;
        psPLC.randScale_Q14 = rand_scale_Q14;
        for (i = 0; i < NB_SUBFR; i++)
        {
            psDecCtrl.pitchL[i] = lag;
        }
    }

    /* Glues concealed frames with new good recieved frames             */
    public static void SKP_Silk_PLC_glue_frames(
        SKP_Silk_decoder_state psDec,             /* I/O decoder state    */
        SKP_Silk_decoder_control psDecCtrl,       /* I/O Decoder control  */
        short[] signal, int signalOff,            /* I/O signal           */
        int length                                /* I length of residual */
    )
    {
        int i, energy_shift;
        int energy;
        SKP_Silk_PLC_struct psPLC;
        psPLC = psDec.sPLC;

        if (psDec.lossCnt != 0)
        {
            /* Calculate energy in concealed residual */
            SKP_Silk_sum_sqr_shift_impl.SKP_Silk_sum_sqr_shift(out psPLC.conc_energy, out psPLC.conc_energy_shift,
                signal, signalOff, length);

            psPLC.last_frame_lost = 1;
        }
        else
        {
            if (psDec.sPLC.last_frame_lost != 0)
            {
                /* Calculate residual in decoded signal if last frame was lost */
                SKP_Silk_sum_sqr_shift_impl.SKP_Silk_sum_sqr_shift(out energy, out energy_shift,
                    signal, signalOff, length);

                /* Normalize energies */
                if (energy_shift > psPLC.conc_energy_shift)
                {
                    psPLC.conc_energy = SKP_RSHIFT(psPLC.conc_energy, energy_shift - psPLC.conc_energy_shift);
                }
                else if (energy_shift < psPLC.conc_energy_shift)
                {
                    energy = SKP_RSHIFT(energy, psPLC.conc_energy_shift - energy_shift);
                }

                /* Fade in the energy difference */
                if (energy > psPLC.conc_energy)
                {
                    int frac_Q24, LZ;
                    int gain_Q12, slope_Q12;

                    LZ = SKP_Silk_CLZ32(psPLC.conc_energy);
                    LZ = LZ - 1;
                    psPLC.conc_energy = SKP_LSHIFT(psPLC.conc_energy, LZ);
                    energy = SKP_RSHIFT(energy, SKP_max_32(24 - LZ, 0));

                    frac_Q24 = SKP_DIV32(psPLC.conc_energy, SKP_max(energy, 1));

                    gain_Q12 = SKP_Silk_SQRT_APPROX(frac_Q24);
                    slope_Q12 = SKP_DIV32_16((1 << 12) - gain_Q12, length);

                    for (i = 0; i < length; i++)
                    {
                        // PORT-NOTE: C 为 int32 赋给 int16 元素（隐式截断）；C# 显式 (short)。
                        signal[signalOff + i] = (short)SKP_RSHIFT(SKP_MUL(gain_Q12, signal[signalOff + i]), 12);
                        gain_Q12 += slope_Q12;
                        gain_Q12 = SKP_min(gain_Q12, (1 << 12));
                    }
                }
            }
            psPLC.last_frame_lost = 0;

        }
    }
}
