using System.Text.Json.Serialization;

namespace SkeletonApi.Common.Configuration;

public class ServerOptions
{
    public string Urls { get; set; } = "http://0.0.0.0:4021";
    public int HttpPort { get; set; } = 4021;
    public int GrpcPort { get; set; } = 4022;
    public int HttpsPort { get; set; } = 4023;
    public string CertFile { get; set; } = string.Empty;
    public string KeyFile { get; set; } = string.Empty;
    public int ShutdownTimeoutSeconds { get; set; } = 30;
    public List<string> ExcludedLogPaths { get; set; } = new();
    public CorsOptions Cors { get; set; } = new();
}

public class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = { "*" };
    public string[] AllowedMethods { get; set; } = { "*" };
    public string[] AllowedHeaders { get; set; } = { "*" };
    public bool AllowCredentials { get; set; } = false;
}

public class DatabaseOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "skeleton";
    public string User { get; set; } = "root";
    public string Password { get; set; } = "root";
    public string Provider { get; set; } = "mysql"; // mysql, postgresql, sqlserver
    public int MaxOpenConnections { get; set; } = 25;
    public int MaxIdleConnections { get; set; } = 5;
    public int ConnectionLifetimeSeconds { get; set; } = 1800;
    public int QueryTimeoutSeconds { get; set; } = 5;

    public string GetConnectionString()
    {
        return Provider.ToLower() switch
        {
            "postgresql" => $"Host={Host};Port={Port};Database={Database};Username={User};Password={Password};Connection Lifetime={ConnectionLifetimeSeconds};",
            "sqlserver" => $"Server={Host},{Port};Database={Database};User Id={User};Password={Password};TrustServerCertificate=True;Connection Lifetime={ConnectionLifetimeSeconds};",
            _ => $"Server={Host};Port={Port};Database={Database};User={User};Password={Password};Connection Lifetime={ConnectionLifetimeSeconds};"
        };
    }
}

public class CacheOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public List<string> Addrs { get; set; } = new(); // For Cluster support
    public int Database { get; set; } = 2;
    public string User { get; set; } = "default";
    public string Password { get; set; } = string.Empty;
    public int MaxLifetimeSeconds { get; set; } = 300;
    public CacheReplicaOptions? Replica { get; set; }

    public string GetConnectionString()
    {
        string conn;
        if (Addrs != null && Addrs.Count > 0)
        {
            conn = string.Join(",", Addrs);
        }
        else
        {
            conn = $"{Host}:{Port}";
        }

        if (!string.IsNullOrEmpty(Password))
        {
            conn = $"{conn},password={Password}";
        }
        if (Database > 0)
        {
            conn = $"{conn},defaultDatabase={Database}";
        }
        // StackExchange.Redis options for cluster/resilience
        conn = $"{conn},abortConnect=false";
        return conn;
    }

    public class CacheReplicaOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6380;
        public List<string> Addrs { get; set; } = new();
        public int Database { get; set; } = 2;
        public string User { get; set; } = "default";
        public string Password { get; set; } = string.Empty;
        public int MaxLifetimeSeconds { get; set; } = 300;

        public string GetConnectionString()
        {
            string conn;
            if (Addrs != null && Addrs.Count > 0)
            {
                conn = string.Join(",", Addrs);
            }
            else
            {
                conn = $"{Host}:{Port}";
            }

            if (!string.IsNullOrEmpty(Password))
            {
                conn = $"{conn},password={Password}";
            }
            if (Database > 0)
            {
                conn = $"{conn},defaultDatabase={Database}";
            }
            conn = $"{conn},abortConnect=false";
            return conn;
        }
    }
}

public class MessagePublishersOptions
{
    public Dictionary<string, object> GeneralMQConfig { get; set; } = new();
    public Dictionary<string, PublisherTopicConfig> Topics { get; set; } = new();
}

public class MessageConsumersOptions
{
    public Dictionary<string, object> GeneralMQConfig { get; set; } = new();
    public string Subscription { get; set; } = string.Empty;
    public Dictionary<string, ConsumerTopicConfig> Topics { get; set; } = new();
}

public class PublisherTopicConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // rabbitmq, kafka, pubsub, mqtt
    public Dictionary<string, object> MQConfig { get; set; } = new();
}

public class ConsumerTopicConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object> MQConfig { get; set; } = new();
}

// Broker Options for Deserialization

public class KafkaOptions
{
    public List<string> Brokers { get; set; } = new();
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ClientId { get; set; } = "skeleton-api-net";
    public string GroupId { get; set; } = "skeleton-group";
    public bool CreateTopics { get; set; } = true;
    public string Compression { get; set; } = "snappy";
    public int ConcurrentConsumers { get; set; } = 1;
    // Topics/Subscriptions dictionaries are not needed for raw broker config anymore, but we'll keep simple structure if needed or minimal
    // keeping minimal for connection config
}

public class PubSubOptions
{
    public string Credentials { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public bool CreateSubscription { get; set; } = true;
    public int ConcurrentConsumers { get; set; } = 1;
}

public class RabbitMQOptions
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 5672;

    [JsonPropertyName("username")]
    public string Username { get; set; } = "guest";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "guest";

    [JsonPropertyName("vhost")]
    public string VHost { get; set; } = "/";

    [JsonPropertyName("message_ttl")]
    public int MessageTtl { get; set; } = 2;

