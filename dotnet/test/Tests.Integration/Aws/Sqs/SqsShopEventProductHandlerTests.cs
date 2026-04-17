using AWS.Messaging;
using Integration.Aws.Sqs;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Tests.Integration.Aws.Sqs;

public class SqsShopEventProductHandlerTests
{
    private readonly TestLogger<SqsShopEventProductHandler> _logger = new();

    // -------------------------------------------------------------------------
    // Handler routing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ShouldRouteToMatchingHandler_WhenTopicMatches()
    {
        var handler = Substitute.For<IShopifyWebhookHandler>();
        handler.TopicName.Returns("products/create");

        var envelope = CreateEnvelope("products/create");

        await CreateSut(handler).HandleAsync(envelope);

        await handler.Received(1).Handle(Arg.Any<SqsShopEventProduct>());
    }

    [Fact]
    public async Task HandleAsync_ShouldPassCorrectPayload_ToMatchingHandler()
    {
        var handler = Substitute.For<IShopifyWebhookHandler>();
        handler.TopicName.Returns("products/create");

        var envelope = CreateEnvelope("products/create", productId: 42);

        await CreateSut(handler).HandleAsync(envelope);

        await handler.Received(1).Handle(
            Arg.Is<SqsShopEventProduct>(p => p.Id == 42));
    }

    [Fact]
    public async Task HandleAsync_ShouldNotCallAnyHandler_WhenTopicHasNoMatch()
    {
        var handler = Substitute.For<IShopifyWebhookHandler>();
        handler.TopicName.Returns("products/create");

        var envelope = CreateEnvelope("products/delete");

        await CreateSut(handler).HandleAsync(envelope);

        await handler.DidNotReceive().Handle(Arg.Any<SqsShopEventProduct>());
    }

    [Fact]
    public async Task HandleAsync_ShouldOnlyCallFirstMatchingHandler_WhenMultipleHandlersRegistered()
    {
        var createHandler = Substitute.For<IShopifyWebhookHandler>();
        createHandler.TopicName.Returns("products/create");

        var updateHandler = Substitute.For<IShopifyWebhookHandler>();
        updateHandler.TopicName.Returns("products/update");

        var envelope = CreateEnvelope("products/create");

        await CreateSut(createHandler, updateHandler).HandleAsync(envelope);

        await createHandler.Received(1).Handle(Arg.Any<SqsShopEventProduct>());
        await updateHandler.DidNotReceive().Handle(Arg.Any<SqsShopEventProduct>());
    }

    // -------------------------------------------------------------------------
    // Return values
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ShouldReturnSuccess_WhenHandlerCompletesWithoutError()
    {
        var handler = Substitute.For<IShopifyWebhookHandler>();
        handler.TopicName.Returns("products/create");

        var result = await CreateSut(handler).HandleAsync(CreateEnvelope("products/create"));

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnSuccess_WhenNoHandlerFoundForTopic()
    {
        var result = await CreateSut().HandleAsync(CreateEnvelope("products/unknown"));

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnFailed_WhenHandlerThrowsException()
    {
        var handler = Substitute.For<IShopifyWebhookHandler>();
        handler.TopicName.Returns("products/create");
        handler.Handle(Arg.Any<SqsShopEventProduct>()).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await CreateSut(handler).HandleAsync(CreateEnvelope("products/create"));

        result.ShouldNotBeNull();
        result.ShouldNotBe(MessageProcessStatus.Success());
    }

    // -------------------------------------------------------------------------
    // Logging
    // -------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_ShouldLogError_WhenHandlerThrowsException()
    {
        var handler = Substitute.For<IShopifyWebhookHandler>();
        handler.TopicName.Returns("products/create");
        handler.Handle(Arg.Any<SqsShopEventProduct>()).ThrowsAsync(new InvalidOperationException("boom"));

        await CreateSut(handler).HandleAsync(CreateEnvelope("products/create"));

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Error);
    }

    [Fact]
    public async Task HandleAsync_ShouldLogInformation_WhenNoHandlerFoundForTopic()
    {
        await CreateSut().HandleAsync(CreateEnvelope("products/unknown"));

        _logger.Entries.ShouldContain(e =>
            e.LogLevel == LogLevel.Information && e.Message.Contains("No handler found"));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private SqsShopEventProductHandler CreateSut(params IShopifyWebhookHandler[] handlers) =>
        new(handlers, _logger);

    private static MessageEnvelope<SqsShopEventProductMessage> CreateEnvelope(
        string topic, long productId = 1)
    {
        var message = new SqsShopEventProductMessage(
            Version: "0",
            Id: "evt-1",
            DetailType: "Shopify Event",
            Source: "shopify",
            Account: "123456789",
            Time: null,
            Region: "us-east-1",
            Resources: [],
            Detail: new SqsShopEventDetail(
                Payload: new SqsShopEventProduct(
                    AdminGraphqlApiId: $"gid://shopify/Product/{productId}",
                    Id: productId,
                    Variants: []),
                Metadata: new SqsShopEventMetadata(
                    ContentType: "application/json",
                    Topic: topic,
                    ShopDomain: "test.myshopify.com",
                    ProductId: productId.ToString(),
                    HmacSHA256: "hash",
                    WebhookId: "wh-1",
                    ApiVersion: "2024-01",
                    EventId: "ev-1",
                    TriggeredAt: "2024-01-01T00:00:00Z")));

        return new MessageEnvelope<SqsShopEventProductMessage> { Message = message };
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception);
}
