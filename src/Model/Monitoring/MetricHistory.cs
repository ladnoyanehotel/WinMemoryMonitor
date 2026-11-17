using System;

#pragma warning disable 1591 // Missing XML comment for publicly visible type or member

namespace WinMemoryCleaner
{
    /// <summary>
    /// Fixed-capacity ring buffer that keeps the last N metric samples (e.g. 60 seconds of CPU usage).
    /// Does not grow over time; underlying array is reused.
    /// </summary>
    public class MetricHistory
    {
        private readonly float[] _values;
        private int _count;
        private int _head;

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricHistory"/> class.
        /// </summary>
        /// <param name="capacity">Maximum number of samples to keep</param>
        public MetricHistory(int capacity)
        {
            if (capacity < 1)
                capacity = 1;

            _values = new float[capacity];
            _count = 0;
            _head = 0;
        }

        /// <summary>
        /// Gets the number of samples currently stored
        /// </summary>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>
        /// Gets the capacity of the buffer
        /// </summary>
        public int Capacity
        {
            get { return _values.Length; }
        }

        /// <summary>
        /// Gets the most recently added sample
        /// </summary>
        public float Last
        {
            get { return _count == 0 ? 0f : _values[_head]; }
        }

        /// <summary>
        /// Adds a sample, overwriting the oldest one when the buffer is full
        /// </summary>
        /// <param name="value">Sample value</param>
        public void Add(float value)
        {
            _head = (_head + 1) % _values.Length;
            _values[_head] = value;

            if (_count < _values.Length)
                _count++;
        }

        /// <summary>
        /// Copies the samples in chronological order (oldest first) into the target array
        /// </summary>
        /// <param name="target">Target array; must be at least <see cref="Count"/> long</param>
        /// <param name="targetOffset">Offset in the target array to start copying to</param>
        /// <returns>The number of samples copied</returns>
        public int CopyTo(float[] target, int targetOffset)
        {
            if (target == null)
                return 0;

            var index = targetOffset;
            var start = (_head + _values.Length - _count + 1) % _values.Length;

            for (var i = 0; i < _count && index < target.Length; i++)
            {
                target[index] = _values[(start + i) % _values.Length];
                index++;
            }

            return index - targetOffset;
        }

        /// <summary>
        /// Removes all samples
        /// </summary>
        public void Clear()
        {
            _count = 0;
            _head = 0;
        }
    }
}

#pragma warning restore 1591 // Missing XML comment for publicly visible type or member
