#nullable disable
// ***********************************************************************
// SKP_Silk_solve_LS_FIX.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_solve_LS_FIX.c
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
using static AaR.Server.Silk.SKP_Silk_Inlines;

namespace AaR.Server.Silk;

/// <summary>
/// SKP_Silk_solve_LS_FIX.c —— Solves Ax = b, assuming A is symmetric (LDL factorization).
///（Copyright (c) 2006-2012, Skype Limited）
/// </summary>
// PORT-NOTE: 本文件函数名（SKP_Silk_solve_LDL_FIX）与文件名不同，无 CS0542 冲突，
// 类名保持 C 文件名。
internal static class SKP_Silk_solve_LS_FIX
{
    /*****************************/
    /* Internal function headers */
    /*****************************/

    /// <summary>对应 C 文件内的局部 typedef struct inv_D_t（LDL 分解中 1/D 的双精度表示）。</summary>
    internal sealed class inv_D_t
    {
        public int Q36_part;
        public int Q48_part;
    }

    /* Solves Ax = b, assuming A is symmetric */
    public static void SKP_Silk_solve_LDL_FIX(
        int[] A, int AOff,               /* I    Pointer to symetric square matrix A         */
        int M,                            /* I    Size of matrix                              */
        int[] b, int bOff,                /* I    Pointer to b vector                         */
        int[] x_Q16, int x_Q16Off         /* O    Pointer to x solution vector                */
    )
    {
        int[] L_Q16 = new int[MAX_MATRIX_SIZE * MAX_MATRIX_SIZE];
        int[] Y = new int[MAX_MATRIX_SIZE];
        // PORT-NOTE: C 的局部 struct 数组（栈上未初始化内存）→ 类引用数组，元素逐一 new
        //（C 代码在读之前必先经 LDL_factorize 写入 Q36/Q48 part，语义等价）。
        inv_D_t[] inv_D = new inv_D_t[MAX_MATRIX_SIZE];
        for (int _i = 0; _i < MAX_MATRIX_SIZE; _i++)
        {
            inv_D[_i] = new inv_D_t();
        }

        /***************************************************
        Factorize A by LDL such that A = L*D*L',
        where L is lower triangular with ones on diagonal
        ****************************************************/
        SKP_Silk_LDL_factorize_FIX(A, AOff, M, L_Q16, 0, inv_D, 0);

        /****************************************************
        * substitute D*L'*x = Y. ie:
        L*D*L'*x = b => L*Y = b <=> Y = inv(L)*b
        ******************************************************/
        SKP_Silk_LS_SolveFirst_FIX(L_Q16, 0, M, b, bOff, Y, 0);

        /****************************************************
        D*L'*x = Y <=> L'*x = inv(D)*Y, because D is
        diagonal just multiply with 1/d_i
        ****************************************************/
        SKP_Silk_LS_divide_Q16_FIX(Y, 0, inv_D, 0, M);

        /****************************************************
        x = inv(L') * inv(D) * Y
        *****************************************************/
        SKP_Silk_LS_SolveLast_FIX(L_Q16, 0, M, Y, 0, x_Q16, x_Q16Off);
    }

