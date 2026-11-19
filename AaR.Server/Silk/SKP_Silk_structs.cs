#nullable disable
// 注：C 结构体字段语义 = 声明即零初始化、由移植代码按需赋值，CS0649/CS0169 为照抄 C 布局的误报。
#pragma warning disable CS0649, CS0169

// ***********************************************************************
// SKP_Silk_structs.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_structs.h + src/SKP_Silk_structs_FIX.h
//   + src/SKP_Silk_resampler_structs.h + interface/SKP_Silk_control.h
//   + interface/SKP_Silk_SDK_API.h 中的 TOC
// Copyright (c) 2006-2012, Skype Limited.（BSD 风格许可，保留原始版权声明）
//
// 移植约定：C 的 struct → C# 类（引用语义）；定长数组字段在构造器中分配。
// C 中对整个 struct 的 SKP_memset(…, 0, sizeof(…)) → 调用 Clear()。
// C 中唯一的整结构体拷贝（encode_frame_FIX.c:320，sNSQ_LBRR ← sNSQ）→ CopyFrom()。
// ***********************************************************************

namespace AaR.Server.Silk;

/// <summary>重采样主函数委托：首参为重采样器状态（resampler_function 字段）。</summary>
internal delegate void SilkResamplerStateFn(SKP_Silk_resampler_state_struct S, short[] outBuf, int outOff, short[] inBuf, int inOff, int inLen);

/// <summary>重采样升降采样委托：首参为 int[] 状态向量（up2/down_pre/up_post 字段）。</summary>
internal delegate void SilkResamplerIntStateFn(int[] S, int sOff, short[] outBuf, int outOff, short[] inBuf, int inOff, int inLen);

/// <summary>SKP_Silk_resampler_structs.h —— 重采样器状态（sIIR 必须是首字段）。</summary>
internal sealed class SKP_Silk_resampler_state_struct
{
    public const int RESAMPLER_SUPPORT_ABOVE_48KHZ = 1;
    public const int SKP_Silk_RESAMPLER_MAX_FIR_ORDER = 16;
    public const int SKP_Silk_RESAMPLER_MAX_IIR_ORDER = 6;

    public int[] sIIR = new int[SKP_Silk_RESAMPLER_MAX_IIR_ORDER];   /* 必须是本结构首字段 */
    public int[] sFIR = new int[SKP_Silk_RESAMPLER_MAX_FIR_ORDER];
    public int[] sDown2 = new int[2];
    public SilkResamplerStateFn resampler_function;
    public SilkResamplerIntStateFn up2_function;
    public int batchSize;
    public int invRatio_Q16;
    public int FIR_Fracs;
    public int input2x;
    public short[] Coefs;
    /* RESAMPLER_SUPPORT_ABOVE_48KHZ = 1 */
    public int[] sDownPre = new int[2];
    public int[] sUpPost = new int[2];
    public SilkResamplerIntStateFn down_pre_function;
    public SilkResamplerIntStateFn up_post_function;
    public int batchSizePrePost;
    public int ratio_Q16;
    public int nPreDownsamplers;
    public int nPostUpsamplers;
    public int magic_number;
}

/// <summary>噪声整形量化器状态（FIX）。</summary>
internal sealed class SKP_Silk_nsq_state
{
    public short[] xq = new short[2 * SKP_Silk_define.MAX_FRAME_LENGTH];                       /* 量化输出缓冲 */
    public int[] sLTP_shp_Q10 = new int[2 * SKP_Silk_define.MAX_FRAME_LENGTH];
    public int[] sLPC_Q14 = new int[SKP_Silk_define.MAX_FRAME_LENGTH / SKP_Silk_define.NB_SUBFR + SKP_Silk_define.NSQ_LPC_BUF_LENGTH];
    public int[] sAR2_Q14 = new int[SKP_Silk_define.MAX_SHAPE_LPC_ORDER];
    public int sLF_AR_shp_Q12;
    public int lagPrev;
    public int sLTP_buf_idx;
    public int sLTP_shp_buf_idx;
    public int rand_seed;
    public int prev_inv_gain_Q16;
    public int rewhite_flag;

    public void Clear()
    {
        Array.Clear(xq, 0, xq.Length);
        Array.Clear(sLTP_shp_Q10, 0, sLTP_shp_Q10.Length);
        Array.Clear(sLPC_Q14, 0, sLPC_Q14.Length);
        Array.Clear(sAR2_Q14, 0, sAR2_Q14.Length);
        sLF_AR_shp_Q12 = 0; lagPrev = 0; sLTP_buf_idx = 0; sLTP_shp_buf_idx = 0;
        rand_seed = 0; prev_inv_gain_Q16 = 0; rewhite_flag = 0;
    }

