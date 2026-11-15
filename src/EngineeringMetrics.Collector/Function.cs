using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using EngineeringMetrics.Collector.GitHub;
using EngineeringMetrics.Storage;

namespace EngineeringMetrics.Collector;

/// <summary>
/// Lambda entry point, triggered on a schedule by EventBridge.
/// Configuration comes from environment variables set by the CDK stack.
/// </summary>
public sealed class Function
{
    // Created on the first invocation and reused while the execution environment stays warm.
    private static CollectorService? _service;

    public async Task HandleAsync(Stream input, ILambdaContext context)
    {
        using var cts = new CancellationTokenSource(context.RemainingTime - TimeSpan.FromSeconds(5));
        var service = _service ??= await CreateServiceAsync(context.Logger, cts.Token);

        var result = await service.RunAsync(cts.Token);

        if (result.FailedRepositories.Count > 0)
        {
            // Fail the invocation so the Lambda error metric (and any alarm on it) picks it up.
            throw new InvalidOperationException($"Failed repositories: {string.Join(", ", result.FailedRepositories)}");
        }
    }

    private static async Task<CollectorService> CreateServiceAsync(ILambdaLogger logger, CancellationToken cancellationToken)
    {
        var repositories = Required("REPOSITORIES")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var windowDays = int.TryParse(Environment.GetEnvironmentVariable("WINDOW_DAYS"), out var days) ? days : 30;

        using var ssm = new AmazonSimpleSystemsManagementClient();
        var parameter = await ssm.GetParameterAsync(new GetParameterRequest
        {
            Name = Required("GITHUB_TOKEN_PARAMETER"),
            WithDecryption = true,
        }, cancellationToken);

        return new CollectorService(
            GitHubGraphQlClient.Create(parameter.Parameter.Value),
            new DynamoMetricsStore(new AmazonDynamoDBClient(), Required("TABLE_NAME")),
            TimeProvider.System,
            new CollectorOptions(repositories, TimeSpan.FromDays(windowDays)),
            message => logger.LogInformation(message));
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Environment variable {name} is not set.");
}
