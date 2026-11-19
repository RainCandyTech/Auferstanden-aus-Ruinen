#nullable disable
// ***********************************************************************
// SKP_Silk_define.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_define.h + interface/SKP_Silk_errors.h
//   + src/SKP_Silk_common_pitch_est_defines.h + src/SKP_Silk_pitch_est_defines.h
//   + SigProc_FIX.h 中的少量常量
// Copyright (c) 2006-2012, Skype Limited.（BSD 风格许可，保留原始版权声明）
// ***********************************************************************

namespace AaR.Server.Silk;

/// <summary>SKP_Silk_define.h —— 编解码器全局常量。</summary>
internal static class SKP_Silk_define
{
    public const int MAX_FRAMES_PER_PACKET = 5;

    /* 码率限制 */
    public const int MIN_TARGET_RATE_BPS = 5000;
    public const int MAX_TARGET_RATE_BPS = 100000;

    /* 模式间切换码率 */
    public const int SWB2WB_BITRATE_BPS = 25000;
    public const int WB2SWB_BITRATE_BPS = 30000;
    public const int WB2MB_BITRATE_BPS = 14000;
    public const int MB2WB_BITRATE_BPS = 18000;
    public const int MB2NB_BITRATE_BPS = 10000;
    public const int NB2MB_BITRATE_BPS = 14000;

    /* 内部采样率下调的积分/滞回阈值 */
    public const int ACCUM_BITS_DIFF_THRESHOLD = 30000000;
    public const int TARGET_RATE_TAB_SZ = 8;

    /* DTX 设置 */
    public const int NO_SPEECH_FRAMES_BEFORE_DTX = 5;      /* eq 100 ms */
    public const int MAX_CONSECUTIVE_DTX = 20;              /* eq 400 ms */

    public const int USE_LBRR = 1;

    /* 连续无 FEC 包的告警阈值 */
    public const int NO_LBRR_THRES = 10;

    /* 真实包与 LBRR 包之间最大延迟 */
    public const int MAX_LBRR_DELAY = 2;
    public const int LBRR_IDX_MASK = 1;

    public const int INBAND_FEC_MIN_RATE_BPS = 18000;      /* 低于此总目标码率不启用 inband FEC */
    public const int LBRR_LOSS_THRES = 1;                  /* 该丢包率起开始添加 LBRR */

    /* LBRR 用法定义 */
    public const int SKP_SILK_NO_LBRR = 0;
    public const int SKP_SILK_ADD_LBRR_TO_PLUS1 = 1;
    public const int SKP_SILK_ADD_LBRR_TO_PLUS2 = 2;

    /* 帧终止指示 */
    public const int SKP_SILK_LAST_FRAME = 0;
    public const int SKP_SILK_MORE_FRAMES = 1;
    public const int SKP_SILK_LBRR_VER1 = 2;
    public const int SKP_SILK_LBRR_VER2 = 3;
    public const int SKP_SILK_EXT_LAYER = 4;

    /* SWB 检测高通滤波器的二阶节数量 */
    public const int NB_SOS = 3;
    public const int HP_8_KHZ_THRES = 10;                  /* 8 kHz 以上平均每样本能量 */
    public const int CONCEC_SWB_SMPLS_THRES = 480 * 15;     /* 300 ms */
    public const int WB_DETECT_ACTIVE_SPEECH_MS_THRES = 15000;

    /* 低复杂度开关（默认构建为 0，即全模式可用） */
    public const int LOW_COMPLEXITY_ONLY = 0;

    /* 模式切换的过渡滤波开关 */
    public const int SWITCH_TRANSITION_FILTERING = 1;

    /* 解码器高通滤波器阶数 */
    public const int DEC_HP_ORDER = 2;

    /* 最大采样频率（kHz） */
    public const int MAX_FS_KHZ = 24;
    public const int MAX_API_FS_KHZ = 48;

    /* 信号类型 */
    public const int SIG_TYPE_VOICED = 0;
    public const int SIG_TYPE_UNVOICED = 1;

    /* VAD 类型 */
    public const int NO_VOICE_ACTIVITY = 0;
    public const int VOICE_ACTIVITY = 1;

    /* 每帧样本数 */
    public const int FRAME_LENGTH_MS = 20;
    public const int MAX_FRAME_LENGTH = FRAME_LENGTH_MS * MAX_FS_KHZ;

    /* 基音分析前瞻（ms） */
    public const int LA_PITCH_MS = 2;
    public const int LA_PITCH_MAX = LA_PITCH_MS * MAX_FS_KHZ;

    /* find pitch 的 LPC 窗长（ms） */
    public const int FIND_PITCH_LPC_WIN_MS = 20 + (LA_PITCH_MS << 1);
    public const int FIND_PITCH_LPC_WIN_MAX = FIND_PITCH_LPC_WIN_MS * MAX_FS_KHZ;

