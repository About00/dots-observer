using Unity.Entities;

namespace DotsObserver
{
    public delegate void ComponentCreatedHandler<T>(in Entity entity, in T value)
        where T : unmanaged, IComponentData;

    public delegate void ComponentChangedHandler<T>(in Entity entity, in T previousValue, in T newValue)
        where T : unmanaged, IComponentData;

    public delegate void ComponentDestroyedHandler<T>(in Entity entity, in T lastValue)
        where T : unmanaged, IComponentData;

    public delegate void ComponentEnabledHandler<T>(in Entity entity, in T value)
        where T : unmanaged, IComponentData;

    public delegate void ComponentDisabledHandler<T>(in Entity entity, in T lastKnownValue)
        where T : unmanaged, IComponentData;

    public delegate void BufferCreatedHandler<T>(in Entity entity)
        where T : unmanaged, IBufferElementData;

    public delegate void BufferChangedHandler<T>(in Entity entity)
        where T : unmanaged, IBufferElementData;

    public delegate void BufferDestroyedHandler<T>(in Entity entity)
        where T : unmanaged, IBufferElementData;
}