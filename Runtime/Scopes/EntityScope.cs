using Unity.Collections;
using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Managed обёртка с C# events над <see cref="EntityObserver{T}"/>.
    /// Живёт только в main-thread контексте (UI / ViewModel / MonoBehaviour).
    /// 
    /// <para>
    /// Явно требует <see cref="SystemState"/> — нет скрытой магии через World.
    /// </para>
    /// </summary>
    public sealed class EntityScope<T> : IEntityScope where T : unmanaged, IComponentData
    {
        private EntityObserver<T> _observer;
        private bool _disposed;

        public event ComponentCreatedHandler<T> OnCreated;
        public event ComponentChangedHandler<T> OnChanged;
        public event ComponentDestroyedHandler<T> OnDestroyed;
        public event ComponentEnabledHandler<T> OnEnabled;
        public event ComponentDisabledHandler<T> OnDisabled;

        public static EntityScope<T> Create(ref SystemState state, Entity entity, in ObserverConfig config)
        {
            var scope = new EntityScope<T>();
            scope._observer = new EntityObserver<T>();
            scope._observer.OnCreate(ref state, in config, entity);
            return scope;
        }

        public static EntityScope<T> CreateWildcard(ref SystemState state, in ObserverConfig config)
        {
            var scope = new EntityScope<T>();
            scope._observer = new EntityObserver<T>();
            scope._observer.OnCreate(ref state, in config, Entity.Null);
            return scope;
        }

        public void Update(ref SystemState state)
        {
            if (_disposed) throw new System.ObjectDisposedException(nameof(EntityScope<T>), "[DotsObserver] Scope already disposed.");
            _observer.Update(ref state);
        }

        public void Flush(ref SystemState state, Allocator allocator = Allocator.Temp)
        {
            if (_disposed) throw new System.ObjectDisposedException(nameof(EntityScope<T>), "[DotsObserver] Scope already disposed.");
            state.Dependency.Complete();

            _observer.FlushToManagedEvents(evt =>
            {
                switch (evt.Type)
                {
                    case ChangeEventType.Created:   
                        OnCreated?.Invoke(evt.Entity, evt.NewValue);                        
                        break;
                    case ChangeEventType.Changed:   
                        OnChanged?.Invoke(evt.Entity, evt.PreviousValue, evt.NewValue);     
                        break;
                    case ChangeEventType.Destroyed:
                        OnDestroyed?.Invoke(evt.Entity, evt.PreviousValue);             
                        break;
                    case ChangeEventType.Enabled:  
                        OnEnabled?.Invoke(evt.Entity, evt.NewValue);             
                        break;
                    case ChangeEventType.Disabled: 
                        OnDisabled?.Invoke(evt.Entity, evt.PreviousValue);        
                        break;
                }
            });
        }

        public void UpdateAndFlush(ref SystemState state, Allocator allocator = Allocator.Temp)
        {
            if (_disposed) throw new System.ObjectDisposedException(nameof(EntityScope<T>), "[DotsObserver] Scope already disposed.");
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