    [JsonPropertyName("queue_expiration")]
    public int QueueExpiration { get; set; } = 3;

    [JsonPropertyName("enable_dlq")]
    public bool EnableDlq { get; set; } = true;

    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; set; } = 5;

    [JsonPropertyName("concurrent_consumers")]
    public int ConcurrentConsumers { get; set; } = 3;

    [JsonPropertyName("queue_type")]
    public string QueueType { get; set; } = "quorum"; // default to quorum for high availability

    [JsonPropertyName("queue_auto_delete")]
    public bool QueueAutoDelete { get; set; } = false;

    [JsonPropertyName("queue_durable")]
    public bool QueueDurable { get; set; } = true;

    [JsonPropertyName("exchange_durable")]
    public bool ExchangeDurable { get; set; } = true;

    public string GetConnectionString()
    {
        var vhost = string.IsNullOrEmpty(VHost) || VHost == "/" ? "" : VHost;
        if (!string.IsNullOrEmpty(vhost) && !vhost.StartsWith("/")) vhost = "/" + vhost;
        return $"amqp://{Username}:{Password}@{Host}:{Port}{vhost}";
    }
}

public class MqttOptions
{
    public string Broker { get; set; } = "tcp://localhost:1883";
    public string ClientId { get; set; } = "skeleton-api-net";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int Qos { get; set; } = 1;
    public bool CleanSession { get; set; } = true;
    public int KeepAliveSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 5;
    public int ConcurrentConsumers { get; set; } = 1;
}

public class HttpClientOptions
{
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 500;
    public int RetryTimeoutSeconds { get; set; } = 5;
    public int BreakDurationSeconds { get; set; } = 30;
    public int MaxAttemptBeforeBreak { get; set; } = 3;
    public int HandlerTimeoutMinutes { get; set; } = 5;
}

public class GrpcClientOptions
{
    public GrpcServiceOptions? UserService { get; set; }
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
    public TlsOptions Tls { get; set; } = new();

    public CircuitBreakerOptions GetCircuitBreakerForUserService() => UserService?.CircuitBreaker ?? CircuitBreaker;
    public TlsOptions GetTlsForUserService() => UserService?.Tls ?? Tls;
}

public class GrpcServiceOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4022;
    public string Address { get; set; } = "http://localhost:4022";
    public CircuitBreakerOptions? CircuitBreaker { get; set; }
    public TlsOptions? Tls { get; set; }
}

public class RestClientOptions
{
    public UserServiceOptions UserService { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
    public TlsOptions Tls { get; set; } = new();

    public CircuitBreakerOptions GetCircuitBreakerForUserService() => UserService?.CircuitBreaker ?? CircuitBreaker;
    public TlsOptions GetTlsForUserService() => UserService?.Tls ?? Tls;

    public class UserServiceOptions
    {
        public string BaseUrl { get; set; } = "http://localhost:4021";
        public int TimeoutSeconds { get; set; } = 10;
        public CircuitBreakerOptions? CircuitBreaker { get; set; }
        public TlsOptions? Tls { get; set; }
    }

    public class RetryOptions
    {
        public int MaxAttempts { get; set; } = 3;
        public int DelayMs { get; set; } = 500;
    }
}

public class CircuitBreakerOptions
{
    public int MaxRequests { get; set; } = 0;
    public int IntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 5;
    public double FailureRatio { get; set; } = 0.5;
    public int MinRequests { get; set; } = 5;
}

public class TlsOptions
{
    public bool Enabled { get; set; } = false;
    public bool InsecureSkipVerify { get; set; } = false;
    public string CaFile { get; set; } = string.Empty;
    public string CertFile { get; set; } = string.Empty;
    public string KeyFile { get; set; } = string.Empty;
}

public class FeatureFlagOptions
{
    public string Provider { get; set; } = "flipt";
    public string Host { get; set; } = "http://localhost:8080";
    public string Path { get; set; } = "config/flags.yaml";
    public int TimeoutSeconds { get; set; } = 2;
    public string NamespaceKey { get; set; } = "default";
    public string ClientToken { get; set; } = string.Empty;
    public FeatureFlagCacheOptions Cache { get; set; } = new();
}

public class FeatureFlagCacheOptions
{
    public bool Enabled { get; set; } = true;
    public int TtlSeconds { get; set; } = 60;
    public int RefreshSeconds { get; set; } = 30;
    public List<string> WarmupFlags { get; set; } = new();
    public bool MetricsEnabled { get; set; } = true;
}

public class ProfilingOptions
{
    public bool Enabled { get; set; } = false;
    public int Port { get; set; } = 6060;
    public string Host { get; set; } = "localhost";
}

public class ObservabilityOptions
{
    public ElasticApmOptions ElasticApm { get; set; } = new();

    public class ElasticApmOptions
    {
        public bool Enabled { get; set; } = false;
        public string ServerUrl { get; set; } = "http://localhost:8200";
        public string ServiceName { get; set; } = "skeleton-api-net";
        public string ServiceVersion { get; set; } = "1.0.0";
        public string Environment { get; set; } = "development";
        public string SecretToken { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string CaptureBody { get; set; } = "all";
        public bool CaptureHeaders { get; set; } = true;
        public double TransactionSampleRate { get; set; } = 1.0;
        public string LogLevel { get; set; } = "Info";
    }
}
