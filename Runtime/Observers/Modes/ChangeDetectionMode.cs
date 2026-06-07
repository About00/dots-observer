namespace DotsObserver
{
    public enum ChangeDetectionMode : byte
    {
        /// <summary>
        /// Только chunk.DidChange — быстро, но ложные срабатывания при любой записи.
        /// </summary>
        ChangeFilterOnly = 0,
        
        /// <summary>
        /// Точное побайтовое сравнение (MemCmp). Нет ложных срабатываний.
        /// </summary>
        EqualsCheck = 1,
        
        /// <summary>
        /// Сначала DidChange, затем MemCmp (баланс по умолчанию).
        /// </summary>
        Both = 2
    }
}