using Unity.Collections;
using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Managed обёртка с C# events над <see cref="BufferObserver{T}"/>.
    /// </summary>
    public sealed class BufferScope<T> : IEntityScope where T : unmanaged, IBufferElementData
    {
        private BufferObserver<T> _observer;
        private bool _disposed;

        public event BufferCreatedHandler<T> OnBufferCreated;
        public event BufferChangedHandler<T> OnBufferChanged;
        public event BufferDestroyedHandler<T> OnBufferDestroyed;

        public static BufferScope<T> Create(ref SystemState state, Entity entity, in ObserverConfig config)
        {
            var scope = new BufferScope<T>();
            scope._observer = new BufferObserver<T>();
            scope._observer.OnCreate(ref state, in config, entity);
            return scope;
        }

        public static BufferScope<T> CreateWildcard(ref SystemState state, in ObserverConfig config)
        {
            var scope = new BufferScope<T>();
            scope._observer = new BufferObserver<T>();
            scope._observer.OnCreate(ref state, in config, Entity.Null);
            return scope;
        }

        public void Update(ref SystemState state)
        {
            if (_disposed) throw new System.ObjectDisposedException(nameof(BufferScope<T>), "[DotsObserver] Scope already disposed.");
            _observer.Update(ref state);
        }

        public void Flush(ref SystemState state, Allocator allocator = Allocator.Temp)
        {
            if (_disposed) throw new System.ObjectDisposedException(nameof(BufferScope<T>), "[DotsObserver] Scope already disposed.");
            state.Dependency.Complete();
            _observer.FlushToManagedEvents(evt =>
            {
                switch (evt.Type)
                {
                    case ChangeEventType.Created:
                        OnBufferCreated?.Invoke(evt.Entity); // NativeArray нельзя передать без копии
                        break;
                    case ChangeEventType.Changed:
                        OnBufferChanged?.Invoke(evt.Entity);
                        break;
                    case ChangeEventType.Destroyed:
                        OnBufferDestroyed?.Invoke(evt.Entity);
                        break;
                }
            });
        }

        public void UpdateAndFlush(ref SystemState state, Allocator allocator = Allocator.Temp)
        {
            if (_disposed) throw new System.ObjectDisposedException(nameof(BufferScope<T>), "[DotsObserver] Scope already disposed.");
            Update(ref state);
            Flush(ref state, allocator);
        }

        public void Dispose(ref SystemState state)
        {
            if (_disposed) return;
            _disposed = true;
            _observer.OnDestroy(ref state);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _observer.Dispose();
        }
    }
}