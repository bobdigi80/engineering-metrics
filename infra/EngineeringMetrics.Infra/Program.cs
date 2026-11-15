using Amazon.CDK;
using EngineeringMetrics.Infra;

var app = new App();
Tags.Of(app).Add("project", "engineering-metrics");

string Context(string key) =>
    app.Node.TryGetContext(key) as string
    ?? throw new InvalidOperationException($"Missing CDK context value '{key}' (set it in cdk.json or pass -c {key}=...).");

static string[] List(string value) =>
    value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

new MetricsStack(app, "EngineeringMetrics", new MetricsStackProps
{
    Description = "Engineering metrics: scheduled GitHub collector, DynamoDB and a public JSON API",
    Repositories = List(Context("repositories")),
    AllowedOrigins = List(Context("allowedOrigins")),
});

// Only synthesised when a budget email is supplied, so CI (which never passes one) cannot touch it.
if (app.Node.TryGetContext("budgetEmail") is string budgetEmail)
{
    new GuardrailsStack(app, "EngineeringMetricsGuardrails", new GuardrailsStackProps
    {
        Description = "Engineering metrics: cost budget and GitHub Actions deploy role",
        BudgetEmail = budgetEmail,
        GitHubRepository = Context("githubRepository"),
        CreateOidcProvider = app.Node.TryGetContext("createOidcProvider") as string != "false",
    });
}

app.Synth();
