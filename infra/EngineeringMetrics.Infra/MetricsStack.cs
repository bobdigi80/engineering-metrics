using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.Events;
using Amazon.CDK.AWS.Events.Targets;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SSM;
using Constructs;
using Attribute = Amazon.CDK.AWS.DynamoDB.Attribute;
using CronOptions = Amazon.CDK.AWS.Events.CronOptions;
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
            Timeout = Duration.Minutes(5),
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

        // The collector fails its invocation when any repository fails, so one alarm on errors covers
        // a bad token, a renamed repository and GitHub being down. Subscribe to the topic by hand
        // (see README) so no email address is committed or passed through CI.
        var alerts = new Topic(this, "Alerts", new TopicProps { DisplayName = "Engineering metrics alerts" });
        var collectorFailed = new Alarm(this, "CollectorFailed", new AlarmProps
        {
            AlarmDescription = "The engineering-metrics collector failed, so the published metrics are going stale.",
            Metric = collector.MetricErrors(new MetricOptions { Period = Duration.Hours(1), Statistic = "Sum" }),
            Threshold = 1,
            EvaluationPeriods = 1,
            ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
            TreatMissingData = TreatMissingData.NOT_BREACHING,
        });
        collectorFailed.AddAlarmAction(new SnsAction(alerts));

        var api = new Function(this, "Api", new FunctionProps
        {
            Description = "Serves the latest pull request metrics as JSON",
            Runtime = Runtime.DOTNET_10,
            Architecture = Architecture.ARM_64,
            Handler = "EngineeringMetrics.Api::EngineeringMetrics.Api.Function::HandleAsync",
            Code = Code.FromAsset("artifacts/api"),
            MemorySize = 512,
            Timeout = Duration.Seconds(10),
            // The URL is public. This bounds what abuse can cost; a budget alert only reports it afterwards.
            ReservedConcurrentExecutions = 5,
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
        new CfnOutput(this, "AlertsTopicArn", new CfnOutputProps { Value = alerts.TopicArn });
    }

    private static LogGroup LogGroup(Construct scope, string id) => new(scope, id, new LogGroupProps
    {
        Retention = RetentionDays.TWO_WEEKS,
        RemovalPolicy = RemovalPolicy.DESTROY,
    });
}
