// ReSharper disable once CheckNamespace
namespace SlimMessageBus;

public static class MessageBusExtensions
{
    /// <summary>
    /// Publishes each message in the sequence individually. SlimMessageBus routes by the
    /// runtime type of the published value, so passing an <see cref="IEnumerable{T}"/>
    /// directly into <see cref="IMessageBus.Publish{TMessage}"/> publishes a single message
    /// of type <c>IEnumerable&lt;T&gt;</c> — which does not match any <c>IConsumer&lt;T&gt;</c>
    /// and fails inside the bus. This helper iterates and publishes one message at a time
    /// so each event is routed to its consumer.
    /// </summary>
    public static async Task PublishBatch<TMessage>(
        this IMessageBus messageBus,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default)
    {
        foreach (var message in messages)
        {
            await messageBus.Publish(message, cancellationToken: cancellationToken);
        }
    }
}
