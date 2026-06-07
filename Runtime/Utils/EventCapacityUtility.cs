using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace DotsObserver
{
    internal static class EventCapacityUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool TryReserveSlot(NativeArray<int> counter, int max)
        {
            if (max <= 0) return true; // 0 = без ограничения
            int* ptr = (int*)counter
                .GetUnsafePtr();
            int after = Interlocked.Increment(ref *ptr);
            if (after <= max) return true;
            Interlocked.Decrement(ref *ptr); // откат - слот не получен
            return false;
        }
    }
}