namespace DotsObserver
{
    public enum ScheduleMode : byte
    {
        /// <summary>
        /// Sequential IJobChunk — Burst, без data race, safe для внешнего NativeHashMap.
        /// </summary>
        Sequential = 0,
        
        /// <summary>
        /// Reserved. Parallel требует NativeStream для state updates (не реализовано в этом MVP).
        /// </summary>
        Parallel = 1
    }
}