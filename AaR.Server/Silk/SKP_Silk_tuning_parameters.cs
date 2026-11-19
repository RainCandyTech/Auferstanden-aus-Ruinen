#nullable disable
// ***********************************************************************
// SKP_Silk_tuning_parameters.cs —— 移植自 SILK SDK v1.0.9：
//   src/SKP_Silk_tuning_parameters.h
// Copyright (c) 2006-2012, Skype Limited.（BSD 风格许可，保留原始版权声明）
// 保持 float 常量原值：调用点通过 SKP_FIX_CONST(C, Q) 转 Q 域定点值，
// 该宏按 C 语义（float 乘 → double 加 0.5 → 截断）复现，逐位一致。
// ***********************************************************************

namespace AaR.Server.Silk;

internal static class SKP_Silk_tuning_parameters
{
    /*******************/
    /* 基音估计器 */
    /*******************/

    /* 基音分析白化滤波 LPC 分析的噪底占比 */
    public const float FIND_PITCH_WHITE_NOISE_FRACTION = 1e-3f;

    /* 基音分析白化滤波的带宽扩展 */
    public const float FIND_PITCH_BANDWITH_EXPANSION = 0.99f;

    /* 基音估计器提前退出门限 */
    public const float FIND_PITCH_CORRELATION_THRESHOLD_HC_MODE = 0.7f;
    public const float FIND_PITCH_CORRELATION_THRESHOLD_MC_MODE = 0.75f;
    public const float FIND_PITCH_CORRELATION_THRESHOLD_LC_MODE = 0.8f;

    /*********************/
    /* 线性预测 */
    /*********************/

    /* LPC 分析定义：正则化与带宽扩展 */
    public const float FIND_LPC_COND_FAC = 2.5e-5f;
    public const float FIND_LPC_CHIRP = 0.99995f;

    /* LTP 分析定义 */
    public const float FIND_LTP_COND_FAC = 1e-5f;
    public const float LTP_DAMPING = 0.01f;
    public const float LTP_SMOOTHING = 0.1f;

    /* LTP 量化设置 */
    public const float MU_LTP_QUANT_NB = 0.03f;
    public const float MU_LTP_QUANT_MB = 0.025f;
    public const float MU_LTP_QUANT_WB = 0.02f;
    public const float MU_LTP_QUANT_SWB = 0.016f;

    /***********************/
    /* 高通滤波 */
    /***********************/

    /* 基音频率低端估计的平滑参数 */
    public const float VARIABLE_HP_SMTH_COEF1 = 0.1f;
    public const float VARIABLE_HP_SMTH_COEF2 = 0.015f;

    /* 基音频率低端估计的最小/最大值 */
    public const float VARIABLE_HP_MIN_FREQ = 80.0f;
    public const float VARIABLE_HP_MAX_FREQ = 150.0f;

    /* 进入平滑器前允许的 log2 基音频差上限 */
    public const float VARIABLE_HP_MAX_DELTA_FREQ = 0.4f;

    /***********/
    /* 杂项 */
    /***********/

    /* 计入有效语音帧所需的活动度 */
    public const float WB_DETECT_ACTIVE_SPEECH_LEVEL_THRES = 0.7f;

    public const float SPEECH_ACTIVITY_DTX_THRES = 0.1f;

    /* LBRR 启用的语音活动度阈值（需调优） */
    public const float LBRR_SPEECH_ACTIVITY_THRES = 0.5f;

    /*************************/
    /* 感知参数 */
    /*************************/

    /* 低语音活动期间编码 SNR 的削减 */
    public const float BG_SNR_DECR_dB = 4.0f;

    /* 有声语音期间量化噪声削减因子 */
    public const float HARM_SNR_INCR_dB = 2.0f;

    /* 无声稀疏信号量化噪声削减因子 */
    public const float SPARSE_SNR_INCR_dB = 2.0f;

    /* 无声帧改用低量化偏移的稀疏度阈值 */
    public const float SPARSENESS_THRESHOLD_QNT_OFFSET = 0.75f;

    /* 变形控制 */
    public const float WARPING_MULTIPLIER = 0.015f;

    /* 加到第一个自相关值上的占比 */
    public const float SHAPE_WHITE_NOISE_FRACTION = 1e-5f;

    /* 噪声整形滤波器啁啾因子 */
    public const float BANDWIDTH_EXPANSION = 0.95f;

    /* 低码率下分析/合成噪声整形滤波啁啾因子之差 */
    public const float LOW_RATE_BANDWIDTH_EXPANSION_DELTA = 0.01f;

    /* 擦音增益削减 */
    public const float DE_ESSER_COEF_SWB_dB = 2.0f;
    public const float DE_ESSER_COEF_WB_dB = 1.0f;

    /* 低码率额外谐波增强（信号整形） */
    public const float LOW_RATE_HARMONIC_BOOST = 0.1f;

    /* 噪声输入信号的额外谐波增强（信号整形） */
    public const float LOW_INPUT_QUALITY_HARMONIC_BOOST = 0.1f;

    /* 谐波噪声整形 */
    public const float HARMONIC_SHAPING = 0.3f;

    /* 高码率或噪声输入的额外谐波噪声整形 */
    public const float HIGH_RATE_OR_LOW_QUALITY_HARMONIC_SHAPING = 0.2f;

    /* 噪声向高频整形的参数 */
    public const float HP_NOISE_COEF = 0.3f;

    /* 有声语音期间噪声更向高频整形的参数 */
    public const float HARM_HP_NOISE_COEF = 0.35f;

    /* 输入信号高通倾斜参数 */
    public const float INPUT_TILT = 0.05f;

    /* 高码率下输入信号额外高通倾斜参数 */
    public const float HIGH_RATE_INPUT_TILT = 0.1f;

    /* 极低频噪声削减参数 */
    public const float LOW_FREQ_SHAPING = 3.0f;

    /* 低频低 SNR 信号减少极低频噪声削减 */
    public const float LOW_QUALITY_LOW_FREQ_SHAPING_DECR = 0.5f;

    /* 限定量化步长下限的噪底 */
    public const float NOISE_FLOOR_dB = 4.0f;

    /* 相对有效语音增益级的噪底 */
    public const float RELATIVE_MIN_GAIN_dB = -50.0f;

    /* 有效语音增益级估计的子帧平滑系数（越小越平滑） */
    public const float GAIN_SMOOTHING_COEF = 1e-3f;

    /* HarmBoost/HarmShapeGain/Tilt 的子帧平滑系数（越小越平滑） */
    public const float SUBFR_SMTH_COEF = 0.4f;

    /* 残差量化器 R/D 权衡参数 */
    public const float LAMBDA_OFFSET = 1.2f;
    public const float LAMBDA_SPEECH_ACT = -0.3f;
    public const float LAMBDA_DELAYED_DECISIONS = -0.05f;
    public const float LAMBDA_INPUT_QUALITY = -0.2f;
    public const float LAMBDA_CODING_QUALITY = -0.1f;
    public const float LAMBDA_QUANT_OFFSET = 1.5f;
}
