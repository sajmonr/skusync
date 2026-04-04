using Integration.Shopify.GraphQl;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShopifySharp;
using ShopifySharp.Factories;
using ShopifySharp.Services.Graph;
using Shouldly;

namespace Tests.Integration.Shopify.GraphQl;

public class ShopifyGraphQlServiceTests
{
    private readonly IOptionsMonitor<ShopifyOptions> options = Substitute.For<IOptionsMonitor<ShopifyOptions>>();
    private readonly IGraphServiceFactory factory = Substitute.For<IGraphServiceFactory>();
    private readonly IGraphService client = Substitute.For<IGraphService>();

    [Fact]
    public async Task ExecuteAsync_ShouldCreateClientUsingConfiguredOptions_AndReturnResponseData()
    {
        var query = """
                    query {
                      shop {
                        id
                      }
                    }
                    """;
        var configuredOptions = new ShopifyOptions
        {
            ShopUrl = "example.myshopify.com",
            ApiKey = "shpat_test"
        };
        var expected = new TestGraphQlResponse
        {
            ShopId = "gid://shopify/Shop/1"
        };

        options.CurrentValue.Returns(configuredOptions);
        factory.Create(configuredOptions.ShopUrl, configuredOptions.ApiKey).Returns(client);
        client.PostAsync<TestGraphQlResponse>(Arg.Any<GraphRequest>())
            .Returns(new GraphResult<TestGraphQlResponse>
            {
                Data = expected
            });

        var sut = new ShopifyGraphQlService(options, factory);

        var result = await sut.ExecuteAsync<TestGraphQlResponse>(query);

        result.ShouldBeSameAs(expected);
        factory.Received(1).Create(configuredOptions.ShopUrl, configuredOptions.ApiKey);
        await client.Received(1).PostAsync<TestGraphQlResponse>(Arg.Is<GraphRequest>(request =>
            request.Query == query &&
            request.Variables == null));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFilterOutVariablesWithNullValues()
    {
        var variables = new Dictionary<string, object?>
        {
            ["first"] = 10,
            ["after"] = null,
            ["query"] = "sku:ABC-123"
        };

        options.CurrentValue.Returns(new ShopifyOptions
        {
            ShopUrl = "example.myshopify.com",
            ApiKey = "shpat_test"
        });
        factory.Create(Arg.Any<string>(), Arg.Any<string>()).Returns(client);
        client.PostAsync<TestGraphQlResponse>(Arg.Any<GraphRequest>())
            .Returns(new GraphResult<TestGraphQlResponse>
            {
                Data = new TestGraphQlResponse()
            });

        var sut = new ShopifyGraphQlService(options, factory);

        await sut.ExecuteAsync<TestGraphQlResponse>("query", variables);

        await client.Received(1).PostAsync<TestGraphQlResponse>(Arg.Is<GraphRequest>(request =>
            request.Query == "query" &&
            request.Variables != null &&
            request.Variables.Count == 2 &&
            (int)request.Variables["first"] == 10 &&
            (string)request.Variables["query"] == "sku:ABC-123" &&
            !request.Variables.ContainsKey("after")));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldThrow_WhenFactoryReturnsNullClient()
    {
        options.CurrentValue.Returns(new ShopifyOptions
        {
            ShopUrl = "example.myshopify.com",
            ApiKey = "shpat_test"
        });
        factory.Create(Arg.Any<string>(), Arg.Any<string>()).Returns((IGraphService?)null);

        var sut = new ShopifyGraphQlService(options, factory);

        var action = () => sut.ExecuteAsync<TestGraphQlResponse>("query");

        var exception = await Should.ThrowAsync<InvalidOperationException>(action);
        exception.Message.ShouldBe("Shopify client could not be created.");
    }

    private sealed class TestGraphQlResponse
    {
        public string? ShopId { get; init; }
    }
}
