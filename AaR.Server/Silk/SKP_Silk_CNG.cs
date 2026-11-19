#nullable disable
// ***********************************************************************
// SKP_Silk_CNG.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_CNG.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - SKP_Silk_CNG_exc 为 C 文件内 SKP_INLINE 函数 → AggressiveInlining。
//   - 跨文件引用（按裸名/类名调用，形态由 PORTING.md 规则推出）：
//       SKP_Silk_NLSF2A_stable.SKP_Silk_NLSF2A_stable(short[], off, int[], off, int)
//       SKP_Silk_LPC_synthesis_order16.SKP_Silk_LPC_synthesis_order16(...)
//       SKP_Silk_LPC_synthesis_filter.SKP_Silk_LPC_synthesis_filter(...)
//   - CNG_sig 同时作为 LPC 合成的输入与输出（同段原地），按规则保留两个
//     （数组, 偏移）参数不合并。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): C 函数名与所在文件同名（SKP_Silk_CNG），C# 禁止成员名与封闭类型
// 同名。按全工程统一约定：**类名**加 "_impl" 后缀，**方法名保持 C 原名**；调用方以
// `using static AaR.Server.Silk.SKP_Silk_CNG_impl;` + 裸名 SKP_Silk_CNG(...) 调用。
internal static class SKP_Silk_CNG_impl
{
    // PORT-NOTE: C 的 SKP_int16_MAX（interface/SKP_Silk_typedef.h，地基未含），值逐位一致。
    private const int SKP_int16_MAX = 32767;

    /* Generates excitation for CNG LPC synthesis */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SKP_Silk_CNG_exc(
        short[] residual, int residualOff,        /* O    CNG residual signal Q0                      */
        int[] exc_buf_Q10, int exc_buf_Q10Off,    /* I    Random samples buffer Q10                   */
        int Gain_Q16,                             /* I    Gain to apply                               */
        int length,                               /* I    Length                                      */
        ref int rand_seed                         /* I/O  Seed to random index generator              */
    )
    {
        int seed;
        int i, idx, exc_mask;

        exc_mask = CNG_BUF_MASK_MAX;
        while (exc_mask > length)
        {
            exc_mask = SKP_RSHIFT(exc_mask, 1);
        }

        seed = rand_seed;
        for (i = 0; i < length; i++)
        {
            seed = SKP_RAND(seed);
            idx = (int)(SKP_RSHIFT(seed, 24) & exc_mask);
            residual[residualOff + i] = (short)SKP_SAT16(
                SKP_RSHIFT_ROUND(SKP_SMULWW(exc_buf_Q10[exc_buf_Q10Off + idx], Gain_Q16), 10));
        }
        rand_seed = seed;
    }

    public static void SKP_Silk_CNG_Reset(
        SKP_Silk_decoder_state psDec               /* I/O  Decoder state                               */
    )
    {
        int i, NLSF_step_Q15, NLSF_acc_Q15;

        NLSF_step_Q15 = SKP_DIV32_16(SKP_int16_MAX, psDec.LPC_order + 1);
        NLSF_acc_Q15 = 0;
        for (i = 0; i < psDec.LPC_order; i++)
        {
            NLSF_acc_Q15 += NLSF_step_Q15;
            psDec.sCNG.CNG_smth_NLSF_Q15[i] = NLSF_acc_Q15;
        }
        psDec.sCNG.CNG_smth_Gain_Q16 = 0;
        psDec.sCNG.rand_seed = 3176576;
    }

