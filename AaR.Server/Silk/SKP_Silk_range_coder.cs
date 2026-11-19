#nullable disable
// ***********************************************************************
// SKP_Silk_range_coder.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_range_coder.c
//
// Copyright (c) 2006-2012, Skype Limited. All rights reserved.（BSD 风格许可，
// 保留原始版权与免责声明。）
//
// C# 移植说明：
//   - base_Q32/range_Q16 为 C# uint（C SKP_uint32）；所有 uint 运算经
//     SKP_MUL_uint/SKP_LSHIFT_uint/SKP_LSHIFT_ovflw/SKP_RSHIFT_uint 宏方法
//     （int 往返，二补回绕）保持与 C 逐位一致，调用点保持宏调用形态。
//   - CDF 参数为（ushort[] prob, int probOff）；调用点可传表中行（offset 0）
//     或表内偏移（shell coder 用 SKP_Silk_shell_code_table_offsets）。
//   - data 输出为标量指针，仅解引用 → out int；range_decoder_multi 以
//     out data[k] 传数组元素。
// ***********************************************************************

using System.Runtime.CompilerServices;
using static AaR.Server.Silk.SKP_Silk_macros;
using static AaR.Server.Silk.SKP_Silk_define;

namespace AaR.Server.Silk;

internal static class SKP_Silk_range_coder
{
    /* Range encoder for one symbol */
    public static void SKP_Silk_range_encoder(
        SKP_Silk_range_coder_state psRC,          /* I/O  compressor data structure                   */
        int data,                                 /* I    uncompressed data                           */
        ushort[] prob, int probOff               /* I    cumulative density functions                */
    )
    {
        uint low_Q16, high_Q16;
        uint base_tmp, range_Q32;

        /* Copy structure data */
        uint base_Q32 = psRC.base_Q32;
        uint range_Q16 = psRC.range_Q16;
        int bufferIx = psRC.bufferIx;
        byte[] buffer = psRC.buffer;              /* C: buffer = psRC->buffer（基址，无偏移） */

        if (psRC.error != 0)
        {
            return;
        }

        /* Update interval */
        low_Q16 = prob[probOff + data];
        high_Q16 = prob[probOff + data + 1];
        base_tmp = base_Q32; /* save current base, to test for carry */
        base_Q32 += (uint)SKP_MUL_uint((int)range_Q16, (int)low_Q16);
        range_Q32 = (uint)SKP_MUL_uint((int)range_Q16, (int)(high_Q16 - low_Q16));

        /* Check for carry */
        if (base_Q32 < base_tmp)
        {
            /* Propagate carry in buffer */
            int bufferIx_tmp = bufferIx;
            while ((++buffer[--bufferIx_tmp]) == 0) { }
        }

        /* Check normalization */
        if ((range_Q32 & 0xFF000000u) != 0)
        {
            /* No normalization */
            range_Q16 = (uint)SKP_RSHIFT_uint((int)range_Q32, 16);
        }
        else
        {
            if ((range_Q32 & 0xFFFF0000u) != 0)
            {
                /* Normalization of 8 bits shift */
                range_Q16 = (uint)SKP_RSHIFT_uint((int)range_Q32, 8);
            }
            else
            {
                /* Normalization of 16 bits shift */
                range_Q16 = range_Q32;
                /* Make sure not to write beyond buffer */
                if (bufferIx >= psRC.bufferLength)
                {
                    psRC.error = RANGE_CODER_WRITE_BEYOND_BUFFER;
                    return;
                }
                /* Write one byte to buffer */
                buffer[bufferIx++] = (byte)SKP_RSHIFT_uint((int)base_Q32, 24);
                base_Q32 = (uint)SKP_LSHIFT_ovflw((int)base_Q32, 8);
            }
            /* Make sure not to write beyond buffer */
            if (bufferIx >= psRC.bufferLength)
            {
                psRC.error = RANGE_CODER_WRITE_BEYOND_BUFFER;
                return;
            }
            /* Write one byte to buffer */
            buffer[bufferIx++] = (byte)SKP_RSHIFT_uint((int)base_Q32, 24);
            base_Q32 = (uint)SKP_LSHIFT_ovflw((int)base_Q32, 8);
        }

        /* Copy structure data back */
        psRC.base_Q32 = base_Q32;
        psRC.range_Q16 = range_Q16;
        psRC.bufferIx = bufferIx;
    }

