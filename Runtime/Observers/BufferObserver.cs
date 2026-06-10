using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Burst.Intrinsics;
using Unity.Jobs;

namespace DotsObserver
{
    public struct BufferObserver<T> : IDisposable where T : unmanaged, IBufferElementData
    {
        private NativeParallelHashMap<Entity, ObserverBufferState<T>> _state;
        private NativeQueue<BufferChangeEvent<T>> _events;
        private NativeParallelHashMap<Entity, byte> _currentSet;
        private EntityQuery _query;
        private BufferTypeHandle<T> _handle;
        private EntityTypeHandle _entityHandle;
        private uint _lastSystemVersion;
        private ObserverConfig _config;
        private Entity _watchedEntity;
        private int _frameCounter;
        private int _processedCount;
        private int _droppedCount;
        private NativeList<Entity> _entitiesToRemove;
        private NativeArray<int> _eventCounterArr;
        private JobHandle _pendingHandle;
        private bool _isEnabled;
        
        public ObserverConfig Config => _config;
        public Entity WatchedEntity => _watchedEntity;
        public NativeParallelHashMap<Entity, ObserverBufferState<T>> State => _state;
        public NativeParallelHashMap<Entity, byte> CurrentSet => _currentSet;
        public NativeQueue<BufferChangeEvent<T>> Events => _events;
        public NativeArray<int> EventCounter => _eventCounterArr;
        public int MaxWriteEvents => _config.MaxEventsPerFrame;
        public bool IsEnabled => _isEnabled;
        public int FrameCounter => _frameCounter;

        internal void IncrementFrameCounter() => _frameCounter++;
        internal void ResetFrameCounter() => _frameCounter = 0;
        internal void ClearCurrentSet() => _currentSet.Clear();
        internal void ResetEventCounter() => _eventCounterArr[0] = 0;
        internal void SetEnabled(bool value) => _isEnabled = value;

        public void OnCreate(ref SystemState state, in ObserverConfig config, Entity watchedEntity = default)
        {
            _config        = config;
            _watchedEntity = watchedEntity;
            _frameCounter  = config.UpdateInterval - 1;
            _processedCount = 0;
            _droppedCount   = 0;

            var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<T>();
            _query = state.GetEntityQuery(builder);
            builder.Dispose();

            _handle       = state.GetBufferTypeHandle<T>(true);
            _entityHandle = state.GetEntityTypeHandle();

            int cap = config.MaxEventsPerFrame > 0 ? config.MaxEventsPerFrame : 64;
            _state            = new NativeParallelHashMap<Entity, ObserverBufferState<T>>(cap, Allocator.Persistent);
            _events           = new NativeQueue<BufferChangeEvent<T>>(Allocator.Persistent);
            _currentSet       = new NativeParallelHashMap<Entity, byte>(cap, Allocator.Persistent);
            _entitiesToRemove = new NativeList<Entity>(cap, Allocator.Persistent);
            _eventCounterArr  = new NativeArray<int>(1, Allocator.Persistent);
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
            _handle.Update(ref state);
            _entityHandle.Update(ref state);
            _lastSystemVersion = state.LastSystemVersion;

            _eventCounterArr[0] = 0;
            var updateJob = new BufferObserverUpdateJob<T>
            {
                BufferHandle        = _handle,
                EntityHandle        = _entityHandle,
                State               = _state,
                CurrentSet          = _currentSet,
                Events              = _events.AsParallelWriter(),
                LastSystemVersion   = _lastSystemVersion,
                GlobalSystemVersion = state.GlobalSystemVersion,
                Config              = _config,
                WatchedEntity       = _watchedEntity,
                EventCounter        = _eventCounterArr,
                MaxWriteEvents      = _config.MaxEventsPerFrame
            };

            JobHandle updateHandle = updateJob.Schedule(_query, state.Dependency);

            var cleanupJob = new BufferObserverCleanupJob<T>
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

        public NativeArray<BufferChangeEvent<T>> UpdateAndFlush(ref SystemState state, Allocator allocator)
        {
            Update(ref state);
            state.Dependency.Complete();
            return FlushEvents(allocator);
        }
        
        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();
            
            if (_state.IsCreated)             _state.Dispose();
            if (_events.IsCreated)            _events.Dispose();
            if (_currentSet.IsCreated)        _currentSet.Dispose();
            if (_entitiesToRemove.IsCreated)  _entitiesToRemove.Dispose();
            if (_eventCounterArr.IsCreated)   _eventCounterArr.Dispose();
        }
        
