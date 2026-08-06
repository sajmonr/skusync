using FastEndpoints;
using FluentValidation;

namespace Web.Api.Shopify.Features.GetProductInformation;

public class GetProductInformationRequestValidator : Validator<GetProductInformationRequest>
{
    public GetProductInformationRequestValidator()
    {
        RuleFor(request => request.ProductId)
            .Must(productId => ShopifyGlobalId.TryParseProductId(productId, out _))
            .WithMessage(
                "ProductId must be a positive Shopify product ID, either numeric or as "
                + "gid://shopify/Product/{id}.");
    }
}
