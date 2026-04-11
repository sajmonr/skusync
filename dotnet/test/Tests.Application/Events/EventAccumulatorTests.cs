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
        var evt = ProductChangedEvent.Created(Guid.NewGuid());

        _sut.Enqueue(evt);
        var result = _sut.DrainAll();

        result.Count.ShouldBe(1);
        result[0].ShouldBe(evt);
    }

    [Fact]
    public void DrainAll_ShouldReturnAllEvents_WhenMultipleEventsWereEnqueued()
    {
        _sut.Enqueue(ProductChangedEvent.Created(Guid.NewGuid()));
        _sut.Enqueue(ProductChangedEvent.Updated(Guid.NewGuid()));
        _sut.Enqueue(ProductChangedEvent.Created(Guid.NewGuid()));

        var result = _sut.DrainAll();

        result.Count.ShouldBe(3);
    }

    [Fact]
    public void DrainAll_ShouldPreserveFifoOrder()
    {
        var first  = ProductChangedEvent.Created(Guid.NewGuid());
        var second = ProductChangedEvent.Updated(Guid.NewGuid());

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
        _sut.Enqueue(ProductChangedEvent.Created(Guid.NewGuid()));

        _sut.DrainAll();
        var secondDrain = _sut.DrainAll();

        secondDrain.ShouldBeEmpty();
    }

    [Fact]
    public void DrainAll_ShouldOnlyReturnEventsThatWereEnqueuedBeforeTheCall()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        _sut.Enqueue(ProductChangedEvent.Created(firstId));

        var firstDrain = _sut.DrainAll();

        _sut.Enqueue(ProductChangedEvent.Updated(secondId));

        var secondDrain = _sut.DrainAll();

        firstDrain.Count.ShouldBe(1);
        firstDrain[0].ProductVariantId.ShouldBe(firstId);

        secondDrain.Count.ShouldBe(1);
        secondDrain[0].ProductVariantId.ShouldBe(secondId);
    }
}
