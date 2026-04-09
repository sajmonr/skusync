using Application.Jobs;
using Application.Shopify;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using Shouldly;

namespace Tests.Application.Jobs;

public class ShopifySyncJobTests
{
    private readonly IShopifyService _shopifyService = Substitute.For<IShopifyService>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<ShopifySyncJob> _logger = new();

    [Fact]
    public async Task Execute_ShouldCallImportProducts()
    {
        var sut = CreateSut();

        await sut.Execute(_context);

        await _shopifyService.Received(1).ImportProducts();
    }

    [Fact]
    public async Task Execute_ShouldLogInformation_WhenJobStarts()
    {
        var sut = CreateSut();

        await sut.Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("started"));
    }

    [Fact]
    public async Task Execute_ShouldLogInformation_WhenJobCompletesSuccessfully()
    {
        _shopifyService.ImportProducts().Returns(ProductImportResult.Success(0, 0));
        var sut = CreateSut();

        await sut.Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public async Task Execute_ShouldThrowJobExecutionException_WhenImportProductsThrows()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _shopifyService.ImportProducts().ThrowsAsync(exception);
        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        thrown.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public async Task Execute_ShouldNotRefireImmediately_WhenJobFails()
    {
        _shopifyService.ImportProducts().ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        thrown.RefireImmediately.ShouldBeFalse();
    }

    [Fact]
    public async Task Execute_ShouldLogError_WhenImportProductsThrows()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _shopifyService.ImportProducts().ThrowsAsync(exception);
        var sut = CreateSut();

        await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        var errorLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errorLogs.Length.ShouldBe(1);
        errorLogs[0].Exception.ShouldBeSameAs(exception);
    }

    private ShopifySyncJob CreateSut() => new(_shopifyService, _logger);

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
