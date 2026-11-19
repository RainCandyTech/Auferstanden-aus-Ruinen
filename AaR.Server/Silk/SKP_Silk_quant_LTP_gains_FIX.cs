#nullable disable
// ***********************************************************************
// SKP_Silk_quant_LTP_gains_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_quant_LTP_gains_FIX.c
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
using static AaR.Server.Silk.SKP_Silk_VQ_nearest_neighbor_FIX;
// 下列码本表来自 SKP_Silk_tables_LTP.c（表簇代理产出，using static 裸名引用）：
//   SKP_Silk_LTP_gain_BITS_Q6_ptrs：C 为 const SKP_int16* const[3]（三段一维 short 码长表）→ short[][]；
//   SKP_Silk_LTP_vq_ptrs_Q14：      C 为 const SKP_int16* const[3]（三段展平的 [n][5] 码本）→ short[][]；
//   SKP_Silk_LTP_vq_sizes：         C 为 const SKP_int[3] → int[]；
//   SKP_Silk_LTP_gain_middle_avg_RD_Q14：C 为 const SKP_int32 标量 → int。
using static AaR.Server.Silk.SKP_Silk_tables_LTP;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_quant_LTP_gains_FIX.c —— LTP tap quantizer.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_quant_LTP_gains_FIX_impl
{
    // PORT-NOTE: C 形参 SKP_int *periodicity_index 在循环内写入、循环后读取；
    // 运行期首帧（k=0）必然先写后读，但 C# 编译器无法静态证明（CS0269），
    // 故按 §3.1 以 ref 承载"运行期先写后读"语义。
    public static void SKP_Silk_quant_LTP_gains_FIX(
        short[] B_Q14, int B_Q14Off,           /* I/O  (un)quantized LTP gains     */
        int[] cbk_index, int cbk_indexOff,     /* O    Codebook Index              */
        ref int periodicity_index,             /* O    Periodicity Index           */
        int[] W_Q18, int W_Q18Off,             /* I    Error Weights in Q18        */
        int mu_Q8,                              /* I    Mu value (R/D tradeoff)     */
        int lowComplexity                      /* I    Flag for low complexity     */
    )
    {
        int j, k;
        int[] temp_idx = new int[NB_SUBFR];
        int cbk_size;
        // PORT-NOTE: C 的指针 cl_ptr/cbk_ptr_Q14（指向 tables_LTP 中各段码本首地址）→
        // short[] 数组引用（段内偏移恒 0）；b_Q14_ptr/W_Q18_ptr → 数组偏移。
        short[] cl_ptr;
        short[] cbk_ptr_Q14;
        int b_Q14_ptrOff;
        int W_Q18_ptrOff;
        int rate_dist_subfr, rate_dist, min_rate_dist;



        /***************************************************/
        /* iterate over different codebooks with different */
        /* rates/distortions, and choose best */
        /***************************************************/
        min_rate_dist = int.MaxValue;    /* SKP_int32_MAX */
        for (k = 0; k < 3; k++)
        {
            cl_ptr = SKP_Silk_LTP_gain_BITS_Q6_ptrs[k];
            cbk_ptr_Q14 = SKP_Silk_LTP_vq_ptrs_Q14[k];
            cbk_size = SKP_Silk_LTP_vq_sizes[k];

            /* Setup pointer to first subframe */
            W_Q18_ptrOff = W_Q18Off;
            b_Q14_ptrOff = B_Q14Off;

            rate_dist = 0;
            for (j = 0; j < NB_SUBFR; j++)
            {

                SKP_Silk_VQ_WMat_EC_FIX(
                    ref temp_idx[j],             /* O    index of best codebook vector                           */
                    out rate_dist_subfr,         /* O    best weighted quantization error + mu * rate            */
                    B_Q14, b_Q14_ptrOff,         /* I    input vector to be quantized                            */
                    W_Q18, W_Q18_ptrOff,         /* I    weighting matrix                                        */
                    cbk_ptr_Q14, 0,              /* I    codebook                                                */
                    cl_ptr, 0,                   /* I    code length for each codebook vector                    */
                    mu_Q8,                       /* I    tradeoff between weighted error and rate                */
                    cbk_size                     /* I    number of vectors in codebook                           */
                );

                rate_dist = SKP_ADD_POS_SAT32(rate_dist, rate_dist_subfr);

                b_Q14_ptrOff += LTP_ORDER;
                W_Q18_ptrOff += LTP_ORDER * LTP_ORDER;
            }

            /* Avoid never finding a codebook */
            rate_dist = SKP_min(int.MaxValue - 1 /* SKP_int32_MAX - 1 */, rate_dist);

            if (rate_dist < min_rate_dist)
            {
                min_rate_dist = rate_dist;
                SKP_memcpy(cbk_index, cbk_indexOff, temp_idx, 0, NB_SUBFR);
                periodicity_index = k;
            }

            /* Break early in low-complexity mode if rate distortion is below threshold */
            if (lowComplexity != 0 && (rate_dist < SKP_Silk_LTP_gain_middle_avg_RD_Q14))
            {
                break;
            }
        }

        cbk_ptr_Q14 = SKP_Silk_LTP_vq_ptrs_Q14[periodicity_index];
        for (j = 0; j < NB_SUBFR; j++)
        {
            for (k = 0; k < LTP_ORDER; k++)
            {
                B_Q14[B_Q14Off + j * LTP_ORDER + k] = cbk_ptr_Q14[SKP_MLA(k, cbk_index[cbk_indexOff + j], LTP_ORDER)];
            }
        }
    }
}
