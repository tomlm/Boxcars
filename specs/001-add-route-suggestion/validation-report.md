# Validation Report: Route Suggestion

**Feature**: `001-add-route-suggestion`  
**Date**: 2026-03-03

## SC-004 Usability Validation

- **Target**: At least 90% of test users correctly identify all suggested route points from circle highlights.
- **Method**: Manual usability scenario run from `quickstart.md` with controlled route examples.
- **Sample Size**: TBD during manual usability run.
- **Result**: Pending manual validation.

## Responsiveness Validation

- **Target**: Perceived route update responsiveness under 200ms for normal map interactions.
- **Method**: Manual interaction timing checks using repeated destination updates, plus local runtime smoke timing sample.
- **Automatable Signal**: Local home-page request measured `83.48ms` (`Invoke-WebRequest` on `http://127.0.0.1:5188/`).
- **Result**: Partial (automated smoke timing captured); full route-interaction validation still pending manual run.

## Runtime Sanity Checks (Automatable)

- `dotnet build Boxcars.slnx` passes.
- App boots via `dotnet run --project src/Boxcars/Boxcars.csproj --urls http://127.0.0.1:5188`.
- Home page responds with `HTTP 200`.
- Protected route `/game/test` returns `HTTP 302` (auth challenge/redirect expected for unauthorized request).

## Notes

- `T029` remains pending manual usability verification (>=90% criterion).
- `T030` remains pending manual route-interaction timing verification; automated local request timing is recorded above.