        public void OnCreate(SystemBase system, in ObserverConfig config, Entity watchedEntity = default)
        {
            _config        = config;
            _watchedEntity = watchedEntity;
            _frameCounter  = config.UpdateInterval - 1;
            _processedCount = 0;
            _droppedCount   = 0;

            _query        = system.CheckedStateRef.GetEntityQuery(ComponentType.ReadOnly<T>());
            _handle       = system.GetBufferTypeHandle<T>(true);
            _entityHandle = system.GetEntityTypeHandle();

            int cap = config.MaxEventsPerFrame > 0 ? config.MaxEventsPerFrame : 64;
            _state            = new NativeParallelHashMap<Entity, ObserverBufferState<T>>(cap, Allocator.Persistent);
            _events           = new NativeQueue<BufferChangeEvent<T>>(Allocator.Persistent);
            _currentSet       = new NativeParallelHashMap<Entity, byte>(cap, Allocator.Persistent);
            _entitiesToRemove = new NativeList<Entity>(cap, Allocator.Persistent);
            _eventCounterArr  = new NativeArray<int>(1, Allocator.Persistent);
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
            _handle = system.GetBufferTypeHandle<T>(true);
            _entityHandle = system.GetEntityTypeHandle();
            _lastSystemVersion = system.LastSystemVersion;

            _eventCounterArr[0] = 0;

            var updateJob = new BufferObserverUpdateJob<T>
            {
                BufferHandle        = _handle,
                EntityHandle        = _entityHandle,
                State               = _state,
                CurrentSet          = _currentSet,
                Events              = _events.AsParallelWriter(),
                LastSystemVersion   = _lastSystemVersion,
                GlobalSystemVersion = system.GlobalSystemVersion,
                Config              = _config,
                WatchedEntity       = _watchedEntity,
                EventCounter        = _eventCounterArr,
                MaxWriteEvents      = _config.MaxEventsPerFrame
            };

            JobHandle updateHandle = updateJob.Schedule(_query, system.CheckedStateRef.Dependency);

            var cleanupJob = new BufferObserverCleanupJob<T>
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

            _handle = system.GetBufferTypeHandle<T>(true);
            _entityHandle = system.GetEntityTypeHandle();
            _lastSystemVersion = system.LastSystemVersion;

            _eventCounterArr[0] = 0;
            var updateJob = new BufferObserverUpdateJob<T>
            {
                BufferHandle        = _handle,
                EntityHandle        = _entityHandle,
                State               = _state,
                CurrentSet          = _currentSet,
                Events              = _events.AsParallelWriter(),
                LastSystemVersion   = _lastSystemVersion,
                GlobalSystemVersion = system.GlobalSystemVersion,
                Config              = _config,
                WatchedEntity       = _watchedEntity,
                EventCounter        = _eventCounterArr,
                MaxWriteEvents      = _config.MaxEventsPerFrame
            };

            JobHandle updateHandle = updateJob.Schedule(_query, inputDep);

            var cleanupJob = new BufferObserverCleanupJob<T>
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

        public NativeArray<BufferChangeEvent<T>> UpdateAndFlush(SystemBase system, Allocator allocator)
        {
            Update(system);
            system.CheckedStateRef.Dependency.Complete();
            return FlushEvents(allocator);
        }

        public void OnDestroy(SystemBase system)
        {
            system.CheckedStateRef.Dependency.Complete();
            
            if (_state.IsCreated)             _state.Dispose();
            if (_events.IsCreated)            _events.Dispose();
            if (_currentSet.IsCreated)        _currentSet.Dispose();
            if (_entitiesToRemove.IsCreated)  _entitiesToRemove.Dispose();
            if (_eventCounterArr.IsCreated)   _eventCounterArr.Dispose();
        }
        
        public NativeArray<BufferChangeEvent<T>> FlushEvents(Allocator allocator)
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

            var result = new NativeArray<BufferChangeEvent<T>>(limit, allocator);
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
        /// Применяет те же лимиты RingQueueCapacity/MaxQueueSize, что и <see cref="FlushEvents"/>.
        /// Вызывать только с main thread после Dependency.Complete().
        /// </summary>
        public void FlushToManagedEvents(Action<BufferChangeEvent<T>> onEvent)
        {
            if (onEvent == null) return;
 
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
        /// Вызывать только после Dependency.Complete() на main thread.
        /// </summary>
        public NativeArray<BufferChangeEvent<T>> GetEvents(Allocator allocator)
        {
            int count = _events.Count;
            var result = new NativeArray<BufferChangeEvent<T>>(count, allocator);
            if (count == 0) return result;

            var temp = new NativeArray<BufferChangeEvent<T>>(count, Allocator.Temp);
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
        public bool TryDequeue(out BufferChangeEvent<T> evt)
        {
            if (_events.TryDequeue(out evt))
            {
                _processedCount++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Возвращает ObserverMetrics без аллокаций.
        /// </summary>
        public ObserverMetrics GetMetrics()
        {
            var level = QueuePressureLevel.Normal;
            if (_config.MaxEventsPerFrame > 0)
            {
                if (_processedCount > _config.MaxEventsPerFrame * 0.8f) level = QueuePressureLevel.Warning;
                if (_processedCount > _config.MaxEventsPerFrame)         level = QueuePressureLevel.Critical;
            }
            return new ObserverMetrics
            {
                ProcessedThisFrame = _processedCount,
                DroppedThisFrame   = _droppedCount,
                PressureLevel      = level
            };
        }

        /// <summary>
        /// Очищает очередь событий без возврата данных.
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
            if (_state.IsCreated)             _state.Dispose();
            if (_events.IsCreated)            _events.Dispose();
            if (_currentSet.IsCreated)        _currentSet.Dispose();
            if (_entitiesToRemove.IsCreated)  _entitiesToRemove.Dispose();
            if (_eventCounterArr.IsCreated)   _eventCounterArr.Dispose();
        }
    }

    [BurstCompile]
    internal struct BufferObserverUpdateJob<T> : IJobChunk where T : unmanaged, IBufferElementData
    {
        [ReadOnly] public BufferTypeHandle<T> BufferHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        public NativeParallelHashMap<Entity, ObserverBufferState<T>> State;
        public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<BufferChangeEvent<T>>.ParallelWriter Events;
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
                                  || chunk.DidChange(ref BufferHandle, LastSystemVersion);

            var bufferAccessor = chunk.GetBufferAccessor(ref BufferHandle);
            var entities = chunk.GetNativeArray(EntityHandle);
            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);

            while (enumerator.NextEntityIndex(out int i))
            {
                var entity = entities[i];
                if (WatchedEntity != Entity.Null && entity != WatchedEntity) continue;
                
                CurrentSet.TryAdd(entity, 1);

                if (!processChanges) continue;

                var buffer = bufferAccessor[i];
                uint hash = BufferHashUtility.ComputeHash(buffer);
                int len = buffer.Length;

                if (State.TryGetValue(entity, out var obsState))
                {
                    bool changed = Config.ChangeDetection == ChangeDetectionMode.ChangeFilterOnly
                        || (hash != obsState.SnapshotHash || len != obsState.SnapshotLength);

                    if (changed)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new BufferChangeEvent<T>
                        {
                            Entity = entity,
                            Type = ChangeEventType.Changed,
                            SystemVersion = GlobalSystemVersion
                        });
                        obsState.SnapshotHash = hash;
                        obsState.SnapshotLength = len;
                        State[entity] = obsState;
                    }
                }
                else
                {
                    if (Config.TrackEntityLifecycle)
                    {
                        if (!EventCapacityUtility.TryReserveSlot(EventCounter, MaxWriteEvents)) continue;

                        Events.Enqueue(new BufferChangeEvent<T>
                        {
                            Entity = entity,
                            Type = ChangeEventType.Created,
                            SystemVersion = GlobalSystemVersion
                        });
                    }

                    State.Add(entity, new ObserverBufferState<T>
                    {
                        SnapshotHash = hash,
                        SnapshotLength = len,
                        Exists = 1
                    });
                }
            }
        }
    }

    [BurstCompile]
    internal struct BufferObserverCleanupJob<T> : IJob where T : unmanaged, IBufferElementData
    {
        public NativeParallelHashMap<Entity, ObserverBufferState<T>> State;
        [ReadOnly] public NativeParallelHashMap<Entity, byte> CurrentSet;
        public NativeQueue<BufferChangeEvent<T>> Events;
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
                    Events.Enqueue(new BufferChangeEvent<T>
                    {
                        Entity        = entity,
                        Type          = ChangeEventType.Destroyed,
                        SystemVersion = GlobalSystemVersion
                    });
                }
                State.Remove(entity);
            }
        }
    }
}