using FastEndpoints;
using FluentValidation;

namespace Web.Api.Shopify.Features.GetVariantInformation;

public class GetVariantInformationRequestValidator : Validator<GetVariantInformationRequest>
{
    public GetVariantInformationRequestValidator()
    {
        RuleFor(request => request.VariantId)
            .Must(variantId => ShopifyGlobalId.TryParseVariantId(variantId, out _))
            .WithMessage(
                "VariantId must be a positive Shopify product variant ID, either numeric or as "
                + "gid://shopify/ProductVariant/{id}.");
    }
}