    /* Updates CNG estimate, and applies the CNG when packet was lost   */
    public static void SKP_Silk_CNG(
        SKP_Silk_decoder_state psDec,              /* I/O  Decoder state                               */
        SKP_Silk_decoder_control psDecCtrl,        /* I/O  Decoder control                             */
        short[] signal, int signalOff,             /* I/O  Signal                                      */
        int length                                 /* I    Length of residual                          */
    )
    {
        int i, subfr;
        int tmp_32, Gain_Q26, max_Gain_Q16;
        short[] LPC_buf = new short[MAX_LPC_ORDER];
        short[] CNG_sig = new short[MAX_FRAME_LENGTH];
        SKP_Silk_CNG_struct psCNG;
        psCNG = psDec.sCNG;

        if (psDec.fs_kHz != psCNG.fs_kHz)
        {
            /* Reset state */
            SKP_Silk_CNG_Reset(psDec);

            psCNG.fs_kHz = psDec.fs_kHz;
        }
        if (psDec.lossCnt == 0 && psDec.vadFlag == NO_VOICE_ACTIVITY)
        {
            /* Update CNG parameters */

            /* Smoothing of LSF's  */
            for (i = 0; i < psDec.LPC_order; i++)
            {
                psCNG.CNG_smth_NLSF_Q15[i] += SKP_SMULWB(
                    psDec.prevNLSF_Q15[i] - psCNG.CNG_smth_NLSF_Q15[i], CNG_NLSF_SMTH_Q16);
            }
            /* Find the subframe with the highest gain */
            max_Gain_Q16 = 0;
            subfr = 0;
            for (i = 0; i < NB_SUBFR; i++)
            {
                if (psDecCtrl.Gains_Q16[i] > max_Gain_Q16)
                {
                    max_Gain_Q16 = psDecCtrl.Gains_Q16[i];
                    subfr = i;
                }
            }
            /* Update CNG excitation buffer with excitation from this subframe */
            SKP_memmove(psCNG.CNG_exc_buf_Q10, psDec.subfr_length, psCNG.CNG_exc_buf_Q10, 0,
                (NB_SUBFR - 1) * psDec.subfr_length);
            SKP_memcpy(psCNG.CNG_exc_buf_Q10, 0, psDec.exc_Q10, subfr * psDec.subfr_length,
                psDec.subfr_length);

            /* Smooth gains */
            for (i = 0; i < NB_SUBFR; i++)
            {
                psCNG.CNG_smth_Gain_Q16 += SKP_SMULWB(
                    psDecCtrl.Gains_Q16[i] - psCNG.CNG_smth_Gain_Q16, CNG_GAIN_SMTH_Q16);
            }
        }

        /* Add CNG when packet is lost and / or when low speech activity */
        if (psDec.lossCnt != 0)
        {//|| psDec->vadFlag == NO_VOICE_ACTIVITY ) {

            /* Generate CNG excitation */
            SKP_Silk_CNG_exc(CNG_sig, 0, psCNG.CNG_exc_buf_Q10, 0,
                psCNG.CNG_smth_Gain_Q16, length, ref psCNG.rand_seed);

            /* Convert CNG NLSF to filter representation */
            // PORT-NOTE: NLSF2A_stable/LPC_synthesis_* 为对端簇 CS0542 同类冲突符号，
            // 按全工程约定其类名带 "_impl" 后缀、方法名保持 C 原名。
            SKP_Silk_NLSF2A_stable_impl.SKP_Silk_NLSF2A_stable(LPC_buf, 0, psCNG.CNG_smth_NLSF_Q15, 0,
                psDec.LPC_order);

            Gain_Q26 = (int)1 << 26; /* 1.0 */

            /* Generate CNG signal, by synthesis filtering */
            if (psDec.LPC_order == 16)
            {
                SKP_Silk_LPC_synthesis_order16_impl.SKP_Silk_LPC_synthesis_order16(
                    CNG_sig, 0, LPC_buf, 0, Gain_Q26, psCNG.CNG_synth_state, 0, CNG_sig, 0, length);
            }
            else
            {
                SKP_Silk_LPC_synthesis_filter_impl.SKP_Silk_LPC_synthesis_filter(
                    CNG_sig, 0, LPC_buf, 0, Gain_Q26, psCNG.CNG_synth_state, 0, CNG_sig, 0, length,
                    psDec.LPC_order);
            }
            /* Mix with signal */
            for (i = 0; i < length; i++)
            {
                tmp_32 = signal[signalOff + i] + CNG_sig[i];
                signal[signalOff + i] = SKP_SAT16(tmp_32);
            }
        }
        else
        {
            SKP_memset(psCNG.CNG_synth_state, 0, 0, psDec.LPC_order);
        }
    }
}
