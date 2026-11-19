#nullable disable
// ***********************************************************************
// SKP_Silk_tables_type_offset.cs —— 移植自 SILK SDK v1.0.9（fixed-point）：src/SKP_Silk_tables_type_offset.c
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
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ***********************************************************************

// 本文件为纯常量表文件，逐值照抄。
using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tuning_parameters;

namespace AaR.Server.Silk;
/// <summary>SKP_Silk_tables_type_offset.c —— 帧类型/偏移熵编码 CDF 表。</summary>
internal static class SKP_Silk_tables_type_offset
{
/* C: const SKP_uint16 SKP_Silk_type_offset_CDF[ 5 ] = */
    internal static readonly ushort[] SKP_Silk_type_offset_CDF = new ushort[]
    {
         0,  37522,  41030,  44212,  65535
    };
    /* C: const SKP_int SKP_Silk_type_offset_CDF_offset = 2; */
    internal static readonly int SKP_Silk_type_offset_CDF_offset = 2;
/* C: const SKP_uint16 SKP_Silk_type_offset_joint_CDF[ 4 ][ 5 ] = */
    internal static readonly ushort[][] SKP_Silk_type_offset_joint_CDF = new ushort[][]
    {
new ushort[]{
         0,  57686,  61230,  62358,  65535
},
new ushort[]{
         0,  18346,  40067,  43659,  65535
},
new ushort[]{
         0,  22694,  24279,  35507,  65535
},
new ushort[]{
         0,   6067,   7215,  13010,  65535
}
    };
}
