# 4. Lambda Function URL instead of API Gateway

Date: 2026-11-15
Status: Accepted

## Context

The API has one public, read-only route returning a small JSON document that changes once a day.

## Decision

Expose the API Lambda with a Function URL (auth type `NONE`) and configure CORS on the URL for the portfolio's origins. Responses carry `Cache-Control: public, max-age=3600`.

## Consequences

- No API Gateway cost and nothing extra to configure.
- Gives up API Gateway's throttling, API keys and custom domains. The main risk is someone hammering the URL. Mitigations: Lambda's free tier covers 1M requests a month, responses are cacheable, and the budget alarm (ADR 5) emails before costs matter. If it becomes a problem, put CloudFront in front (also free at this scale) or set reserved concurrency.