    /* find pitch 的 LPC 阶数 */
    public const int MAX_FIND_PITCH_LPC_ORDER = 16;

    public const int PITCH_EST_COMPLEXITY_HC_MODE = 2;
    public const int PITCH_EST_COMPLEXITY_MC_MODE = 1;
    public const int PITCH_EST_COMPLEXITY_LC_MODE = 0;

    /* 噪声整形分析前瞻（ms） */
    public const int LA_SHAPE_MS = 5;
    public const int LA_SHAPE_MAX = LA_SHAPE_MS * MAX_FS_KHZ;

    /* 噪声整形分析 LPC 窗长上限 */
    public const int SHAPE_LPC_WIN_MAX = 15 * MAX_FS_KHZ;

    /* payload 输出缓冲最大字节数（可含多帧） */
    public const int MAX_ARITHM_BYTES = 1024;

    public const int RANGE_CODER_WRITE_BEYOND_BUFFER = -1;
    public const int RANGE_CODER_CDF_OUT_OF_RANGE = -2;
    public const int RANGE_CODER_NORMALIZATION_FAILED = -3;
    public const int RANGE_CODER_ZERO_INTERVAL_WIDTH = -4;
    public const int RANGE_CODER_DECODER_CHECK_FAILED = -5;
    public const int RANGE_CODER_READ_BEYOND_BUFFER = -6;
    public const int RANGE_CODER_ILLEGAL_SAMPLING_RATE = -7;
    public const int RANGE_CODER_DEC_PAYLOAD_TOO_LONG = -8;

    /* 增益量化区间 */
    public const int MIN_QGAIN_DB = 6;
    public const int MAX_QGAIN_DB = 86;
    public const int N_LEVELS_QGAIN = 64;
    public const int MAX_DELTA_GAIN_QUANT = 40;
    public const int MIN_DELTA_GAIN_QUANT = -4;

    /* 量化偏移（4 的倍数） */
    public const int OFFSET_VL_Q10 = 32;
    public const int OFFSET_VH_Q10 = 100;
    public const int OFFSET_UVL_Q10 = 100;
    public const int OFFSET_UVH_Q10 = 256;

    /* LPC 稳定化最大迭代次数 */
    public const int MAX_LPC_STABILIZE_ITERATIONS = 20;

    public const int MAX_LPC_ORDER = 16;
    public const int MIN_LPC_ORDER = 10;

    /* find pred coef 定义 */
    public const int LTP_ORDER = 5;

    /* LTP 量化设置 */
    public const int NB_LTP_CBKS = 3;

    /* 子帧数 */
    public const int NB_SUBFR = 4;

    /* 谐波噪声整形开关 */
    public const int USE_HARM_SHAPING = 1;

    /* 噪声整形滤波器最大 LPC 阶数 */
    public const int MAX_SHAPE_LPC_ORDER = 16;

    public const int HARM_SHAPE_FIR_TAPS = 3;

    /* 延迟判决状态数上限 */
    public const int MAX_DEL_DEC_STATES = 4;

    public const int LTP_BUF_LENGTH = 512;
    public const int LTP_MASK = LTP_BUF_LENGTH - 1;

    public const int DECISION_DELAY = 32;
    public const int DECISION_DELAY_MASK = DECISION_DELAY - 1;

    /* 激励熵编码的子帧长度 */
    public const int SHELL_CODEC_FRAME_LENGTH = 16;
    public const int MAX_NB_SHELL_BLOCKS = MAX_FRAME_LENGTH / SHELL_CODEC_FRAME_LENGTH;

    /* 激励熵编码的码率级数 */
    public const int N_RATE_LEVELS = 10;

    /* 每 shell 编码帧的最大脉冲和 */
    public const int MAX_PULSES = 18;

    public const int MAX_MATRIX_SIZE = MAX_LPC_ORDER;

    public const int NSQ_LPC_BUF_LENGTH = MAX_LPC_ORDER > DECISION_DELAY ? MAX_LPC_ORDER : DECISION_DELAY;

    /***********************/
    /* 高通滤波            */
    /***********************/
    public const int HIGH_PASS_INPUT = 1;

    /***************************/
    /* 语音活动检测器          */
    /***************************/
    public const int VAD_N_BANDS = 4;

    public const int VAD_INTERNAL_SUBFRAMES_LOG2 = 2;
    public const int VAD_INTERNAL_SUBFRAMES = 1 << VAD_INTERNAL_SUBFRAMES_LOG2;

    public const int VAD_NOISE_LEVEL_SMOOTH_COEF_Q16 = 1024;   /* 必须小于 4096 */
    public const int VAD_NOISE_LEVELS_BIAS = 50;

