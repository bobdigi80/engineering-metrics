using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using EngineeringMetrics.Core;

namespace EngineeringMetrics.Api;

/// <summary>
/// Handles requests from the Lambda Function URL (payload format 2.0).
/// CORS preflight is answered by the Function URL itself, configured in the CDK stack.
/// </summary>
public sealed class MetricsEndpoint(IMetricsReader reader)
{
    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(
        APIGatewayHttpApiV2ProxyRequest request, CancellationToken cancellationToken)
    {
        var method = request.RequestContext?.Http?.Method ?? "GET";
        var path = string.IsNullOrEmpty(request.RawPath) ? "/" : request.RawPath;

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return Json(405, new { error = "Method not allowed" });
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

    private static APIGatewayHttpApiV2ProxyResponse Json(int status, object body, int? cacheSeconds = null)
    {
        var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
        headers["Cache-Control"] = cacheSeconds is { } seconds ? $"public, max-age={seconds}" : "no-store";

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = status,
            Headers = headers,
            Body = JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
        };
    }
}
