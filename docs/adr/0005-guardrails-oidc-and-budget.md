# 5. Cost guardrails and keyless deploys

Date: 2026-11-15
Status: Accepted

## Context

This runs in a personal AWS account. Two things must not happen: a surprise bill, and AWS keys leaking from CI.

## Decision

A separate `EngineeringMetricsGuardrails` stack, deployed once by hand, contains:

- **An AWS Budget** of $5/month that emails at 50% and 100% of actual spend, and at 100% of forecast.
- **A GitHub OIDC identity provider and deploy role.** GitHub Actions exchanges a short-lived OIDC token for one-hour AWS credentials. The role's trust policy only accepts tokens for this repository's `main` branch, and its only permission is to assume the CDK bootstrap roles.

It is separate from the application stack so CI cannot modify its own permissions or the budget. The stack is only synthesised when a `budgetEmail` context value is passed, which CI never does, and the address never appears in the repository.

## Consequences

- No AWS secrets stored in GitHub.
- Pull requests and forks cannot deploy.
- Changing the guardrails needs a manual `cdk deploy`, which is the point.
