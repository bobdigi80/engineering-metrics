using Amazon.CDK;
using Amazon.CDK.AWS.Budgets;
using Amazon.CDK.AWS.IAM;
using Constructs;

namespace EngineeringMetrics.Infra;

public sealed class GuardrailsStackProps : StackProps
{
    /// <summary>Email for budget alerts. Passed on the command line so it is never committed.</summary>
    public required string BudgetEmail { get; init; }

    public double MonthlyBudgetUsd { get; init; } = 5;

    /// <summary>The repository allowed to deploy, in <c>owner/name</c> form.</summary>
    public required string GitHubRepository { get; init; }

    /// <summary>
    /// The prefix of the OIDC subject claim GitHub issues for the repository. Newer repositories use
    /// immutable IDs (<c>repo:owner@ownerId/name@repoId</c>) rather than <c>repo:owner/name</c>. Read it with
    /// <c>gh api repos/OWNER/NAME/actions/oidc/customization/sub</c> (<c>sub_claim_prefix</c>).
    /// </summary>
    public required string GitHubOidcSubjectPrefix { get; init; }

    /// <summary>False if the account already has the GitHub OIDC provider (there can only be one).</summary>
    public bool CreateOidcProvider { get; init; } = true;
}

/// <summary>
/// Account-level guardrails, deployed once by hand rather than from CI:
/// a monthly cost budget with email alerts, and a role GitHub Actions can assume through OIDC,
/// so no long-lived AWS keys are stored in GitHub. See docs/adr/0005.
/// </summary>
public sealed class GuardrailsStack : Stack
{
    public GuardrailsStack(Construct scope, string id, GuardrailsStackProps props) : base(scope, id, props)
    {
        new CfnBudget(this, "MonthlyBudget", new CfnBudgetProps
        {
            Budget = new CfnBudget.BudgetDataProperty
            {
                BudgetName = "engineering-metrics-monthly",
                BudgetType = "COST",
                TimeUnit = "MONTHLY",
                BudgetLimit = new CfnBudget.SpendProperty { Amount = props.MonthlyBudgetUsd, Unit = "USD" },
            },
            NotificationsWithSubscribers = new object[]
            {
                Alert("ACTUAL", 50, props.BudgetEmail),
                Alert("ACTUAL", 100, props.BudgetEmail),
                Alert("FORECASTED", 100, props.BudgetEmail),
            },
        });

        const string issuer = "token.actions.githubusercontent.com";
        var providerArn = props.CreateOidcProvider
            ? new OidcProviderNative(this, "GitHubOidc", new OidcProviderNativeProps
            {
                Url = $"https://{issuer}",
                ClientIds = ["sts.amazonaws.com"],
            }).OidcProviderArn
            : $"arn:aws:iam::{Account}:oidc-provider/{issuer}";

        var deployRole = new Role(this, "GitHubDeployRole", new RoleProps
        {
            Description = $"Assumed by GitHub Actions in {props.GitHubRepository} to deploy with CDK",
            MaxSessionDuration = Duration.Hours(1),
            AssumedBy = new WebIdentityPrincipal(providerArn, new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, string>
                {
                    [$"{issuer}:aud"] = "sts.amazonaws.com",
                    // Only workflows running on the main branch of this repository.
                    [$"{issuer}:sub"] = $"{props.GitHubOidcSubjectPrefix}:ref:refs/heads/main",
                },
            }),
        });

        // CDK deploys through the roles created by `cdk bootstrap`, so this role only needs to assume them.
        deployRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Actions = ["sts:AssumeRole"],
            Resources = [$"arn:aws:iam::{Account}:role/cdk-*"],
        }));

        new CfnOutput(this, "DeployRoleArn", new CfnOutputProps { Value = deployRole.RoleArn });
    }

    private static CfnBudget.NotificationWithSubscribersProperty Alert(string type, double percent, string email) => new()
    {
        Notification = new CfnBudget.NotificationProperty
        {
            NotificationType = type,
            ComparisonOperator = "GREATER_THAN",
            Threshold = percent,
            ThresholdType = "PERCENTAGE",
        },
        Subscribers = new object[] { new CfnBudget.SubscriberProperty { SubscriptionType = "EMAIL", Address = email } },
    };
}
