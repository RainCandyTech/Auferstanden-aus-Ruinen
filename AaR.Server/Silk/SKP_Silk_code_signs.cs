#nullable disable
// ***********************************************************************
// SKP_Silk_code_signs.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_code_signs.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - SKP_enc_map/SKP_dec_map 为 C 宏（移位避免 if 语句），展开为内联静态方法。
//   - 引用 SKP_Silk_sign_CDF（表簇 SKP_Silk_tables_sign.cs 产出，裸名）。
//   - 局部 cdf[3] 为无符号 ushort 数组，传给 range coder 时 offset 为 0。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;
using static AaR.Server.Silk.SKP_Silk_tables_sign;      // SKP_Silk_sign_CDF（表簇产出，裸名）

namespace AaR.Server.Silk;

internal static class SKP_Silk_code_signs
{
    // PORT-NOTE: C 文件内宏定义（/* shifting avoids if-statement */）：
    //   //#define SKP_enc_map(a)  ((a) > 0 ? 1 : 0)
    //   //#define SKP_dec_map(a)  ((a) > 0 ? 1 : -1)
    //   #define SKP_enc_map(a)    ( SKP_RSHIFT( (a), 15 ) + 1 )
    //   #define SKP_dec_map(a)    ( SKP_LSHIFT( (a),  1 ) - 1 )
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SKP_enc_map(int a) { return SKP_RSHIFT(a, 15) + 1; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SKP_dec_map(int a) { return SKP_LSHIFT(a, 1) - 1; }

    /* Encodes signs of excitation */
    public static void SKP_Silk_encode_signs(
        SKP_Silk_range_coder_state sRC,           /* I/O  Range coder state                       */
        sbyte[] q, int qOff,                      /* I    Pulse signal                            */
        int length,                               /* I    Length of input                         */
        int sigtype,                              /* I    Signal type                             */
        int QuantOffsetType,                      /* I    Quantization offset type                */
        int RateLevelIndex                        /* I    Rate level index                        */
    )
    {
        int i;
        int inData;
        ushort[] cdf = new ushort[3];

        i = SKP_SMULBB(N_RATE_LEVELS - 1, SKP_LSHIFT(sigtype, 1) + QuantOffsetType) + RateLevelIndex;
        cdf[0] = 0;
        cdf[1] = SKP_Silk_sign_CDF[i];
        cdf[2] = 65535;

        for (i = 0; i < length; i++)
        {
            if (q[qOff + i] != 0)
            {
                inData = SKP_enc_map(q[qOff + i]); /* - = 0, + = 1 */
                SKP_Silk_range_coder.SKP_Silk_range_encoder(sRC, inData, cdf, 0);
            }
        }
    }

    /* Decodes signs of excitation */
    public static void SKP_Silk_decode_signs(
        SKP_Silk_range_coder_state sRC,           /* I/O  Range coder state                           */
        int[] q, int qOff,                        /* I/O  pulse signal                                */
        int length,                               /* I    length of output                            */
        int sigtype,                              /* I    Signal type                                 */
        int QuantOffsetType,                      /* I    Quantization offset type                    */
        int RateLevelIndex                        /* I    Rate Level Index                            */
    )
    {
        int i;
        int data;
        ushort[] cdf = new ushort[3];

        i = SKP_SMULBB(N_RATE_LEVELS - 1, SKP_LSHIFT(sigtype, 1) + QuantOffsetType) + RateLevelIndex;
        cdf[0] = 0;
        cdf[1] = SKP_Silk_sign_CDF[i];
        cdf[2] = 65535;

        for (i = 0; i < length; i++)
        {
            if (q[qOff + i] > 0)
            {
                SKP_Silk_range_coder.SKP_Silk_range_decoder(out data, sRC, cdf, 0, 1);
                /* attach sign */
                /* implementation with shift, subtraction, multiplication */
                q[qOff + i] *= SKP_dec_map(data);
            }
        }
    }
}
