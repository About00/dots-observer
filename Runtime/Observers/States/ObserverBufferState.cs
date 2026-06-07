namespace DotsObserver
{
    /// <summary>
    /// Состояние наблюдателя за DynamicBuffer&lt;T&gt;.
    /// </summary>
    public struct ObserverBufferState<T> where T : unmanaged
    {
        public uint SnapshotHash;
        public int SnapshotLength;
        public byte Exists; // 0/1
    }
}