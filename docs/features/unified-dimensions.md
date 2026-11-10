# Feature Specification: Unified Dimensions Across TenantPlatform

**Project:** TenantPlatform  
**Feature:** Unified dimension-based classification  
**Target modules:** Shared Dimensions, Agreements, Leasing, Dashboard and Demo Data  
**Execution:** AgentOrchestrator 0.5.1 — Autonomous Development (explicit source-only policy grants)  
**Scope:** Domain, Application, Infrastructure, Web, EF Core migrations, localization, demo-data generation and automated tests

---

## 1. Objective

Replace the existing dedicated Geography and Organization Structure classification models with the shared Dimensions framework throughout TenantPlatform.

Dimensions shall become the common mechanism for configurable, hierarchical classification across Agreements and Leasing, with the possibility of reuse in additional modules.

The implementation must:

1. Extend Dimensions with multilingual names and descriptions.
2. Introduce independent module-specific dimension configuration for Agreements.
3. Support generic dimension assignments on agreements.
4. Completely replace the legacy Geography and Organization Structure classification models.
5. Preserve and convert the complete existing demo-data generation logic.
6. Generate approximately 300 demo agreements with equivalent geographic and departmental assignments.
7. Support hierarchical aggregation and drill-down in agreement dashboard analytics.
8. Remove obsolete domain models, services, UI, EF Core mappings and database structures.
9. Preserve existing Leasing functionality and unrelated business behavior.
10. Implement and verify the feature autonomously within the configured trusted execution environment.

**Important:** The existing development database will be manually deleted and recreated by the user after implementation. Existing persisted data does not need to be migrated. However, all existing demo-data generation logic and its resulting logical structure must be preserved and converted to Dimensions.

This specification supersedes all previous versions and amendments of the unified-dimensions feature specification.

## 2. Development Database Strategy

TenantPlatform is currently under development. The existing application database contains disposable demo data.

The user will manually delete and recreate the development database after the feature has been implemented and reviewed.

### 2.1 Existing database data

The implementation shall not:

- Migrate existing Geography records from the current database.
- Migrate existing Organization Structure records from the current database.
- Migrate existing agreement classification assignments from the current database.
- Access, reset, modify or delete the existing application database.
- Run destructive migrations against the existing application database.

No data-copying migration logic is required solely to preserve existing development database records.

### 2.2 Database schema

The implementation must nevertheless:

- Introduce the necessary new tables, columns, indexes and relationships.
- Remove obsolete legacy classification tables and columns through new EF Core migrations.
- Preserve the existing historical migration files.
- Ensure the complete migration chain is logically consistent for a newly created database.
- Update the EF Core model snapshot.
- Preserve existing unrelated tables and relationships.

Do not rewrite or delete historical migrations.

Historical migrations may create legacy classification tables. The new migration shall remove those structures after introducing the replacement schema.

### 2.3 Verification boundary

AgentOrchestrator must not access the ordinary application database.

Use only configured trusted build and smoke-test verification commands.

PostgreSQL migration execution, fresh-database creation, and full database-backed demo initialization are explicitly deferred to `POST_RUN_VERIFICATION`. They are not autonomous implementation steps, must not be attempted, and must not be classified as requests for destructive-database or infrastructure authorization.

The final report must explicitly mark these checks **NOT PERFORMED — POST_RUN_VERIFICATION**, rather than PASS or FAIL. Source-level tests and migration compilation are still required wherever supported by trusted commands.

Do not claim that successful compilation proves successful PostgreSQL migration execution.

The user will perform the final database recreation and database-backed verification after reviewing the implementation.

## 3. Architectural Principles

### 3.1 Reuse the shared Dimensions framework

The existing Dimensions implementation shall be extended, not replaced.

Dimensions are account-scoped classification definitions containing:

