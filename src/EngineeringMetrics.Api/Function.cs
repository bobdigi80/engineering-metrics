using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using EngineeringMetrics.Storage;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace EngineeringMetrics.Api;

/// <summary>Lambda entry point behind a public Function URL.</summary>
public sealed class Function
{
    private static readonly MetricsEndpoint Endpoint = new(
        new DynamoMetricsStore(
            new AmazonDynamoDBClient(),
            Environment.GetEnvironmentVariable("TABLE_NAME")
                ?? throw new InvalidOperationException("Environment variable TABLE_NAME is not set.")));

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(1));
        return await Endpoint.HandleAsync(request, cts.Token);
    }
}
