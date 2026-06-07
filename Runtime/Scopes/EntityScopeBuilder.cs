using System.Collections.Generic;
using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Fluent builder для батчевой регистрации. Нет скрытых sync points.
    /// </summary>
    public sealed class EntityScopeBuilder
    {
        private readonly ObserverConfig _config;
        private readonly List<IEntityScope> _scopes = new();

        public EntityScopeBuilder(in ObserverConfig config)
        {
            _config = config;
        }

        public EntityScopeBuilder() : this(ObserverConfig.Default) { }

        public EntityScope<T> Watch<T>(ref SystemState state, Entity entity) where T : unmanaged, IComponentData
        {
            var scope = EntityScope<T>.Create(ref state, entity, _config);
            _scopes.Add(scope);
            return scope;
        }

        public EntityScope<T> WatchAll<T>(ref SystemState state) where T : unmanaged, IComponentData
        {
            var scope = EntityScope<T>.CreateWildcard(ref state, _config);
            _scopes.Add(scope);
            return scope;
        }

        public BufferScope<T> WatchBuffer<T>(ref SystemState state, Entity entity) where T : unmanaged, IBufferElementData
        {
            var scope = BufferScope<T>.Create(ref state, entity, _config);
            _scopes.Add(scope);
            return scope;
        }

        public BufferScope<T> WatchAllBuffers<T>(ref SystemState state) where T : unmanaged, IBufferElementData
        {
            var scope = BufferScope<T>.CreateWildcard(ref state, _config);
            _scopes.Add(scope);
            return scope;
        }

        public EntityScopeGroup Build()
        {
            var group = new EntityScopeGroup();
            foreach (var s in _scopes) group.Add(s);
            _scopes.Clear();
            return group;
        }
    }
}