    /// <summary>对应 C：SKP_memcpy(&dst, &src, sizeof(SKP_Silk_nsq_state))（深拷贝）。</summary>
    public void CopyFrom(SKP_Silk_nsq_state src)
    {
        Array.Copy(src.xq, 0, xq, 0, xq.Length);
        Array.Copy(src.sLTP_shp_Q10, 0, sLTP_shp_Q10, 0, sLTP_shp_Q10.Length);
        Array.Copy(src.sLPC_Q14, 0, sLPC_Q14, 0, sLPC_Q14.Length);
        Array.Copy(src.sAR2_Q14, 0, sAR2_Q14, 0, sAR2_Q14.Length);
        sLF_AR_shp_Q12 = src.sLF_AR_shp_Q12;
        lagPrev = src.lagPrev;
        sLTP_buf_idx = src.sLTP_buf_idx;
        sLTP_shp_buf_idx = src.sLTP_shp_buf_idx;
        rand_seed = src.rand_seed;
        prev_inv_gain_Q16 = src.prev_inv_gain_Q16;
        rewhite_flag = src.rewhite_flag;
    }
}

/// <summary>低码率冗余（LBRR）信息。</summary>
internal sealed class SKP_SILK_LBRR_struct
{
    public byte[] payload = new byte[SKP_Silk_define.MAX_ARITHM_BYTES];
    public int nBytes;                    /* payload 中的字节数 */
    public int usage;                     /* payload 作为 FEC 的使用方式 */

    public void Clear()
    {
        Array.Clear(payload, 0, payload.Length);
        nBytes = 0; usage = 0;
    }
}

/// <summary>VAD 状态。</summary>
internal sealed class SKP_Silk_VAD_state
{
    public int[] AnaState = new int[2];                            /* 分析滤波器组状态：0-8 kHz */
    public int[] AnaState1 = new int[2];                           /* 分析滤波器组状态：0-4 kHz */
    public int[] AnaState2 = new int[2];                           /* 分析滤波器组状态：0-2 kHz */
    public int[] XnrgSubfr = new int[SKP_Silk_define.VAD_N_BANDS];  /* 子帧能量 */
    public int[] NrgRatioSmth_Q8 = new int[SKP_Silk_define.VAD_N_BANDS]; /* 各频带平滑能量级 */
    public short HPstate;                                          /* 最低频带差分器状态 */
    public int[] NL = new int[SKP_Silk_define.VAD_N_BANDS];        /* 各频带噪声能量级 */
    public int[] inv_NL = new int[SKP_Silk_define.VAD_N_BANDS];    /* 各频带噪声能量倒数 */
    public int[] NoiseLevelBias = new int[SKP_Silk_define.VAD_N_BANDS]; /* 噪声级估计器偏置 */
    public int counter;                                            /* 初始阶段使用的帧计数器 */

    public void Clear()
    {
        Array.Clear(AnaState, 0, 2); Array.Clear(AnaState1, 0, 2); Array.Clear(AnaState2, 0, 2);
        Array.Clear(XnrgSubfr, 0, XnrgSubfr.Length);
        Array.Clear(NrgRatioSmth_Q8, 0, NrgRatioSmth_Q8.Length);
        HPstate = 0;
        Array.Clear(NL, 0, NL.Length); Array.Clear(inv_NL, 0, inv_NL.Length);
        Array.Clear(NoiseLevelBias, 0, NoiseLevelBias.Length);
        counter = 0;
    }
}

/// <summary>范围编/解码器状态。</summary>
internal sealed class SKP_Silk_range_coder_state
{
    public int bufferLength;
    public int bufferIx;
    public uint base_Q32;
    public uint range_Q16;
    public int error;
    public byte[] buffer = new byte[SKP_Silk_define.MAX_ARITHM_BYTES];   /* 存放 payload 的缓冲 */

    public void Clear()
    {
        bufferLength = 0; bufferIx = 0; base_Q32 = 0; range_Q16 = 0; error = 0;
        Array.Clear(buffer, 0, buffer.Length);
    }
}

