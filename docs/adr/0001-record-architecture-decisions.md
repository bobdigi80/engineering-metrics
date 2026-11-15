# 1. Record architecture decisions

Date: 2026-11-15
Status: Accepted

## Context

Decisions made early in a project are hard to reconstruct later. A reviewer, a new team member, or me in six months will want to know not just what was built but why, and what was rejected.

## Decision

Record significant decisions as short Architecture Decision Records in `docs/adr`, numbered and never deleted. A decision that changes gets a new ADR that supersedes the old one.

## Consequences

- Trade-offs are visible next to the code they shaped.
- Writing one takes ten minutes, so the bar is "would someone reasonably ask why?"
