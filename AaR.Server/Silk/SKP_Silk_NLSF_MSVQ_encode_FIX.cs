#nullable disable
// ***********************************************************************
// SKP_Silk_NLSF_MSVQ_encode_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NLSF_MSVQ_encode_FIX.c
//（C 文件内含的版权注释按原样以 // 形式保留如下）
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

/***********************/
/* NLSF vector encoder */
/***********************/

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_sort;
using static AaR.Server.Silk.SKP_Silk_NLSF_MSVQ_decode_impl;
using static AaR.Server.Silk.SKP_Silk_NLSF_VQ_rate_distortion_FIX_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_NLSF_MSVQ_encode_FIX.c —— NLSF vector encoder（tree search MSVQ）.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_NLSF_MSVQ_encode_FIX_impl
{
    public static void SKP_Silk_NLSF_MSVQ_encode_FIX(
          int[] NLSFIndices, int NLSFIndicesOff,        /* O    Codebook path vector [ CB_STAGES ]      */
          int[] pNLSF_Q15, int pNLSF_Q15Off,            /* I/O  Quantized NLSF vector [ LPC_ORDER ]     */
          SKP_Silk_NLSF_CB_struct psNLSF_CB,             /* I    Codebook object                         */
          int[] pNLSF_q_Q15_prev, int pNLSF_q_Q15_prevOff, /* I    Prev. quantized NLSF vector [LPC_ORDER] */
          int[] pW_Q6, int pW_Q6Off,                     /* I    NLSF weight vector [ LPC_ORDER ]        */
          int NLSF_mu_Q15,                                /* I    Rate weight for the RD optimization     */
          int NLSF_mu_fluc_red_Q16,                       /* I    Fluctuation reduction error weight      */
          int NLSF_MSVQ_Survivors,                        /* I    Max survivors from each stage           */
          int LPC_order,                                  /* I    LPC order                               */
          int deactivate_fluc_red                         /* I    Deactivate fluctuation reduction        */
    )
    {
        int i, s, k, cur_survivors = 0, prev_survivors, min_survivors, input_index, cb_index, bestIndex;
        int rateDistThreshold_Q18;
        /* #if( NLSF_MSVQ_FLUCTUATION_REDUCTION == 1 ) —— 该宏为 1，此分支保留 */
        int se_Q15, wsse_Q20, bestRateDist_Q20;

        /* #if( LOW_COMPLEXITY_ONLY == 1 ) —— LOW_COMPLEXITY_ONLY == 0（默认构建），
           按 §7 只移植 #else 分支的数组尺寸。 */
        int[] pRateDist_Q18 = new int[NLSF_MSVQ_TREE_SEARCH_MAX_VECTORS_EVALUATED];
        int[] pRate_Q5 = new int[MAX_NLSF_MSVQ_SURVIVORS];
        int[] pRate_new_Q5 = new int[MAX_NLSF_MSVQ_SURVIVORS];
        int[] pTempIndices = new int[MAX_NLSF_MSVQ_SURVIVORS];
        int[] pPath = new int[MAX_NLSF_MSVQ_SURVIVORS * NLSF_MSVQ_MAX_CB_STAGES];
        int[] pPath_new = new int[MAX_NLSF_MSVQ_SURVIVORS * NLSF_MSVQ_MAX_CB_STAGES];
        int[] pRes_Q15 = new int[MAX_NLSF_MSVQ_SURVIVORS * MAX_LPC_ORDER];
        int[] pRes_new_Q15 = new int[MAX_NLSF_MSVQ_SURVIVORS * MAX_LPC_ORDER];

        // PORT-NOTE: C 的指针 pConstInt/pInt/pCB_element（均在对应数组内步进）→ 数组 + 偏移。
        int pConstIntOff;
        int pIntOff;
        int pCB_elementOff;
        SKP_Silk_NLSF_CBS pCurrentCBStage;

        /****************************************************/
        /* Tree search for the multi-stage vector quantizer */
        /****************************************************/

        /* Clear accumulated rates */
        SKP_memset(pRate_Q5, 0, 0, NLSF_MSVQ_Survivors);

        /* Copy NLSFs into residual signal vector */
        for (i = 0; i < LPC_order; i++)
        {
            pRes_Q15[i] = pNLSF_Q15[pNLSF_Q15Off + i];
        }

        /* Set first stage values */
        prev_survivors = 1;

        /* Minimum number of survivors */
        min_survivors = NLSF_MSVQ_Survivors / 2;

        /* Loop over all stages */
        for (s = 0; s < psNLSF_CB.nStages; s++)
        {

            /* Set a pointer to the current stage codebook */
            pCurrentCBStage = psNLSF_CB.CBStages[s];

            /* Calculate the number of survivors in the current stage */
            cur_survivors = SKP_min_32(NLSF_MSVQ_Survivors, SKP_SMULBB(prev_survivors, pCurrentCBStage.nVectors));

            /* Nearest neighbor clustering for multiple input data vectors */
            SKP_Silk_NLSF_VQ_rate_distortion_FIX(pRateDist_Q18, 0, pCurrentCBStage, pRes_Q15, 0, pW_Q6, pW_Q6Off,
                pRate_Q5, 0, NLSF_mu_Q15, prev_survivors, LPC_order);

            /* Sort the rate-distortion errors */
            SKP_Silk_insertion_sort_increasing(pRateDist_Q18, 0, pTempIndices, 0,
                prev_survivors * pCurrentCBStage.nVectors, cur_survivors);

            /* Discard survivors with rate-distortion values too far above the best one */
            if (pRateDist_Q18[0] < int.MaxValue /* SKP_int32_MAX */ / MAX_NLSF_MSVQ_SURVIVORS)
            {
                rateDistThreshold_Q18 = SKP_SMLAWB(pRateDist_Q18[0],
                    SKP_MUL(NLSF_MSVQ_Survivors, pRateDist_Q18[0]), SKP_FIX_CONST(NLSF_MSVQ_SURV_MAX_REL_RD, 16));
                while (pRateDist_Q18[cur_survivors - 1] > rateDistThreshold_Q18 && cur_survivors > min_survivors)
                {
                    cur_survivors--;
                }
            }
            /* Update accumulated codebook contributions for the 'cur_survivors' best codebook indices */
            for (k = 0; k < cur_survivors; k++)
            {
                if (s > 0)
                {
                    /* Find the indices of the input and the codebook vector */
                    if (pCurrentCBStage.nVectors == 8)
                    {
                        input_index = SKP_RSHIFT(pTempIndices[k], 3);
                        cb_index = pTempIndices[k] & 7;
                    }
                    else
                    {
                        input_index = SKP_DIV32_16(pTempIndices[k], pCurrentCBStage.nVectors);
                        cb_index = pTempIndices[k] - SKP_SMULBB(input_index, pCurrentCBStage.nVectors);
                    }
                }
                else
                {
                    /* Find the indices of the input and the codebook vector */
                    input_index = 0;
                    cb_index = pTempIndices[k];
                }

                /* Subtract new contribution from the previous residual vector for each of 'cur_survivors' */
                pConstIntOff = SKP_SMULBB(input_index, LPC_order);
                pCB_elementOff = SKP_SMULBB(cb_index, LPC_order);
                pIntOff = SKP_SMULBB(k, LPC_order);
                for (i = 0; i < LPC_order; i++)
                {
                    pRes_new_Q15[pIntOff + i] = pRes_Q15[pConstIntOff + i] - (int)pCurrentCBStage.CB_NLSF_Q15[pCB_elementOff + i];
                }

                /* Update accumulated rate for stage 1 to the current */
                pRate_new_Q5[k] = pRate_Q5[input_index] + pCurrentCBStage.Rates_Q5[cb_index];

                /* Copy paths from previous matrix, starting with the best path */
                pConstIntOff = SKP_SMULBB(input_index, psNLSF_CB.nStages);
                pIntOff = SKP_SMULBB(k, psNLSF_CB.nStages);
                for (i = 0; i < s; i++)
                {
                    pPath_new[pIntOff + i] = pPath[pConstIntOff + i];
                }
                /* Write the current stage indices for the 'cur_survivors' to the best path matrix */
                pPath_new[pIntOff + s] = cb_index;
            }

            if (s < psNLSF_CB.nStages - 1)
            {
                /* Copy NLSF residual matrix for next stage */
                SKP_memcpy(pRes_Q15, 0, pRes_new_Q15, 0, SKP_SMULBB(cur_survivors, LPC_order));

                /* Copy rate vector for next stage */
                SKP_memcpy(pRate_Q5, 0, pRate_new_Q5, 0, cur_survivors);

                /* Copy best path matrix for next stage */
                SKP_memcpy(pPath, 0, pPath_new, 0, SKP_SMULBB(cur_survivors, psNLSF_CB.nStages));
            }

            prev_survivors = cur_survivors;
        }

        /* (Preliminary) index of the best survivor, later to be decoded */
        bestIndex = 0;

        /* #if( NLSF_MSVQ_FLUCTUATION_REDUCTION == 1 ) —— 保留分支 */
        /******************************/
        /* NLSF fluctuation reduction */
        /******************************/
        if (deactivate_fluc_red != 1)
        {

            /* Search among all survivors, now taking also weighted fluctuation errors into account */
            bestRateDist_Q20 = int.MaxValue;    /* SKP_int32_MAX */
            for (s = 0; s < cur_survivors; s++)
            {
                /* Decode survivor to compare with previous quantized NLSF vector */
                SKP_Silk_NLSF_MSVQ_decode(pNLSF_Q15, pNLSF_Q15Off, psNLSF_CB, pPath_new, SKP_SMULBB(s, psNLSF_CB.nStages), LPC_order);

                /* Compare decoded NLSF vector with the previously quantized vector */
                wsse_Q20 = 0;
                for (i = 0; i < LPC_order; i += 2)
                {
                    /* Compute weighted squared quantization error for index i */
                    se_Q15 = pNLSF_Q15[pNLSF_Q15Off + i] - pNLSF_q_Q15_prev[pNLSF_q_Q15_prevOff + i]; // range: [ -32767 : 32767 ]
                    wsse_Q20 = SKP_SMLAWB(wsse_Q20, SKP_SMULBB(se_Q15, se_Q15), pW_Q6[pW_Q6Off + i]);

                    /* Compute weighted squared quantization error for index i + 1 */
                    se_Q15 = pNLSF_Q15[pNLSF_Q15Off + i + 1] - pNLSF_q_Q15_prev[pNLSF_q_Q15_prevOff + i + 1]; // range: [ -32767 : 32767 ]
                    wsse_Q20 = SKP_SMLAWB(wsse_Q20, SKP_SMULBB(se_Q15, se_Q15), pW_Q6[pW_Q6Off + i + 1]);
                }

                /* Add the fluctuation reduction penalty to the rate distortion error */
                wsse_Q20 = SKP_ADD_POS_SAT32(pRateDist_Q18[s], SKP_SMULWB(wsse_Q20, NLSF_mu_fluc_red_Q16));

                /* Keep index of best survivor */
                if (wsse_Q20 < bestRateDist_Q20)
                {
                    bestRateDist_Q20 = wsse_Q20;
                    bestIndex = s;
                }
            }
        }

        /* Copy best path to output argument */
        SKP_memcpy(NLSFIndices, NLSFIndicesOff, pPath_new, SKP_SMULBB(bestIndex, psNLSF_CB.nStages), psNLSF_CB.nStages);

        /* Decode and stabilize the best survivor */
        SKP_Silk_NLSF_MSVQ_decode(pNLSF_Q15, pNLSF_Q15Off, psNLSF_CB, NLSFIndices, NLSFIndicesOff, LPC_order);

    }
}
