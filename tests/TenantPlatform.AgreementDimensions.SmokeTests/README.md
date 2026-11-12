# Agreement dimensions: database-free checks

From the repository root:

```sh
dotnet build tests/TenantPlatform.AgreementDimensions.SmokeTests/TenantPlatform.AgreementDimensions.SmokeTests.csproj --no-restore
dotnet run --project tests/TenantPlatform.AgreementDimensions.SmokeTests --no-build --no-restore
```

For offline restoration when packages are already cached:

```sh
dotnet restore tests/TenantPlatform.AgreementDimensions.SmokeTests/TenantPlatform.AgreementDimensions.SmokeTests.csproj --source "$HOME/.nuget/packages" -p:NuGetAudit=false
```

The console entrypoint does not start Web, open a database connection, execute SQL, apply migrations or invoke platform/demo initialization. All fixture data stays in EF InMemory. Npgsql metadata and migration operations are inspected using the existing offline design-time factory, with its connection-opening guard intact.

Public contracts for rule administration, create/read/edit catalogs, agreement details/options and history are tested directly, as are public authorization failures where they occur before relational operations. Valid create/update/rule writes invoke the private production `CreateAgreementCoreAsync`, `UpdateAgreementCoreAsync` and `SaveAgreementDimensionRuleCoreAsync` through reflection. These are the same authorized service operations used by the public relational wrappers and include validation, mutation, GUID revision changes, history and SaveChanges with the production audit interceptor. Only the outer transaction and PostgreSQL account/agreement row locks are skipped by this test harness. No provider-specific production bypass exists.

Coverage includes required/enabled normalization and Leasing independence; nullable patch semantics; create-required including invalid required configuration; account/dimension/value integrity; duplicates; parent, single/multiple and leaf-only rules; inactive ancestors; retained historical values and new additions; unrelated dimensions with newly missing required selections; read/edit/admin access and inactive membership; archived agreements; optimistic stale-input and stale-tracked-context conflicts; immutable history; full unrelated agreement-property and financial-version preservation; localization and paths; and additive schema metadata.

These checks do not prove PostgreSQL persistence, referential or uniqueness enforcement, transactions, locks, atomic rollback or concurrency behavior. The model checks prove only that the appropriate FK/index/check/concurrency metadata and migration source exist and the active snapshot matches. The analysis approval path uses the same assignment validator under its existing transaction and account lock, but its database-backed draft-lock/document promotion workflow is not executed by this runner.

Run the phase-2 Dimensions and existing Dashboard runners separately. The ordinary Agreements/Analysis/Leasing/etc. smoke programs use PostgreSQL and must not be run under the source-only phase constraints.
