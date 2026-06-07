using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Bulk-управление множеством scope'ов. Только main thread.
    /// </summary>
    public sealed class EntityScopeGroup : IDisposable
    {
        private readonly List<IEntityScope> _scopes = new();

        public void Add(IEntityScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            _scopes.Add(scope);
        }

        public void UpdateAll(ref SystemState state)
        {
            foreach (var s in _scopes)
                s.Update(ref state);
        }

        public void FlushAll(ref SystemState state, Allocator allocator = Allocator.Temp)
        {
            foreach (var s in _scopes)
                s.Flush(ref state, allocator);
        }

        public void UpdateAndFlushAll(ref SystemState state, Allocator allocator = Allocator.Temp)
        {
            foreach (var s in _scopes)
                s.UpdateAndFlush(ref state, allocator);
        }

        public void DisposeAll(ref SystemState state)
        {
            foreach (var s in _scopes)
            {
                try { s.Dispose(ref state); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[DotsObserver] Scope dispose error: {ex}"); }
            }
            _scopes.Clear();
        }

        public void Dispose()
        {
            foreach (var s in _scopes)
            {
                try { s.Dispose(); }
                catch (Exception ex) { UnityEngine.Debug.LogError($"[DotsObserver] Scope dispose error: {ex}"); }
            }
            _scopes.Clear();
        }
    }
}