- Stable language-independent codes.
- Localized names and descriptions.
- Active/inactive status.
- Single-selection or multiple-selection configuration.
- Leaf-only or unrestricted hierarchical selection.
- Hierarchical dimension values.
- Existing revision and history mechanisms.

Dimension values may have arbitrary hierarchy depth, subject to existing system constraints.

Do not introduce a second classification framework.

### 3.2 Configuration responsibilities

Dimension-level settings define how values can be selected.

Module-level settings define where dimensions can be used and whether they are required.

| Setting | Owner |
|---|---|
| Multiple values allowed | Dimension |
| Leaf-only selection | Dimension |
| Active/inactive | Dimension |
| Available in Leasing | Leasing |
| Required in Leasing | Leasing |
| Allowed in Leasing cost allocation | Leasing |
| Available in Agreements | Agreements |
| Required at agreement registration | Agreements |

Do not duplicate global dimension selection rules in Agreements configuration.

### 3.3 Preserve actual business organizations

The actual `Organization` business entity represents legal entities, counterparties and other real organizations.

These entities must remain.

Only the legacy Organization Structure classification represented by `OrganizationElement` shall be removed.

Similarly, remove legacy Geography classification represented by `GeographicArea`, but preserve unrelated address, country, postal, building, unit and location information.

### 3.4 Authorization and account isolation

All dimensions, values, module configurations and assignments must remain account-scoped.

Preserve the established authorization architecture and existing Application-layer security patterns.

All reads and writes must enforce account isolation.

Do not implement authorization exclusively in Razor components.

## 4. Dimension Selection Rules

Preserve the existing dimension-level configuration.

### 4.1 Single selection

If `AllowMultiple` is false, an agreement may have at most one explicitly assigned value for that dimension.

### 4.2 Multiple selection

If `AllowMultiple` is true, an agreement may have multiple explicitly assigned values.

Duplicate assignments of the same dimension value to the same agreement are prohibited.

### 4.3 Leaf-only selection

If `LeafOnly` is true, only leaf nodes may be selected.

If `LeafOnly` is false, both parent and leaf nodes may be selected.

### 4.4 Hierarchical membership

A selected value implies membership in that value and all its ancestors.

Example:

- Norge
  - Østfold
    - Moss
    - Fredrikstad

An agreement explicitly assigned to Moss belongs to:

- Moss
- Østfold
- Norge

Only the explicit assignment to Moss shall be persisted.

Ancestor membership must be calculated from the hierarchy.

Do not store redundant ancestor assignments.

### 4.5 Inactive dimensions and values

Inactive dimensions and values must not be available for new assignments.

Existing historical assignments must remain readable and must not be silently removed.

Preserve existing Leasing behavior regarding inactive dimension values.

## 5. Multilingual Dimension Names and Descriptions

Extend Dimensions to support localized names and descriptions.

### 5.1 Requirements

1. Dimension codes remain stable and language-independent.
2. Dimension names and descriptions support multiple languages.
3. Support the application's existing Norwegian and English language conventions, including `nb-NO` and the relevant English culture.
4. Preserve existing Swedish UI localization where applicable.
5. Reuse the existing translation architecture.
6. Allow translations to be edited in Dimensions administration.
7. Use localized dimension names in Agreements, Leasing, selectors and dashboards.
8. Preserve existing dimension names as fallback values.
9. Do not change identifiers or relationships when the UI language changes.

Inspect the existing `SupportedLanguages`, `LanguageHelper` and translation patterns before choosing culture codes or storage conventions.

Do not introduce a competing localization system.

### 5.2 Fallback

Resolve dimension display text in this order:

1. Requested UI language.
2. Account default language, if available.
3. Stored fallback name or description.
4. Stable dimension code for names.

Missing descriptions may remain empty.

### 5.3 Dimension values

Preserve existing dimension-value behavior and names.

If the existing architecture already supports value translations, reuse that mechanism.

Do not expand the feature into an unrelated redesign of value management.

## 6. Agreements Dimension Configuration

