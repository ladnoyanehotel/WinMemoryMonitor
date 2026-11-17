namespace WinMemoryCleaner
{
    /// <summary>
    /// IMemoryService
    /// </summary>
    public interface IMemoryService
    {
        /// <summary>
        /// Gets the memory info (RAM)
        /// </summary>
        Memory Memory { get; }
    }
}