/// <summary>输入频带宽度检测状态。</summary>
internal sealed class SKP_Silk_detect_SWB_state
{
    public int[][] S_HP_8_kHz = new int[SKP_Silk_define.NB_SOS][] { new int[2], new int[2], new int[2] };  /* HP 滤波状态 */
    public int ConsecSmplsAboveThres;
    public int ActiveSpeech_ms;                    /* 累计的有效语音时长 */
    public int SWB_detected;                       /* SWB 输入标志 */
    public int WB_detected;                         /* WB 输入标志 */

    public void Clear()
    {
        for (int i = 0; i < SKP_Silk_define.NB_SOS; i++) Array.Clear(S_HP_8_kHz[i], 0, 2);
        ConsecSmplsAboveThres = 0; ActiveSpeech_ms = 0; SWB_detected = 0; WB_detected = 0;
    }
}

/// <summary>可变截止频率低通滤波状态（SWITCH_TRANSITION_FILTERING）。</summary>
internal sealed class SKP_Silk_LP_state
{
    public int[] In_LP_State = new int[2];         /* 低通滤波状态 */
    public int transition_frame_no;                /* 映射到截止频率的计数器 */
    public int mode;                               /* 工作模式，0：下切，1：上切 */

    public void Clear()
    {
        Array.Clear(In_LP_State, 0, 2);
        transition_frame_no = 0; mode = 0;
    }
}

/// <summary>MSVQ 单级描述。</summary>
internal sealed class SKP_Silk_NLSF_CBS
{
    public readonly int nVectors;
    public readonly short[] CB_NLSF_Q15;
    public readonly short[] Rates_Q5;

    public SKP_Silk_NLSF_CBS(int nVectors, short[] cb_NLSF_Q15, short[] rates_Q5)
    {
        this.nVectors = nVectors;
        this.CB_NLSF_Q15 = cb_NLSF_Q15;
        this.Rates_Q5 = rates_Q5;
    }
}

/// <summary>NLSF MSVQ 码本。</summary>
internal sealed class SKP_Silk_NLSF_CB_struct
{
    public readonly int nStages;

    /* （反）量化字段 */
    public readonly SKP_Silk_NLSF_CBS[] CBStages;
    public readonly int[] NDeltaMin_Q15;

    /* 算术（反）编码字段 */
    public readonly ushort[] CDF;
    public readonly ushort[][] StartPtr;
    public readonly int[] MiddleIx;

    public SKP_Silk_NLSF_CB_struct(int nStages, SKP_Silk_NLSF_CBS[] cbStages, int[] nDeltaMin_Q15,
        ushort[] cdf, ushort[][] startPtr, int[] middleIx)
    {
        this.nStages = nStages;
        this.CBStages = cbStages;
        this.NDeltaMin_Q15 = nDeltaMin_Q15;
        this.CDF = cdf;
        this.StartPtr = startPtr;
        this.MiddleIx = middleIx;
    }
}

/// <summary>编码器状态（公共部分，structs.h）。</summary>
internal sealed class SKP_Silk_encoder_state
{
    public SKP_Silk_range_coder_state sRC = new();                       /* 范围编码器状态 */
    public SKP_Silk_range_coder_state sRC_LBRR = new();                  /* 范围编码器状态（低码率冗余） */
    public SKP_Silk_nsq_state sNSQ = new();                              /* 噪声整形量化器状态 */
    public SKP_Silk_nsq_state sNSQ_LBRR = new();                         /* 噪声整形量化器状态（LBRR） */

    /* HIGH_PASS_INPUT = 1 */
    public int[] In_HP_State = new int[2];                               /* 高通滤波状态 */
    /* SWITCH_TRANSITION_FILTERING = 1 */
    public SKP_Silk_LP_state sLP = new();                                /* 低通滤波状态 */
    public SKP_Silk_VAD_state sVAD = new();                             /* VAD 状态 */