    /* Range encoder for multiple symbols */
    public static void SKP_Silk_range_encoder_multi(
        SKP_Silk_range_coder_state psRC,          /* I/O  compressor data structure                   */
        int[] data, int dataOff,                  /* I    uncompressed data    [nSymbols]             */
        ushort[][] prob, int probOff,             /* I    cumulative density functions                */
        int nSymbols                              /* I    number of data symbols                      */
    )
    {
        // PORT-NOTE: C 的 prob 为 cdf 指针数组；C# 表簇产出 ushort[][]（每行已含起点偏移），
        // 故每行以 offset 0 传入 SKP_Silk_range_encoder。
        for (int k = 0; k < nSymbols; k++)
        {
            SKP_Silk_range_encoder(psRC, data[dataOff + k], prob[probOff + k], 0);
        }
    }

    /* Range decoder for one symbol */
    public static void SKP_Silk_range_decoder(
        out int data,                             /* O    uncompressed data                           */
        SKP_Silk_range_coder_state psRC,         /* I/O  compressor data structure                   */
        ushort[] prob, int probOff,              /* I    cumulative density function                 */
        int probIx                                /* I    initial (middle) entry of cdf               */
    )
    {
        uint low_Q16, high_Q16;
        uint base_tmp, range_Q32;

        /* Copy structure data */
        uint base_Q32 = psRC.base_Q32;
        uint range_Q16 = psRC.range_Q16;
        int bufferIx = psRC.bufferIx;
        byte[] buffer = psRC.buffer;
        int bufferOff = 4;                        /* PORT-NOTE: C 为 buffer = &psRC->buffer[ 4 ] */

        if (psRC.error != 0)
        {
            /* Set output to zero */
            data = 0;
            return;
        }

        high_Q16 = prob[probOff + probIx];
        base_tmp = (uint)SKP_MUL_uint((int)range_Q16, (int)high_Q16);
        if (base_tmp > base_Q32)
        {
            while (true)
            {
                low_Q16 = prob[probOff + --probIx];
                base_tmp = (uint)SKP_MUL_uint((int)range_Q16, (int)low_Q16);
                if (base_tmp <= base_Q32)
                {
                    break;
                }
                high_Q16 = low_Q16;
                /* Test for out of range */
                if (high_Q16 == 0)
                {
                    psRC.error = RANGE_CODER_CDF_OUT_OF_RANGE;
                    /* Set output to zero */
                    data = 0;
                    return;
                }
            }
        }
        else
        {
            while (true)
            {
                low_Q16 = high_Q16;
                high_Q16 = prob[probOff + ++probIx];
                base_tmp = (uint)SKP_MUL_uint((int)range_Q16, (int)high_Q16);
                if (base_tmp > base_Q32)
                {
                    probIx--;
                    break;
                }
                /* Test for out of range */
                if (high_Q16 == 0xFFFF)
                {
                    psRC.error = RANGE_CODER_CDF_OUT_OF_RANGE;
                    /* Set output to zero */
                    data = 0;
                    return;
                }
            }
        }
        data = probIx;
        base_Q32 -= (uint)SKP_MUL_uint((int)range_Q16, (int)low_Q16);
        range_Q32 = (uint)SKP_MUL_uint((int)range_Q16, (int)(high_Q16 - low_Q16));

        /* Check normalization */
        if ((range_Q32 & 0xFF000000u) != 0)
        {
            /* No normalization */
            range_Q16 = (uint)SKP_RSHIFT_uint((int)range_Q32, 16);
        }
        else
        {
            if ((range_Q32 & 0xFFFF0000u) != 0)
            {
                /* Normalization of 8 bits shift */
                range_Q16 = (uint)SKP_RSHIFT_uint((int)range_Q32, 8);
                /* Check for errors */
                if (SKP_RSHIFT_uint((int)base_Q32, 24) != 0)
                {
                    psRC.error = RANGE_CODER_NORMALIZATION_FAILED;
                    /* Set output to zero */
                    data = 0;
                    return;
                }
            }
            else
            {
                /* Normalization of 16 bits shift */
                range_Q16 = range_Q32;
                /* Check for errors */
                // PORT-NOTE: C 的 SKP_RSHIFT 作用于 SKP_uint32（逻辑右移 16）；此处对 (int)
                // 做算术右移——两者的"非零"判定逐位等价（bit31 置位时两者均非零）。
                if (SKP_RSHIFT((int)base_Q32, 16) != 0)
                {
                    psRC.error = RANGE_CODER_NORMALIZATION_FAILED;
                    /* Set output to zero */
                    data = 0;
                    return;
                }
                /* Update base */
                base_Q32 = (uint)SKP_LSHIFT_uint((int)base_Q32, 8);
                /* Make sure not to read beyond buffer */
                if (bufferIx < psRC.bufferLength)
                {
                    /* Read one byte from buffer */
                    base_Q32 |= buffer[bufferOff + bufferIx++];
                }
            }
            /* Update base */
            base_Q32 = (uint)SKP_LSHIFT_uint((int)base_Q32, 8);
            /* Make sure not to read beyond buffer */
            if (bufferIx < psRC.bufferLength)
            {
                /* Read one byte from buffer */
                base_Q32 |= buffer[bufferOff + bufferIx++];
            }
        }

        /* Check for zero interval length */
        if (range_Q16 == 0)
        {
            psRC.error = RANGE_CODER_ZERO_INTERVAL_WIDTH;
            /* Set output to zero */
            data = 0;
            return;
        }

        /* Copy structure data back */
        psRC.base_Q32 = base_Q32;
        psRC.range_Q16 = range_Q16;
        psRC.bufferIx = bufferIx;
    }

