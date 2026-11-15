using MassTransit;

namespace Strategist.Infrastructure.Messaging
{

    /// <summary>
    /// The platform's MassTransit bus over RabbitMQ (hexagonal P2, replacing the
    /// raw-AMQP RabbitBus): native MassTransit envelope and topology, one bus per
    /// host. The broker URL resolves environment-first — RABBITMQ_URL (real env
    /// or a developer's .env via DotEnv.Load) wins over "RabbitMq:Url"
    /// (RabbitMq__Url in compose), defaulting to the local broker — so a stale
    /// appsettings default can never shadow the developer's broker credentials. Hosts register their consumers/endpoints through the callbacks;
    /// a publish-only host passes none.
    /// </summary>
    public static class StrategistBus
    {
        public static void AddStrategistBus(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<IBusRegistrationConfigurator>? registerConsumers = null,
            Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureEndpoints = null,
            string? url = null)
        {
            services.AddMassTransit(x =>
            {
                registerConsumers?.Invoke(x);
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(new Uri(url ?? Url(configuration)));
                    configureEndpoints?.Invoke(context, cfg);
                });
            });
        }

        public static string Url(IConfiguration configuration)
        {
            var env = Environment.GetEnvironmentVariable("RABBITMQ_URL");
            if (!string.IsNullOrEmpty(env)) return env;
            var configured = configuration["RabbitMq:Url"];
            return string.IsNullOrEmpty(configured) ? "amqp://localhost:5672" : configured;
        }
    }
}