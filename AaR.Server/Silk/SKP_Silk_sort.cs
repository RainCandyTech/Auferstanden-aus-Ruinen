#nullable disable
// ***********************************************************************
// SKP_Silk_sort.cs —— 移植自 SILK SDK v1.0.9：src/SKP_Silk_sort.c
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

namespace AaR.Server.Silk;

/// <summary>SKP_Silk_sort.c —— 插入排序（对几乎有序的数组较快）。</summary>
internal static class SKP_Silk_sort
{
    /* Insertion sort (fast for already almost sorted arrays):   */
    /* Best case:  O(n)   for an already sorted array            */
    /* Worst case: O(n^2) for an inversely sorted array          */

    // PORT-NOTE: C 的 a 与 index 都是指向缓冲的数组参数，按 §3 一律改为 数组 + 偏移。
    // decreasing_int16 版本中 C 语句 a[ j + 1 ] = value（int 值截回 int16）
    // 在 C# 必须显式 (short) 截断（§2）。
    public static void SKP_Silk_insertion_sort_increasing(
        int[] a, int aOff,              /* I/O:  Unsorted / Sorted vector               */
        int[] index, int indexOff,      /* O:    Index vector for the sorted elements   */
        int L,                          /* I:    Vector length                          */
        int K                           /* I:    Number of correctly sorted output positions   */
    )
    {
        int value;
        int i, j;

        /* Write start indices in index vector */
        for (i = 0; i < K; i++)
        {
            index[indexOff + i] = i;
        }

        /* Sort vector elements by value, increasing order */
        for (i = 1; i < K; i++)
        {
            value = a[aOff + i];
            for (j = i - 1; (j >= 0) && (value < a[aOff + j]); j--)
            {
                a[aOff + j + 1] = a[aOff + j];             /* Shift value */
                index[indexOff + j + 1] = index[indexOff + j];  /* Shift index */
            }
            a[aOff + j + 1] = value;    /* Write value */
            index[indexOff + j + 1] = i;    /* Write index */
        }

        /* If less than L values are asked for, check the remaining values, */
        /* but only spend CPU to ensure that the K first values are correct */
        for (i = K; i < L; i++)
        {
            value = a[aOff + i];
            if (value < a[aOff + K - 1])
            {
                for (j = K - 2; (j >= 0) && (value < a[aOff + j]); j--)
                {
                    a[aOff + j + 1] = a[aOff + j];             /* Shift value */
                    index[indexOff + j + 1] = index[indexOff + j];  /* Shift index */
                }
                a[aOff + j + 1] = value;    /* Write value */
                index[indexOff + j + 1] = i; /* Write index */
            }
        }
    }

    public static void SKP_Silk_insertion_sort_decreasing_int16(
        short[] a, int aOff,            /* I/O: Unsorted / Sorted vector                */
        int[] index, int indexOff,      /* O:   Index vector for the sorted elements    */
        int L,                           /* I:   Vector length                           */
        int K                            /* I:   Number of correctly sorted output positions    */
    )
    {
        int i, j;
        int value;

        /* Write start indices in index vector */
        for (i = 0; i < K; i++)
        {
            index[indexOff + i] = i;
        }

        /* Sort vector elements by value, decreasing order */
        for (i = 1; i < K; i++)
        {
            value = a[aOff + i];
            for (j = i - 1; (j >= 0) && (value > a[aOff + j]); j--)
            {
                a[aOff + j + 1] = (short)a[aOff + j];            /* Shift value */
                index[indexOff + j + 1] = index[indexOff + j];   /* Shift index */
            }
            a[aOff + j + 1] = (short)value;    /* Write value */
            index[indexOff + j + 1] = i;      /* Write index */
        }

        /* If less than L values are asked for, check the remaining values, */
        /* but only spend CPU to ensure that the K first values are correct */
        for (i = K; i < L; i++)
        {
            value = a[aOff + i];
            if (value > a[aOff + K - 1])
            {
                for (j = K - 2; (j >= 0) && (value > a[aOff + j]); j--)
                {
                    a[aOff + j + 1] = (short)a[aOff + j];            /* Shift value */
                    index[indexOff + j + 1] = index[indexOff + j];   /* Shift index */
                }
                a[aOff + j + 1] = (short)value;    /* Write value */
                index[indexOff + j + 1] = i;       /* Write index */
            }
        }
    }

    public static void SKP_Silk_insertion_sort_increasing_all_values(
        int[] a, int aOff,              /* I/O: Unsorted / Sorted vector                */
        int L                            /* I:   Vector length                           */
    )
    {
        int value;
        int i, j;

        /* Sort vector elements by value, increasing order */
        for (i = 1; i < L; i++)
        {
            value = a[aOff + i];
            for (j = i - 1; (j >= 0) && (value < a[aOff + j]); j--)
            {
                a[aOff + j + 1] = a[aOff + j];    /* Shift value */
            }
            a[aOff + j + 1] = value;    /* Write value */
        }
    }
}
