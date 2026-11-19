#nullable disable
// ***********************************************************************
// SKP_Silk_LTP_scale_ctrl_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_LTP_scale_ctrl_FIX.c
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
using static AaR.Server.Silk.SKP_Silk_sigm_Q15_impl;
// SKP_Silk_LTPScales_table_Q14 表来自 SKP_Silk_tables_other.c（表簇代理产出，
// using static 裸名引用，与 C 调用点形态一致）。
using static AaR.Server.Silk.SKP_Silk_tables_other;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_LTP_scale_ctrl_FIX.c —— Calculation of LTP state scaling.
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE(CS0542): C# 禁止成员名与封闭类同名，而本文件主函数名 == 文件名。
// 为保持函数名（跨代理咬合点）不变，类名在 C 文件名基础上加 "_impl" 后缀。
internal static class SKP_Silk_LTP_scale_ctrl_FIX_impl
{
    private const int NB_THRESHOLDS = 11;

    /* Table containing trained thresholds for LTP scaling */
    internal static readonly short[] LTPScaleThresholds_Q15 = new short[NB_THRESHOLDS]
    {
        31129, 26214, 16384, 13107, 9830, 6554,
         4915,  3276,  2621,  2458,    0
    };

    public static void SKP_Silk_LTP_scale_ctrl_FIX(
        SKP_Silk_encoder_state_FIX psEnc,      /* I/O  encoder state FIX                           */
        SKP_Silk_encoder_control_FIX psEncCtrl /* I/O  encoder control FIX                         */
    )
    {
        int round_loss, frames_per_packet;
        int g_out_Q5, g_limit_Q15, thrld1_Q15, thrld2_Q15;

        /* 1st order high-pass filter */
        psEnc.HPLTPredCodGain_Q7 = SKP_max_int(psEncCtrl.LTPredCodGain_Q7 - psEnc.prevLTPredCodGain_Q7, 0)
            + SKP_RSHIFT_ROUND(psEnc.HPLTPredCodGain_Q7, 1);

        psEnc.prevLTPredCodGain_Q7 = psEncCtrl.LTPredCodGain_Q7;

        /* combine input and filtered input */
        g_out_Q5 = SKP_RSHIFT_ROUND(SKP_RSHIFT(psEncCtrl.LTPredCodGain_Q7, 1) + SKP_RSHIFT(psEnc.HPLTPredCodGain_Q7, 1), 3);
        g_limit_Q15 = SKP_Silk_sigm_Q15(g_out_Q5 - (3 << 5));

        /* Default is minimum scaling */
        psEncCtrl.sCmn.LTP_scaleIndex = 0;

        /* Round the loss measure to whole pct */
        round_loss = psEnc.sCmn.PacketLoss_perc;

        /* Only scale if first frame in packet 0% */
        if (psEnc.sCmn.nFramesInPayloadBuf == 0)
        {

            frames_per_packet = SKP_DIV32_16(psEnc.sCmn.PacketSize_ms, FRAME_LENGTH_MS);

            round_loss += frames_per_packet - 1;
            thrld1_Q15 = LTPScaleThresholds_Q15[SKP_min_int(round_loss, NB_THRESHOLDS - 1)];
            thrld2_Q15 = LTPScaleThresholds_Q15[SKP_min_int(round_loss + 1, NB_THRESHOLDS - 1)];

            if (g_limit_Q15 > thrld1_Q15)
            {
                /* Maximum scaling */
                psEncCtrl.sCmn.LTP_scaleIndex = 2;
            }
            else if (g_limit_Q15 > thrld2_Q15)
            {
                /* Medium scaling */
                psEncCtrl.sCmn.LTP_scaleIndex = 1;
            }
        }
        psEncCtrl.LTP_scale_Q14 = SKP_Silk_LTPScales_table_Q14[psEncCtrl.sCmn.LTP_scaleIndex];
    }
}