    /* Sigmoid 设置 */
    public const int VAD_NEGATIVE_OFFSET_Q5 = 128;             /* sigmoid 在 -128 处为 0 */
    public const int VAD_SNR_FACTOR_Q16 = 45000;

    /* SNR 平滑 */
    public const int VAD_SNR_SMOOTH_COEF_Q18 = 4096;

    /******************/
    /* NLSF 量化器     */
    /******************/
    public const int NLSF_MSVQ_MAX_CB_STAGES = 10;                    /* 更改码本需手动更新 */
    public const int NLSF_MSVQ_MAX_VECTORS_IN_STAGE = 128;            /* 更改码本需手动更新 */
    public const int NLSF_MSVQ_MAX_VECTORS_IN_STAGE_TWO_TO_END = 16;  /* 更改码本需手动更新 */

    public const int NLSF_MSVQ_FLUCTUATION_REDUCTION = 1;
    public const int MAX_NLSF_MSVQ_SURVIVORS = 16;
    public const int MAX_NLSF_MSVQ_SURVIVORS_LC_MODE = 2;
    public const int MAX_NLSF_MSVQ_SURVIVORS_MC_MODE = 4;

    public const float NLSF_MSVQ_SURV_MAX_REL_RD = 0.1f;              /* 必须小于 0.5 */

    /* 基于上述定义计算需要分配的内存量（取较大者） */
    public const int NLSF_MSVQ_TREE_SEARCH_MAX_VECTORS_EVALUATED_LC_MODE =
        NLSF_MSVQ_MAX_VECTORS_IN_STAGE > (MAX_NLSF_MSVQ_SURVIVORS_LC_MODE * NLSF_MSVQ_MAX_VECTORS_IN_STAGE_TWO_TO_END)
            ? NLSF_MSVQ_MAX_VECTORS_IN_STAGE
            : MAX_NLSF_MSVQ_SURVIVORS_LC_MODE * NLSF_MSVQ_MAX_VECTORS_IN_STAGE_TWO_TO_END;

    public const int NLSF_MSVQ_TREE_SEARCH_MAX_VECTORS_EVALUATED =
        NLSF_MSVQ_MAX_VECTORS_IN_STAGE > (MAX_NLSF_MSVQ_SURVIVORS * NLSF_MSVQ_MAX_VECTORS_IN_STAGE_TWO_TO_END)
            ? NLSF_MSVQ_MAX_VECTORS_IN_STAGE
            : MAX_NLSF_MSVQ_SURVIVORS * NLSF_MSVQ_MAX_VECTORS_IN_STAGE_TWO_TO_END;

    /* 模式切换的过渡滤波（SWITCH_TRANSITION_FILTERING=1） */
    public const int TRANSITION_TIME_UP_MS = 5120;     /* 64 * FRAME_LENGTH_MS * (TRANSITION_INT_NUM - 1) */
    public const int TRANSITION_TIME_DOWN_MS = 2560;   /* 32 * FRAME_LENGTH_MS * (TRANSITION_INT_NUM - 1) */
    public const int TRANSITION_NB = 3;                /* 表中硬编码 */
    public const int TRANSITION_NA = 2;                /* 表中硬编码 */
    public const int TRANSITION_INT_NUM = 5;           /* 表中硬编码 */
    public const int TRANSITION_FRAMES_UP = TRANSITION_TIME_UP_MS / FRAME_LENGTH_MS;
    public const int TRANSITION_FRAMES_DOWN = TRANSITION_TIME_DOWN_MS / FRAME_LENGTH_MS;
    public const int TRANSITION_INT_STEPS_UP = TRANSITION_FRAMES_UP / (TRANSITION_INT_NUM - 1);
    public const int TRANSITION_INT_STEPS_DOWN = TRANSITION_FRAMES_DOWN / (TRANSITION_INT_NUM - 1);

    /* 丢包后应用的 BWE 因子 */
    public const int BWE_AFTER_LOSS_Q16 = 63570;

    /* CN 生成定义 */
    public const int CNG_BUF_MASK_MAX = 255;           /* 2^floor(log2(MAX_FRAME_LENGTH))-1 */
    public const int CNG_GAIN_SMTH_Q16 = 4634;        /* 0.25^(1/4) */
    public const int CNG_NLSF_SMTH_Q16 = 16348;       /* 0.25 */

    // ============ SigProc_FIX.h 中的常量 ============
    public const int SKP_Silk_MAX_ORDER_LPC = 16;      /* schur()/k2a() 的 LPC 分析最大阶数 */
    public const int SKP_Silk_MAX_CORRELATION_LENGTH = 640;

