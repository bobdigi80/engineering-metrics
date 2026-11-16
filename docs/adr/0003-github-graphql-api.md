# 3. Use the GitHub GraphQL API

Date: 2026-11-15
Status: Accepted

## Context

Each metric needs, per merged pull request: created and merged times, lines added and deleted, and the reviews with their authors and timestamps.

With the REST API, the pull request list does not include sizes, and reviews are a separate endpoint. Fifty pull requests cost 1 + 50 + 50 = 101 requests per repository.

## Decision

Use the GraphQL API. One query returns a page of 50 merged pull requests with their sizes and first 20 reviews. The collector pages through them, most recently updated first, until a page reaches a pull request last updated before the start of the window.

## Consequences

- One request per 50 pull requests instead of ~100; far inside rate limits, even for a repository merging hundreds a month.
- GraphQL requires authentication, so the collector needs a token. A fine-grained personal access token with read-only access to public repositories is enough. It is stored as an SSM SecureString (free) rather than in Secrets Manager (~$0.40/month), created by hand so it never passes through CloudFormation.
- GraphQL cannot order pull requests by merge time, so they are ordered by `UPDATED_AT`. A pull request is always updated at or after it is merged, so stopping at the first page that reaches the start of the window misses nothing; the cost is fetching some older pull requests that were updated recently, which the calculator then filters out.
- The first version fetched a single page of 50, which under-counted busy repositories badly (24 reported against roughly 500 merged). Found on the first real run and fixed on 2026-11-16. A page limit now fails the repository rather than publishing a partial count.
