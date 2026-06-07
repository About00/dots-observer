namespace DotsObserver
{
    /// <summary>
    /// Единая структура состояния наблюдателя на одну entity.
    /// </summary>
    public struct ObserverEntityState<T> where T : unmanaged
    {
        public T PreviousValue;
        public byte Exists;      // 0/1
        public byte WasEnabled;  // 0/1, актуально если T : IEnableableComponent
    }
}