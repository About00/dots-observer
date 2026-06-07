using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Burst-safe событие изменения компонента. Не содержит managed-ссылок.
    /// </summary>
    public struct ChangeEvent<T> where T : unmanaged, IComponentData
    {
        /// <summary>
        /// Target entity. Валидно для всех типов событий.
        /// </summary>
        public Entity Entity;

        /// <summary>
        /// Значение компонента из предыдущего кадра (snapshot).
        /// Валидно для: <see cref="ChangeEventType.Changed"/>, <see cref="ChangeEventType.Destroyed"/>, <see cref="ChangeEventType.Disabled"/>.
        /// Для Created/Enabled содержит default(T).
        /// </summary>
        public T PreviousValue;

        /// <summary>
        /// Актуальное значение компонента на момент события.
        /// Валидно для: <see cref="ChangeEventType.Created"/>, <see cref="ChangeEventType.Changed"/>, <see cref="ChangeEventType.Enabled"/>.
        /// Для Destroyed/Disabled содержит default(T).
        /// </summary>
        public T NewValue;

        /// <summary>
        /// Created / Changed / Destroyed / Enabled / Disabled
        /// </summary>
        public ChangeEventType Type;

        /// <summary>
        /// GlobalSystemVersion в момент обнаружения изменения.
        /// Это счётчик структурных изменений ECS, а не номер кадра.
        /// </summary>
        public uint SystemVersion;
    }
}