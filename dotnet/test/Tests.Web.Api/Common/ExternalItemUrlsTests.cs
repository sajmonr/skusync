using SharedKernel;
using Shouldly;

namespace Tests.Web.Api.Common;

public class ExternalItemUrlsTests
{
    [Fact]
    public void CreateShopifyProductUrl_ShouldUseProductAndVariantIds()
    {
        var result = ExternalItemUrls.CreateShopifyProductUrl(123, 456);

        result.ShouldBe("https://admin.shopify.com/store/ivyandlavyboutique/products/123/variants/456");
    }

    [Fact]
    public void CreateSkulabsItemUrl_ShouldEscapeTheItemId()
    {
        var result = ExternalItemUrls.CreateSkulabsItemUrl("item 1&2");

        result.ShouldBe("https://app.skulabs.com/item?id=item%201%262");
    }
}
