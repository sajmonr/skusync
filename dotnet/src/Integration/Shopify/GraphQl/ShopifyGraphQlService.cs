using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using ShopifySharp;
using ShopifySharp.Factories;

namespace Integration.Shopify.GraphQl;

internal class ShopifyGraphQlService(IOptionsMonitor<ShopifyOptions> options, IGraphServiceFactory factory)
    : IShopifyGraphQlService
{
    public async Task<TResult> ExecuteAsync<TResult>([StringSyntax("graphql")] string query,
        IDictionary<string, object?>? variables = null)
    {
        var client = factory.Create(options.CurrentValue.ShopUrl, options.CurrentValue.ApiKey);

        if (client is null)
        {
            throw new InvalidOperationException("Shopify client could not be created.");
        }

        var request = new GraphRequest
        {
            Query = query,
            Variables = variables
                ?.Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value!)
        };

        var result = await client.PostAsync<TResult>(request);

        return result.Data;
    }
}