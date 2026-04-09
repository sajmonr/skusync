using Application.Events;
using Application.Products.Events;
using Shouldly;

namespace Tests.Application.Events;

public class EventAccumulatorTests
{
    private readonly EventAccumulator<ProductChangedEvent> _sut = new();

    // -------------------------------------------------------------------------
    // Enqueue
    // -------------------------------------------------------------------------

    [Fact]
    public void DrainAll_ShouldReturnEmptyList_WhenNothingHasBeenEnqueued()
    {
        var result = _sut.DrainAll();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void DrainAll_ShouldReturnEnqueuedEvent_WhenOneEventWasAdded()
    {
        var evt = new ProductChangedEvent(100L, ProductChangeType.Created);

        _sut.Enqueue(evt);
        var result = _sut.DrainAll();

        result.Count.ShouldBe(1);
        result[0].ShouldBe(evt);
    }

    [Fact]
    public void DrainAll_ShouldReturnAllEvents_WhenMultipleEventsWereEnqueued()
    {
        _sut.Enqueue(new ProductChangedEvent(100L, ProductChangeType.Created));
        _sut.Enqueue(new ProductChangedEvent(200L, ProductChangeType.Updated));
        _sut.Enqueue(new ProductChangedEvent(300L, ProductChangeType.Created));

        var result = _sut.DrainAll();

        result.Count.ShouldBe(3);
    }

    [Fact]
    public void DrainAll_ShouldPreserveFifoOrder()
    {
        var first  = new ProductChangedEvent(100L, ProductChangeType.Created);
        var second = new ProductChangedEvent(200L, ProductChangeType.Updated);

        _sut.Enqueue(first);
        _sut.Enqueue(second);

        var result = _sut.DrainAll();

        result[0].ShouldBe(first);
        result[1].ShouldBe(second);
    }

    // -------------------------------------------------------------------------
    // DrainAll clears the queue
    // -------------------------------------------------------------------------

    [Fact]
    public void DrainAll_ShouldEmptyTheQueue_SoSubsequentDrainReturnsNothing()
    {
        _sut.Enqueue(new ProductChangedEvent(100L, ProductChangeType.Created));

        _sut.DrainAll();
        var secondDrain = _sut.DrainAll();

        secondDrain.ShouldBeEmpty();
    }

    [Fact]
    public void DrainAll_ShouldOnlyReturnEventsThatWereEnqueuedBeforeTheCall()
    {
        _sut.Enqueue(new ProductChangedEvent(100L, ProductChangeType.Created));

        var firstDrain = _sut.DrainAll();

        _sut.Enqueue(new ProductChangedEvent(200L, ProductChangeType.Updated));

        var secondDrain = _sut.DrainAll();

        firstDrain.Count.ShouldBe(1);
        firstDrain[0].VariantId.ShouldBe(100L);

        secondDrain.Count.ShouldBe(1);
        secondDrain[0].VariantId.ShouldBe(200L);
    }
}
