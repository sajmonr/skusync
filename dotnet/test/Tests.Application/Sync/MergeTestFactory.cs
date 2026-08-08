using Application.Skus;
using Application.Sync;
using Application.Sync.Merge;
using Application.Sync.Merge.Rules;
using Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Application.Sync;

/// <summary>
/// Builds the real reconciler over the real rule chain.
/// <para>
/// Tests of the ingest handlers use this rather than a substituted <see cref="IReconciler"/>. They
/// were written to assert outcomes — a new variant ends up with a generated SKU owed to Shopify —
/// and those outcomes still hold even though the step that produces them moved out of ingest and
/// into the merge rules. Substituting the reconciler would turn them into assertions that a
/// collaborator was called, which is the mechanism, not the behaviour.
/// </para>
/// </summary>
internal static class MergeTestFactory
{
    public static Reconciler CreateReconciler(ApplicationDbContext dbContext, ISkuGenerator skuGenerator) =>
        new(dbContext, CreateChain(skuGenerator), NullLogger<Reconciler>.Instance);

    public static Reconciler CreateReconciler(ApplicationDbContext dbContext) =>
        CreateReconciler(dbContext, CreateSkuGenerator(dbContext));

    public static MergeRuleChain CreateChain(ISkuGenerator skuGenerator) =>
        new([
            new SkuMergeRule(skuGenerator, NullLogger<SkuMergeRule>.Instance),
            new BarcodeMergeRule(),
            new TitleMergeRule(),
            new LocationMergeRule()
        ]);

    public static SkuGenerator CreateSkuGenerator(ApplicationDbContext dbContext) =>
        new(dbContext, Options.Create(new SkuGeneratorOptions()), NullLogger<SkuGenerator>.Instance);
}
