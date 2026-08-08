using Application.Sync.Merge;
using Application.Sync.Merge.Rules;
using Shouldly;

namespace Tests.Application.Sync;

/// <summary>
/// The chain's job is to make overlapping field ownership a startup failure rather than a silent
/// last-writer-wins. Field authority that depends on which rule happens to run last is the class of
/// bug this whole mechanism replaced, so it must not be reachable by adding a rule.
/// </summary>
public class MergeRuleChainTests
{
    [Fact]
    public void Constructor_ShouldThrow_WhenTwoRulesClaimTheSameField()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            new MergeRuleChain([new StubRule(ItemField.Sku), new StubRule(ItemField.Sku)]));

        exception.Message.ShouldContain(nameof(StubRule));
        exception.Message.ShouldContain(nameof(ItemField.Sku));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOwnershipOverlapsOnOnlyOneOfSeveralFields()
    {
        Should.Throw<InvalidOperationException>(() =>
            new MergeRuleChain([
                new StubRule(ItemField.Sku, ItemField.Barcode),
                new StubRule(ItemField.Barcode, ItemField.Title)
            ]));
    }

    [Fact]
    public void Constructor_ShouldAccept_DisjointOwnership()
    {
        var chain = new MergeRuleChain([
            new StubRule(ItemField.Sku),
            new StubRule(ItemField.Barcode, ItemField.Title)
        ]);

        chain.UnownedFields.ShouldBe([ItemField.Location]);
    }

    /// <summary>
    /// The shipped set is the one that matters: a registration mistake here would otherwise only
    /// surface when a host boots.
    /// </summary>
    [Fact]
    public void Constructor_ShouldAccept_TheProductionRuleSet_AndLeaveNoFieldUnowned()
    {
        var chain = MergeTestFactory.CreateChain(skuGenerator: null!);

        chain.UnownedFields.ShouldBeEmpty();
    }

    [Fact]
    public async Task Apply_ShouldRunRulesInSequence_EachSeeingTheRunningResult()
    {
        var chain = new MergeRuleChain([
            new SetterRule(ItemField.Sku, context => context.Result.Sku = "first"),
            new SetterRule(ItemField.Barcode, context => context.Result.Barcode = context.Result.Sku + "-second")
        ]);

        var result = await chain.Apply(BuildContext());

        result.Sku.ShouldBe("first");
        result.Barcode.ShouldBe("first-second");
        result.ChangedFields.ShouldBe([ItemField.Sku, ItemField.Barcode], ignoreOrder: true);
    }

    /// <summary>Silence means "leave it alone", so a field no rule touches keeps its seeded value.</summary>
    [Fact]
    public async Task Apply_ShouldLeaveUntouchedFieldsAlone()
    {
        var chain = new MergeRuleChain([new SetterRule(ItemField.Sku, context => context.Result.Sku = "new")]);

        var result = await chain.Apply(BuildContext(title: "seeded title"));

        result.Title.ShouldBe("seeded title");
        result.Changed(ItemField.Title).ShouldBeFalse();
    }

    /// <summary>Assigning the value already there is not a change, so it earns no audit event.</summary>
    [Fact]
    public async Task Apply_ShouldNotRecordAChange_WhenARuleAssignsTheSameValue()
    {
        var chain = new MergeRuleChain([new SetterRule(ItemField.Sku, context => context.Result.Sku = "same")]);

        var result = await chain.Apply(BuildContext(sku: "same"));

        result.HasChanges.ShouldBeFalse();
    }

    private static MergeContext BuildContext(string sku = "", string title = "") =>
        new(MergeOrigin.Routine, 1, "Product", "Variant",
            ItemObservation.None, ItemObservation.None,
            new MergeResult(sku, "", title, ""),
            new HashSet<string>(StringComparer.Ordinal));

    private sealed class StubRule(params ItemField[] fields) : IMergeRule
    {
        public IReadOnlyCollection<ItemField> OwnedFields { get; } = fields;

        public ValueTask Apply(MergeContext context, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class SetterRule(ItemField field, Action<MergeContext> apply) : IMergeRule
    {
        public IReadOnlyCollection<ItemField> OwnedFields { get; } = [field];

        public ValueTask Apply(MergeContext context, CancellationToken cancellationToken = default)
        {
            apply(context);
            return ValueTask.CompletedTask;
        }
    }
}
