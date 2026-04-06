namespace Integration.Aws.Sqs;

public interface IShopifyWebhookHandler
{
    string TopicName { get; }

    Task Handle(SqsShopEventProduct product);
}