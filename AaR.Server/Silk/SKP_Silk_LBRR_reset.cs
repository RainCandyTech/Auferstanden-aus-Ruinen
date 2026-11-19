#nullable disable
// ***********************************************************************
// SKP_Silk_LBRR_reset.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_LBRR_reset.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - C 源仅把 LBRR_buffer[i].usage 置为 SKP_SILK_NO_LBRR（并无整结构
//     SKP_memset），逐行照抄；数组元素为类引用，按字段赋值即可。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;

namespace AaR.Server.Silk;

// PORT-NOTE(CS0542): C 函数名与所在文件同名，按全工程统一约定：类名加 "_impl"
// 后缀、方法名保持 C 原名；调用方以 `using static ...SKP_Silk_LBRR_reset_impl;`
// + 裸名 SKP_Silk_LBRR_reset(...) 调用。
internal static class SKP_Silk_LBRR_reset_impl
{
    /* Resets LBRR buffer, used if packet size changes */
    public static void SKP_Silk_LBRR_reset(
        SKP_Silk_encoder_state psEncC              /* I/O  state                                       */
    )
    {
        int i;

        for (i = 0; i < MAX_LBRR_DELAY; i++)
        {
            psEncC.LBRR_buffer[i].usage = SKP_SILK_NO_LBRR;
        }
    }
}