    /* Range decoder for multiple symbols */
    public static void SKP_Silk_range_decoder_multi(
        int[] data, int dataOff,                  /* O    uncompressed data                [nSymbols] */
        SKP_Silk_range_coder_state psRC,         /* I/O  compressor data structure                   */
        ushort[][] prob, int probOff,            /* I    cumulative density functions                */
        int[] probStartIx, int probStartIxOff,    /* I    initial (middle) entries of cdfs [nSymbols] */
        int nSymbols                              /* I    number of data symbols                      */
    )
    {
        // PORT-NOTE: 同 encoder_multi，prob 行内 offset 0。
        for (int k = 0; k < nSymbols; k++)
        {
            SKP_Silk_range_decoder(out data[dataOff + k], psRC, prob[probOff + k], 0,
                probStartIx[probStartIxOff + k]);
        }
    }

    /* Initialize range encoder */
    public static void SKP_Silk_range_enc_init(
        SKP_Silk_range_coder_state psRC           /* O    compressor data structure                   */
    )
    {
        /* Initialize structure */
        psRC.bufferLength = MAX_ARITHM_BYTES;
        psRC.range_Q16 = 0x0000FFFF;
        psRC.bufferIx = 0;
        psRC.base_Q32 = 0;
        psRC.error = 0;
    }

    /* Initialize range decoder */
    public static void SKP_Silk_range_dec_init(
        SKP_Silk_range_coder_state psRC,          /* O    compressor data structure                   */
        byte[] buffer, int bufferOff,             /* I    buffer for compressed data [bufferLength]   */
        int bufferLength                          /* I    buffer length (in bytes)                    */
    )
    {
        /* check input */
        if ((bufferLength > MAX_ARITHM_BYTES) || (bufferLength < 0))
        {
            psRC.error = RANGE_CODER_DEC_PAYLOAD_TOO_LONG;
            return;
        }
        /* Initialize structure */
        /* Copy to internal buffer */
        SKP_memcpy(psRC.buffer, 0, buffer, bufferOff, bufferLength);
        psRC.bufferLength = bufferLength;
        psRC.bufferIx = 0;
        psRC.base_Q32 =
            (uint)SKP_LSHIFT_uint(buffer[bufferOff + 0], 24) |
            (uint)SKP_LSHIFT_uint(buffer[bufferOff + 1], 16) |
            (uint)SKP_LSHIFT_uint(buffer[bufferOff + 2], 8) |
            buffer[bufferOff + 3];
        psRC.range_Q16 = 0x0000FFFF;
        psRC.error = 0;
    }

