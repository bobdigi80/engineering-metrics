using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using EngineeringMetrics.Api;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Tests;

public class MetricsEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 5, 0, 0, TimeSpan.Zero);

    private readonly FakeReader _reader = new(
        new MetricsSnapshot("zeta/repo", Now, 30, 4, 2.5, 6, 20, 48, 120, 0.75),
        new MetricsSnapshot("Alpha/repo", Now, 30, 0, null, null, null, null, null, null));

    private readonly MetricsEndpoint _endpoint;

    public MetricsEndpointTests() => _endpoint = new MetricsEndpoint(_reader, new FixedTime(Now));

    private static APIGatewayHttpApiV2ProxyRequest Request(string method, string path, string? repository = null) => new()
    {
        RawPath = path,
        QueryStringParameters = repository is null ? null : new Dictionary<string, string> { ["repository"] = repository },
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
    public async Task History_returns_one_repository_oldest_first_within_the_window()
    {
        _reader.History =
        [
            new MetricsSnapshot("zeta/repo", Now, 30, 4, 2.5, 6, 20, 48, 120, 0.75),
            new MetricsSnapshot("zeta/repo", Now.AddDays(-1), 30, 3, 4.5, 6, 20, 48, 120, 0.75),
        ];

        var response = await _endpoint.HandleAsync(Request("GET", "/history", "zeta/repo"), CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("public, max-age=3600", response.Headers["Cache-Control"]);
        Assert.Equal(("zeta/repo", Now - MetricsEndpoint.HistoryWindow), _reader.HistoryRequest);

        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("zeta/repo", json.RootElement.GetProperty("repository").GetString());
        var snapshots = json.RootElement.GetProperty("snapshots");
        Assert.Equal(2, snapshots.GetArrayLength());
        Assert.Equal(4.5, snapshots[0].GetProperty("medianHoursToFirstReview").GetDouble());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task History_without_a_repository_returns_400(string? repository)
    {
        var response = await _endpoint.HandleAsync(Request("GET", "/history", repository), CancellationToken.None);

        Assert.Equal(400, response.StatusCode);
        Assert.Null(_reader.HistoryRequest);
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
        public MetricsSnapshot[] History { get; set; } = [];

        public (string Repository, DateTimeOffset Since)? HistoryRequest { get; private set; }

        public Task<IReadOnlyList<MetricsSnapshot>> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetricsSnapshot>>(snapshots);

        public Task<IReadOnlyList<MetricsSnapshot>> GetHistoryAsync(
            string repository, DateTimeOffset since, CancellationToken cancellationToken)
        {
            HistoryRequest = (repository, since);
            return Task.FromResult<IReadOnlyList<MetricsSnapshot>>(History);
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
