using Infrastructure.Database.Entities;
using Shouldly;

namespace Tests.Application.Infrastructure;

public class ShopifyProductVariantEntityTests
{
    // -------------------------------------------------------------------------
    // FullTitle generation
    // -------------------------------------------------------------------------

    [Fact]
    public void FullTitle_ShouldBeProductTitleOnly_WhenVariantTitleIsEmpty()
    {
        var entity = new ShopifyProductVariantEntity { ProductTitle = "T-Shirt", VariantTitle = "" };

        entity.FullTitle.ShouldBe("T-Shirt");
    }

    [Fact]
    public void FullTitle_ShouldCombineProductAndVariantTitle_WhenBothAreSet()
    {
        var entity = new ShopifyProductVariantEntity { ProductTitle = "T-Shirt", VariantTitle = "Large" };

        entity.FullTitle.ShouldBe("T-Shirt (Large)");
    }

    [Fact]
    public void FullTitle_ShouldUpdate_WhenProductTitleIsChangedAfterConstruction()
    {
        var entity = new ShopifyProductVariantEntity { ProductTitle = "T-Shirt", VariantTitle = "Large" };

        entity.ProductTitle = "Hoodie";

        entity.FullTitle.ShouldBe("Hoodie (Large)");
    }

    [Fact]
    public void FullTitle_ShouldUpdate_WhenVariantTitleIsChangedAfterConstruction()
    {
        var entity = new ShopifyProductVariantEntity { ProductTitle = "T-Shirt", VariantTitle = "Small" };

        entity.VariantTitle = "Large";

        entity.FullTitle.ShouldBe("T-Shirt (Large)");
    }

    [Fact]
    public void FullTitle_ShouldDropParentheses_WhenVariantTitleIsChangedToEmpty()
    {
        var entity = new ShopifyProductVariantEntity { ProductTitle = "T-Shirt", VariantTitle = "Large" };

        entity.VariantTitle = "";

        entity.FullTitle.ShouldBe("T-Shirt");
    }

    // -------------------------------------------------------------------------
    // VariantTitle "Default Title" normalisation
    // -------------------------------------------------------------------------

    [Fact]
    public void VariantTitle_ShouldBeNormalisedToEmpty_WhenSetToDefaultTitle()
    {
        var entity = new ShopifyProductVariantEntity { VariantTitle = "Default Title" };

        entity.VariantTitle.ShouldBe("");
    }

    [Fact]
    public void FullTitle_ShouldBeProductTitleOnly_WhenVariantTitleIsDefaultTitle()
    {
        var entity = new ShopifyProductVariantEntity { ProductTitle = "T-Shirt", VariantTitle = "Default Title" };

        entity.FullTitle.ShouldBe("T-Shirt");
    }

    [Fact]
    public void VariantTitle_ShouldPreserveValue_WhenNotDefaultTitle()
    {
        var entity = new ShopifyProductVariantEntity { VariantTitle = "Large / Blue" };

        entity.VariantTitle.ShouldBe("Large / Blue");
    }

    [Fact]
    public void VariantTitle_ShouldRemainEmpty_WhenReassignedToDefaultTitle()
    {
        var entity = new ShopifyProductVariantEntity { VariantTitle = "Large" };

        entity.VariantTitle = "Default Title";

        entity.VariantTitle.ShouldBe("");
    }
}
