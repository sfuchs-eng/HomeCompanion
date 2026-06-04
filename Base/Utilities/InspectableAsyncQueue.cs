using System.Diagnostics;

namespace HomeCompanion.Base.Utilities;

public class DequeueResult<T>
{
    public T? Item { get; }
    public bool Success { get; private set; }

    public DequeueResult(T item)
    {
        Item = item;
        Success = true;
    }

    public DequeueResult()
    {
        Item = default;
        Success = false;
    }
}

public class InspectableAsyncQueue<T>
{
    private readonly LinkedList<T> _queue = new();
    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly object _lockObj = new();

    /// <summary>
    /// Adds a command to the end of the queue.
    /// </summary>
    public void Enqueue(T item)
    {
        lock (_lockObj)
        {
            _queue.AddLast(item);
        }
        _semaphore.Release();
    }

    /// <summary>
    /// Awaits the next command. Returns default(T) if the timeout is reached.
    /// </summary>
    public async Task<DequeueResult<T>> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var remainingTimeout = timeout - stopwatch.Elapsed;
            if (remainingTimeout <= TimeSpan.Zero)
            {
                return new DequeueResult<T>(); // Timeout reached
            }

            // Asynchronously wait for an item or a timeout
            bool signaled = await _semaphore.WaitAsync(remainingTimeout, cancellationToken);
            if (!signaled)
            {
                return new DequeueResult<T>(); // Timeout reached
            }

            lock (_lockObj)
            {
                if (_queue.Count > 0)
                {
                    var item = _queue.First!.Value;
                    _queue.RemoveFirst();
                    return new DequeueResult<T>(item);
                }

                // Crucial Edge Case: If we are here, the semaphore was signaled, 
                // but an external event modified/removed the item before we grabbed the lock.
                // Loop back to safely resume waiting with the remaining time.
            }
        }
    }

    /// <summary>
    /// Safely inspects or modifies the underlying queue using a thread-safe lock.
    /// </summary>
    public void InspectAndModify(Action<LinkedList<T>> action)
    {
        lock (_lockObj)
        {
            action(_queue);
        }
    }
}