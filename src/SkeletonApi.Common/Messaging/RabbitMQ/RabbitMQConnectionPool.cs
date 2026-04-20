using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace SkeletonApi.Common.Messaging.RabbitMQ;

public class RabbitMQConnectionPool : IConnectionPool
{
    private readonly List<IConnection> _connections;
    private readonly object _lock = new();
    private int _index;
    private bool _disposed;
    private readonly ILogger<RabbitMQConnectionPool> _logger;
    private readonly string _name;

    public RabbitMQConnectionPool(string name, ConnectionFactory factory, int size, ILogger<RabbitMQConnectionPool> logger)
    {
        _name = name;
        _logger = logger;
        _connections = new List<IConnection>(size);

        try
        {
            for (int i = 0; i < size; i++)
            {
                // Create connection synchronously to ensure pool is ready
                var conn = factory.CreateConnectionAsync().GetAwaiter().GetResult();
                _connections.Add(conn);
            }
            _logger.LogInformation("Initialized RabbitMQ Connection Pool '{Name}' with {Size} connections", name, size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ Connection Pool '{Name}'", name);
            // Clean up any established connections
            Dispose();
            throw;
        }
    }

    public IConnection GetConnection()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RabbitMQConnectionPool));

        lock (_lock)
        {
            if (_connections.Count == 0) throw new InvalidOperationException("No connections available in pool");

            // Try to find an open connection, starting from current index
            for (int i = 0; i < _connections.Count; i++)
            {
                var idx = (_index + i) % _connections.Count;
                var conn = _connections[idx];
                if (conn.IsOpen)
                {
                    _index = (idx + 1) % _connections.Count;
                    return conn;
                }
            }

            // All connections are closed — return first one (may be recovering via AutomaticRecovery)
            _logger.LogWarning("All connections in pool '{Name}' are closed, returning first connection (may be recovering)", _name);
            _index = 1 % _connections.Count;
            return _connections[0];
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            foreach (var conn in _connections)
            {
                try
                {
                    conn.CloseAsync().GetAwaiter().GetResult();
                    conn.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing connection in pool '{Name}'", _name);
                }
            }
            _connections.Clear();
            _disposed = true;
        }
        _logger.LogInformation("RabbitMQ Connection Pool '{Name}' disposed", _name);
    }
}