    // ============ 基音估计器复杂度（SigProc_FIX.h） ============
    public const int SKP_Silk_PITCH_EST_MIN_COMPLEX = 0;
    public const int SKP_Silk_PITCH_EST_MID_COMPLEX = 1;
    public const int SKP_Silk_PITCH_EST_MAX_COMPLEX = 2;

    // ============ pitch_est_defines.h ============
    public const int PITCH_EST_SHORTLAG_BIAS_Q15 = 6554;     /* 0.2f，对数加权用 */
    public const int PITCH_EST_PREVLAG_BIAS_Q15 = 6554;     /* 前一 lag 偏置 */
    public const int PITCH_EST_FLATCONTOUR_BIAS_Q20 = 52429; /* 0.05f */

    // ============ common_pitch_est_defines.h ============
    public const int PITCH_EST_MAX_FS_KHZ = 24;                    /* 基音估计使用的最大采样率 */
    public const int PITCH_EST_FRAME_LENGTH_MS = 40;               /* 40 ms */
    public const int PITCH_EST_MAX_FRAME_LENGTH = PITCH_EST_FRAME_LENGTH_MS * PITCH_EST_MAX_FS_KHZ;
    public const int PITCH_EST_MAX_FRAME_LENGTH_ST_1 = PITCH_EST_MAX_FRAME_LENGTH >> 2;
    public const int PITCH_EST_MAX_FRAME_LENGTH_ST_2 = PITCH_EST_MAX_FRAME_LENGTH >> 1;
    // PITCH_EST_MAX_SF_FRAME_LENGTH 引用了从未定义的 PITCH_EST_SUB_FRAME（上游死代码，不移植）
    public const int PITCH_EST_MAX_LAG_MS = 18;                    /* 18 ms -> 56 Hz */
    public const int PITCH_EST_MIN_LAG_MS = 2;                     /* 2 ms -> 500 Hz */
    public const int PITCH_EST_MAX_LAG = PITCH_EST_MAX_LAG_MS * PITCH_EST_MAX_FS_KHZ;
    public const int PITCH_EST_MIN_LAG = PITCH_EST_MIN_LAG_MS * PITCH_EST_MAX_FS_KHZ;
    public const int PITCH_EST_NB_SUBFR = 4;
    public const int PITCH_EST_D_SRCH_LENGTH = 24;
    public const int PITCH_EST_MAX_DECIMATE_STATE_LENGTH = 7;
    public const int PITCH_EST_NB_STAGE3_LAGS = 5;
    public const int PITCH_EST_NB_CBKS_STAGE2 = 3;
    public const int PITCH_EST_NB_CBKS_STAGE2_EXT = 11;
    public const int PITCH_EST_CB_mn2 = 1;
    public const int PITCH_EST_CB_mx2 = 2;
    public const int PITCH_EST_NB_CBKS_STAGE3_MAX = 34;
    public const int PITCH_EST_NB_CBKS_STAGE3_MID = 24;
    public const int PITCH_EST_NB_CBKS_STAGE3_MIN = 16;

    /* NLSF 余弦表尺寸（SigProc_FIX.h） */
    public const int LSF_COS_TAB_SZ_FIX = 128;

    // 行/列访问：C 宏 matrix_ptr/matrix_c_ptr 是左值，移植为索引计算方法。
    //   读：A[matrix_idx(row, column, N)]；写：A[matrix_idx(row, column, N)] = v;
    public static int matrix_idx(int row, int column, int N) => row * N + column;
    public static int matrix_c_idx(int row, int column, int M) => row + M * column;
}

/// <summary>interface/SKP_Silk_errors.h —— 错误码。</summary>
internal static class SKP_Silk_errors
{
    public const int SKP_SILK_NO_ERROR = 0;

    /* 编码器错误 */
    public const int SKP_SILK_ENC_INPUT_INVALID_NO_OF_SAMPLES = -1;
    public const int SKP_SILK_ENC_FS_NOT_SUPPORTED = -2;
    public const int SKP_SILK_ENC_PACKET_SIZE_NOT_SUPPORTED = -3;
    public const int SKP_SILK_ENC_PAYLOAD_BUF_TOO_SHORT = -4;
    public const int SKP_SILK_ENC_INVALID_LOSS_RATE = -5;
    public const int SKP_SILK_ENC_INVALID_COMPLEXITY_SETTING = -6;
    public const int SKP_SILK_ENC_INVALID_INBAND_FEC_SETTING = -7;
    public const int SKP_SILK_ENC_INVALID_DTX_SETTING = -8;
    public const int SKP_SILK_ENC_INTERNAL_ERROR = -9;

    /* 解码器错误 */
    public const int SKP_SILK_DEC_INVALID_SAMPLING_FREQUENCY = -10;
    public const int SKP_SILK_DEC_PAYLOAD_TOO_LARGE = -11;
    public const int SKP_SILK_DEC_PAYLOAD_ERROR = -12;
}
