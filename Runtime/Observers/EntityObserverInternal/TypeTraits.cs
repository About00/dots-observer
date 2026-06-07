using System;
using Unity.Entities;

namespace DotsObserver
{
    /// <summary>
    /// Runtime-информация о типе T. Инициализация - на main thread (static ctor),
    /// использование - safely в Burst как readonly bool.
    /// </summary>
    internal static class TypeTraits<T> where T : unmanaged, IComponentData
    {
        public static readonly bool IsEnableable =
            typeof(IEnableableComponent).IsAssignableFrom(typeof(T));
        
        public static readonly bool IsIEquatable =
            typeof(IEquatable<T>).IsAssignableFrom(typeof(T));
    }
}