# TenantPlatform Codex Instructions

## Project overview

TenantPlatform is a multi-tenant property service platform built on .NET 10.

The solution currently consists of three projects:

- `TenantPlatform.Core`
- `TenantPlatform.Infrastructure`
- `TenantPlatform.Web`

Preserve the existing architecture unless a task explicitly requires an architectural change.

## Project responsibilities

### TenantPlatform.Core

Contains the domain model and enums, including accounts, organizations, buildings and properties, occupancies, users and identity-related domain objects, services, networking, auditing, and localization-related domain concepts.

Core must not depend on Infrastructure or Web.

### TenantPlatform.Infrastructure

Contains Entity Framework Core persistence, `TenantPlatformDbContext`, entity mappings using `IEntityTypeConfiguration<T>`, PostgreSQL/Npgsql configuration, migrations, database initialization, audit interception, and infrastructure services.

Infrastructure depends on Core.

### TenantPlatform.Web

Contains the ASP.NET Core application host, Blazor UI, application services, DTOs, authentication and authorization, cookie handling, and background workers.

Web depends on Core and Infrastructure.

Application services currently belong in Web. Do not introduce a separate Application project unless explicitly requested.

## Architecture principles

Prefer consistency with the existing implementation over introducing new architectural patterns.

Do not introduce repositories, MediatR, CQRS, Result<T> abstractions, mapping frameworks, validation frameworks, or similar abstractions unless explicitly requested or clearly necessary for the task.

Before implementing new functionality, inspect similar existing functionality and follow its structure and conventions.

Keep changes scoped to the requested task. Do not refactor unrelated code.

## Entity Framework Core

The application uses PostgreSQL through Npgsql.

`IDbContextFactory<TenantPlatformDbContext>` is used for most application operations.

For application services and Blazor-related operations, prefer creating a short-lived context with `CreateDbContextAsync()` and disposing it with `await using`.

Do not keep a DbContext alive for the duration of an interactive Blazor circuit.

Do not run concurrent EF Core operations against the same DbContext instance.

Use `AsNoTracking()` for read-only queries where appropriate.

Follow the existing direct-EF service pattern. Do not introduce repository abstractions unless explicitly requested.

Entity mappings belong in separate `IEntityTypeConfiguration<T>` classes and are discovered through assembly scanning.

## Multi-tenancy and security

`Account` is the primary tenant isolation boundary.

Tenant isolation is currently enforced primarily through explicit `AccountId` predicates in application queries.

There is no global EF Core tenant query filter.

Therefore:

- Every query involving tenant-owned data must explicitly preserve the correct `AccountId` boundary.
- Never trust an AccountId, OrganizationId, BuildingId, or other resource identifier supplied by the client without verifying access.
- Never create queries that could return data belonging to another account.
- Writes must verify that referenced resources belong to the selected account.
- Platform administrator behavior must remain distinct from normal account-level authorization.

When modifying existing queries, pay particular attention to whether an `AccountId` restriction could accidentally be removed.

Security and tenant isolation take priority over convenience.

## Authentication and authorization

Authentication uses custom cookie authentication together with the existing User/LoginAccount model.

Do not replace this with ASP.NET Core Identity authentication unless explicitly requested.

The selected account is stored in the authentication context/cookie.

Authorization is handled through the existing `TenantAuthorizationService` and explicit permission/resource checks.

Follow existing authorization patterns when adding new pages and application services.

Do not rely solely on hiding UI elements for authorization. Server-side access must still be enforced.

Be aware that `CurrentUserContextService` may live for the lifetime of an interactive Blazor circuit and may therefore contain cached identity/account information.

## Blazor

The UI is implemented using Blazor.

Pages are organized by feature under `Components/Pages`.

Shared layouts and navigation belong under `Layout`.

Reusable components belong under `Shared` or the existing appropriate feature folder.

The application mixes static server rendering and `InteractiveServer`.

Do not make the entire application interactive unless explicitly requested.

Follow the render mode used by similar existing pages.

Components should normally call application services rather than performing direct database operations.

Follow existing Bootstrap styling and CSS conventions.

Do not introduce a new frontend framework.

## Localization

The application supports:

- Norwegian Bokmål
- British English
- Swedish

Follow the existing localization/resource pattern for new user-visible text.

Do not hard-code UI strings when the surrounding functionality is localized.

## Database migrations

Database schema changes must use EF Core migrations.

Create migrations using `bash scripts/add-migration.sh MigrationName`.

Apply migrations using `bash scripts/update-database.sh`.

Migrations belong in `TenantPlatform.Infrastructure/Persistence/Migrations`.

Do not modify an existing migration that may already have been applied. Create a new migration instead.

Do not run `update-database.sh` or otherwise apply migrations unless explicitly requested.

The application automatically applies pending migrations during startup.

## Building and validation

After making code changes, run `dotnet build TenantPlatform.sln`.

Fix compilation errors introduced by the change.

Do not make unrelated changes merely to remove existing warnings.

If relevant tests exist in the future, run them.

Currently the solution does not contain a test project.

## Git

Do not commit changes unless explicitly requested.

Do not push changes unless explicitly requested.

Do not change branches unless explicitly requested.

Do not rewrite Git history.

Do not reformat unrelated files.

Before significant changes, inspect the existing implementation and keep the diff focused.

## Working style

For non-trivial tasks:

1. Inspect the relevant existing code.
2. Identify similar functionality already present.
3. Briefly state the proposed implementation approach.
4. Implement the smallest coherent change.
5. Build the solution.
6. Report what changed and any remaining concerns.

When asked to investigate or analyze something without making changes, do not edit files.

When uncertain about an architectural decision, ask or present the trade-off instead of silently introducing a new pattern.
