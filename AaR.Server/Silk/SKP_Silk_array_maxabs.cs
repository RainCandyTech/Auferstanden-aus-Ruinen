#nullable disable
// ***********************************************************************
// SKP_Silk_array_maxabs.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_array_maxabs.c
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

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_int16_array_maxabs.c —— Function that returns the maximum
/// absolut value of the input vector.（Copyright 2006 (c), Skype Limited，Date: 060221）
/// </summary>
internal static class SKP_Silk_array_maxabs
{
    /* Function that returns the maximum absolut value of the input vector */
    public static short SKP_Silk_int16_array_maxabs(    /* O    Maximum absolute value, max: 2^15-1   */
        short[] vec, int vecOff,                        /* I    Input vector  [len]                   */
        int len                                         /* I    Length of input vector                */
    )
    {
        int max = 0, i, lvl = 0, ind;
        if (len == 0) return 0;

        ind = len - 1;
        max = SKP_SMULBB(vec[vecOff + ind], vec[vecOff + ind]);
        for (i = len - 2; i >= 0; i--)
        {
            lvl = SKP_SMULBB(vec[vecOff + i], vec[vecOff + i]);
            if (lvl > max)
            {
                max = lvl;
                ind = i;
            }
        }

        /* Do not return 32768, as it will not fit in an int16 so may lead to problems later on */
        if (max >= 1073676289)
        { // (2^15-1)^2 = 1073676289
            return short.MaxValue;    /* SKP_int16_MAX */
        }
        else
        {
            if (vec[vecOff + ind] < 0)
            {
                return (short)-vec[vecOff + ind];
            }
            else
            {
                return vec[vecOff + ind];
            }
        }
    }
}
