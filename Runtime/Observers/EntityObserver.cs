using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Burst.Intrinsics;
using Unity.Jobs;

namespace DotsObserver
{
    public struct EntityObserver<T> : IDisposable where T : unmanaged, IComponentData
    {
        private NativeParallelHashMap<Entity, ObserverEntityState<T>> _state;
        private NativeQueue<ChangeEvent<T>> _events;
        private NativeParallelHashMap<Entity, byte> _currentSet;
        private EntityQuery _query;
        private ComponentTypeHandle<T> _handle;
        private EntityTypeHandle _entityHandle;
        private uint _lastSystemVersion;
        private ObserverConfig _config;
        private Entity _watchedEntity;
        private int _frameCounter;
        private int _processedCount;
        private int _droppedCount;
        private IObserverUpdateScheduler<T> _scheduler;
        private NativeList<Entity> _entitiesToRemove;
        private NativeArray<int> _eventCounterArr;
        private JobHandle _pendingHandle;
        private bool _isEnabled;
        
        public ObserverConfig Config => _config;
        public Entity WatchedEntity => _watchedEntity;
        public NativeParallelHashMap<Entity, ObserverEntityState<T>> State => _state;
        public NativeParallelHashMap<Entity, byte> CurrentSet => _currentSet;
        public NativeQueue<ChangeEvent<T>> Events => _events;
        public NativeArray<int> EventCounter => _eventCounterArr;
        public int MaxWriteEvents => _config.MaxEventsPerFrame;
        public bool IsEnabled => _isEnabled;
        public int FrameCounter => _frameCounter;

        internal void IncrementFrameCounter() => _frameCounter++;
        internal void ResetFrameCounter() => _frameCounter = 0;
        internal void ClearCurrentSet() => _currentSet.Clear();
        internal void ResetEventCounter() => _eventCounterArr[0] = 0;
        internal void SetEnabled(bool value) => _isEnabled = value;
        
        // === ISystem API ===
        public void OnCreate(ref SystemState state, in ObserverConfig config, Entity watchedEntity = default)
        {
            _config = config;
            _watchedEntity = watchedEntity;
            _frameCounter = config.UpdateInterval - 1;
            _processedCount = 0;
            
            var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<T>();
            if (config.TrackEnableable && TypeTraits<T>.IsEnableable)
                builder.WithOptions(EntityQueryOptions.IgnoreComponentEnabledState);
            _query = state.GetEntityQuery(builder);
            builder.Dispose();

            _handle = state.GetComponentTypeHandle<T>(true);
            _entityHandle = state.GetEntityTypeHandle();

            int cap = config.MaxEventsPerFrame > 0 ? config.MaxEventsPerFrame : 64;
            _state            = new NativeParallelHashMap<Entity, ObserverEntityState<T>>(cap, Allocator.Persistent);
            _events           = new NativeQueue<ChangeEvent<T>>(Allocator.Persistent);
            _currentSet       = new NativeParallelHashMap<Entity, byte>(cap, Allocator.Persistent);
            _entitiesToRemove = new NativeList<Entity>(cap, Allocator.Persistent);
            _eventCounterArr  = new NativeArray<int>(1, Allocator.Persistent);

            _scheduler = UpdateSchedulerFactory.Create<T>(config.TrackEnableable);
            _isEnabled = true;
        }
        
        public void OnCreate(ref SystemState state, in ObserverConfig config, EntityQuery customQuery, Entity watchedEntity = default)
        {
            _config        = config;
            _watchedEntity = watchedEntity;
            _frameCounter  = config.UpdateInterval - 1;
            _processedCount = 0;

            _query        = customQuery;
            _handle       = state.GetComponentTypeHandle<T>(true);
            _entityHandle = state.GetEntityTypeHandle();

            int cap = config.MaxEventsPerFrame > 0 ? config.MaxEventsPerFrame : 64;
            _state            = new NativeParallelHashMap<Entity, ObserverEntityState<T>>(cap, Allocator.Persistent);
            _events           = new NativeQueue<ChangeEvent<T>>(Allocator.Persistent);
            _currentSet       = new NativeParallelHashMap<Entity, byte>(cap, Allocator.Persistent);
            _entitiesToRemove = new NativeList<Entity>(cap, Allocator.Persistent);
            _eventCounterArr  = new NativeArray<int>(1, Allocator.Persistent);

            _scheduler = UpdateSchedulerFactory.Create<T>(config.TrackEnableable);
            _isEnabled = true;
        }