    public int LBRRprevLastGainIndex;
    public int prev_sigtype;
    public int typeOffsetPrev;                       /* 上一信号类型与量化偏移 */
    public int prevLag;
    public int prev_lagIndex;
    public int API_fs_Hz;                            /* API 采样率（Hz） */
    public int prev_API_fs_Hz;                       /* 上一 API 采样率（Hz） */
    public int maxInternal_fs_kHz;                   /* 最大内部采样率（kHz） */
    public int fs_kHz;                               /* 内部采样率（kHz） */
    public int fs_kHz_changed;                       /* 是否已切换 */
    public int frame_length;                         /* 帧长（样本） */
    public int subfr_length;                         /* 子帧长（样本） */
    public int la_pitch;                             /* 基音分析前瞻（样本） */
    public int la_shape;                             /* 噪声整形分析前瞻（样本） */
    public int shapeWinLength;                       /* 噪声整形分析窗长（样本） */
    public int TargetRate_bps;                       /* 目标码率（bps） */
    public int PacketSize_ms;                        /* 每包毫秒数 */
    public int PacketLoss_perc;                      /* 对端测得的丢包率 */
    public int frameCounter;
    public int Complexity;                           /* 复杂度：0 低；1 中；2 高 */
    public int nStatesDelayedDecision;               /* 延迟判决状态数 */
    public int useInterpolatedNLSFs;                  /* 是否使用 NLSF 插值 */
    public int shapingLPCOrder;                       /* 噪声整形滤波器阶数 */
    public int predictLPCOrder;                      /* 预测滤波器阶数 */
    public int pitchEstimationComplexity;             /* 基音估计器复杂度 */
    public int pitchEstimationLPCOrder;              /* 基音估计器白化滤波器阶数 */
    public int pitchEstimationThreshold_Q16;         /* 基音估计器阈值 */
    public int LTPQuantLowComplexity;                /* 低复杂度 LTP 量化标志 */
    public int NLSF_MSVQ_Survivors;                   /* NLSF MSVQ 幸存数 */
    public int first_frame_after_reset;               /* 复位后停用 NLSF 插值/波动削减的标志 */
    public int controlled_since_last_payload;        /* 每包仅控制一次的标志 */
    public int warping_Q16;                          /* 变形噪声整形参数 */

    /* 输入/输出缓冲 */
    public short[] inputBuf = new short[SKP_Silk_define.MAX_FRAME_LENGTH];  /* 输入信号缓冲 */
    public int inputBufIx;
    public int nFramesInPayloadBuf;                  /* 输出缓冲中的帧数 */
    public int nBytesInPayloadBuf;                    /* 输出缓冲中的字节数 */

    /* LTP 尺度控制参数 */
    public int frames_since_onset;

    public SKP_Silk_NLSF_CB_struct[] psNLSF_CB = new SKP_Silk_NLSF_CB_struct[2];  /* 有/无声码本 */

    /* inband LBRR 结构 */
    public SKP_SILK_LBRR_struct[] LBRR_buffer = new SKP_SILK_LBRR_struct[SKP_Silk_define.MAX_LBRR_DELAY]
        { new(), new() };
    public int oldest_LBRR_idx;
    public int useInBandFEC;                         /* 保存 API 设置供查询 */
    public int LBRR_enabled;
    public int LBRR_GainIncreases;                   /* LBRR 有声帧增益右移数 */

    /* 码率控制 */
    public int bitrateDiff;                          /* 目标码率与切换码率间累计差 */
    public int bitrate_threshold_up;                 /* 上调内部采样率阈值 */
    public int bitrate_threshold_down;               /* 下调内部采样率阈值 */

    public SKP_Silk_resampler_state_struct resampler_state = new();

    /* DTX */
    public int noSpeechCounter;                      /* 连续非活动帧计数（DTX 用） */
    public int useDTX;                               /* 启用 DTX 标志 */
    public int inDTX;                                /* DTX 期标志 */
    public int vadFlag;                              /* 语音活动标志 */

    /* SWB 输入检测结构 */
    public SKP_Silk_detect_SWB_state sSWBdetect = new();

    /* 缓冲 */
    public sbyte[] q = new sbyte[SKP_Silk_define.MAX_FRAME_LENGTH];           /* 脉冲信号缓冲 */
    public sbyte[] q_LBRR = new sbyte[SKP_Silk_define.MAX_FRAME_LENGTH];      /* 脉冲信号缓冲 */
}

/// <summary>编码器控制参数（公共部分）。</summary>
internal sealed class SKP_Silk_encoder_control
{
    /* 量化索引 */
    public int lagIndex;
    public int contourIndex;
    public int PERIndex;
    public int[] LTPIndex = new int[SKP_Silk_define.NB_SUBFR];
    public int[] NLSFIndices = new int[SKP_Silk_define.NLSF_MSVQ_MAX_CB_STAGES];   /* 量化 LSF 路径 */
    public int NLSFInterpCoef_Q2;
    public int[] GainsIndices = new int[SKP_Silk_define.NB_SUBFR];
    public int Seed;
    public int LTP_scaleIndex;
    public int RateLevelIndex;
    public int QuantOffsetType;
    public int sigtype;

    /* 预测与编码参数 */
    public int[] pitchL = new int[SKP_Silk_define.NB_SUBFR];