Add a new tab to the existing Dimensions administration page:

**Bruk i Avtaler**

Follow the visual and interaction patterns of the existing **Bruk i Leasing** tab.

The new tab shall contain:

| Norwegian label | Meaning |
|---|---|
| Tilgjengelig | Dimension is available in Agreements |
| Obligatorisk ved registrering | Selection is required when creating an agreement |

### 6.1 Configuration behavior

- Required implies available.
- Disabling availability also disables required.
- Unavailable dimensions are not offered for new assignments.
- Required dimensions are validated server-side when creating agreements.
- Existing assignments remain when a dimension becomes unavailable.
- Editing an agreement must not silently remove unavailable assignments.
- Configuration changes are persisted and audited.
- Configuration is account-scoped.
- Existing concurrency conventions are preserved.

Do not add separate Agreements settings for multiple selection or leaf-only selection.

### 6.2 Existing agreements

Changing a dimension to required must not automatically invalidate existing agreements.

When editing an agreement that lacks a newly required dimension:

- Unrelated edits must remain possible.
- The missing assignment must not be created automatically.
- If the user explicitly changes that dimension's assignments, required validation applies.

## 7. Agreement Dimension Assignments

Extend Agreements to support generic dimension assignments.

### 7.1 Data model

An agreement may reference multiple dimensions.

Each dimension may have one or multiple explicitly assigned values according to its global selection rules.

Assignments must reference stable IDs.

The data model must enforce account ownership and valid dimension-value relationships.

Prevent duplicate assignments.

### 7.2 Server-side validation

Validate:

- Agreement ownership and access.
- Dimension account ownership.
- Dimension-value account ownership.
- Value belongs to the selected dimension.
- Dimension availability.
- Active state for new assignments.
- `AllowMultiple`.
- `LeafOnly`.
- Required dimensions on agreement creation.
- Duplicate selections.
- Preservation of historical assignments when editing.

Retain existing agreement authorization and concurrency behavior.

### 7.3 Agreement user interface

Replace the dedicated Geography and Organization Structure controls with a generic dimension section.

The section must:

- Display available dimensions dynamically.
- Display localized names.
- Indicate required dimensions.
- Display hierarchical value paths.
- Support single and multiple selection.
- Permit parent-node selection when configured.
- Preserve selections after validation errors.
- Display validation messages using existing conventions.
- Support creation, editing and details views.
- Preserve unavailable or inactive historical assignments appropriately.

### 7.4 Preserve unrelated agreement properties

Do not change agreement lifecycle, counterparties, owners, financial information, notice periods, indexation, reminders, documents or unrelated agreement functionality.

## 8. Complete Removal of Legacy Classification Models

Completely replace the legacy Geography and Organization Structure classification implementations with Dimensions.

### 8.1 Dependency inventory

Inspect the entire solution for references to:

- `GeographicArea`
- `OrganizationElement`
- `GeographicAreaId`
- `OrganizationElementId`
- Legacy classification DTOs
- Domain models
- EF Core mappings
- Services
- API contracts
- Razor components
- Navigation entries
- Dashboard queries
- Demo-data generation
- Tests

Do not assume these classifications are used only in Agreements.

### 8.2 Remove obsolete implementations

Remove obsolete:

- Domain entities.
- Dedicated account-settings services.
- Dedicated administration pages.
- Service registrations.
- Navigation links.
- Legacy agreement properties and DTO fields.
- Legacy dashboard grouping implementations.
- EF Core configurations and DbSets.
- Database tables, indexes, foreign keys and classification columns.
- Demo-data generation of legacy entities.

Remove or replace tests that depend on the obsolete models.

### 8.3 Historical migrations

Do not modify previously created EF Core migrations.

References to legacy entities or tables inside historical migration files are acceptable because those files represent the historical schema.

The current domain model, active application code, current EF Core snapshot and new migrations must use the new Dimensions model.

