# Pull Request

## Description
<!-- Brief description of what this PR does -->

## Related Spec
<!-- Link to spec directory, e.g., specs/001-initial-shell-app/ -->

## Checklist

### Code Quality
- [ ] Solution builds with `dotnet build` — 0 errors, 0 warnings
- [ ] `TreatWarningsAsErrors` is enabled in `Directory.Build.props`
- [ ] CA1848 compliance: all logging uses `[LoggerMessage]` source generators (no raw `ILogger.Log*` calls)
- [ ] Classes with `[LoggerMessage]` methods are declared `partial`

### Async / CancellationToken
- [ ] All I/O-bound methods are `async` and return `Task` or `Task<T>`
- [ ] All async methods accept `CancellationToken` as the last parameter
- [ ] `CancellationToken` is forwarded to downstream calls (never dropped)
- [ ] Async methods use the `Async` suffix (e.g., `GetProfileAsync`)
- [ ] No `.Result` or `.Wait()` calls on tasks (no sync-over-async)

### LINQ & Style
- [ ] LINQ uses extension-method (fluent) syntax, not query syntax
- [ ] File-scoped namespaces used throughout (`namespace X;`)
- [ ] Collection properties/parameters use plural naming
- [ ] Record types used for DTOs and immutable models where appropriate

### Architecture
- [ ] Repositories expose interfaces (`IXxxRepository`) and are registered via DI
- [ ] Scoped services used for request-lifetime dependencies
- [ ] Singleton services used for stateless/thread-safe infrastructure
- [ ] Azure Table entities implement `ITableEntity` with PK/RK formatter helpers
- [ ] Endpoints are mapped via extension methods (`MapXxxEndpoints`)

### Security
- [ ] Authenticated routes use `[Authorize]` or `.RequireAuthorization()`
- [ ] Public routes explicitly use `[AllowAnonymous]` or `.AllowAnonymous()`
- [ ] Identity linking uses verified email only (no unverified email linking)
- [ ] No secrets or connection strings committed to source control

### Testing
- [ ] Core scenarios validated against quickstart.md test focus items
- [ ] Auth flow tested: success redirects to dashboard, failure shows error on landing
- [ ] Stats visibility: hidden when `gamesPlayed == 0`
- [ ] Join conflict: dashboard remains with message, not redirected
- [ ] Profile save: UI refreshes immediately after settings update
