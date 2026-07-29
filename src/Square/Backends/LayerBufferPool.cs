namespace Square.Backends;

internal sealed class LayerBufferPool
{
    internal const int MaxBufferBytes = 4 * 1024 * 1024;
    internal const int MaxRetainedBytes = 16 * 1024 * 1024;
    internal const int MaxBuffersPerBucket = 2;

    private readonly Dictionary<int, Stack<byte[]>> _buckets = new();

    internal int RetainedBytes { get; private set; }
    internal int RetainedBufferCount { get; private set; }
    internal int AllocationCount { get; private set; }
    internal int ReuseCount { get; private set; }

    internal byte[] Rent(int minimumLength)
    {
        if (minimumLength <= 0) return [];
        if (minimumLength > MaxBufferBytes)
        {
            AllocationCount++;
            return new byte[minimumLength];
        }

        var bucketSize = RoundUpToPowerOfTwo(minimumLength);
        if (_buckets.TryGetValue(bucketSize, out var bucket) && bucket.Count > 0)
        {
            var buffer = bucket.Pop();
            RetainedBytes -= buffer.Length;
            RetainedBufferCount--;
            ReuseCount++;
            return buffer;
        }

        AllocationCount++;
        return new byte[bucketSize];
    }

    internal void Return(byte[] buffer)
    {
        if (buffer.Length == 0 || buffer.Length > MaxBufferBytes) return;
        if (RetainedBytes + buffer.Length > MaxRetainedBytes) return;

        if (!_buckets.TryGetValue(buffer.Length, out var bucket))
        {
            bucket = new Stack<byte[]>();
            _buckets.Add(buffer.Length, bucket);
        }
        if (bucket.Count >= MaxBuffersPerBucket) return;

        bucket.Push(buffer);
        RetainedBytes += buffer.Length;
        RetainedBufferCount++;
    }

    internal void Clear()
    {
        _buckets.Clear();
        RetainedBytes = 0;
        RetainedBufferCount = 0;
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        var result = 256;
        while (result < value) result <<= 1;
        return result;
    }
}
