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
    private readonly IShopifyImportService _shopifyImportService = Substitute.For<IShopifyImportService>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly TestLogger<ShopifySyncJob> _logger = new();

    [Fact]
    public async Task Execute_ShouldCallImportProducts()
    {
        var sut = CreateSut();

        await sut.Execute(_context);

        await _shopifyImportService.Received(1).ImportProducts();
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
        var sut = CreateSut();

        await sut.Execute(_context);

        _logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Information && e.Message.Contains("completed"));
    }

    [Fact]
    public async Task Execute_ShouldThrowJobExecutionException_WhenImportProductsThrows()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _shopifyImportService.ImportProducts().ThrowsAsync(exception);
        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        thrown.InnerException.ShouldBeSameAs(exception);
    }

    [Fact]
    public async Task Execute_ShouldNotRefireImmediately_WhenJobFails()
    {
        _shopifyImportService.ImportProducts().ThrowsAsync(new InvalidOperationException());
        var sut = CreateSut();

        var thrown = await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        thrown.RefireImmediately.ShouldBeFalse();
    }

    [Fact]
    public async Task Execute_ShouldLogError_WhenImportProductsThrows()
    {
        var exception = new InvalidOperationException("Shopify unavailable");
        _shopifyImportService.ImportProducts().ThrowsAsync(exception);
        var sut = CreateSut();

        await Should.ThrowAsync<JobExecutionException>(() => sut.Execute(_context));

        var errorLogs = _logger.Entries.Where(e => e.LogLevel == LogLevel.Error).ToArray();
        errorLogs.Length.ShouldBe(1);
        errorLogs[0].Exception.ShouldBeSameAs(exception);
    }

    private ShopifySyncJob CreateSut() => new(_shopifyImportService, _logger);

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
