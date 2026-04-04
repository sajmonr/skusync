using System.Diagnostics.CodeAnalysis;

namespace Integration.Shopify.GraphQl;

/// <summary>
/// Defines a service interface for working with Shopify's GraphQL API.
/// </summary>
public interface IShopifyGraphQlService
{
    /// <summary>
    /// Executes a GraphQL query against the Shopify API and retrieves the result of the specified type.
    /// </summary>
    /// <typeparam name="TResult">The type of the result expected from the GraphQL API response.</typeparam>
    /// <param name="query">The GraphQL query string to be executed.</param>
    /// <param name="variables">An optional dictionary of variables to pass into the GraphQL query.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains the response data of type <typeparamref name="TResult"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the Shopify client cannot be created.</exception>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="query"/> is null or empty.</exception>
    Task<TResult> ExecuteAsync<TResult>([StringSyntax("graphql")] string query,
        IDictionary<string, object?>? variables = null);
}