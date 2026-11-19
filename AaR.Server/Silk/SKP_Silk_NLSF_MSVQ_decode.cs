#nullable disable
// ***********************************************************************
// SKP_Silk_NLSF_MSVQ_decode.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NLSF_MSVQ_decode.c
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

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
using static AaR.Server.Silk.SKP_Silk_NLSF_stabilize_impl;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_NLSF_MSVQ_decode.c —— NLSF vector decoder.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_NLSF_MSVQ_decode_impl
{
    /* NLSF vector decoder */
    public static void SKP_Silk_NLSF_MSVQ_decode(
        int[] pNLSF_Q15, int pNLSF_Q15Off,   /* O    Pointer to decoded output vector [LPC_ORDER x 1]    */
        SKP_Silk_NLSF_CB_struct psNLSF_CB,   /* I    Pointer to NLSF codebook struct                     */
        int[] NLSFIndices, int NLSFIndicesOff, /* I    Pointer to NLSF indices          [nStages x 1]      */
        int LPC_order                          /* I    LPC order used                                      */
    )
    {
        // PORT-NOTE: C 的指针 pCB_element（在码本数组内指向各向量首元素）→ 数组 + 偏移。
        int pCB_elementOff;
        int s;
        int i;

        /* Point to the first vector element */
        pCB_elementOff = SKP_MUL(NLSFIndices[NLSFIndicesOff + 0], LPC_order);

        /* Initialize with the codebook vector from stage 0 */
        for (i = 0; i < LPC_order; i++)
        {
            pNLSF_Q15[pNLSF_Q15Off + i] = (int)psNLSF_CB.CBStages[0].CB_NLSF_Q15[pCB_elementOff + i];
        }

        for (s = 1; s < psNLSF_CB.nStages; s++)
        {
            if (LPC_order == 16)
            {
                /* Point to the first vector element */
                pCB_elementOff = SKP_LSHIFT(NLSFIndices[NLSFIndicesOff + s], 4);

                /* Add the codebook vector from the current stage */
                pNLSF_Q15[pNLSF_Q15Off + 0] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 0];
                pNLSF_Q15[pNLSF_Q15Off + 1] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 1];
                pNLSF_Q15[pNLSF_Q15Off + 2] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 2];
                pNLSF_Q15[pNLSF_Q15Off + 3] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 3];
                pNLSF_Q15[pNLSF_Q15Off + 4] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 4];
                pNLSF_Q15[pNLSF_Q15Off + 5] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 5];
                pNLSF_Q15[pNLSF_Q15Off + 6] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 6];
                pNLSF_Q15[pNLSF_Q15Off + 7] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 7];
                pNLSF_Q15[pNLSF_Q15Off + 8] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 8];
                pNLSF_Q15[pNLSF_Q15Off + 9] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 9];
                pNLSF_Q15[pNLSF_Q15Off + 10] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 10];
                pNLSF_Q15[pNLSF_Q15Off + 11] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 11];
                pNLSF_Q15[pNLSF_Q15Off + 12] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 12];
                pNLSF_Q15[pNLSF_Q15Off + 13] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 13];
                pNLSF_Q15[pNLSF_Q15Off + 14] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 14];
                pNLSF_Q15[pNLSF_Q15Off + 15] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + 15];
            }
            else
            {
                /* Point to the first vector element */
                pCB_elementOff = SKP_SMULBB(NLSFIndices[NLSFIndicesOff + s], LPC_order);

                /* Add the codebook vector from the current stage */
                for (i = 0; i < LPC_order; i++)
                {
                    pNLSF_Q15[pNLSF_Q15Off + i] += psNLSF_CB.CBStages[s].CB_NLSF_Q15[pCB_elementOff + i];
                }
            }
        }

        /* NLSF stabilization */
        SKP_Silk_NLSF_stabilize(pNLSF_Q15, pNLSF_Q15Off, psNLSF_CB.NDeltaMin_Q15, 0, LPC_order);
    }
}
