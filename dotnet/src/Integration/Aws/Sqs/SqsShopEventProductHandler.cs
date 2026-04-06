using AWS.Messaging;
using Microsoft.Extensions.Logging;

namespace Integration.Aws.Sqs;

public class SqsShopEventProductHandler(IEnumerable<IShopifyWebhookHandler> handlers, ILogger<SqsShopEventProductHandler> logger)
    : IMessageHandler<SqsShopEventProductMessage>
{
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