### 8.4 Preserve unrelated functionality

Do not remove actual Organization entities or unrelated geographic/address information.

Do not modify Leasing cost-allocation semantics.

## 9. Demo-Data Conversion — Mandatory Full Preservation

This section is especially important.

The existing `DemoDataInitializer` creates extensive hierarchical Geography and Organization Structure demo data and approximately 300 agreements with deterministic assignments to those hierarchies.

**All of this generation logic must be converted to Dimensions without simplifying the dataset.**

The requirement concerns the source-code generation logic and the logical dataset produced by it, not the records currently stored in the development database.

### 9.1 Inspect the existing generator

Before changing the generator, identify and document internally:

- All generated Geography levels.
- All generated Geography values.
- All generated Organization Structure levels.
- All generated Organization Structure values.
- Existing parent-child relationships.
- Existing deterministic identifier generation.
- Existing agreement-to-geography assignment logic.
- Existing agreement-to-department assignment logic.
- Existing distributions across the hierarchies.
- The actual number of generated agreements.

Use the existing generator as the authoritative reference.

Do not replace it with a small illustrative dataset.

### 9.2 Geography dimension

Replace legacy Geography generation with an ordinary dimension.

| Property | Value |
|---|---|
| Code | `GEO` |
| Norwegian name | Geografi |
| English name | Geography |
| Active | Yes |
| AllowMultiple | False |
| LeafOnly | False |
| Available in Agreements | Yes |
| Required in Agreements | No |

Do not automatically enable this dimension in Leasing.

### 9.3 Department dimension

Replace legacy Organization Structure generation with an ordinary dimension.

| Property | Value |
|---|---|
| Code | `DEPT` |
| Norwegian name | Avdeling |
| English name | Department |
| Active | Yes |
| AllowMultiple | False |
| LeafOnly | False |
| Available in Agreements | Yes |
| Required in Agreements | No |

Do not automatically enable this dimension in Leasing.

### 9.4 Preserve the complete hierarchies

Convert every previously generated `GeographicArea` into a `DimensionValue` belonging to `GEO`.

Convert every previously generated `OrganizationElement` into a `DimensionValue` belonging to `DEPT`.

Preserve:

- All generated values.
- All hierarchy levels.
- All names.
- All parent-child relationships.
- The existing ordering where meaningful.
- Deterministic identity and generation behavior where practical.

Do not reduce the number of generated values.

Do not flatten the hierarchies.

Do not replace the original structures with generic examples.

### 9.5 Preserve deterministic assignments

The existing demo generator determines which geographic area and organizational element each generated agreement belongs to.

Replace those assignments with generic dimension assignments.

For each generated agreement:

- Create exactly one `GEO` assignment.
- Create exactly one `DEPT` assignment.
- Use the corresponding values from the converted hierarchies.
- Preserve the existing deterministic mapping logic.
- Preserve the original distribution across geographic areas and departments.
- Do not randomly redistribute agreements.
- Do not substitute arbitrary parent or leaf nodes.

The resulting logical dataset must be equivalent to the previous generator, except that classifications now use Dimensions.

### 9.6 Preserve the approximately 300 agreements

Continue generating the existing approximately 300 demo agreements.

Preserve their existing:

- Titles.
- Types.
- Counterparties.
- Owners.
- Statuses.
- Dates.
- Financial properties.
- Other generated relationships.
- Deterministic generation behavior.

Only replace the legacy classification references.

Do not delete or simplify unrelated demo-data generation.

### 9.7 Idempotency

Repeated demo-data initialization must not create duplicates.

Ensure:

- `GEO` and `DEPT` are created once per relevant demo account.
- Dimension values are not duplicated.
- Agreement assignments are not duplicated.
- Existing generated records are handled consistently with established initialization conventions.
- Account isolation is preserved.

### 9.8 No legacy generation

After implementation, `DemoDataInitializer` must no longer generate `GeographicArea` or `OrganizationElement` entities.

