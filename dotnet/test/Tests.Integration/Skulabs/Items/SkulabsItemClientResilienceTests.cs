using System.Net;
using System.Text;
using Integration.RateLimiting;
using Integration.Skulabs.Items;
using Integration.Skulabs.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Tests.Integration.Skulabs.Items;

/// <summary>
/// Exercises the resilience pipeline wired up around <see cref="SkulabsItemClient"/>: confirms
/// retries fire on transient failures and that the client gives up after exhausting attempts.
/// </summary>
public class SkulabsItemClientResilienceTests
{
    private const string BaseUrl = "https://api.skulabs.test/";

    [Fact]
    public async Task GetAllItems_ShouldRetryAndSucceed_When429IsFollowedBySuccess()
    {
        var transport = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.TooManyRequests, "{}"),
            JsonResponse(HttpStatusCode.TooManyRequests, "{}"),
            JsonResponse(HttpStatusCode.OK, "[]")
        );

        var client = BuildClient(transport);

        var result = await client.GetAllItems();

        result.ShouldBeEmpty();
        transport.RequestCount.ShouldBe(3, "two 429s should trigger two retries before the success");
    }

    [Fact]
    public async Task GetAllItems_ShouldRetry_OnServerError()
    {
        var transport = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.InternalServerError, ""),
            JsonResponse(HttpStatusCode.OK, "[]")
        );

        var client = BuildClient(transport);

        var result = await client.GetAllItems();

        result.ShouldBeEmpty();
        transport.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllItems_ShouldThrow_AfterExhaustingAllRetries()
    {
        // Configured policy is 3 retries → 4 total attempts. All fail.
        var transport = new ScriptedHttpMessageHandler(
            Enumerable.Repeat(0, 10)
                .Select(_ => JsonResponse(HttpStatusCode.TooManyRequests, "{}"))
                .ToArray());

        var client = BuildClient(transport);

        await Should.ThrowAsync<HttpRequestException>(() => client.GetAllItems());
        transport.RequestCount.ShouldBe(4, "1 initial attempt + 3 retries");
    }

    [Fact]
    public async Task GetAllItems_ShouldCapRetryAfter_WhenServerSendsHostileRetryAfterHeader()
    {
        // 429 with Retry-After: 3600 (1 hour). Without MaxDelay this test would hang for
        // an hour; with the 50ms cap configured in BuildClient the retry must fire promptly
        // and the call must complete in well under a second.
        var hostile429 = JsonResponse(HttpStatusCode.TooManyRequests, "{}");
        hostile429.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));

        var transport = new ScriptedHttpMessageHandler(
            hostile429,
            JsonResponse(HttpStatusCode.OK, "[]")
        );

        var client = BuildClient(transport);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await client.GetAllItems();
        stopwatch.Stop();

        result.ShouldBeEmpty();
        transport.RequestCount.ShouldBe(2);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5),
            "MaxDelay should clamp the 1-hour Retry-After down to the 50ms test cap");
    }

    [Fact]
    public async Task UpdateItems_ShouldRetryAndSucceed_When429IsFollowedBySuccess()
    {
        var transport = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.TooManyRequests, "{}"),
            JsonResponse(HttpStatusCode.OK, "{}")
        );

        var client = BuildClient(transport);

        await client.UpdateItems([new SkulabsItemUpdateWithId("item-1", "Name")]);

        transport.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllItems_ShouldNotRetry_OnClientErrorOtherThan429()
    {
        // 401 Unauthorized is a permanent failure — retries would be wasteful.
        var transport = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.Unauthorized, "")
        );

        var client = BuildClient(transport);

        await Should.ThrowAsync<HttpRequestException>(() => client.GetAllItems());
        transport.RequestCount.ShouldBe(1, "401 is not a transient failure; no retries expected");
    }

    /// <summary>
    /// Builds an <see cref="ISkulabsItemClient"/> with the production resilience pipeline applied
    /// and a substituted transport handler at the bottom of the pipeline. This mirrors how DI
    /// wires the client in Web.Api, so the test exercises the same retry policy that ships.
    /// </summary>
    private static ISkulabsItemClient BuildClient(HttpMessageHandler primaryHandler)
    {
        var services = new ServiceCollection();
        var options = new SkulabsApiOptions { BaseUrl = BaseUrl, ApiKey = "test-key" };
        services.AddSingleton<IOptionsMonitor<SkulabsApiOptions>>(new StaticOptionsMonitor<SkulabsApiOptions>(options));
        var rateLimitService = Substitute.For<IRateLimitService>();
        rateLimitService.GetRemainingCooldown(Arg.Any<string>()).Returns((TimeSpan?)null);
        services.AddSingleton(rateLimitService);

        services.AddHttpClient<ISkulabsItemClient, SkulabsItemClient>()
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler)
            // Same Configure as production, but with tiny delays so the test doesn't pay the
            // real 2s × exponential backoff and the MaxDelay cap stays visible (asserting
            // the cap is the whole point of the Retry-After test below).
            .AddResilienceHandler("skulabs-retry",
                pipeline => SkulabsResiliencePipeline.Configure(pipeline,
                    baseDelay: TimeSpan.FromMilliseconds(1),
                    maxDelay: TimeSpan.FromMilliseconds(50)));

        return services.BuildServiceProvider().GetRequiredService<ISkulabsItemClient>();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// Returns a queue of pre-canned responses in order. Tracks call count for assertions.
    /// </summary>
    private sealed class ScriptedHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = _index < responses.Length ? responses[_index] : responses[^1];
            _index++;
            return Task.FromResult(response);
        }
    }

    /// <summary>Trivial <see cref="IOptionsMonitor{T}"/> implementation backed by a single value.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
