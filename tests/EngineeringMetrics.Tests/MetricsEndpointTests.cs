using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using EngineeringMetrics.Api;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Tests;

public class MetricsEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 5, 0, 0, TimeSpan.Zero);

    private readonly MetricsEndpoint _endpoint = new(new FakeReader(
        new MetricsSnapshot("zeta/repo", Now, 30, 4, 2.5, 6, 20, 48, 120, 0.75),
        new MetricsSnapshot("Alpha/repo", Now, 30, 0, null, null, null, null, null, null)));

    private static APIGatewayHttpApiV2ProxyRequest Request(string method, string path) => new()
    {
        RawPath = path,
        RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
        {
            Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Method = method },
        },
    };

    [Theory]
    [InlineData("/")]
    [InlineData("/metrics")]
    public async Task Get_returns_snapshots_sorted_by_repository_with_cache_header(string path)
    {
        var response = await _endpoint.HandleAsync(Request("GET", path), CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("public, max-age=3600", response.Headers["Cache-Control"]);
        Assert.Equal("Origin", response.Headers["Vary"]);

        using var json = JsonDocument.Parse(response.Body);
        var repos = json.RootElement.GetProperty("repositories");
        Assert.Equal("Alpha/repo", repos[0].GetProperty("repository").GetString());
        Assert.Equal("zeta/repo", repos[1].GetProperty("repository").GetString());
        Assert.Equal(2.5, repos[1].GetProperty("medianHoursToFirstReview").GetDouble());
    }

    [Fact]
    public async Task Unknown_path_returns_404() =>
        Assert.Equal(404, (await _endpoint.HandleAsync(Request("GET", "/admin"), CancellationToken.None)).StatusCode);

    [Fact]
    public async Task Non_get_returns_405()
    {
        var response = await _endpoint.HandleAsync(Request("POST", "/"), CancellationToken.None);

        Assert.Equal(405, response.StatusCode);
        Assert.Equal("no-store", response.Headers["Cache-Control"]);
    }

    private sealed class FakeReader(params MetricsSnapshot[] snapshots) : IMetricsReader
    {
        public Task<IReadOnlyList<MetricsSnapshot>> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetricsSnapshot>>(snapshots);
    }
}