All corresponding data must be generated directly as Dimensions and DimensionValues.

## 10. Agreement Dashboard

Update the existing agreement distribution widget to support fixed agreement properties and dynamically configured dimensions.

### 10.1 Fixed grouping options

Preserve:

- Avtaletype
- Avtalepartner
- Status
- Avtaleansvarlig

### 10.2 Dynamic dimensions

Include all active dimensions configured as available in Agreements.

Use localized dimension names.

`GEO` and `DEPT` must appear as ordinary dimensions.

Do not retain dedicated Geography or Organization Structure grouping logic.

### 10.3 Hierarchical aggregation

Each agreement contributes to every explicitly assigned value and all its ancestors.

Example:

- Norge
  - Østfold
    - Moss
    - Fredrikstad

An agreement assigned to Moss contributes once to:

- Moss
- Østfold
- Norge

An agreement assigned to both Moss and Fredrikstad contributes:

| Node | Count |
|---|---:|
| Moss | 1 |
| Fredrikstad | 1 |
| Østfold | 1 |
| Norge | 1 |

Within any individual node, an agreement must be counted at most once.

This applies even when multiple assignments share an ancestor.

Do not persist redundant ancestor assignments.

### 10.4 Drill-down

Support hierarchical navigation:

1. Show top-level categories.
2. Select a parent to view its children.
3. Continue through any supported hierarchy depth.
4. Navigate back to previous levels.

Preserve existing dashboard filters.

Changing filters must refresh counts while maintaining a valid drill-down context.

### 10.5 Counts and percentages

Distinguish between:

- Total unique agreements.
- Distinct agreements within each hierarchy node.
- Occurrences across displayed sibling groups.

Rules:

1. Count each agreement once per node.
2. An agreement may contribute to multiple sibling groups.
3. Sibling counts may therefore sum to more than the number of unique agreements.
4. Calculate percentages using the sum of counts across the currently displayed sibling groups.
5. Do not combine ancestors and descendants in the same percentage denominator.
6. Preserve unique agreement totals separately.
7. Explain overlapping counts when multiple selection is enabled.

### 10.6 Parent-node assignments

An agreement explicitly assigned to a parent belongs to that parent and its ancestors.

It must not automatically be counted under its descendants.

When drilling into a parent, agreements assigned directly to that parent must remain represented, for example through a distinct direct-assignment category, so the drill-down does not silently omit them.

### 10.7 Unassigned agreements

Agreements without an assignment for the selected dimension must appear in a separate **Ikke angitt** category at the top-level grouping.

Do not place unassigned agreements under hierarchy nodes.

### 10.8 Authorization

Dashboard aggregation must use only agreements visible to the current user.

Preserve existing account filters and agreement-access rules.

### 10.9 Financial aggregation

Do not introduce financial allocation or financial aggregation across dimensions.

This feature concerns agreement counts.

## 11. Database Schema and EF Core Migrations

Implement the necessary EF Core changes for:

- Dimension translations.
- Agreements dimension configuration.
- Agreement dimension assignments.
- Account-aware foreign keys.
- Uniqueness constraints.
- Required indexes.
- Removal of obsolete classification columns and tables.

### Requirements

1. Preserve existing historical migrations.
2. Add new migrations for the new schema.
3. Update the model snapshot.
4. Preserve account isolation.
5. Preserve unrelated business entities and relationships.
6. Ensure the current model does not depend on removed legacy entities.
7. Ensure a newly created database can logically progress through the historical migration chain.
8. Do not implement data-copying migration logic for disposable development database contents.
9. Do not access the ordinary application database during autonomous execution.

PostgreSQL migration execution and fresh-database initialization are mandatory **post-run manual verification gates**. The autonomous agent must only author migration source and update the specifically authorized current snapshot. It must never execute migrations, open database connections, reset a database, or issue SQL.

## 12. Automated Tests

