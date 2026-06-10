using Unity.Collections;
using Unity.Entities;

namespace DotsObserver
{
    public struct ObserverBatchData<T> where T : unmanaged, IComponentData
    {
        public Entity WatchedEntity;
        public ObserverConfig Config;
        public NativeParallelHashMap<Entity, ObserverEntityState<T>> State;
        public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<ChangeEvent<T>>.ParallelWriter Events;
        public NativeArray<int> EventCounter;
        public int MaxWriteEvents;
    }
}