using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using SkeletonApi.Application.Interfaces;
using SkeletonApi.Common.Errors;
using SkeletonApi.Domain.Entities;
using SkeletonApi.Infrastructure.Interfaces;

namespace SkeletonApi.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public UserRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    private IDbConnection CreateConnection() => _dbConnectionFactory.CreateConnection();
    private string GetDbProvider() => _dbConnectionFactory.GetDbProvider();

    public async Task CreateAsync(User user)
    {
        const string sql = @"
            INSERT INTO users (id, username, email, password, full_name, role, is_active, created_at, updated_at)
            VALUES (@Id, @Username, @Email, @Password, @FullName, @Role, @IsActive, @CreatedAt, @UpdatedAt)";

        try
        {
            await Elastic.Apm.Agent.Tracer.CurrentTransaction.CaptureSpan("INSERT INTO users", "db", async () =>
            {
                using var connection = CreateConnection();
                await connection.ExecuteAsync(sql, user, commandTimeout: _dbConnectionFactory.QueryTimeout);
            }, GetDbProvider());
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException("Failed to create user in database", ex);
        }
    }

    public async Task DeleteAsync(string id)
    {
        const string sql = "DELETE FROM users WHERE id = @Id";
        try
        {
            using var connection = CreateConnection();
            await connection.ExecuteAsync(sql, new { Id = id }, commandTimeout: _dbConnectionFactory.QueryTimeout);
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException($"Failed to delete user {id} from database", ex);
        }
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        const string sql = "SELECT * FROM users";
        using var connection = CreateConnection();
        return await connection.QueryAsync<User>(sql, commandTimeout: _dbConnectionFactory.QueryTimeout);
    }

    public async Task<(IEnumerable<User> Items, int TotalItems)> GetAllPaginatedAsync(int page, int pageSize)
    {
        var offset = (page - 1) * pageSize;
        const string sql = @"
            SELECT * FROM users ORDER BY created_at DESC LIMIT @PageSize OFFSET @Offset;
            SELECT COUNT(*) FROM users;";

        using var connection = CreateConnection();
        using var multi = await connection.QueryMultipleAsync(sql, new { PageSize = pageSize, Offset = offset }, commandTimeout: _dbConnectionFactory.QueryTimeout);

        var items = await multi.ReadAsync<User>();
        var totalItems = await multi.ReadFirstAsync<int>();

        return (items, totalItems);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        const string sql = "SELECT * FROM users WHERE email = @Email";
        using var connection = CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Email = email }, commandTimeout: _dbConnectionFactory.QueryTimeout);
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        const string sql = "SELECT * FROM users WHERE id = @Id";
        try
        {
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Id = id }, commandTimeout: _dbConnectionFactory.QueryTimeout);
        }
        catch (Exception ex)
        {
            throw new DataAccessHubException($"Failed to get user {id} from database", ex);
        }
    }

    public async Task<IEnumerable<User>> SearchAsync(string query)
    {
        const string sql = @"
            SELECT * FROM users 
            WHERE username LIKE @Query OR email LIKE @Query OR full_name LIKE @Query";

        using var connection = CreateConnection();
        return await connection.QueryAsync<User>(sql, new { Query = $"%{query}%" }, commandTimeout: _dbConnectionFactory.QueryTimeout);
    }

    public async Task UpdateAsync(User user)
    {
        const string sql = @"
            UPDATE users 
            SET full_name = @FullName, 
                is_active = @IsActive, 
                updated_at = @UpdatedAt 
            WHERE id = @Id";

        using var connection = CreateConnection();
        await connection.ExecuteAsync(sql, user, commandTimeout: _dbConnectionFactory.QueryTimeout);
    }
}
