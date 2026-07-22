namespace Integration.Aws.Sqs;

/// <summary>
/// Defines a handler for a specific Shopify webhook topic received via SQS.
/// Implement this interface for each Shopify topic (e.g. <c>products/create</c>,
/// <c>products/update</c>) that the application needs to react to.
/// </summary>
public interface IShopifyWebhookHandler
{
    /// <summary>
    /// Gets the Shopify webhook topic this handler is responsible for,
    /// e.g. <c>products/create</c> or <c>products/update</c>.
    /// </summary>
    string TopicName { get; }

    /// <summary>
    /// Processes the Shopify product payload received for this handler's topic.
    /// </summary>
    /// <param name="product">The deserialized product payload from the SQS message.</param>
    Task Handle(SqsShopEventProduct product);
}