Implement tests for the new functionality and update existing tests affected by legacy removal.

### 12.1 Dimension configuration

Verify:

- Agreements availability.
- Required-implies-available behavior.
- Independent Agreements and Leasing configuration.
- Persistence.
- Authorization.
- Tenant isolation.

### 12.2 Agreement assignments

Verify:

- Single selection.
- Multiple selection.
- Duplicate prevention.
- Parent selection.
- Leaf-only restrictions.
- Required validation.
- Cross-account rejection.
- Editing assignments.
- Inactive and unavailable historical assignments.
- Preservation of unrelated agreement properties.

### 12.3 Dashboard

Verify:

- Dynamic grouping options.
- Hierarchical ancestor roll-up.
- Distinct agreement counting per node.
- Shared-ancestor deduplication.
- Multiple sibling assignments.
- Drill-down and back navigation.
- Parent-only assignments.
- Direct-assignment category.
- Unassigned category.
- Percentage calculations.
- Existing filters.
- Authorization and tenant isolation.

### 12.4 Localization

Verify:

- Norwegian dimension names.
- English dimension names.
- Fallback behavior.
- Consistent labels across Agreements, Leasing and Dashboard.

### 12.5 Demo-data equivalence

Add tests that compare the new demo-data generation logic against the previous deterministic behavior.

Verify:

- The complete Geography hierarchy is preserved.
- The complete Organization Structure hierarchy is preserved.
- The number of generated values matches the previous generator.
- Parent-child relationships match.
- The approximately 300 agreements are still generated.
- Every generated agreement receives one `GEO` assignment.
- Every generated agreement receives one `DEPT` assignment.
- The distribution of assignments matches the previous generator.
- No legacy classification records are generated.
- Initialization is idempotent.

Prefer deterministic checks that do not require access to the ordinary application database.

### 12.6 Regression

Verify:

- Existing Leasing classification.
- Leasing allocation calculations.
- Agreement creation and editing.
- Agreement dashboard.
- Existing authorization.
- Account isolation.
- Relevant application contracts.

### 12.7 Verification honesty

Execute all configured trusted verification commands.

Do not claim that tests passed if they were only compiled.

Do not claim that PostgreSQL migrations were tested if no isolated PostgreSQL database was used.

Report verified and unverified acceptance criteria separately. Specifically report PostgreSQL migration application, fresh-database initialization, and database-backed demo equivalence as `NOT PERFORMED — POST_RUN_VERIFICATION`. Never treat compilation or in-memory tests as proof of PostgreSQL behavior.

## 13. Autonomous Execution Requirements

AgentOrchestrator 0.5.1 shall implement this feature as one autonomous development task.

### 13.1 Execution

- Plan the complete implementation.
- Divide the work into manageable dependent steps.
- Execute without intermediate human approvals.
- Automatically handle decisions within the configured trusted scope.
- Record decisions and their rationale.
- Run trusted verification commands.
- Attempt bounded automatic repair.
- Re-run verification after repairs.
- Preserve file-scope and command restrictions.
- Preserve Git-worktree isolation.
- Do not request expanded permissions.
- Do not automatically merge or deploy.
- Do not access the ordinary application database.

### 13.2 Explicit source-authoring authorization and policy boundaries

This task intentionally changes the application architecture. The operator must explicitly enable the following **reviewed opt-ins** in the trusted `develop.json` under its existing `autonomous` object before starting a **fresh** autonomous run:

```json
"authorizedCategories": ["ARCHITECTURE"],
"migrationSourceAuthoring": {
  "directories": [
    "src/TenantPlatform.Infrastructure/Persistence/Migrations/"
  ],
  "snapshotPaths": [
    "src/TenantPlatform.Infrastructure/Persistence/Migrations/TenantPlatformDbContextModelSnapshot.cs"
  ]
}
```