    /* Factorize square matrix A into LDL form */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_Silk_LDL_factorize_FIX(
        int[] A, int AOff,                /* I/O Pointer to Symetric Square Matrix */
        int M,                            /* I   Size of Matrix */
        int[] L_Q16, int L_Q16Off,        /* I/O Pointer to Square Upper triangular Matrix */
        inv_D_t[] inv_D, int inv_DOff     /* I/O Pointer to vector holding inverted diagonal elements of D */
    )
    {
        int i, j, k, status, loop_count;
        // PORT-NOTE: C 的指针 ptr1/ptr2（分别沿 A 与 L_Q16 的行步进）→ 数组偏移。
        int ptr1Off, ptr2Off;
        int diag_min_value, tmp_32, err;
        int[] v_Q0 = new int[MAX_MATRIX_SIZE];
        int[] D_Q0 = new int[MAX_MATRIX_SIZE];
        int one_div_diag_Q36, one_div_diag_Q40, one_div_diag_Q48;

        status = 1;
        diag_min_value = SKP_max_32(SKP_SMMUL(SKP_ADD_SAT32(A[AOff + 0], A[AOff + SKP_SMULBB(M, M) - 1]), SKP_FIX_CONST(FIND_LTP_COND_FAC, 31)), 1 << 9);
        for (loop_count = 0; loop_count < M && status == 1; loop_count++)
        {
            status = 0;
            for (j = 0; j < M; j++)
            {
                ptr1Off = L_Q16Off + matrix_idx(j, 0, M);
                tmp_32 = 0;
                for (i = 0; i < j; i++)
                {
                    v_Q0[i] = SKP_SMULWW(D_Q0[i], L_Q16[ptr1Off + i]); /* Q0 */
                    tmp_32 = SKP_SMLAWW(tmp_32, v_Q0[i], L_Q16[ptr1Off + i]); /* Q0 */
                }
                tmp_32 = SKP_SUB32(A[AOff + matrix_idx(j, j, M)], tmp_32);

                if (tmp_32 < diag_min_value)
                {
                    tmp_32 = SKP_SUB32(SKP_SMULBB(loop_count + 1, diag_min_value), tmp_32);
                    /* Matrix not positive semi-definite, or ill conditioned */
                    for (i = 0; i < M; i++)
                    {
                        A[AOff + matrix_idx(i, i, M)] = SKP_ADD32(A[AOff + matrix_idx(i, i, M)], tmp_32);
                    }
                    status = 1;
                    break;
                }
                D_Q0[j] = tmp_32;                         /* always < max(Correlation) */

                /* two-step division */
                one_div_diag_Q36 = SKP_INVERSE32_varQ(tmp_32, 36);                    /* Q36 */
                one_div_diag_Q40 = SKP_LSHIFT(one_div_diag_Q36, 4);                   /* Q40 */
                err = SKP_SUB32(1 << 24, SKP_SMULWW(tmp_32, one_div_diag_Q40));     /* Q24 */
                one_div_diag_Q48 = SKP_SMULWW(err, one_div_diag_Q40);                 /* Q48 */

                /* Save 1/Ds */
                inv_D[inv_DOff + j].Q36_part = one_div_diag_Q36;
                inv_D[inv_DOff + j].Q48_part = one_div_diag_Q48;

                L_Q16[L_Q16Off + matrix_idx(j, j, M)] = 65536; /* 1.0 in Q16 */
                ptr1Off = AOff + matrix_idx(j, 0, M);
                ptr2Off = L_Q16Off + matrix_idx(j + 1, 0, M);
                for (i = j + 1; i < M; i++)
                {
                    tmp_32 = 0;
                    for (k = 0; k < j; k++)
                    {
                        tmp_32 = SKP_SMLAWW(tmp_32, v_Q0[k], L_Q16[ptr2Off + k]); /* Q0 */
                    }
                    tmp_32 = SKP_SUB32(A[ptr1Off + i], tmp_32); /* always < max(Correlation) */

                    /* tmp_32 / D_Q0[j] : Divide to Q16 */
                    L_Q16[L_Q16Off + matrix_idx(i, j, M)] = SKP_ADD32(SKP_SMMUL(tmp_32, one_div_diag_Q48),
                        SKP_RSHIFT(SKP_SMULWW(tmp_32, one_div_diag_Q36), 4));

                    /* go to next column */
                    ptr2Off += M;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_Silk_LS_divide_Q16_FIX(
        int[] T, int TOff,                 /* I/O Numenator vector */
        inv_D_t[] inv_D, int inv_DOff,     /* I   1 / D vector     */
        int M                              /* I   dimension        */
    )
    {
        int i;
        int tmp_32;
        int one_div_diag_Q36, one_div_diag_Q48;

        for (i = 0; i < M; i++)
        {
            one_div_diag_Q36 = inv_D[inv_DOff + i].Q36_part;
            one_div_diag_Q48 = inv_D[inv_DOff + i].Q48_part;

            tmp_32 = T[TOff + i];
            T[TOff + i] = SKP_ADD32(SKP_SMMUL(tmp_32, one_div_diag_Q48), SKP_RSHIFT(SKP_SMULWW(tmp_32, one_div_diag_Q36), 4));
        }
    }

    /* Solve Lx = b, when L is lower triangular and has ones on the diagonal */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_Silk_LS_SolveFirst_FIX(
        int[] L_Q16, int L_Q16Off,         /* I Pointer to Lower Triangular Matrix */
        int M,                              /* I Dim of Matrix equation */
        int[] b, int bOff,                 /* I b Vector */
        int[] x_Q16, int x_Q16Off          /* O x Vector */
    )
    {
        int i, j;
        int ptr32Off;
        int tmp_32;

        for (i = 0; i < M; i++)
        {
            ptr32Off = L_Q16Off + matrix_idx(i, 0, M);
            tmp_32 = 0;
            for (j = 0; j < i; j++)
            {
                tmp_32 = SKP_SMLAWW(tmp_32, L_Q16[ptr32Off + j], x_Q16[x_Q16Off + j]);
            }
            x_Q16[x_Q16Off + i] = SKP_SUB32(b[bOff + i], tmp_32);
        }
    }

    /* Solve L^t*x = b, where L is lower triangular with ones on the diagonal */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SKP_Silk_LS_SolveLast_FIX(
        int[] L_Q16, int L_Q16Off,         /* I Pointer to Lower Triangular Matrix */
        int M,                              /* I Dim of Matrix equation */
        int[] b, int bOff,                 /* I b Vector */
        int[] x_Q16, int x_Q16Off          /* O x Vector */
    )
    {
        int i, j;
        int ptr32Off;
        int tmp_32;

        for (i = M - 1; i >= 0; i--)
        {
            ptr32Off = L_Q16Off + matrix_idx(0, i, M);
            tmp_32 = 0;
            for (j = M - 1; j > i; j--)
            {
                tmp_32 = SKP_SMLAWW(tmp_32, L_Q16[ptr32Off + SKP_SMULBB(j, M)], x_Q16[x_Q16Off + j]);
            }
            x_Q16[x_Q16Off + i] = SKP_SUB32(b[bOff + i], tmp_32);
        }
    }
}
