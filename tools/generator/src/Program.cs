using SkeletonApi.Generator.Commands;
using Spectre.Console.Cli;

namespace SkeletonApi.Generator;

class Program
{
    static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("generator");

            config.AddCommand<ProjectCommand>("project")
                .WithDescription("Generate a new project (Full API, MQ-only, or Entity-based)")
                .WithExample(new[] { "project", "--input", "user.proto", "--type", "proto", "--name", "my-service", "--output", "./my-service" })
                .WithExample(new[] { "project", "--input", "api-spec.yaml", "--type", "openapi", "--name", "my-service", "--output", "./my-service" });

            config.AddCommand<EntityCommand>("entity")
                .WithDescription("Add entity to existing project")
                .WithExample(new[] { "entity", "--input", "product.proto", "--type", "proto", "--output", "." });

            config.AddCommand<ClientCommand>("client")
                .WithDescription("Generate external service client (REST/gRPC)")
                .WithExample(new[] { "client", "--input", "payment.proto", "--type", "grpc", "--name", "PaymentService", "--output", "." })
                .WithExample(new[] { "client", "--input", "external-api.yaml", "--type", "openapi", "--name", "ExternalApiClient", "--output", "." });

            config.AddCommand<MqCommand>("mq")
                .WithDescription("Generate message publisher (RabbitMQ/PubSub) or consumer (if input has subscription)")
                .WithExample(new[] { "mq", "--input", "mq_publisher.json", "--output", "." });

            config.AddCommand<ConsumerCommand>("consumer")
                .WithDescription("Generate message consumer logic (Deprecated, use mq command)")
                .WithExample(new[] { "consumer", "--input", "mq_subscriber.json", "--output", "." });


            config.AddCommand<NewConsumerCommand>("new-consumer")
                .WithDescription("Generate a new Worker Service project for MQ consumption")
                .WithExample(new[] { "new-consumer", "--name", "OrderConsumer", "--input", "mq_subscriber.json", "--output", "." });
        });

        return app.Run(args);
    }
}
