using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Api;

/// <summary>
/// Handles requests from the Lambda Function URL (payload format 2.0).
/// CORS preflight is answered by the Function URL itself, configured in the CDK stack.
/// </summary>
public sealed class MetricsEndpoint(IMetricsReader reader, TimeProvider? time = null)
{
    /// <summary>How far back <c>/history</c> goes. Bounds the size and cost of a single request.</summary>
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(90);

    // Longer than any real "owner/name"; stops arbitrary strings being sent to the database.
    private const int MaxRepositoryLength = 140;

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(
        APIGatewayHttpApiV2ProxyRequest request, CancellationToken cancellationToken)
    {
        var method = request.RequestContext?.Http?.Method ?? "GET";
        var path = string.IsNullOrEmpty(request.RawPath) ? "/" : request.RawPath;

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return Json(405, new { error = "Method not allowed" });
        }

        if (path == "/history")
        {
            return await HistoryAsync(request, cancellationToken);
        }

        if (path is not ("/" or "/metrics"))
        {
            return Json(404, new { error = "Not found" });
        }

        var snapshots = await reader.GetLatestAsync(cancellationToken);
        var body = new { repositories = snapshots.OrderBy(s => s.Repository, StringComparer.OrdinalIgnoreCase) };

        // Data changes once a day, so let browsers and any CDN cache it for an hour.
        return Json(200, body, cacheSeconds: 3600);
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HistoryAsync(
        APIGatewayHttpApiV2ProxyRequest request, CancellationToken cancellationToken)
    {
        string? repository = null;
        request.QueryStringParameters?.TryGetValue("repository", out repository);

        if (string.IsNullOrWhiteSpace(repository) || repository.Length > MaxRepositoryLength)
        {
            return Json(400, new { error = "Pass ?repository=owner/name" });
        }

        var since = (time ?? TimeProvider.System).GetUtcNow() - HistoryWindow;
        var snapshots = await reader.GetHistoryAsync(repository, since, cancellationToken);

        return Json(200, new { repository, snapshots = snapshots.OrderBy(s => s.GeneratedAt) }, cacheSeconds: 3600);
    }

    private static APIGatewayHttpApiV2ProxyResponse Json(int status, object body, int? cacheSeconds = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            // The Function URL only adds the CORS headers (and its own Vary) when a request has an Origin.
            // Without this, a browser that cached a direct visit would reuse that copy, which has no
            // Access-Control-Allow-Origin, for a later cross-origin fetch, and the fetch would fail.
            ["Vary"] = "Origin",
        };
        headers["Cache-Control"] = cacheSeconds is { } seconds ? $"public, max-age={seconds}" : "no-store";

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = status,
            Headers = headers,
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        };
    }
}
