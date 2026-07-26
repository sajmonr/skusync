using System.Reflection;
using System.Text;
using SlimMessageBus;
using SlimMessageBus.Host;
using SlimMessageBus.Host.RabbitMQ;
using SlimMessageBus.Host.Serialization.SystemTextJson;

namespace Application.Messaging;

/// <summary>
/// RabbitMQ topology for the cross-process application events, derived by scanning this assembly
/// for <see cref="IConsumer{TMessage}"/> implementations — the interface is the single source of
/// truth, so a new event/consumer needs no registration or annotation here.
///
/// Each event gets a durable fanout exchange named <c>skusync.{event-type}</c> (the <c>Event</c>
/// suffix is kept, e.g. <c>skusync.product-variant-created-event</c>); each consumer gets a
/// durable work queue named <c>skusync.{consumer}</c> with the <c>Consumer</c> suffix trimmed,
/// itself suffixed with <c>.{event-type}</c> when the consumer handles more than one event, bound
/// to that exchange. Keeping the event suffix stops an exchange from colliding with a queue that
/// shares the base name. Names come from the type names, so a queue/exchange that reads badly is
/// a signal to rename the type, not to add a string.
/// Because each queue is named and durable, every running instance of AppServer competes on the
/// same queue — a published event is handled by exactly one instance per consumer, however many
/// AppServers run. Distinct consumers each own a queue, so both still see every event. A consumer
/// that throws nacks its message onto <see cref="DeadLetterExchange"/> rather than blocking.
///
/// Production (<see cref="ConfigureProducers"/>) and consumption (<see cref="ConfigureConsumers"/>)
/// are configured separately so a host can produce without consuming: Web.Api produces only,
/// AppServer does both. The producible events are those with at least one consumer in this
/// assembly; an event with no consumer is not published anywhere and so needs no exchange.
/// </summary>
internal static class ApplicationEventBus
{
    /// <summary>Named connection string (<c>ConnectionStrings:RabbitMq</c>) carrying the AMQP URI.</summary>
    public const string ConnectionStringName = "RabbitMq";

    /// <summary>
    /// Builds a RabbitMQ <see cref="ConnectionFactory"/> from the AMQP connection string with the
    /// project's standard settings applied. Used both by the startup connectivity preflight and by
    /// the health-check connection, so every connection this codebase opens is configured the same
    /// way as the message bus itself.
    /// </summary>
    public static RabbitMQ.Client.ConnectionFactory CreateConnectionFactory(string connectionString)
    {
        var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(connectionString) };
        ConfigureConnectionFactory(factory);
        return factory;
    }

    // Automatic recovery lets a consumer/producer reconnect after a transient broker blip instead
    // of leaving a dead channel behind (the failure mode that motivated this health work). Topology
    // recovery re-declares the exchanges, queues, and bindings on the recovered connection.
    private static void ConfigureConnectionFactory(RabbitMQ.Client.ConnectionFactory factory)
    {
        factory.AutomaticRecoveryEnabled = true;
        factory.TopologyRecoveryEnabled = true;
    }

    // Single shared dead-letter exchange every work queue nacks poison messages onto.
    private const string DeadLetterExchange = "skusync.dead-letter";

    private static readonly MethodInfo DeclareProducerMethod =
        typeof(ApplicationEventBus).GetMethod(nameof(DeclareProducer), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo DeclareConsumerMethod =
        typeof(ApplicationEventBus).GetMethod(nameof(DeclareConsumer), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Registers the RabbitMQ provider, JSON serialization, and an exchange producer for every
    /// event consumed in this assembly. Configured by every host that can publish events — i.e.
    /// any host calling <c>AddApplication</c>.
    /// </summary>
    public static void ConfigureProducers(MessageBusBuilder bus, string connectionString)
    {
        bus.WithProviderRabbitMQ(rabbit =>
        {
            rabbit.ConnectionString = connectionString;
            ConfigureConnectionFactory(rabbit.ConnectionFactory);
            rabbit.UseExchangeDefaults(durable: true);
            rabbit.UseQueueDefaults(durable: true);
        });
        bus.AddJsonSerializer();

        foreach (var eventType in DiscoverBindings().Select(b => b.EventType).Distinct())
        {
            DeclareProducerMethod.MakeGenericMethod(eventType).Invoke(null, [bus]);
        }
    }

    /// <summary>
    /// Registers every consumer discovered in this assembly and binds its work queue to the
    /// matching exchange. Configured only by hosts that process events (AppServer), on top of
    /// <see cref="ConfigureProducers"/> — the provider and serializer are established there.
    /// <c>AddServicesFromAssemblyContaining</c> registers every <c>IConsumer&lt;T&gt;</c> with the
    /// DI container so SlimMessageBus can resolve them when a message arrives.
    /// </summary>
    public static void ConfigureConsumers(MessageBusBuilder bus)
    {
        bus.AddServicesFromAssembly(typeof(ApplicationEventBus).Assembly);

        foreach (var (consumerType, eventType, handlesMultipleEvents) in DiscoverBindings())
        {
            var queue = QueueName(consumerType, eventType, handlesMultipleEvents);
            DeclareConsumerMethod.MakeGenericMethod(eventType, consumerType).Invoke(null, [bus, queue]);
        }
    }

    private static void DeclareProducer<TEvent>(MessageBusBuilder bus) =>
        bus.Produce<TEvent>(x => x.Exchange(ExchangeName(typeof(TEvent)), exchangeType: ExchangeType.Fanout));

    private static void DeclareConsumer<TEvent, TConsumer>(MessageBusBuilder bus, string queue)
        where TConsumer : class, IConsumer<TEvent> =>
        bus.Consume<TEvent>(x => x
            .Queue(queue)
            .ExchangeBinding(ExchangeName(typeof(TEvent)))
            .DeadLetterExchange(DeadLetterExchange, exchangeType: ExchangeType.Fanout, durable: true)
            .WithConsumer<TConsumer>());

    private static IEnumerable<(Type ConsumerType, Type EventType, bool HandlesMultipleEvents)> DiscoverBindings()
    {
        foreach (var consumerType in typeof(ApplicationEventBus).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            var eventTypes = EventTypesConsumedBy(consumerType);
            var handlesMultipleEvents = eventTypes.Count > 1;
            foreach (var eventType in eventTypes)
            {
                yield return (consumerType, eventType, handlesMultipleEvents);
            }
        }
    }

    private static IReadOnlyList<Type> EventTypesConsumedBy(Type type) =>
        type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

    // The "Event" suffix is kept in exchange (and multi-event queue) names so an event-derived
    // exchange never collides with a consumer-derived queue that happens to share the base name.
    private static string ExchangeName(Type eventType) =>
        $"skusync.{Kebab(eventType.Name)}";

    private static string QueueName(Type consumerType, Type eventType, bool handlesMultipleEvents)
    {
        var purpose = Kebab(TrimSuffix(consumerType.Name, "Consumer"));
        return handlesMultipleEvents
            ? $"skusync.{purpose}.{Kebab(eventType.Name)}"
            : $"skusync.{purpose}";
    }

    private static string TrimSuffix(string name, string suffix) =>
        name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;

    private static string Kebab(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
