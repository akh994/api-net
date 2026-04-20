using global::RabbitMQ.Client;

namespace SkeletonApi.Common.Messaging.RabbitMQ;

public interface IConnectionPool : IDisposable
{
    IConnection GetConnection();
}