    public int LBRR_usage;                            /* 低码率冗余使用方式 */
}

/// <summary>丢包隐藏（PLC）结构。</summary>
internal sealed class SKP_Silk_PLC_struct
{
    public int pitchL_Q8;                            /* 有声隐藏用基音滞后 */
    public short[] LTPCoef_Q14 = new short[SKP_Silk_define.LTP_ORDER];
    public short[] prevLPC_Q12 = new short[SKP_Silk_define.MAX_LPC_ORDER];
    public int last_frame_lost;                      /* 上一帧是否丢失 */
    public int rand_seed;                            /* 无声信号生成种子 */
    public short randScale_Q14;                      /* 无声随机信号缩放 */
    public int conc_energy;
    public int conc_energy_shift;
    public short prevLTP_scale_Q14;
    public int[] prevGain_Q16 = new int[SKP_Silk_define.NB_SUBFR];
    public int fs_kHz;

    public void Clear()
    {
        pitchL_Q8 = 0;
        Array.Clear(LTPCoef_Q14, 0, LTPCoef_Q14.Length);
        Array.Clear(prevLPC_Q12, 0, prevLPC_Q12.Length);
        last_frame_lost = 0; rand_seed = 0; randScale_Q14 = 0;
        conc_energy = 0; conc_energy_shift = 0; prevLTP_scale_Q14 = 0;
        Array.Clear(prevGain_Q16, 0, prevGain_Q16.Length);
        fs_kHz = 0;
    }
}

/// <summary>CNG 结构。</summary>
internal sealed class SKP_Silk_CNG_struct
{
    public int[] CNG_exc_buf_Q10 = new int[SKP_Silk_define.MAX_FRAME_LENGTH];
    public int[] CNG_smth_NLSF_Q15 = new int[SKP_Silk_define.MAX_LPC_ORDER];
    public int[] CNG_synth_state = new int[SKP_Silk_define.MAX_LPC_ORDER];
    public int CNG_smth_Gain_Q16;
    public int rand_seed;
    public int fs_kHz;

    public void Clear()
    {
        Array.Clear(CNG_exc_buf_Q10, 0, CNG_exc_buf_Q10.Length);
        Array.Clear(CNG_smth_NLSF_Q15, 0, CNG_smth_NLSF_Q15.Length);
        Array.Clear(CNG_synth_state, 0, CNG_synth_state.Length);
        CNG_smth_Gain_Q16 = 0; rand_seed = 0; fs_kHz = 0;
    }
}

/// <summary>解码器状态。</summary>
internal sealed class SKP_Silk_decoder_state
{
    public SKP_Silk_range_coder_state sRC = new();                       /* 范围解码器状态 */
    public int prev_inv_gain_Q16;
    public int[] sLTP_Q16 = new int[2 * SKP_Silk_define.MAX_FRAME_LENGTH];
    public int[] sLPC_Q14 = new int[SKP_Silk_define.MAX_FRAME_LENGTH / SKP_Silk_define.NB_SUBFR + SKP_Silk_define.MAX_LPC_ORDER];
    public int[] exc_Q10 = new int[SKP_Silk_define.MAX_FRAME_LENGTH];
    public int[] res_Q10 = new int[SKP_Silk_define.MAX_FRAME_LENGTH];
    public short[] outBuf = new short[2 * SKP_Silk_define.MAX_FRAME_LENGTH];   /* 输出信号缓冲 */
    public int lagPrev;                                   /* 上一滞后 */
    public int LastGainIndex;                              /* 上一增益索引 */
    public int LastGainIndex_EnhLayer;                     /* 上一增益索引 */
    public int typeOffsetPrev;                             /* 上一信号类型与量化偏移 */
    public int[] HPState = new int[SKP_Silk_define.DEC_HP_ORDER];   /* HP 滤波状态 */
    public short[] HP_A;                                  /* HP 滤波 AR 系数 */
    public short[] HP_B;                                  /* HP 滤波 MA 系数 */
    public int fs_kHz;                                    /* 采样率（kHz） */
    public int prev_API_sampleRate;                       /* 上一 API 采样率（Hz） */
    public int frame_length;                              /* 帧长（样本） */
    public int subfr_length;                              /* 子帧长（样本） */
    public int LPC_order;                                 /* LPC 阶数 */
    public int[] prevNLSF_Q15 = new int[SKP_Silk_define.MAX_LPC_ORDER];   /* NLSF 插值用 */
    public int first_frame_after_reset;                   /* 复位后停用 NLSF 插值/波动削减的标志 */

