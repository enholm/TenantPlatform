This remains a PostgreSQL-only runner. Do not run it during offline implementation phases.

The historical part stops at AddAccountRegisterHierarchies and reads unmapped DTOs through SQL to verify the old rename/data-preservation boundary. It then migrates to the current schema, including the intentional legacy drop. It does not depend on removed current CLR entities and does not assert that dropped rows survive the final migration.

Current-runtime checks use DimensionService: authorization/account isolation, normalization, validation, revisions, hierarchy moves, concurrent cycle prevention and relational parent FKs. Deletion protection is now Restrict metadata/assignment coverage and deactivation, since Dimensions has no public delete API. Account deletion decision checks also run independently without PostgreSQL in TenantPlatform.Dimensions.SmokeTests.

A separately authorized run requires ACCOUNT_REGISTER_TEST_CONNECTION targeting the disposable tenant_account_register_tests database. The runner logs the database and its unique register_test_<GUID> schema before connecting. It drops that schema only after all checks succeed. A failure after schema creation preserves the schema and rethrows the original exception for diagnosis; do not automatically rerun or remove the failed schema. Compilation alone does not verify any PostgreSQL behavior.
