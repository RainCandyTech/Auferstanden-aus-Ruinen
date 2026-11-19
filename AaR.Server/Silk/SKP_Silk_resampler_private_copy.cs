#nullable disable
// ***********************************************************************
// SKP_Silk_resampler_private_copy.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：
//   src/SKP_Silk_resampler_private_copy.c
//
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
// NO EXPRESS OR IMPLIED LICENSES TO ANY PARTY'S PATENT RIGHTS ARE GRANTED
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

//																		*
// File Name:	SKP_Silk_resampler_private_copy.c                     *
//																		*
// Description: Copy.                                                   *
//                                                                      *
// Copyright 2010 (c), Skype Limited                                    *
// All rights reserved.													*
//                                                                      */

// PORT-NOTE(类名 CS0542): 同本簇其他文件——主函数名与 .c 文件名相同，函数名保持
// C 原名，类名加 `_impl` 后缀。
// PORT-NOTE(void* 首参): C 首参为 void* SS（未使用）；按委托 SilkResamplerStateFn
// 首参类型移植为强类型状态对象（§3 规则 3/父指令），函数体不使用它。

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;
namespace AaR.Server.Silk;

internal static class SKP_Silk_resampler_private_copy_impl
{
    /* Copy */
    public static void SKP_Silk_resampler_private_copy(
        SKP_Silk_resampler_state_struct S,  /* I/O: Resampler state (unused)	*/
        short[] @out, int outOff,           /* O:	Output signal 			*/
        short[] @in, int inOff,             /* I:	Input signal				*/
        int inLen                           /* I:	Number of input samples	*/
    )
    {
        SKP_memcpy( @out, outOff, @in, inOff, inLen );
    }
}