        public void Update(ref SystemState state)
        {
            if (!_isEnabled) return;
            
            _frameCounter++;
            if (_frameCounter < _config.UpdateInterval) return;
            _frameCounter = 0;
            
            state.Dependency.Complete();
            _events.Clear();
            _currentSet.Clear();
            _droppedCount = 0;

            if (_config.ExecutionMode == ObserverExecutionMode.MainThread)
            {
                UpdateMainThread(ref state);
                return;
            }
            
            _handle.Update(ref state);
            _entityHandle.Update(ref state);
            _lastSystemVersion = state.LastSystemVersion;

            _eventCounterArr[0] = 0;
            JobHandle updateHandle = _scheduler.Schedule(
                _handle, _entityHandle, _state, _currentSet,
                _events.AsParallelWriter(),
                _lastSystemVersion, state.GlobalSystemVersion,
                _config, _watchedEntity, _query,
                _eventCounterArr,
                _config.MaxEventsPerFrame,
                state.Dependency);

            var cleanupJob = new EntityObserverCleanupJob<T>
            {
                State                = _state,
                CurrentSet           = _currentSet,
                Events               = _events,
                EntitiesToRemove     = _entitiesToRemove,
                GlobalSystemVersion  = state.GlobalSystemVersion,
                TrackEntityLifecycle = _config.TrackEntityLifecycle,
                EventCounter         = _eventCounterArr,
                MaxWriteEvents       = _config.MaxEventsPerFrame
            };

            _pendingHandle = state.Dependency = cleanupJob.Schedule(updateHandle);
        }

        public NativeArray<ChangeEvent<T>> UpdateAndFlush(ref SystemState state, Allocator allocator)
        {
            Update(ref state);
            state.Dependency.Complete();
            return FlushEvents(allocator);
        }
        
        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();
            
            if (_state.IsCreated)            _state.Dispose();
            if (_events.IsCreated)           _events.Dispose();
            if (_currentSet.IsCreated)       _currentSet.Dispose();
            if (_entitiesToRemove.IsCreated) _entitiesToRemove.Dispose();
            if (_eventCounterArr.IsCreated)  _eventCounterArr.Dispose();
        }

        // === SystemBase API (для MVVM / PresentationSystemGroup) ===
        public void OnCreate(SystemBase system, in ObserverConfig config, Entity watchedEntity = default)
        {
            _config = config;
            _watchedEntity = watchedEntity;
            _frameCounter = config.UpdateInterval - 1;
            _processedCount = 0;

            if (config.TrackEnableable && TypeTraits<T>.IsEnableable)
            {
                var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<T>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState);
                _query = system.CheckedStateRef.GetEntityQuery(builder);
                builder.Dispose();
            }
            else
            {
                _query = system.CheckedStateRef.GetEntityQuery(ComponentType.ReadOnly<T>());
            }

            _handle = system.GetComponentTypeHandle<T>(true);
            _entityHandle = system.GetEntityTypeHandle();

            int cap = config.MaxEventsPerFrame > 0 ? config.MaxEventsPerFrame : 64;
            _state      = new NativeParallelHashMap<Entity, ObserverEntityState<T>>(cap, Allocator.Persistent);
            _events     = new NativeQueue<ChangeEvent<T>>(Allocator.Persistent);
            _currentSet         = new NativeParallelHashMap<Entity, byte>(cap, Allocator.Persistent);
            _entitiesToRemove   = new NativeList<Entity>(cap, Allocator.Persistent);
            _eventCounterArr    = new NativeArray<int>(1, Allocator.Persistent);