These are fragments of the existing `autonomous` object, not a complete configuration file. Keep `allowedPaths` restricted to `src/` and `tests/`; the grant does not enlarge that boundary. The named snapshot path must match the actual active snapshot before use; if it does not, stop and request a reviewed configuration correction rather than silently widening access.

The `ARCHITECTURE` grant authorizes architectural **source-code changes** only within the configured file scopes. The `migrationSourceAuthoring` grant permits creation of new EF Core migration source files in the named directory and modification of the explicitly named **current** model snapshot. Existing historical migrations are immutable. Do not delete or rewrite historical migrations, delete the snapshot, or delete migration source files. Obsolete ordinary source files within allowed scopes may be removed when required, but this does not authorize database operations.

**Authoring is not execution.** It is permitted to author a new migration whose `Up` method removes obsolete legacy tables or columns, including `DropTable` and `DropColumn` operations, provided it preserves unrelated schema. It is **not** permitted to apply that migration, invoke `dotnet ef database update`, run SQL, connect to PostgreSQL, reset a database, or access production or development data. Do not request or grant `DESTRUCTIVE_DATABASE`, `INFRASTRUCTURE`, `DEPLOYMENT`, `AUTHENTICATION`, or `AUTHORIZATION` merely to implement this feature.

Preserve the existing authentication and authorization architecture. Account-scoped queries, existing Application-layer access checks, and tests verifying tenant isolation are ordinary feature work, **not** a request to redesign security. Do not change authentication mechanisms, authorization policy architecture, privilege boundaries, or credential handling. If an actual security-architecture change proves necessary, report it as blocked rather than reclassifying or bypassing policy.

The run plan must use exact file scopes for its steps, contained by the directory grants, and must identify the new migration and current snapshot explicitly where required. Use the installed AgentOrchestrator policy enforcement, trusted verification commands, worktree isolation, and audit logging; do not weaken them. Any source change requiring a protected file outside these grants must be reported rather than attempted.

### 13.3 Fresh plan and deferred operational decisions

Start a **new** autonomous plan from this revised specification. Do not resume or reuse the previously blocked plan or its unresolved decisions. In particular:

- Migration source authoring is approved within the named scope; **destructive database execution is not approved**. A source-only migration step must not be classified as database execution merely because its generated migration removes obsolete schema.
- PostgreSQL migration testing, database recreation and demo initialization are **explicitly deferred** to `POST_RUN_VERIFICATION`. They are not infrastructure changes for the agent to approve or perform.
- The plan must not add an execution step that requires database access, external infrastructure, deployment, or a new trusted command.
- If a required implementation change genuinely needs an ungranted sensitive category, record the blocker and stop that work; do not infer approval from this document.

### 13.4 Verification-only steps

Verification-only steps may use empty file scopes when supported by AgentOrchestrator.

Such steps must execute only existing trusted verification commands and must not modify source files.

### 13.5 Completion

The run must distinguish between:

**IMPLEMENTATION COMPLETE:** All required code changes are implemented and all configured trusted verification commands pass.

**IMPLEMENTATION FAILED:** A blocking issue prevents completing the required implementation.

Database-backed PostgreSQL verification is a separate, explicitly deferred `POST_RUN_VERIFICATION` acceptance gate. Its unperformed status does not block completion of source-only implementation, and does not imply permission to access databases.

Do not report PostgreSQL migration execution or fresh-database initialization as verified unless those operations actually ran in an isolated environment.

### 13.6 Safety

Do not weaken trusted execution restrictions to complete the task.

Do not access production or existing development databases.

Do not delete user data.

Do not perform automatic deployment.

## 14. Final Report

Produce one consolidated report containing:

