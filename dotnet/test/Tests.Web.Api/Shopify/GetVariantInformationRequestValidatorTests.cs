using Shouldly;
using Web.Api.Shopify.Features.GetVariantInformation;

namespace Tests.Web.Api.Shopify;

public class GetVariantInformationRequestValidatorTests
{
    private readonly GetVariantInformationRequestValidator _validator = new();

    [Theory]
    [InlineData("gid://shopify/ProductVariant/987654321")]
    [InlineData("987654321")]
    public void Validate_AcceptsGlobalIdsAndBareNumbers(string variantId)
    {
        var result = _validator.Validate(new GetVariantInformationRequest { VariantId = variantId });

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-variant")]
    [InlineData("gid://shopify/Product/987654321")]
    [InlineData("0")]
    public void Validate_RejectsAnythingThatIsNotAPositiveVariantId(string? variantId)
    {
        var result = _validator.Validate(new GetVariantInformationRequest { VariantId = variantId });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == "VariantId");
    }
}