            _scheduler = UpdateSchedulerFactory.Create<T>(config.TrackEnableable);
            _isEnabled = true;
        }
        
        public void OnCreate(SystemBase system, in ObserverConfig config, EntityQuery customQuery, Entity watchedEntity = default)
        {
            _config        = config;
            _watchedEntity = watchedEntity;
            _frameCounter  = config.UpdateInterval - 1;
            _processedCount = 0;

            _query        = customQuery;
            _handle       = system.GetComponentTypeHandle<T>(true);
            _entityHandle = system.GetEntityTypeHandle();

            int cap = config.MaxEventsPerFrame > 0 ? config.MaxEventsPerFrame : 64;
            _state            = new NativeParallelHashMap<Entity, ObserverEntityState<T>>(cap, Allocator.Persistent);
            _events           = new NativeQueue<ChangeEvent<T>>(Allocator.Persistent);
            _currentSet       = new NativeParallelHashMap<Entity, byte>(cap, Allocator.Persistent);
            _entitiesToRemove = new NativeList<Entity>(cap, Allocator.Persistent);
            _eventCounterArr  = new NativeArray<int>(1, Allocator.Persistent);

            _scheduler = UpdateSchedulerFactory.Create<T>(config.TrackEnableable);
            _isEnabled = true;
        }

        public void Update(SystemBase system)
        {
            if (!_isEnabled) return;
            
            _frameCounter++;
            if (_frameCounter < _config.UpdateInterval) return;
            _frameCounter = 0;
            
            system.CheckedStateRef.Dependency.Complete();
            _events.Clear();
            _currentSet.Clear();
            _droppedCount = 0;

            if (_config.ExecutionMode == ObserverExecutionMode.MainThread)
            {
                UpdateMainThread(ref system.CheckedStateRef);
                return;
            }
            
            _handle = system.GetComponentTypeHandle<T>(true);
            _entityHandle = system.GetEntityTypeHandle();
            _lastSystemVersion = system.LastSystemVersion;

            _eventCounterArr[0] = 0;
            JobHandle updateHandle = _scheduler.Schedule(
                _handle, _entityHandle, _state, _currentSet,
                _events.AsParallelWriter(),
                _lastSystemVersion, system.GlobalSystemVersion,
                _config, _watchedEntity, _query,
                _eventCounterArr,
                _config.MaxEventsPerFrame,
                system.CheckedStateRef.Dependency);

            var cleanupJob = new EntityObserverCleanupJob<T>
            {
                State                = _state,
                CurrentSet           = _currentSet,
                Events               = _events,
                EntitiesToRemove     = _entitiesToRemove,
                GlobalSystemVersion  = system.GlobalSystemVersion,
                TrackEntityLifecycle = _config.TrackEntityLifecycle,
                EventCounter         = _eventCounterArr,
                MaxWriteEvents       = _config.MaxEventsPerFrame
            };

            _pendingHandle = system.CheckedStateRef.Dependency = cleanupJob.Schedule(updateHandle);
        }
        
        internal JobHandle ScheduleUpdate(SystemBase system, JobHandle inputDep)
        {
            if (!_isEnabled) return inputDep;
            
            _frameCounter++;
            if (_frameCounter < _config.UpdateInterval) return inputDep;
            _frameCounter = 0;
            
            _events.Clear();
            _currentSet.Clear();
            _droppedCount = 0;

            if (_config.ExecutionMode == ObserverExecutionMode.MainThread)
            {
                inputDep.Complete();
                UpdateMainThread(ref system.CheckedStateRef);
                return default;
            }

            _handle = system.GetComponentTypeHandle<T>(true);
            _entityHandle = system.GetEntityTypeHandle();
            _lastSystemVersion = system.LastSystemVersion;

            _eventCounterArr[0] = 0;
            JobHandle updateHandle = _scheduler.Schedule(
                _handle, _entityHandle, _state, _currentSet,
                _events.AsParallelWriter(),
                _lastSystemVersion, system.GlobalSystemVersion,
                _config, _watchedEntity, _query,
                _eventCounterArr,
                _config.MaxEventsPerFrame,
                inputDep);

            var cleanupJob = new EntityObserverCleanupJob<T>
            {
                State                = _state,
                CurrentSet           = _currentSet,
                Events               = _events,
                EntitiesToRemove     = _entitiesToRemove,
                GlobalSystemVersion  = system.GlobalSystemVersion,
                TrackEntityLifecycle = _config.TrackEntityLifecycle,
                EventCounter         = _eventCounterArr,
                MaxWriteEvents       = _config.MaxEventsPerFrame
            };

            return _pendingHandle = cleanupJob.Schedule(updateHandle);
        }
        
        public NativeArray<ChangeEvent<T>> UpdateAndFlush(SystemBase system, Allocator allocator)
        {
            Update(system);
            system.CheckedStateRef.Dependency.Complete();
            return FlushEvents(allocator);
        }
        
        public void OnDestroy(SystemBase system)
        {
            system.CheckedStateRef.Dependency.Complete();
            
            if (_state.IsCreated)            _state.Dispose();
            if (_events.IsCreated)           _events.Dispose();
            if (_currentSet.IsCreated)       _currentSet.Dispose();
            if (_entitiesToRemove.IsCreated) _entitiesToRemove.Dispose();
            if (_eventCounterArr.IsCreated)  _eventCounterArr.Dispose();
        }
        
        // === Shared ===
        
        /// <summary>
        /// Синхронное обновление на main thread без IJobChunk.
        /// TrackEnableable в этом режиме не поддерживается.
        /// </summary>
        private unsafe void UpdateMainThread(ref SystemState state)
        {
            var em             = state.EntityManager;
            uint globalVersion = state.GlobalSystemVersion;

            using var entities = _query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (_watchedEntity != Entity.Null && entity != _watchedEntity) continue;

                _currentSet.TryAdd(entity, 1);
                var current = em.GetComponentData<T>(entity);

                if (_state.TryGetValue(entity, out var obsState))
                {
                    bool valueChanged = UnsafeUtility.MemCmp(
                        UnsafeUtility.AddressOf(ref current),
                        UnsafeUtility.AddressOf(ref obsState.PreviousValue),
                        UnsafeUtility.SizeOf<T>()) != 0;

                    if (valueChanged)
                    {
                        _events.Enqueue(new ChangeEvent<T>
                        {
                            Entity        = entity,
                            PreviousValue = obsState.PreviousValue,
                            NewValue      = current,
                            Type          = ChangeEventType.Changed,
                            SystemVersion = globalVersion
                        });
                        obsState.PreviousValue = current;
                        _state[entity] = obsState;
                    }
                }
                else
                {
                    if (_config.TrackEntityLifecycle)
                    {
                        _events.Enqueue(new ChangeEvent<T>
                        {
                            Entity        = entity,
                            PreviousValue = default,
                            NewValue      = current,
                            Type          = ChangeEventType.Created,
                            SystemVersion = globalVersion
                        });
                    }
                    _state.Add(entity, new ObserverEntityState<T>
                    {
                        PreviousValue = current,
                        Exists        = 1,
                        WasEnabled    = 1
                    });
                }
            }
            
            // Cleanup: найти исчезнувшие entity
            _entitiesToRemove.Clear();
            var en = _state.GetEnumerator();
            while (en.MoveNext())
            {
                if (!_currentSet.ContainsKey(en.Current.Key))
                    _entitiesToRemove.Add(en.Current.Key);
            }

            for (int i = 0; i < _entitiesToRemove.Length; i++)
            {
                var entity = _entitiesToRemove[i];
                if (_config.TrackEntityLifecycle)
                {
                    var obsState = _state[entity];
                    _events.Enqueue(new ChangeEvent<T>
                    {
                        Entity        = entity,
                        PreviousValue = obsState.PreviousValue,
                        NewValue      = default,
                        Type          = ChangeEventType.Destroyed,
                        SystemVersion = globalVersion
                    });
                }
                _state.Remove(entity);
            }
        }
        
        public NativeArray<ChangeEvent<T>> FlushEvents(Allocator allocator)
        {
            int count = _events.Count;
            
            if (_config.RingQueueCapacity > 0 && count > _config.RingQueueCapacity)
            {
                int toDrop = count - _config.RingQueueCapacity;
                for (int i = 0; i < toDrop; i++) _events.TryDequeue(out _);
                _droppedCount += toDrop;
                count = _config.RingQueueCapacity;
            }
            
            int limit = (_config.MaxQueueSize > 0 && count > _config.MaxQueueSize)
                ? _config.MaxQueueSize
                : count;
            _droppedCount += count - limit;

            var result = new NativeArray<ChangeEvent<T>>(limit, allocator);
            for (int i = 0; i < limit; i++)
            {
                _events.TryDequeue(out var evt);
                result[i] = evt;
            }
            
            while (_events.TryDequeue(out _)) { }

            _processedCount = limit;
            return result;
        }
        
        /// <summary>
        /// Синхронный flush с прямой диспетчеризацией в managed-делегаты.
        /// Не создаёт промежуточный NativeArray — нет лишней аллокации.
        /// Вызывать только с main thread после <see cref="FlushEvents"/> или после Dependency.Complete().
        /// </summary>
        public void FlushToManagedEvents(
            Action<ChangeEvent<T>> onEvent)
        {
            if (onEvent == null) return;

            int count = _events.Count;

            // Применяем те же лимиты, что и FlushEvents
            if (_config.RingQueueCapacity > 0 && count > _config.RingQueueCapacity)
            {
                int toDrop = count - _config.RingQueueCapacity;
                for (int i = 0; i < toDrop; i++) _events.TryDequeue(out _);
                _droppedCount += toDrop;
                count = _config.RingQueueCapacity;
            }

            int limit = (_config.MaxQueueSize > 0 && count > _config.MaxQueueSize)
                ? _config.MaxQueueSize
                : count;
            _droppedCount += count - limit;

            for (int i = 0; i < limit; i++)
            {
                if (_events.TryDequeue(out var evt))
                    onEvent(evt);
            }
            while (_events.TryDequeue(out _)) { }
            _processedCount = limit;
        }
        
        /// <summary>
        /// Возвращает копию текущих событий БЕЗ очистки очереди.
        /// Позволяет обрабатывать события в Burst, а затем вызвать <see cref="ClearEvents"/>.
        /// Вызывать только после Dependency.Complete() на main thread.
        /// </summary>
        public NativeArray<ChangeEvent<T>> GetEvents(Allocator allocator)
        {
            int count = _events.Count;
            var result = new NativeArray<ChangeEvent<T>>(count, allocator);
            if (count == 0) return result;

            // Drain -> copy -> refill: NativeQueue не поддерживает non-destructive peek.
            // Allocator.Temp - стековая аллокация, GC-free.
            var temp = new NativeArray<ChangeEvent<T>>(count, Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                _events.TryDequeue(out var evt);
                temp[i]   = evt;
                result[i] = evt;
            }
            for (int i = 0; i < count; i++)
                _events.Enqueue(temp[i]);

            temp.Dispose();
            return result;
        }
        
        /// <summary>
        /// Извлекает одно событие из очереди без применения лимитов RingQueueCapacity/MaxQueueSize.
        /// Подходит для caller-controlled цикла: <c>while (observer.TryDequeue(out var e)) Handle(e);</c>
        /// Вызывать только с main thread после Dependency.Complete().
        /// </summary>
        public bool TryDequeue(out ChangeEvent<T> evt)
        {
            if (_events.TryDequeue(out evt))
            {
                _processedCount++;
                return true;
            }
            return false;
        }

        public ObserverMetrics GetMetrics()
        {
            var level = QueuePressureLevel.Normal;
            if (_config.MaxEventsPerFrame > 0)
            {
                if (_processedCount > _config.MaxEventsPerFrame * 0.8f) level = QueuePressureLevel.Warning;
                if (_processedCount > _config.MaxEventsPerFrame)        level = QueuePressureLevel.Critical;
            }
            return new ObserverMetrics
            {
                ProcessedThisFrame = _processedCount,
                DroppedThisFrame = _droppedCount,
                PressureLevel      = level
            };
        }
        
        /// <summary>
        /// Очищает очередь событий без возврата данных.
        /// Вызывать только после Dependency.Complete() на main thread.
        /// </summary>
        public void ClearEvents()
        {
            _events.Clear();
            _processedCount = 0;
            _droppedCount   = 0;
        }
        
        public void Dispose()
        {
            _pendingHandle.Complete();
            if (_state.IsCreated)            _state.Dispose();
            if (_events.IsCreated)           _events.Dispose();
            if (_currentSet.IsCreated)       _currentSet.Dispose();
            if (_entitiesToRemove.IsCreated) _entitiesToRemove.Dispose();
            if (_eventCounterArr.IsCreated)  _eventCounterArr.Dispose();
        }
    }
    
    [BurstCompile]
    internal struct EntityObserverUpdateJob<T> : IJobChunk where T : unmanaged, IComponentData
    {
        [ReadOnly] public ComponentTypeHandle<T> ComponentHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        public NativeParallelHashMap<Entity, ObserverEntityState<T>> State;
        public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<ChangeEvent<T>>.ParallelWriter Events;
        public uint LastSystemVersion;
        public uint GlobalSystemVersion;
        public ObserverConfig Config;
        public Entity WatchedEntity;
        [NativeDisableParallelForRestriction] public NativeArray<int> EventCounter;
        public int MaxWriteEvents;

        public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            bool processChanges = Config.ChangeDetection == ChangeDetectionMode.EqualsCheck 
                                  || chunk.DidChange(ref ComponentHandle, LastSystemVersion);

            var components = chunk.GetNativeArray(ref ComponentHandle);
            var entities = chunk.GetNativeArray(EntityHandle);
            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out int i))
            {
                var entity = entities[i];
                if (WatchedEntity != Entity.Null && entity != WatchedEntity) continue;

                CurrentSet.TryAdd(entity, 1);
                if (!processChanges) continue;

                var current = components[i];

                if (State.TryGetValue(entity, out var obsState))
                {
                    bool valueChanged = Config.ChangeDetection == ChangeDetectionMode.ChangeFilterOnly
                        || UnsafeUtility.MemCmp(
                            UnsafeUtility.AddressOf(ref current),
                            UnsafeUtility.AddressOf(ref obsState.PreviousValue),
                            UnsafeUtility.SizeOf<T>()) != 0;

                    if (valueChanged)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = obsState.PreviousValue,
                            NewValue = current,
                            Type = ChangeEventType.Changed,
                            SystemVersion = GlobalSystemVersion
                        });
                        obsState.PreviousValue = current;
                        State[entity] = obsState;
                    }
                }
                else
                {
                    if (Config.TrackEntityLifecycle)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = default,
                            NewValue = current,
                            Type = ChangeEventType.Created,
                            SystemVersion = GlobalSystemVersion
                        });
                    }
                    State.Add(entity, new ObserverEntityState<T>
                    {
                        PreviousValue = current,
                        Exists = 1,
                        WasEnabled = 1
                    });
                }
            }
        }
    }
    
    [BurstCompile]
    internal struct EntityObserverEnableableUpdateJob<T> : IJobChunk
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        [ReadOnly] public ComponentTypeHandle<T> ComponentHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        public NativeParallelHashMap<Entity, ObserverEntityState<T>> State;
        public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<ChangeEvent<T>>.ParallelWriter Events;
        public uint LastSystemVersion;
        public uint GlobalSystemVersion;
        public ObserverConfig Config;
        public Entity WatchedEntity;
        [NativeDisableParallelForRestriction] public NativeArray<int> EventCounter;
        public int MaxWriteEvents;

        public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            bool processChanges = Config.ChangeDetection == ChangeDetectionMode.EqualsCheck 
                                  || chunk.DidChange(ref ComponentHandle, LastSystemVersion);

            var components = chunk.GetNativeArray(ref ComponentHandle);
            var entities = chunk.GetNativeArray(EntityHandle);
            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out int i))
            {
                var entity = entities[i];
                if (WatchedEntity != Entity.Null && entity != WatchedEntity) continue;

                CurrentSet.TryAdd(entity, 1);
                if (!processChanges) continue;

                var current = components[i];
                bool isEnabled = chunk.IsComponentEnabled(ref ComponentHandle, i);

                if (State.TryGetValue(entity, out var obsState))
                {
                    bool valueChanged = Config.ChangeDetection == ChangeDetectionMode.ChangeFilterOnly
                        || UnsafeUtility.MemCmp(
                            UnsafeUtility.AddressOf(ref current),
                            UnsafeUtility.AddressOf(ref obsState.PreviousValue),
                            UnsafeUtility.SizeOf<T>()) != 0;

                    bool enableChanged = isEnabled != (obsState.WasEnabled != 0);

                    if (valueChanged)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = obsState.PreviousValue,
                            NewValue = current,
                            Type = ChangeEventType.Changed,
                            SystemVersion = GlobalSystemVersion
                        });
                        obsState.PreviousValue = current;
                    }

                    if (enableChanged)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = isEnabled ? default : obsState.PreviousValue,
                            NewValue = isEnabled ? current : default,
                            Type = isEnabled ? ChangeEventType.Enabled : ChangeEventType.Disabled,
                            SystemVersion = GlobalSystemVersion
                        });
                        obsState.WasEnabled = (byte)(isEnabled ? 1 : 0);
                    }

                    if (valueChanged || enableChanged)
                        State[entity] = obsState;
                }
                else
                {
                    if (Config.TrackEntityLifecycle)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = default,
                            NewValue = current,
                            Type = ChangeEventType.Created,
                            SystemVersion = GlobalSystemVersion
                        });
                    }

                    if (!isEnabled)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = default,
                            NewValue = default,
                            Type = ChangeEventType.Disabled,
                            SystemVersion = GlobalSystemVersion
                        });
                    }

                    State.Add(entity, new ObserverEntityState<T>
                    {
                        PreviousValue = current,
                        Exists = 1,
                        WasEnabled = (byte)(isEnabled ? 1 : 0)
                    });
                }
            }
        }
    }
    
    [BurstCompile]
    internal struct EntityObserverUpdateJobEquatable<T> : IJobChunk
        where T : unmanaged, IComponentData, IEquatable<T>
    {
        [ReadOnly] public ComponentTypeHandle<T> ComponentHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        public NativeParallelHashMap<Entity, ObserverEntityState<T>> State;
        public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<ChangeEvent<T>>.ParallelWriter Events;
        public uint LastSystemVersion;
        public uint GlobalSystemVersion;
        public ObserverConfig Config;
        public Entity WatchedEntity;
        [NativeDisableParallelForRestriction] public NativeArray<int> EventCounter;
        public int MaxWriteEvents;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            bool processChanges = Config.ChangeDetection == ChangeDetectionMode.EqualsCheck 
                                  || chunk.DidChange(ref ComponentHandle, LastSystemVersion);

            var components = chunk.GetNativeArray(ref ComponentHandle);
            var entities = chunk.GetNativeArray(EntityHandle);
            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out int i))
            {
                var entity = entities[i];
                if (WatchedEntity != Entity.Null && entity != WatchedEntity) continue;

                CurrentSet.TryAdd(entity, 1);
                if (!processChanges) continue;

                var current = components[i];

                if (State.TryGetValue(entity, out var obsState))
                {
                    bool valueChanged = Config.ChangeDetection == ChangeDetectionMode.ChangeFilterOnly
                        || !current.Equals(obsState.PreviousValue);

                    if (valueChanged)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = obsState.PreviousValue,
                            NewValue = current,
                            Type = ChangeEventType.Changed,
                            SystemVersion = GlobalSystemVersion
                        });
                        obsState.PreviousValue = current;
                        State[entity] = obsState;
                    }
                }
                else
                {
                    if (Config.TrackEntityLifecycle)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new ChangeEvent<T>
                        {
                            Entity = entity,
                            PreviousValue = default,
                            NewValue = current,
                            Type = ChangeEventType.Created,
                            SystemVersion = GlobalSystemVersion
                        });
                    }
                    State.Add(entity, new ObserverEntityState<T>
                    {
                        PreviousValue = current,
                        Exists = 1,
                        WasEnabled = 1
                    });
                }
            }
        }
    }
    
    [BurstCompile]
    internal struct EntityObserverCleanupJob<T> : IJob where T : unmanaged, IComponentData
    {
        public NativeParallelHashMap<Entity, ObserverEntityState<T>> State;
        [ReadOnly] public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<ChangeEvent<T>> Events;
        public NativeList<Entity> EntitiesToRemove;
        public uint GlobalSystemVersion;
        public bool TrackEntityLifecycle;
        public NativeArray<int> EventCounter;
        public int MaxWriteEvents;

        public void Execute()
        {
            EntitiesToRemove.Clear();

            var en = State.GetEnumerator();
            while (en.MoveNext())
            {
                if (!CurrentSet.ContainsKey(en.Current.Key))
                    EntitiesToRemove.Add(en.Current.Key);
            }

            for (int i = 0; i < EntitiesToRemove.Length; i++)
            {
                var entity = EntitiesToRemove[i];
                if (TrackEntityLifecycle)
                {
                    if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents))
                    {
                        State.Remove(entity);
                        continue;
                    }

                    var obsState = State[entity];
                    Events.Enqueue(new ChangeEvent<T>
                    {
                        Entity        = entity,
                        PreviousValue = obsState.PreviousValue,
                        NewValue      = default,
                        Type          = ChangeEventType.Destroyed,
                        SystemVersion = GlobalSystemVersion
                    });
                }
                State.Remove(entity);
            }
        }
    }
}