    /* Determine length of bitstream */
    public static int SKP_Silk_range_coder_get_length(             /* O    returns number of BITS in stream */
        SKP_Silk_range_coder_state psRC,                           /* I    compressed data structure        */
        out int nBytes                                             /* O    number of BYTES in stream        */
    )
    {
        int nBits;

        /* Number of bits in stream */
        // PORT-NOTE: C 为 CLZ32( psRC->range_Q16 - 1 )，uint 减法回绕后按位重解释为 int。
        nBits = SKP_LSHIFT(psRC.bufferIx, 3) + SKP_Silk_CLZ32((int)(psRC.range_Q16 - 1u)) - 14;

        nBytes = SKP_RSHIFT(nBits + 7, 3);

        /* Return number of bits in bitstream */
        return nBits;
    }

    /* Write shortest uniquely decodable stream to buffer, and determine its length */
    public static void SKP_Silk_range_enc_wrap_up(
        SKP_Silk_range_coder_state psRC           /* I/O  compressed data structure                   */
    )
    {
        int bufferIx_tmp, bits_to_store, bits_in_stream, nBytes, mask;
        uint base_Q24;

        /* Lower limit of interval, shifted 8 bits to the right */
        base_Q24 = (uint)SKP_RSHIFT_uint((int)psRC.base_Q32, 8);

        bits_in_stream = SKP_Silk_range_coder_get_length(psRC, out nBytes);

        /* Number of additional bits (1..9) required to be stored to stream */
        bits_to_store = bits_in_stream - SKP_LSHIFT(psRC.bufferIx, 3);
        /* Round up to required resolution */
        base_Q24 += (uint)SKP_RSHIFT_uint(0x00800000, bits_to_store - 1);
        base_Q24 &= (uint)SKP_LSHIFT_ovflw(unchecked((int)0xFFFFFFFF), 24 - bits_to_store);

        /* Check for carry */
        if ((base_Q24 & 0x01000000u) != 0)
        {
            /* Propagate carry in buffer */
            bufferIx_tmp = psRC.bufferIx;
            while ((++psRC.buffer[--bufferIx_tmp]) == 0) { }
        }

        /* Store to stream, making sure not to write beyond buffer */
        if (psRC.bufferIx < psRC.bufferLength)
        {
            psRC.buffer[psRC.bufferIx++] = (byte)SKP_RSHIFT_uint((int)base_Q24, 16);
            if (bits_to_store > 8)
            {
                if (psRC.bufferIx < psRC.bufferLength)
                {
                    psRC.buffer[psRC.bufferIx++] = (byte)SKP_RSHIFT_uint((int)base_Q24, 8);
                }
            }
        }

        /* Fill up any remaining bits in the last byte with 1s */
        if ((bits_in_stream & 7) != 0)
        {
            mask = SKP_RSHIFT(0xFF, bits_in_stream & 7);
            if (nBytes - 1 < psRC.bufferLength)
            {
                // PORT-NOTE: C 为 psRC->buffer[ nBytes - 1 ] |= mask（int 对 uint8 截断）；
                // C# 复合赋值不允许 int 右值，需显式 (byte)（mask ≤ 0x7F，无回绕差异）。
                psRC.buffer[nBytes - 1] |= (byte)mask;
            }
        }
    }

    /* Check that any remaining bits in the last byte are set to 1 */
    public static void SKP_Silk_range_coder_check_after_decoding(
        SKP_Silk_range_coder_state psRC           /* I/O  compressed data structure                   */
    )
    {
        int bits_in_stream, nBytes, mask;

        bits_in_stream = SKP_Silk_range_coder_get_length(psRC, out nBytes);

        /* Make sure not to read beyond buffer */
        if (nBytes - 1 >= psRC.bufferLength)
        {
            psRC.error = RANGE_CODER_DECODER_CHECK_FAILED;
            return;
        }

        /* Test any remaining bits in last byte */
        if ((bits_in_stream & 7) != 0)
        {
            mask = SKP_RSHIFT(0xFF, bits_in_stream & 7);
            if ((psRC.buffer[nBytes - 1] & mask) != mask)
            {
                psRC.error = RANGE_CODER_DECODER_CHECK_FAILED;
                return;
            }
        }
    }
}