1. Implementation summary.
2. Architectural changes.
3. New dimension data model.
4. Agreements configuration changes.
5. Agreement assignment implementation.
6. Legacy model removal.
7. EF Core migration changes.
8. Localization changes.
9. Complete demo-data conversion summary.
10. Generated hierarchy equivalence.
11. Agreement assignment equivalence.
12. Dashboard roll-up and drill-down implementation.
13. Leasing regression results.
14. Build and smoke-test results.
15. Decisions made within explicitly granted policy scope and their audit records.
16. Repair attempts and outcomes.
17. Remaining limitations.
18. `POST_RUN_VERIFICATION` checks marked NOT PERFORMED, with no claim of database success.
19. Manual `POST_RUN_VERIFICATION` checklist: review migration source and destructive operations, recreate only the user-approved disposable development database, apply the full migration chain, initialize demo data, verify hierarchy and agreement distributions, repeat initialization for idempotency, and run PostgreSQL-backed regression checks. These actions are for the user or a separately authorized process, not this autonomous run.
20. Worktree path and instructions for reviewing the implementation.

## 15. Acceptance Criteria

The implementation is ready for user review when:

- Dimensions remain the shared classification framework.
- Dimension names and descriptions support localization.
- The Dimensions administration page contains **Bruk i Avtaler**.
- Agreements availability and required settings work.
- Agreements support generic dimension assignments.
- Single/multiple and leaf-only selection rules work.
- `GEO` and `DEPT` are generated as ordinary dimensions.
- Both are active, single-select and permit parent-node selection.
- Both are available but optional in Agreements.
- Neither is automatically enabled in Leasing.
- The complete existing Geography demo hierarchy is generated as dimension values.
- The complete existing Organization Structure demo hierarchy is generated as dimension values.
- Hierarchy levels, names and parent-child relationships are preserved.
- Approximately 300 demo agreements are generated.
- Every demo agreement receives the corresponding `GEO` and `DEPT` assignments.
- Existing deterministic assignment distributions are preserved.
- Demo-data initialization is idempotent.
- Legacy classification generation is completely removed.
- Legacy domain models, services, UI, mappings and current database structures are removed.
- Actual Organization entities and unrelated geographic data remain intact.
- Dashboard grouping dynamically includes available dimensions.
- Hierarchical roll-up and drill-down work.
- Agreements are counted once per hierarchy node.
- Parent-only assignments remain visible during drill-down.
- Percentages use the displayed sibling-level counts.
- Existing Leasing functionality remains unchanged.
- Relevant trusted automated verification passes.
- The final report clearly identifies all deferred database-specific acceptance criteria as `NOT PERFORMED — POST_RUN_VERIFICATION`, with manual follow-up steps.
- New migration source files compile and the specifically authorized current snapshot is updated; historical migrations remain byte-for-byte unchanged.
- No database connections, SQL execution, migration application, database resets, deployment or security-architecture changes occur during autonomous execution.

## 16. Explicit Non-Goals

The following are outside scope:

- Preserving records currently stored in the disposable development database.
- Migrating legacy classification data from that database.
- Automatically deleting or recreating the application database.
- Replacing actual Organization business entities.
- Removing address or postal information.
- Changing Leasing cost-allocation semantics.
- Introducing financial allocation across dimensions.
- Changing agreement lifecycle or approval workflows.
- Introducing a new authorization model.
- Introducing a second classification framework.
- Automatically merging or deploying the completed implementation.

---

## 17. Final Implementation Principle

**The new implementation must generate the same comprehensive logical demo dataset as before, but entirely through the shared Dimensions framework.**

The existing Geography and Organization Structure generators are the reference for the new dimension hierarchies.

The existing agreement generator is the reference for the approximately 300 agreements and their deterministic classification assignments.

Do not simplify the demo dataset.

Do not randomly redistribute agreement assignments.

Do not retain parallel legacy classification implementations.

Do not migrate disposable database records.

Do not access the existing application database.

Complete the source-only implementation autonomously under the reviewed `ARCHITECTURE` and `migrationSourceAuthoring` grants, verify everything permitted by the trusted environment, and explicitly defer database recreation and PostgreSQL checks to `POST_RUN_VERIFICATION` for the user.
