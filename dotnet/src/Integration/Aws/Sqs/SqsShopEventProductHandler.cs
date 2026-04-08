using AWS.Messaging;
using Microsoft.Extensions.Logging;

namespace Integration.Aws.Sqs;

/// <summary>
/// AWS.Messaging handler that receives <see cref="SqsShopEventProductMessage"/> messages
/// from the configured SQS queue and routes them to the appropriate
/// <see cref="IShopifyWebhookHandler"/> based on the Shopify webhook topic in the message
/// metadata. Messages whose topic has no registered handler are silently discarded.
/// </summary>
public class SqsShopEventProductHandler(IEnumerable<IShopifyWebhookHandler> handlers, ILogger<SqsShopEventProductHandler> logger)
    : IMessageHandler<SqsShopEventProductMessage>
{
    /// <summary>
    /// Dispatches the incoming SQS message to the matching <see cref="IShopifyWebhookHandler"/>.
    /// Returns <see cref="MessageProcessStatus.Success"/> on both successful processing and
    /// unrecognised topics; returns <see cref="MessageProcessStatus.Failed"/> if the handler
    /// throws an exception, signalling the message should be retried or sent to a dead-letter queue.
    /// </summary>
    /// <param name="messageEnvelope">The SQS message envelope containing the Shopify event.</param>
    /// <param name="token">Cancellation token.</param>
    public async Task<MessageProcessStatus> HandleAsync(MessageEnvelope<SqsShopEventProductMessage> messageEnvelope,
        CancellationToken token = new ())
    {
        try
        {
            logger.LogInformation(
                "Started processing Shopify message: {MessageDetails}",
                messageEnvelope.Message.ToShortString());

            var handlerForTopic =
                handlers.FirstOrDefault(handler => handler.TopicName == messageEnvelope.Message.Detail.Metadata.Topic);

            if (handlerForTopic is null)
            {
                logger.LogInformation("No handler found for topic: {Topic}. This message will be discarded.",
                    messageEnvelope.Message.Detail.Metadata.Topic);
                return MessageProcessStatus.Success();
            }

            logger.LogDebug(
                "Starting processing Shopify message for topic [{MetadataTopic}] using handler [{HandlerName}].",
                messageEnvelope.Message.Detail.Metadata.Topic, handlerForTopic.GetType().Name);

            await handlerForTopic.Handle(messageEnvelope.Message.Detail.Payload);

            logger.LogInformation(
                "Finished processing Shopify message: {MessageDetails}",
                messageEnvelope.Message.ToShortString());

            return MessageProcessStatus.Success();
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Failed to process Shopify message for topic: {MetadataTopic}.", messageEnvelope.Message.Detail.Metadata
                    .Topic);

            return MessageProcessStatus.Failed();
        }
    }
}