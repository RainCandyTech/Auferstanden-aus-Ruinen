// ***********************************************************************
// SilkV3Stream.cs —— AaR 托管 SILK v3 编码门面。
//
// 底层为 SILK SDK 1.0.9 定点参考实现的纯 C# 位等移植（本目录 SKP_Silk_*.cs，
// BSD-2，Skype Limited 版权保留），输出与 kn007 silk_v3_encoder 逐字节一致
// （112 项参数矩阵 + 现网生产二进制 4/4 实测，见 AaR-Test 报告）。
//
// 容器行为与原生 CLI 完全一致：
//   Tencent 模式（默认）: 0x02 + "#!SILK_V3" + [小端 u16 帧长][帧]…，无结尾终止符；
//   标准模式:            "#!SILK_V3" + [小端 u16 帧长][帧]… + 结尾 0xFFFF。
//   不足一个 20ms 帧的 PCM 尾部按原 CLI 行为丢弃。
// ***********************************************************************

namespace AaR.Server.Silk;

/// <summary>SILK 编码参数（默认值 = silk_v3_encoder CLI 默认 = QQ 语音生产参数）。</summary>
public sealed class SilkV3EncoderOptions
{
    /// <summary>API 采样率（Hz）：8000/12000/16000/24000/32000/44100/48000。</summary>
    public int ApiSampleRateHz { get; set; } = 24000;

    /// <summary>最大内部采样率（Hz）；0 表示取 min(ApiSampleRateHz, 24000)。</summary>
    public int MaxInternalSampleRateHz { get; set; } = 0;

    /// <summary>包长（ms）：20/40/60/80/100。</summary>
    public int PacketSizeMs { get; set; } = 20;

    /// <summary>目标码率（bps）。</summary>
    public int BitrateBps { get; set; } = 25000;

    /// <summary>上行丢包率估计（0-100）。</summary>
    public int PacketLossPercent { get; set; } = 0;

    /// <summary>复杂度：0 低 / 1 中 / 2 高。</summary>
    public int Complexity { get; set; } = 2;

    /// <summary>启用 inband FEC。</summary>
    public bool UseInBandFec { get; set; }

    /// <summary>启用 DTX。</summary>
    public bool UseDtx { get; set; }

    /// <summary>腾讯 QQ/微信兼容容器（0x02 头 + 无终止符）。</summary>
    public bool Tencent { get; set; } = true;
}

/// <summary>托管 SILK v3 流编码器（与原生 silk_v3_encoder 位等，无外部二进制依赖）。</summary>
public static class SilkV3Stream
{
    private const int MaxBytesPerFrame = 250;        // 峰值 100 kbps
    private const int MaxInputFrames = 5;
    private const int MaxApiFsKhz = 48;

    /// <summary>把 s16le 单声道 PCM 编码为 SILK v3 流（容器规则见类注释）。</summary>
    /// <param name="pcmS16Le">小端有符号 16 位单声道 PCM 字节。</param>
    /// <param name="options">编码参数；null 时用默认（QQ 语音生产参数）。</param>
    /// <exception cref="ArgumentException">采样率等参数非法。</exception>
    public static byte[] EncodePcmS16Le(byte[] pcmS16Le, SilkV3EncoderOptions? options = null)
    {
        var o = options ?? new SilkV3EncoderOptions();

        int apiFsHz = o.ApiSampleRateHz;
        if (apiFsHz > MaxApiFsKhz * 1000 || apiFsHz <= 0)
            throw new ArgumentException($"API sampling rate {apiFsHz} out of range, valid range 8000 - 48000", nameof(options));

        int maxInternalFsHz = o.MaxInternalSampleRateHz;
        if (maxInternalFsHz == 0)
        {
            maxInternalFsHz = 24000;
            if (apiFsHz < maxInternalFsHz) maxInternalFsHz = apiFsHz;
        }

        int samplesPerRead = 20 * apiFsHz / 1000;    // CLI 固定按 20ms 帧读取
        int totalSamples = pcmS16Le.Length / 2;
        int packetSizeMs = o.PacketSizeMs;

        using var silk = new MemoryStream(64 + pcmS16Le.Length / 12);

        // 容器头
        if (o.Tencent) silk.WriteByte(0x02);
        silk.Write("#!SILK_V3"u8);

        // 创建/复位编码器
        var psEnc = new SKP_Silk_encoder_state_FIX();
        var encControl = new SKP_SILK_SDK_EncControlStruct();
        var encStatus = new SKP_SILK_SDK_EncControlStruct();
        int ret = SKP_Silk_enc_API.SKP_Silk_SDK_InitEncoder(psEnc, encStatus);
        if (ret != 0)
            throw new InvalidOperationException($"SKP_Silk_init_encoder returned {ret}");

        encControl.API_sampleRate = apiFsHz;
        encControl.maxInternalSampleRate = maxInternalFsHz;
        encControl.packetSize = packetSizeMs * apiFsHz / 1000;
        encControl.packetLossPercentage = o.PacketLossPercent;
        encControl.useInBandFEC = o.UseInBandFec ? 1 : 0;
        encControl.useDTX = o.UseDtx ? 1 : 0;
        encControl.complexity = o.Complexity;
        encControl.bitRate = o.BitrateBps > 0 ? o.BitrateBps : 0;

        var payload = new byte[MaxBytesPerFrame * MaxInputFrames];
        var inBuf = new short[20 * MaxApiFsKhz * MaxInputFrames];

        int pos = 0, shortPos = 0, smplsSinceLastPacket = 0;
        while (true)
        {
            int counter = Math.Min(samplesPerRead, totalSamples - shortPos);
            if (counter < samplesPerRead) break;    // 不足一帧：与原生 CLI 一致，丢弃尾部
            for (int i = 0; i < counter; i++)
            {
                inBuf[i] = (short)(pcmS16Le[pos] | (pcmS16Le[pos + 1] << 8));
                pos += 2;
            }
            shortPos += counter;

            short nBytes = MaxBytesPerFrame * MaxInputFrames;
            ret = SKP_Silk_enc_API.SKP_Silk_SDK_Encode(psEnc, encControl, inBuf, 0, counter, payload, 0, ref nBytes);
            if (ret != 0)
                throw new InvalidOperationException($"SKP_Silk_Encode returned {ret}");

            packetSizeMs = 1000 * encControl.packetSize / encControl.API_sampleRate;
            smplsSinceLastPacket += counter;
            if (1000 * smplsSinceLastPacket / apiFsHz == packetSizeMs)
            {
                silk.WriteByte((byte)nBytes);
                silk.WriteByte((byte)((uint)nBytes >> 8));
                if (nBytes > 0) silk.Write(payload, 0, nBytes);
                smplsSinceLastPacket = 0;
            }
        }

        if (!o.Tencent)
        {
            silk.WriteByte(0xFF);                     // 哑终止符 0xFFFF（仅标准模式）
            silk.WriteByte(0xFF);
        }

        return silk.ToArray();
    }
}
