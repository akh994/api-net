using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Npgsql;
using SkeletonApi.Common.Configuration;
using SkeletonApi.Infrastructure.Interfaces;

namespace SkeletonApi.Infrastructure.Repositories;

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _options;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _options = configuration.GetSection("Database").Get<DatabaseOptions>()
            ?? throw new ArgumentNullException(nameof(configuration), "Database configuration not found.");
    }

    public IDbConnection CreateConnection()
    {
        return _options.Provider.ToLower() switch
        {
            "postgresql" => new NpgsqlConnection(_options.GetConnectionString()),
            "sqlserver" => new SqlConnection(_options.GetConnectionString()),
            _ => new MySqlConnection(_options.GetConnectionString())
        };
    }

    public string GetDbProvider() => _options.Provider.ToLower();

    public int QueryTimeout => _options.QueryTimeoutSeconds;
}
