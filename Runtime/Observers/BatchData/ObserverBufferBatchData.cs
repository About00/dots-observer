using Unity.Collections;
using Unity.Entities;

namespace DotsObserver
{
    public struct ObserverBufferBatchData<T> where T : unmanaged, IBufferElementData
    {
        public Entity WatchedEntity;
        public ObserverConfig Config;
        public NativeParallelHashMap<Entity, ObserverBufferState<T>> State;
        public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<BufferChangeEvent<T>>.ParallelWriter Events;
        public NativeArray<int> EventCounter;
        public int MaxWriteEvents;
    }
}