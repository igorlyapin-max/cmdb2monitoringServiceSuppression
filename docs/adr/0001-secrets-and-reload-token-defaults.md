# ADR 0001: Secrets and Reload Token Defaults

## Status

Accepted

## Context

Tracked configuration previously contained local reload-token placeholders and integration password examples. Even when intended for development, these values are indistinguishable from weak production defaults during audits and secret scanning.

## Decision

- Tracked appsettings keep reload endpoints disabled by default.
- Runtime reload is enabled only through environment variables, mounted config, or `secret://` / `aapm://` companion fields.
- UI and materializer reload helpers are disabled by default unless the same reload token source is configured for every target.
- GitLab CI runs secret scanning on every pipeline.

## Consequences

- Local reload workflows require explicit opt-in configuration.
- Configuration files are safer to reuse as examples and no longer contain reusable credentials.