    /* 多帧分包时的 payload 缓冲 */
    public int nBytesLeft;
    public int nFramesDecoded;
    public int nFramesInPacket;
    public int moreInternalDecoderFrames;
    public int FrameTermination;

    public SKP_Silk_resampler_state_struct resampler_state = new();

    public SKP_Silk_NLSF_CB_struct[] psNLSF_CB = new SKP_Silk_NLSF_CB_struct[2];  /* 有/无声码本 */

    /* inband FEC 检测参数 */
    public int vadFlag;
    public int no_FEC_counter;                            /* 无 inband FEC 帧计数 */
    public int inband_FEC_offset;                          /* 0：无 FEC；1：偏 1 包；2：偏 2 包 */

    /* CNG 状态 */
    public SKP_Silk_CNG_struct sCNG = new();

    /* PLC 相关 */
    public int lossCnt;
    public int prev_sigtype;                              /* 上一信号类型 */
    public SKP_Silk_PLC_struct sPLC = new();

    /// <summary>对应 C：SKP_memset(psDec, 0, sizeof(SKP_Silk_decoder_state))（InitDecoder 用）。</summary>
    public void Clear()
    {
        sRC.Clear();
        prev_inv_gain_Q16 = 0;
        Array.Clear(sLTP_Q16, 0, sLTP_Q16.Length);
        Array.Clear(sLPC_Q14, 0, sLPC_Q14.Length);
        Array.Clear(exc_Q10, 0, exc_Q10.Length);
        Array.Clear(res_Q10, 0, res_Q10.Length);
        Array.Clear(outBuf, 0, outBuf.Length);
        lagPrev = 0; LastGainIndex = 0; LastGainIndex_EnhLayer = 0; typeOffsetPrev = 0;
        Array.Clear(HPState, 0, HPState.Length);
        HP_A = null; HP_B = null;
        fs_kHz = 0; prev_API_sampleRate = 0;
        frame_length = 0; subfr_length = 0; LPC_order = 0;
        Array.Clear(prevNLSF_Q15, 0, prevNLSF_Q15.Length);
        first_frame_after_reset = 0;
        nBytesLeft = 0; nFramesDecoded = 0; nFramesInPacket = 0;
        moreInternalDecoderFrames = 0; FrameTermination = 0;
        Array.Clear(resampler_state.sIIR, 0, resampler_state.sIIR.Length);
        Array.Clear(resampler_state.sFIR, 0, resampler_state.sFIR.Length);
        Array.Clear(resampler_state.sDown2, 0, resampler_state.sDown2.Length);
        resampler_state.resampler_function = null;
        resampler_state.up2_function = null;
        resampler_state.batchSize = 0; resampler_state.invRatio_Q16 = 0;
        resampler_state.FIR_Fracs = 0; resampler_state.input2x = 0;
        resampler_state.Coefs = null;
        Array.Clear(resampler_state.sDownPre, 0, resampler_state.sDownPre.Length);
        Array.Clear(resampler_state.sUpPost, 0, resampler_state.sUpPost.Length);
        resampler_state.down_pre_function = null;
        resampler_state.up_post_function = null;
        resampler_state.batchSizePrePost = 0; resampler_state.ratio_Q16 = 0;
        resampler_state.nPreDownsamplers = 0; resampler_state.nPostUpsamplers = 0;
        resampler_state.magic_number = 0;
        psNLSF_CB[0] = null; psNLSF_CB[1] = null;
        vadFlag = 0; no_FEC_counter = 0; inband_FEC_offset = 0;
        sCNG.Clear();
        lossCnt = 0; prev_sigtype = 0;
        sPLC.Clear();
    }
}

/// <summary>解码器控制参数。</summary>
internal sealed class SKP_Silk_decoder_control
{
    public int[] pitchL = new int[SKP_Silk_define.NB_SUBFR];
    public int[] Gains_Q16 = new int[SKP_Silk_define.NB_SUBFR];
    public int Seed;
    public short[][] PredCoef_Q12 = new short[2][]
    {
        new short[SKP_Silk_define.MAX_LPC_ORDER],
        new short[SKP_Silk_define.MAX_LPC_ORDER],
    };
    public short[] LTPCoef_Q14 = new short[SKP_Silk_define.LTP_ORDER * SKP_Silk_define.NB_SUBFR];
    public int LTP_scale_Q14;

