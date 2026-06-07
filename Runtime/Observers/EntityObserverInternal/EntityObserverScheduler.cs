using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DotsObserver
{
    /// <summary>
    /// Абстракция диспетчеризации джоба обновления.
    /// Живёт только на main thread — Burst не требуется.
    /// </summary>
    internal interface IObserverUpdateScheduler<T> where T : unmanaged, IComponentData
    {
        JobHandle Schedule(
            ComponentTypeHandle<T> componentHandle,
            EntityTypeHandle entityHandle,
            NativeParallelHashMap<Entity, ObserverEntityState<T>> state,
            NativeParallelHashMap<Entity, byte> currentSet,
            NativeQueue<ChangeEvent<T>>.ParallelWriter events,
            uint lastSystemVersion,
            uint globalSystemVersion,
            ObserverConfig config,
            Entity watchedEntity,
            EntityQuery query,
            NativeArray<int> eventCounter,
            int maxWriteEvents,
            JobHandle dependency);
    }

    /// <summary>
    /// Диспетчер для обычных IComponentData (без IEnableableComponent).
    /// </summary>
    internal sealed class RegularUpdateScheduler<T> : IObserverUpdateScheduler<T>
        where T : unmanaged, IComponentData
    {
        public JobHandle Schedule(
            ComponentTypeHandle<T> componentHandle,
            EntityTypeHandle entityHandle,
            NativeParallelHashMap<Entity, ObserverEntityState<T>> state,
            NativeParallelHashMap<Entity, byte> currentSet,
            NativeQueue<ChangeEvent<T>>.ParallelWriter events,
            uint lastSystemVersion, 
            uint globalSystemVersion,
            ObserverConfig config, 
            Entity watchedEntity,
            EntityQuery query,
            NativeArray<int> eventCounter, 
            int maxWriteEvents,
            JobHandle dependency)
        {
            var job = new EntityObserverUpdateJob<T>
            {
                ComponentHandle     = componentHandle,
                EntityHandle        = entityHandle,
                State               = state,
                CurrentSet          = currentSet,
                Events              = events,
                LastSystemVersion   = lastSystemVersion,
                GlobalSystemVersion = globalSystemVersion,
                Config              = config,
                WatchedEntity       = watchedEntity,
                EventCounter        = eventCounter,
                MaxWriteEvents      = maxWriteEvents
            };
            return job.Schedule(query, dependency);
        }
    }

    /// <summary>
    /// Диспетчер для IEnableableComponent — имеет нужный constraint,
    /// чтобы джоб мог вызывать chunk.IsComponentEnabled.
    /// </summary>
    internal sealed class EnableableUpdateScheduler<T> : IObserverUpdateScheduler<T>
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        public JobHandle Schedule(
            ComponentTypeHandle<T> componentHandle,
            EntityTypeHandle entityHandle,
            NativeParallelHashMap<Entity, ObserverEntityState<T>> state,
            NativeParallelHashMap<Entity, byte> currentSet,
            NativeQueue<ChangeEvent<T>>.ParallelWriter events,
            uint lastSystemVersion, 
            uint globalSystemVersion,
            ObserverConfig config, 
            Entity watchedEntity,
            EntityQuery query,
            NativeArray<int> eventCounter, 
            int maxWriteEvents,
            JobHandle dependency)
        {
            var job = new EntityObserverEnableableUpdateJob<T>
            {
                ComponentHandle     = componentHandle,
                EntityHandle        = entityHandle,
                State               = state,
                CurrentSet          = currentSet,
                Events              = events,
                LastSystemVersion   = lastSystemVersion,
                GlobalSystemVersion = globalSystemVersion,
                Config              = config,
                WatchedEntity       = watchedEntity,
                EventCounter        = eventCounter,
                MaxWriteEvents      = maxWriteEvents
            };
            return job.Schedule(query, dependency);
        }
    }
    
    /// <summary>
    /// Диспетчер для T : IEquatable{T} — использует T.Equals() вместо MemCmp в Burst-джобе.
    /// Регистрируется через рефлексию единожды; runtime cost = 0.
    /// </summary>
    internal sealed class EquatableUpdateScheduler<T> : IObserverUpdateScheduler<T>
        where T : unmanaged, IComponentData, IEquatable<T>
    {
        public JobHandle Schedule(
            ComponentTypeHandle<T> componentHandle, EntityTypeHandle entityHandle,
            NativeParallelHashMap<Entity, ObserverEntityState<T>> state,
            NativeParallelHashMap<Entity, byte> currentSet,
            NativeQueue<ChangeEvent<T>>.ParallelWriter events,
            uint lastSystemVersion, 
            uint globalSystemVersion,
            ObserverConfig config, 
            Entity watchedEntity,
            EntityQuery query,
            NativeArray<int> eventCounter, 
            int maxWriteEvents,
            JobHandle dependency)
        {
            var job = new EntityObserverUpdateJobEquatable<T>
            {
                ComponentHandle     = componentHandle,
                EntityHandle        = entityHandle,
                State               = state,
                CurrentSet          = currentSet,
                Events              = events,
                LastSystemVersion   = lastSystemVersion,
                GlobalSystemVersion = globalSystemVersion,
                Config              = config,
                WatchedEntity       = watchedEntity,
                EventCounter        = eventCounter,
                MaxWriteEvents      = maxWriteEvents
            };
            return job.Schedule(query, dependency);
        }
    }
    
    /// <summary>
    /// Фабрика: возвращает нужный диспетчер по runtime-информации о типе T.
    /// Рефлексия срабатывает ровно один раз на тип — результат кешируется.
    /// </summary>
    internal static class UpdateSchedulerFactory
    {
        private static readonly Dictionary<Type, object> _enableableCache = new();
        private static readonly Dictionary<Type, object> _equatableCache  = new();

        public static IObserverUpdateScheduler<T> Create<T>(bool trackEnableable)
            where T : unmanaged, IComponentData
        {
            if (trackEnableable && TypeTraits<T>.IsEnableable)
            {
                var type = typeof(T);
                if (!_enableableCache.TryGetValue(type, out var cached))
                {
                    cached = Activator.CreateInstance(
                        typeof(EnableableUpdateScheduler<>).MakeGenericType(type));
                    _enableableCache[type] = cached;
                }
                return (IObserverUpdateScheduler<T>)cached;
            }
            
            if (TypeTraits<T>.IsIEquatable)
            {
                var type = typeof(T);
                if (!_equatableCache.TryGetValue(type, out var cached))
                {
                    cached = Activator.CreateInstance(
                        typeof(EquatableUpdateScheduler<>).MakeGenericType(type));
                    _equatableCache[type] = cached;
                }
                return (IObserverUpdateScheduler<T>)cached;
            }

            return new RegularUpdateScheduler<T>();
        }
    }
}