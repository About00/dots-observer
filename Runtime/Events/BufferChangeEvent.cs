using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Unmanaged event для изменений DynamicBuffer&lt;T&gt;. Blittable, Burst-safe.
    /// </summary>
    public struct BufferChangeEvent<T> where T : unmanaged, IBufferElementData
    {
        /// <summary>
        /// Target entity. Валидно для всех типов событий.
        /// </summary>
        public Entity Entity;

        /// <summary>
        /// Тип события: Created / Changed / Destroyed.
        /// </summary>
        public ChangeEventType Type;

        /// <summary>
        /// GlobalSystemVersion в момент обнаружения изменения.
        /// Это счётчик структурных изменений ECS, а не номер кадра.
        /// </summary>
        public uint SystemVersion;
    }
}