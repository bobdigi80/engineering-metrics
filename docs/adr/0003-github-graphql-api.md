# 3. Use the GitHub GraphQL API

Date: 2026-11-15
Status: Accepted

## Context

Each metric needs, per merged pull request: created and merged times, lines added and deleted, and the reviews with their authors and timestamps.

With the REST API, the pull request list does not include sizes, and reviews are a separate endpoint. Fifty pull requests cost 1 + 50 + 50 = 101 requests per repository.

## Decision

Use the GraphQL API. One query per repository returns the 50 most recently updated merged pull requests with their sizes and first 20 reviews.

## Consequences

- One request per repository instead of ~100; far inside rate limits.
- GraphQL requires authentication, so the collector needs a token. A fine-grained personal access token with read-only access to public repositories is enough. It is stored as an SSM SecureString (free) rather than in Secrets Manager (~$0.40/month), created by hand so it never passes through CloudFormation.
- Ordering by `UPDATED_AT` approximates "recently merged". Pull requests updated after merging (e.g. late comments) can push others out of the 50, which slightly under-samples very busy repositories. Acceptable for trend data.
