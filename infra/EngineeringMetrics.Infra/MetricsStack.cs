using Amazon.CDK;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SSM;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;
using HttpMethod = Amazon.CDK.AWS.Lambda.HttpMethod;
using LogGroupProps = Amazon.CDK.AWS.Logs.LogGroupProps;

namespace EngineeringMetrics.Infra;

public sealed class MetricsStackProps : StackProps
{
    public required string[] Repositories { get; init; }
    public required string[] AllowedOrigins { get; init; }
    public string GitHubTokenParameter { get; init; } = "/engineering-metrics/github-token";
}

/// <summary>
/// The application: a scheduled collector Lambda, a DynamoDB table and a read-only API Lambda behind a Function URL.
/// Everything here is pay-per-use and sits inside the AWS free tier at portfolio traffic.
/// </summary>
public sealed class MetricsStack : Stack
{
    public MetricsStack(Construct scope, string id, MetricsStackProps props) : base(scope, id, props)
    {
        var table = new TableV2(this, "Metrics", new TablePropsV2
        {
            PartitionKey = new Attribute { Name = "pk", Type = AttributeType.STRING },
            SortKey = new Attribute { Name = "sk", Type = AttributeType.STRING },
            Billing = Billing.OnDemand(),
            // The data is rebuilt by the next collector run, so the table is safe to delete with the stack.
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        var collector = new Function(this, "Collector", new FunctionProps
        {
            Description = "Collects pull request metrics from GitHub on a schedule",
            Runtime = Runtime.DOTNET_10,
            Architecture = Architecture.ARM_64,
            Handler = "EngineeringMetrics.Collector::EngineeringMetrics.Collector.Function::HandleAsync",
            Code = Code.FromAsset("artifacts/collector"),
            MemorySize = 512,
            Timeout = Duration.Minutes(2),
            LogGroup = LogGroup(this, "CollectorLogs"),
            Environment = new Dictionary<string, string>
            {
                ["TABLE_NAME"] = table.TableName,
                ["REPOSITORIES"] = string.Join(',', props.Repositories),
                ["GITHUB_TOKEN_PARAMETER"] = props.GitHubTokenParameter,
                ["WINDOW_DAYS"] = "30",
            },
        });
        table.GrantWriteData(collector);

        // Created by hand (see README) so the token never passes through CloudFormation.
        StringParameter
            .FromSecureStringParameterAttributes(this, "GitHubToken", new SecureStringParameterAttributes
            {
                ParameterName = props.GitHubTokenParameter,
            })
            .GrantRead(collector);

        new Rule(this, "DailySchedule", new RuleProps
        {
            Description = "Runs the collector once a day",
            Schedule = Schedule.Cron(new CronOptions { Minute = "0", Hour = "5" }),
            Targets = [new LambdaFunction(collector, new LambdaFunctionProps { RetryAttempts = 1 })],
        });

        var api = new Function(this, "Api", new FunctionProps
        {
            Description = "Serves the latest pull request metrics as JSON",
            Runtime = Runtime.DOTNET_10,
            Architecture = Architecture.ARM_64,
            Handler = "EngineeringMetrics.Api::EngineeringMetrics.Api.Function::HandleAsync",
            Code = Code.FromAsset("artifacts/api"),
            MemorySize = 512,
            Timeout = Duration.Seconds(10),
            LogGroup = LogGroup(this, "ApiLogs"),
            Environment = new Dictionary<string, string> { ["TABLE_NAME"] = table.TableName },
        });
        table.GrantReadData(api);

        // A Function URL instead of API Gateway: free, and this API has one read-only route. See docs/adr/0004.
        var url = api.AddFunctionUrl(new FunctionUrlOptions
        {
            AuthType = FunctionUrlAuthType.NONE,
            Cors = new FunctionUrlCorsOptions
            {
                AllowedOrigins = props.AllowedOrigins,
                AllowedMethods = [HttpMethod.GET],
                MaxAge = Duration.Hours(1),
            },
        });

        new CfnOutput(this, "ApiUrl", new CfnOutputProps { Value = url.Url });
        new CfnOutput(this, "TableName", new CfnOutputProps { Value = table.TableName });
    }

    private static LogGroup LogGroup(Construct scope, string id) => new(scope, id, new LogGroupProps
    {
        Retention = RetentionDays.TWO_WEEKS,
        RemovalPolicy = RemovalPolicy.DESTROY,
    });
}
