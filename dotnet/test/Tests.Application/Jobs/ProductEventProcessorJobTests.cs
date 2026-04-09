using Application.Events;
using Application.Products.Events;
using Application.Products.Jobs;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using Shouldly;

namespace Tests.Application.Jobs;

public class ProductEventProcessorJobTests
{
    private readonly IEventAccumulator<ProductChangedEvent> _eventAccumulator = Substitute.For<IEventAccumulator<ProductChangedEvent>>();
    private readonly IJobExecutionContext _context = Substitute.For<IJobExecutionContext>();
    private readonly ILogger<ProductEventProcessorJob> _logger = Substitute.For<ILogger<ProductEventProcessorJob>>();

    public ProductEventProcessorJobTests()
    {
        // Default: no accumulated events.
        _eventAccumulator.DrainAll().Returns([]);
    }

    // -------------------------------------------------------------------------
    // Accumulator interaction
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Execute_ShouldDrainAccumulator_OnEveryRun()
    {
        await CreateSut().Execute(_context);

        _eventAccumulator.Received(1).DrainAll();
    }

    [Fact]
    public async Task Execute_ShouldProcessAllDrainedEvents_WhenEventsArePresent()
    {
        _eventAccumulator.DrainAll().Returns(
        [
            new ProductChangedEvent(100L, ProductChangeType.Created),
            new ProductChangedEvent(200L, ProductChangeType.Updated)
        ]);

        // Should complete without throwing.
        await Should.NotThrowAsync(async () => await CreateSut().Execute(_context));
    }

    private ProductEventProcessorJob CreateSut() => new(_eventAccumulator, _logger);

}
