using System.Data;

namespace SkeletonApi.Infrastructure.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
    string GetDbProvider();
    int QueryTimeout { get; }
}
