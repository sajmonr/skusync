using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Integration.RateLimiting;
using Integration.Skulabs.Items;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Tests.Integration.Skulabs.Items;

public class SkulabsRateLimitHandlerTests
{
    private readonly IRateLimitService _rateLimitService = Substitute.For<IRateLimitService>();

    [Fact]
    public async Task SendAsync_PassesThrough_AndDoesNotRecord_WhenResponseIsSuccessful()
    {
        var inner = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = BuildClient(inner);

        var response = await sut.GetAsync("https://api.skulabs.test/anything");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.RequestCount.ShouldBe(1);
        _rateLimitService.DidNotReceiveWithAnyArgs().RecordRateLimit(default!, default);
    }

    [Fact]
    public async Task SendAsync_Records429_WithRetryAfterDelta()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
        var sut = BuildClient(new RecordingHandler(response));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.Received(1).RecordRateLimit(
            SkulabsRateLimitHandler.RateLimitKey,
            TimeSpan.FromSeconds(90));
    }

    [Fact]
    public async Task SendAsync_Records429_WithRetryAfterHttpDate()
    {
        var futureDate = DateTimeOffset.UtcNow.AddMinutes(2);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(futureDate);
        var sut = BuildClient(new RecordingHandler(response));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.Received(1).RecordRateLimit(
            SkulabsRateLimitHandler.RateLimitKey,
            Arg.Is<TimeSpan?>(t => t.HasValue && t.Value.TotalSeconds > 90 && t.Value.TotalSeconds <= 121));
    }

    [Fact]
    public async Task SendAsync_Records429_WithNullRetryAfter_WhenHeaderMissing()
    {
        var sut = BuildClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.Received(1).RecordRateLimit(
            SkulabsRateLimitHandler.RateLimitKey,
            (TimeSpan?)null);
    }

    /// <summary>
    /// The production path: SkuLabs sends no Retry-After header, only a body wait. Without this the
    /// handler falls back to a five-minute default against a wait of tens of minutes.
    /// </summary>
    [Fact]
    public async Task SendAsync_Records429_WithWaitSecondsFromBody_WhenHeaderMissing()
    {
        var sut = BuildClient(new RecordingHandler(RateLimitedResponse(waitSeconds: 1508)));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.Received(1).RecordRateLimit(
            SkulabsRateLimitHandler.RateLimitKey,
            TimeSpan.FromSeconds(1508));
    }

    [Fact]
    public async Task SendAsync_PrefersRetryAfterHeader_OverBodyWaitSeconds()
    {
        var response = RateLimitedResponse(waitSeconds: 1508);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        var sut = BuildClient(new RecordingHandler(response));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.Received(1).RecordRateLimit(
            SkulabsRateLimitHandler.RateLimitKey,
            TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// A quota window is an hour, so a longer wait is malformed and must not mute the client for
    /// the rest of the day.
    /// </summary>
    [Fact]
    public async Task SendAsync_CapsBodyWaitSeconds_AtOneHour()
    {
        var sut = BuildClient(new RecordingHandler(RateLimitedResponse(waitSeconds: 43_200)));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.Received(1).RecordRateLimit(
            SkulabsRateLimitHandler.RateLimitKey,
            TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"error\":{\"message\":\"no data object\"}}")]
    [InlineData("{\"error\":{\"data\":{\"wait_seconds\":0}}}")]
    public async Task SendAsync_Records429_WithNullRetryAfter_WhenBodyCarriesNoUsableWait(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(body)
        };
        var sut = BuildClient(new RecordingHandler(response));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.Received(1).RecordRateLimit(
            SkulabsRateLimitHandler.RateLimitKey,
            (TimeSpan?)null);
    }

    /// <summary>
    /// The handler consumes the body to find the wait; the client still logs the same response
    /// afterwards, so it has to remain readable.
    /// </summary>
    [Fact]
    public async Task SendAsync_LeavesResponseBodyReadable_AfterParsingWaitSeconds()
    {
        var sut = BuildClient(new RecordingHandler(RateLimitedResponse(waitSeconds: 1508)));

        var response = await sut.GetAsync("https://api.skulabs.test/anything");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("wait_seconds");
    }

    /// <summary>Mirrors a real SkuLabs 429 body — no Retry-After header, wait carried in the data object.</summary>
    private static HttpResponseMessage RateLimitedResponse(double waitSeconds) =>
        new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                """
                {"error":{"message":"Rate limited.","code":"","statusCode":429,
                "data":{"key":"rate-limit-2:acct:basic_2025:api-2500-per-day","interval_seconds":3600,
                "limit":104,"remaining":"0","wait_seconds":WAIT_SECONDS},"user_error":true}}
                """.Replace("WAIT_SECONDS", waitSeconds.ToString(CultureInfo.InvariantCulture)))
        };

    [Fact]
    public async Task SendAsync_DoesNotRecord_OnNon429ErrorResponses()
    {
        var sut = BuildClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await sut.GetAsync("https://api.skulabs.test/anything");

        _rateLimitService.DidNotReceiveWithAnyArgs().RecordRateLimit(default!, default);
    }

    private HttpClient BuildClient(HttpMessageHandler inner)
    {
        var handler = new SkulabsRateLimitHandler(_rateLimitService, NullLogger<SkulabsRateLimitHandler>.Instance)
        {
            InnerHandler = inner
        };
        return new HttpClient(handler);
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response);
        }
    }
}
