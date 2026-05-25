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
