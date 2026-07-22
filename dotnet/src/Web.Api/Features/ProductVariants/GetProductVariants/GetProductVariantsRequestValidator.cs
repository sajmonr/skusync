using Web.Api.Common.Paging;

namespace Web.Api.Features.ProductVariants.GetProductVariants;

public class GetProductVariantsRequestValidator : GridQueryValidator<GetProductVariantsRequest>
{
    public GetProductVariantsRequestValidator()
    {
        AddGridifyValidation(ProductVariantGridMapper.Instance);
    }
}