    /* 量化索引 */
    public int PERIndex;
    public int RateLevelIndex;
    public int QuantOffsetType;
    public int sigtype;
    public int NLSFInterpCoef_Q2;
}

/// <summary>噪声整形分析状态（FIX）。</summary>
internal sealed class SKP_Silk_shape_state_FIX
{
    public int LastGainIndex;
    public int HarmBoost_smth_Q16;
    public int HarmShapeGain_smth_Q16;
    public int Tilt_smth_Q16;

    public void Clear()
    {
        LastGainIndex = 0; HarmBoost_smth_Q16 = 0; HarmShapeGain_smth_Q16 = 0; Tilt_smth_Q16 = 0;
    }
}

/// <summary>预滤波状态（FIX）。</summary>
internal sealed class SKP_Silk_prefilter_state_FIX
{
    public short[] sLTP_shp = new short[SKP_Silk_define.LTP_BUF_LENGTH];
    public int[] sAR_shp = new int[SKP_Silk_define.MAX_SHAPE_LPC_ORDER + 1];   // Q14
    public int sLTP_shp_buf_idx;
    public int sLF_AR_shp_Q12;
    public int sLF_MA_shp_Q12;
    public int sHarmHP;
    public int rand_seed;
    public int lagPrev;

    public void Clear()
    {
        Array.Clear(sLTP_shp, 0, sLTP_shp.Length);
        Array.Clear(sAR_shp, 0, sAR_shp.Length);
        sLTP_shp_buf_idx = 0; sLF_AR_shp_Q12 = 0; sLF_MA_shp_Q12 = 0;
        sHarmHP = 0; rand_seed = 0; lagPrev = 0;
    }
}

/// <summary>预测分析状态（FIX）。</summary>
internal sealed class SKP_Silk_predict_state_FIX
{
    public int pitch_LPC_win_length;
    public int min_pitch_lag;                                       /* 最小可能基音滞后（样本） */
    public int max_pitch_lag;                                       /* 最大可能基音滞后（样本） */
    public int[] prev_NLSFq_Q15 = new int[SKP_Silk_define.MAX_LPC_ORDER];   /* 上一量化 NLSF 向量 */

    public void Clear()
    {
        pitch_LPC_win_length = 0; min_pitch_lag = 0; max_pitch_lag = 0;
        Array.Clear(prev_NLSFq_Q15, 0, prev_NLSFq_Q15.Length);
    }
}

/// <summary>编码器状态（FIX，含公共部分）。</summary>
internal sealed class SKP_Silk_encoder_state_FIX
{
    public SKP_Silk_encoder_state sCmn = new();                     /* 与浮点代码共享的公共结构 */

    /* HIGH_PASS_INPUT = 1 */
    public int variable_HP_smth1_Q15;                               /* 第一平滑器状态 */
    public int variable_HP_smth2_Q15;                               /* 第二平滑器状态 */

    public SKP_Silk_shape_state_FIX sShape = new();                 /* 整形状态 */
    public SKP_Silk_prefilter_state_FIX sPrefilt = new();           /* 预滤波状态 */
    public SKP_Silk_predict_state_FIX sPred = new();                 /* 预测状态 */

    /* find pitch 与噪声整形分析缓冲 */
    public short[] x_buf = new short[2 * SKP_Silk_define.MAX_FRAME_LENGTH + SKP_Silk_define.LA_SHAPE_MAX];
    public int LTPCorr_Q15;                                          /* 基音滞后估计器归一化相关，约 Q15 */
    public int mu_LTP_Q8;                                            /* LTP 量化率失真权衡 */
    public int SNR_dB_Q7;                                            /* 质量设置 */
    public int avgGain_Q16;                                          /* 有效语音期间平均增益 */
    public int avgGain_Q16_one_bit_per_sample;                      /* 有效语音期间平均增益 */
    public int BufferedInChannel_ms;                                 /* 超过 TargetRate_bps 模拟出的缓冲 ms */
    public int speech_activity_Q8;                                  /* Q8 语音活动度 */

    /* LTP 尺度控制参数 */
    public int prevLTPredCodGain_Q7;
    public int HPLTPredCodGain_Q7;

    public int inBandFEC_SNR_comp_Q8;                                /* inband FEC 有声帧的 SNR_dB 补偿 */
}

/// <summary>编码器控制参数（FIX，含公共部分）。</summary>
internal sealed class SKP_Silk_encoder_control_FIX
{
    public SKP_Silk_encoder_control sCmn = new();                   /* 与浮点代码共享的公共结构 */

