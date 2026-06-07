using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Утилита хэширования содержимого <see cref="DynamicBuffer{T}"/>.
    ///
    /// <para>
    /// <b>Выбор алгоритма:</b><br/>
    /// По умолчанию используется FNV-1a 32-бит — нет зависимостей, Burst-совместим.<br/>
    /// Для больших буферов (> ~256 байт) рекомендуется xxHash3 64-бит:
    /// лучшее распределение, меньше ложных «изменений», SIMD-векторизуем Burst-ом.
    /// </para>
    ///
    /// <para>
    /// Чтобы переключиться на xxHash3 добавьте в <em>Scripting Define Symbols</em>:
    /// <c>DOTS_OBSERVER_USE_XXHASH3</c>
    /// </para>
    ///
    /// <para>
    /// <b>Коллизии и ложные события:</b><br/>
    /// Ложное «изменилось» — теоретически при коллизии хэша. xxHash3 (64-битный)
    /// имеет вероятность коллизии примерно в 2³² раз ниже, чем 32-битный FNV-1a,
    /// что особенно заметно для буферов размером &gt; 64 байт.
    /// </para>
    /// </summary>
    internal static class BufferHashUtility
    {
        internal const uint FnvOffsetBasis = 2166136261u;
        internal const uint FnvPrime       = 16777619u;
        
        /// <summary>
        /// Возвращает 32-битный хэш содержимого буфера.
        /// </summary>
        internal static uint ComputeHash<T>(DynamicBuffer<T> buffer)
            where T : unmanaged, IBufferElementData
        {
#if DOTS_OBSERVER_USE_FNV1A
            return ComputeFnv1aHash(buffer);
#else
            return (uint)(ComputeXxHash3(buffer) & 0xFFFF_FFFF);
#endif
        }
        
        /// <summary>
        /// 32-битный FNV-1a хэш по всем байтам буфера.
        /// Возвращает <see cref="FnvOffsetBasis"/> для пустого буфера.
        /// </summary>
        internal static unsafe uint ComputeFnv1aHash<T>(DynamicBuffer<T> buffer)
            where T : unmanaged, IBufferElementData
        {
            if (buffer.Length == 0) return FnvOffsetBasis;

            int   byteCount = buffer.Length * UnsafeUtility.SizeOf<T>();
            byte* ptr       = (byte*)buffer.GetUnsafeReadOnlyPtr();

            uint hash = FnvOffsetBasis;
            for (int b = 0; b < byteCount; b++)
            {
                hash ^= ptr[b];
                hash *= FnvPrime;
            }
            return hash;
        }
        
#if !DOTS_OBSERVER_USE_FNV1A
        /// <summary>
        /// 64-битный xxHash3 по всем байтам буфера.
        ///
        /// <para>
        /// Использует <c>Unity.Collections.xxHash3.Hash64(void*, int)</c> —
        /// SIMD-векторизуем Burst-ом на SSE2 / NEON. Нет heap-аллокаций.
        /// </para>
        ///
        /// <para>
        /// Требует <c>Unity.Collections</c> ≥ 1.1 и
        /// <c>DOTS_OBSERVER_USE_XXHASH3</c> в Scripting Define Symbols.
        /// </para>
        /// </summary>
        internal static unsafe ulong ComputeXxHash3<T>(DynamicBuffer<T> buffer)
            where T : unmanaged, IBufferElementData
        {
            if (buffer.Length == 0) return 0UL;

            int   byteCount = buffer.Length * UnsafeUtility.SizeOf<T>();
            void* ptr       = buffer.GetUnsafeReadOnlyPtr();
            
            var hash64 = xxHash3.Hash64(ptr, byteCount);
            
            return ((ulong) hash64.x << 32) | hash64.y;
        }
#endif
    }
}