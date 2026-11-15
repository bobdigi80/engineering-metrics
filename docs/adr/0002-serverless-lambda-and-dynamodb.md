# 2. Serverless: Lambda and DynamoDB

Date: 2026-11-15
Status: Accepted

## Context

The workload is tiny and bursty: one collection run a day, and an API read a handful of times a day by portfolio visitors. It should cost close to nothing when idle and need no patching.

Options considered:

| Option | Idle cost | Ops burden |
|---|---|---|
| ECS Fargate or App Runner + RDS | ~$15-30/month minimum | Patching, connection management |
| EC2 + SQLite | ~$4+/month | OS patching, backups |
| **Lambda + DynamoDB on-demand** | **$0** | **None** |

## Decision

Two .NET 10 Lambda functions on ARM64 (Graviton, ~20% cheaper per GB-second), with a DynamoDB table in on-demand mode.

The table uses a single-table design with two item shapes:

- `pk = REPO#{owner/name}`, `sk = {timestamp}`: history, one item per run.
- `pk = LATEST`, `sk = {owner/name}`: overwritten each run.

The API's only query, "latest snapshot for every repository", is one `Query` on `pk = LATEST`. Both items are written in one transaction so they cannot disagree.

## Consequences

- Stays inside the AWS free tier at expected volumes.
- Cold starts of a few hundred milliseconds on the API. Acceptable because responses are cached for an hour; Native AOT is the lever if it ever matters.
- Snapshots are stored as a JSON blob, not as separate attributes. Fine while nothing queries on metric values; revisit if it does.