    /* 预测与编码参数 */
    public int[] Gains_Q16 = new int[SKP_Silk_define.NB_SUBFR];
    public short[][] PredCoef_Q12 = new short[2][]
    {
        new short[SKP_Silk_define.MAX_LPC_ORDER],
        new short[SKP_Silk_define.MAX_LPC_ORDER],
    };
    public short[] LTPCoef_Q14 = new short[SKP_Silk_define.LTP_ORDER * SKP_Silk_define.NB_SUBFR];
    public int LTP_scale_Q14;

    /* 噪声整形参数 */
    public short[] AR1_Q13 = new short[SKP_Silk_define.NB_SUBFR * SKP_Silk_define.MAX_SHAPE_LPC_ORDER];
    public short[] AR2_Q13 = new short[SKP_Silk_define.NB_SUBFR * SKP_Silk_define.MAX_SHAPE_LPC_ORDER];
    public int[] LF_shp_Q14 = new int[SKP_Silk_define.NB_SUBFR];    /* 每个 int32 打包两个 int16 系数 */
    public int[] GainsPre_Q14 = new int[SKP_Silk_define.NB_SUBFR];
    public int[] HarmBoost_Q14 = new int[SKP_Silk_define.NB_SUBFR];
    public int[] Tilt_Q14 = new int[SKP_Silk_define.NB_SUBFR];
    public int[] HarmShapeGain_Q14 = new int[SKP_Silk_define.NB_SUBFR];
    public int Lambda_Q10;
    public int input_quality_Q14;
    public int coding_quality_Q14;
    public int pitch_freq_low_Hz;
    public int current_SNR_dB_Q7;

    /* 度量 */
    public int sparseness_Q8;
    public int predGain_Q16;
    public int LTPredCodGain_Q7;
    public int[] input_quality_bands_Q15 = new int[SKP_Silk_define.VAD_N_BANDS];
    public int input_tilt_Q15;
    public int[] ResNrg = new int[SKP_Silk_define.NB_SUBFR];         /* 每子帧残差能量 */
    public int[] ResNrgQ = new int[SKP_Silk_define.NB_SUBFR];        /* 残差能量 Q 域（> 0） */
}

/// <summary>interface/SKP_Silk_control.h —— 编码器控制结构。</summary>
internal sealed class SKP_SILK_SDK_EncControlStruct
{
    public int API_sampleRate;                 /* I: 输入采样率（Hz）；8000/12000/16000/24000/32000/44100/48000 */
    public int maxInternalSampleRate;          /* I: 最大内部采样率（Hz） */
    public int packetSize;                     /* I: 每包样本数；对应 20/40/60/80/100 ms */
    public int bitRate;                        /* I: 有效语音码率（bps），内部限幅 */
    public int packetLossPercentage;           /* I: 上行丢包率（0-100） */
    public int complexity;                     /* I: 复杂度；0 低，1 中，2 高 */
    public int useInBandFEC;                   /* I: 启用 inband FEC 标志；0/1 */
    public int useDTX;                         /* I: 启用 DTX 标志；0/1 */
}

/// <summary>interface/SKP_Silk_control.h —— 解码器控制结构。</summary>
internal sealed class SKP_SILK_SDK_DecControlStruct
{
    public int API_sampleRate;                 /* I: 输出采样率（Hz） */
    public int frameSize;                      /* O: 每帧样本数 */
    public int framesPerPacket;                /* O: 每包帧数 1..5 */
    public int moreInternalDecoderFrames;      /* O: 内部还有剩余 payload 标志 */
    public int inBandFECOffset;                 /* O: 主/冗余 payload 间隔（包） */
}

/// <summary>interface/SKP_Silk_SDK_API.h —— TOC 结构。</summary>
internal sealed class SKP_Silk_TOC_struct
{
    public const int SILK_MAX_FRAMES_PER_PACKET = 5;

    public int framesInPacket;                             /* 包内 20 ms 帧数 */
    public int fs_kHz;                                     /* 包内采样率（kHz） */
    public int inbandLBRR;                                 /* 包内是否含 LBRR 信息 */
    public int corrupt;                                    /* 包是否损坏 */
    public int[] vadFlags = new int[SILK_MAX_FRAMES_PER_PACKET];      /* 包内每帧 VAD 标志 */
    public int[] sigtypeFlags = new int[SILK_MAX_FRAMES_PER_PACKET];  /* 包内每帧信号类型 */
}
