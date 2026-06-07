namespace DotsObserver
{
    /// <summary>
    /// Режим выполнения обновления наблюдателя.
    /// </summary>
    public enum ObserverExecutionMode : byte
    {
        /// <summary>
        /// Burst-скомпилированный IJobChunk (рекомендуется).
        /// </summary>
        BurstJob   = 0,
        
        /// <summary>
        /// Синхронное выполнение на main thread без шедулинга.
        /// TrackEnableable в этом режиме не поддерживается — будет проигнорирован.
        /// </summary>
        MainThread = 1,
    }
}