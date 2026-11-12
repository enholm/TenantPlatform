# Dimensions: database-free smoke checks

Run from the repository root after restoring packages from an available package source:

```sh
dotnet build tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --no-restore
dotnet run --project tests/TenantPlatform.Dimensions.SmokeTests --no-build --no-restore
```

For an offline restore when all packages are already cached locally:

```sh
dotnet restore tests/TenantPlatform.Dimensions.SmokeTests/TenantPlatform.Dimensions.SmokeTests.csproj --source "$HOME/.nuget/packages" -p:NuGetAudit=false
```

This console runner is independent of the PostgreSQL-bound Leasing and Agreements programs. It does not start the Web host, open database connections, execute SQL, apply migrations or run initializers. No application settings, secrets or external service are required.

Coverage includes all three languages and existing language aliases; per-field fallback; missing/unknown languages and absent translations; unchanged legacy TranslationHelper.Select behavior; translation patches, clearing and preservation; supported write languages, length limits and duplicate prevention; selected-account and administrator access; ordinary Leasing-owner access; parent revision conflicts; DimensionHistory and the production audit interceptor; localized register/Leasing read models; rendered editor/overview/picker/current-detail/history components; retention of modal edits after a validation error; unchanged Leasing snapshots and allocation calculations in all three split modes.

Public service reads and authorization failures run normally against EF InMemory. Valid writes exercise the private production `DimensionService.ApplyDimensionChanges` tracked-graph operation through reflection, followed by InMemory `SaveChangesAsync`. This deliberately avoids the relational transaction and account row lock in `SaveDimensionAsync`; no production provider guards or test-only authorization bypasses were added. The runner verifies the explicit revision check and InMemory parent concurrency token. It does **not** demonstrate PostgreSQL transactions, atomicity, locking, referential integrity, uniqueness enforcement or real database persistence.

The offline design-time factory is used only to inspect Npgsql model metadata, compare the active snapshot with the model (`HasPendingModelChanges`) and inspect the generated migration's operations. Database connection objects remain closed. Metadata assertions are not PostgreSQL constraint-execution tests. The factory has an EF connection-opening guard and cannot apply migrations; real database verification uses a separately authorized runtime context/test environment.

Component checks use HtmlRenderer and the repository's existing reflection-based interaction pattern. They are not browser layout, keyboard or end-to-end tests. The existing Dashboard smoke runner remains a separate regression command.

Phase 7 adds database-free account-deletion decisions for all shared/Agreements/Leasing dimension collections, absence of retired CLR/request/schema fields, final generic FK metadata, new migration operation checks and structural replay of all 45 migrations. Replay never executes SQL or proves PostgreSQL constraints/types/data backfills. The unchanged offline guard is invoked directly in a simulated opening check, without asking EF/Npgsql to open a connection.
