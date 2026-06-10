using Unity.Collections;
using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Unified interface для managed scope'ов (PresentationEntityScope, PresentationBufferScope).
    /// </summary>
    public interface IEntityScope : System.IDisposable
    {
        bool IsEnabled { get; }
        void Enable();
        void Disable();
        void Update(ref SystemState state);
        void Flush(ref SystemState state, Allocator allocator = Allocator.Temp);
        void UpdateAndFlush(ref SystemState state, Allocator allocator = Allocator.Temp);
        void Dispose(ref SystemState state);
    }
}