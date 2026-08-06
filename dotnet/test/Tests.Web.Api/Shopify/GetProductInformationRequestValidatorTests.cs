using Shouldly;
using Web.Api.Shopify.Features.GetProductInformation;

namespace Tests.Web.Api.Shopify;

public class GetProductInformationRequestValidatorTests
{
    private readonly GetProductInformationRequestValidator _validator = new();

    [Theory]
    [InlineData("gid://shopify/Product/987654321")]
    [InlineData("987654321")]
    public void Validate_AcceptsGlobalIdsAndBareNumbers(string productId)
    {
        var result = _validator.Validate(new GetProductInformationRequest { ProductId = productId });

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-product")]
    [InlineData("gid://shopify/ProductVariant/987654321")]
    [InlineData("0")]
    public void Validate_RejectsAnythingThatIsNotAPositiveProductId(string? productId)
    {
        var result = _validator.Validate(new GetProductInformationRequest { ProductId = productId });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == "ProductId");
    }
}
