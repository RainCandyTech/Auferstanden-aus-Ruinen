#nullable disable
// ***********************************************************************
// SKP_Silk_NLSF_VQ_weights_laroia.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_NLSF_VQ_weights_laroia.c
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

/*
R. Laroia, N. Phamdo and N. Farvardin, "Robust and Efficient Quantization of Speech LSP
Parameters Using Structured Vector Quantization", Proc. IEEE Int. Conf. Acoust., Speech,
Signal Processing, pp. 641-644, 1991.
*/

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_NLSF_VQ_weights_laroia.c —— Laroia low complexity NLSF weights.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_NLSF_VQ_weights_laroia_impl
{
    private const int Q_OUT = 6;
    private const int MIN_NDELTA = 3;

    /* Laroia low complexity NLSF weights */
    public static void SKP_Silk_NLSF_VQ_weights_laroia(
        int[] pNLSFW_Q6, int pNLSFW_Q6Off,    /* O:    Pointer to input vector weights           [D x 1]     */
        int[] pNLSF_Q15, int pNLSF_Q15Off,   /* I:    Pointer to input vector                   [D x 1]     */
        int D                                 /* I:    Input vector dimension (even)                         */
    )
    {
        int k;
        int tmp1_int, tmp2_int;

        /* Check that we are guaranteed to end up within the required range */

        /* First value */
        tmp1_int = SKP_max_int(pNLSF_Q15[pNLSF_Q15Off + 0], MIN_NDELTA);
        tmp1_int = SKP_DIV32_16(1 << (15 + Q_OUT), tmp1_int);
        tmp2_int = SKP_max_int(pNLSF_Q15[pNLSF_Q15Off + 1] - pNLSF_Q15[pNLSF_Q15Off + 0], MIN_NDELTA);
        tmp2_int = SKP_DIV32_16(1 << (15 + Q_OUT), tmp2_int);
        pNLSFW_Q6[pNLSFW_Q6Off + 0] = (int)SKP_min_int(tmp1_int + tmp2_int, short.MaxValue /* SKP_int16_MAX */);

        /* Main loop */
        for (k = 1; k < D - 1; k += 2)
        {
            tmp1_int = SKP_max_int(pNLSF_Q15[pNLSF_Q15Off + k + 1] - pNLSF_Q15[pNLSF_Q15Off + k], MIN_NDELTA);
            tmp1_int = SKP_DIV32_16(1 << (15 + Q_OUT), tmp1_int);
            pNLSFW_Q6[pNLSFW_Q6Off + k] = (int)SKP_min_int(tmp1_int + tmp2_int, short.MaxValue /* SKP_int16_MAX */);

            tmp2_int = SKP_max_int(pNLSF_Q15[pNLSF_Q15Off + k + 2] - pNLSF_Q15[pNLSF_Q15Off + k + 1], MIN_NDELTA);
            tmp2_int = SKP_DIV32_16(1 << (15 + Q_OUT), tmp2_int);
            pNLSFW_Q6[pNLSFW_Q6Off + k + 1] = (int)SKP_min_int(tmp1_int + tmp2_int, short.MaxValue /* SKP_int16_MAX */);
        }

        /* Last value */
        tmp1_int = SKP_max_int((1 << 15) - pNLSF_Q15[pNLSF_Q15Off + D - 1], MIN_NDELTA);
        tmp1_int = SKP_DIV32_16(1 << (15 + Q_OUT), tmp1_int);
        pNLSFW_Q6[pNLSFW_Q6Off + D - 1] = (int)SKP_min_int(tmp1_int + tmp2_int, short.MaxValue /* SKP_int16_MAX */);
    }
}
