using System.Collections.Concurrent;

namespace SkeletonApi.Common.Concurrency;

/// <summary>
/// Provides a mechanism to ensure that only one execution of a given function 
/// for a specific key is in flight at any given time.
/// </summary>
public interface ISingleFlight
{
    /// <summary>
    /// Executes the given function if there is no other execution in progress for the given key.
    /// If there is an execution in progress, waits for it to complete and returns its result.
    /// </summary>
    Task<T> DoAsync<T>(string key, Func<Task<T>> func);
}

public class SingleFlight : ISingleFlight
{
    private readonly ConcurrentDictionary<string, Task> _flights = new();

    public async Task<T> DoAsync<T>(string key, Func<Task<T>> func)
    {
        var task = _flights.GetOrAdd(key, _ => func());

        if (task is not Task<T> typedTask)
        {
            throw new InvalidOperationException($"SingleFlight key collision '{key}' with different return type.");
        }

        try
        {
            return await typedTask;
        }
        finally
        {
            // Remove the task from the dictionary when it completes
            // Waiters who already have the task reference will still get the result
            _flights.TryRemove(key, out _);
        }
    }
}
