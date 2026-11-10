# Feature Specification: Unified Dimensions Across TenantPlatform

**Project:** TenantPlatform  
**Feature:** Unified dimension-based classification  
**Target:** Agreements, Leasing, Dashboard, Demo Data and Shared Dimensions  
**Execution:** AgentOrchestrator 0.5 – Fully Autonomous  
**Scope:** Domain, Application, Infrastructure, Web, database migrations, localization, demo data and tests

---

## 1. Objective

Replace the existing dedicated Geography and Organization Structure classification models with the shared Dimensions framework across TenantPlatform.

Dimensions shall become the common mechanism for configurable, hierarchical classification in Agreements and Leasing, and shall be reusable by other modules in the future.

The implementation must:

1. Extend Dimensions with multilingual names and descriptions.
2. Introduce module-specific dimension configuration for Agreements.
3. Support dimension assignments on agreements.
4. Replace all existing Geography and Organization Structure classifications with dimensions.
5. Support hierarchical aggregation and drill-down in agreement dashboard analytics.
6. Update demo-data generation, including approximately 300 demo agreements.
7. Migrate existing persisted data and remove obsolete models, relationships, services and database tables.
8. Preserve existing Leasing functionality and unrelated business behavior.
9. Implement and automatically verify the entire feature as one autonomous development task.

The task must not require intermediate human approvals.

The user will review and test the complete integrated result after execution.

## 2. Architectural Principles

### 2.1 Shared dimension framework

Dimensions are account-scoped, reusable classification definitions.

Each dimension contains:

- A stable, language-independent code.
- Localized names and descriptions.
- An active/inactive state.
- Configuration controlling single or multiple selection.
- Configuration controlling whether non-leaf nodes can be selected.
- A hierarchy of dimension values.

Dimension values may have arbitrary hierarchy depth, subject to existing system constraints.

Reuse the existing Dimensions implementation rather than introducing a parallel classification framework.

### 2.2 Separation of configuration responsibilities

Dimension-level configuration determines how values can be selected.

Module-level configuration determines where dimensions are available and whether selection is mandatory.

| Configuration | Scope |
|---|---|
| Multiple values allowed | Dimension |
| Leaf-only selection | Dimension |
| Active/inactive | Dimension |
| Available in Leasing | Leasing |
| Required at registration in Leasing | Leasing |
| Allowed in cost allocation | Leasing |
| Available in Agreements | Agreements |
| Required at registration in Agreements | Agreements |

Do not duplicate dimension-level selection rules in module-specific configuration.

### 2.3 Preserve real business entities

The Organizations domain entity represents actual business organizations, legal entities and counterparties.

These entities must remain.

The legacy Organization Structure classification, including Organization Elements, shall be replaced by dimensions.

Similarly, legacy Geography classification entities shall be replaced by dimensions.

Do not remove address fields, postal addresses, country information or other geographic properties that serve purposes beyond classification.

### 2.4 Tenant isolation and authorization

All dimension definitions, values, configurations and assignments must remain account-scoped.

Preserve existing Application-layer authorization and tenant-isolation patterns.

Do not implement security decisions exclusively in Razor components.

All reads and writes must enforce account boundaries.

## 3. Dimension Definition and Selection Rules

Preserve the existing dimension-level settings:

- Allow multiple selected values.
- Require selection of leaf nodes only.
- Active/inactive status.
- Existing hierarchy and value management.

These settings must apply consistently in Agreements and Leasing.

### 3.1 Single selection

If multiple selection is disabled, an agreement may have at most one explicitly assigned value from that dimension.

### 3.2 Multiple selection

If multiple selection is enabled, an agreement may have multiple distinct explicitly assigned values from that dimension.

Duplicate assignments of the same value are not permitted.

### 3.3 Leaf-node restriction

If leaf-only selection is enabled, users may select only values without children.

If leaf-only selection is disabled, users may select both parent and leaf values.

### 3.4 Hierarchical membership

Selecting a dimension value implies membership in that value and all its ancestors.

For example:

Geografi:

- Norge
  - Østfold
    - Moss
    - Fredrikstad

An agreement assigned to Moss belongs to:

- Moss
- Østfold
- Norge

The agreement's explicit assignment remains Moss. Ancestor membership is derived from the hierarchy.

Do not persist redundant ancestor assignments merely to support reporting.

## 4. Dimension Configuration for Agreements

Add a new tab to the existing Dimensions administration page:

**Bruk i Avtaler**

The tab shall follow the existing visual and interaction patterns of **Bruk i Leasing**.

It shall contain two settings:

| Norwegian label | Description |
|---|---|
| Tilgjengelig | The dimension can be used in Agreements |
| Obligatorisk ved registrering | A value must be selected when registering an agreement |

### 4.1 Configuration behavior

1. Required implies available.
2. Disabling availability must also disable required.
3. Unavailable dimensions must not be offered for new agreement assignments.
4. Required dimensions must be validated server-side during agreement registration.
5. Existing assignments must remain intact if a dimension later becomes unavailable.
6. Editing an existing agreement must not silently remove assignments to unavailable dimensions.
7. Changes to configuration must be persisted and audited using established conventions.
8. The configuration must be account-scoped.

Do not introduce a separate multi-selection setting for Agreements.

The existing dimension-level setting controls selection behavior.

### 4.2 Existing agreements

Making a dimension required must not invalidate or modify existing agreements automatically.

Required validation applies when registering new agreements.

When editing existing agreements, preserve existing assignments and avoid introducing unrelated validation failures. If an existing agreement lacks a newly required dimension, require it when the user explicitly changes that dimension's assignments, but do not block unrelated edits solely because the historical agreement lacks it.

## 5. Multilingual Dimension Names

Extend the Dimensions framework to support localized dimension names and descriptions.

### 5.1 Requirements

1. Dimension codes remain stable and language-independent.
2. Dimension names and descriptions support multiple languages.
3. Support at least `nb-NO` and `en-US`.
4. Reuse the existing localization and translation architecture.
5. The Dimensions administration UI must support editing translations.
6. Agreement forms, Leasing screens, dimension selectors and dashboards must display localized dimension names.
7. Existing dimensions must retain their names through migration.
8. Missing translations must use a deterministic fallback.
9. Changing language must not change dimension identifiers, codes or relationships.

Use the existing dimension-value localization approach where applicable.

### 5.2 Fallback

Preferred fallback order:

1. Requested UI language.
2. Account default language, if available.
3. Existing/default stored name.
4. Stable dimension code.

Do not display raw localization keys such as `error` or missing-resource identifiers as user-facing text.

### 5.3 Existing data migration

Migrate existing dimension names and descriptions into the translation model without data loss.

Preserve the original names and descriptions as fallback values if required for compatibility.

## 6. Agreement Dimension Assignments

Extend the Agreements domain and application services to support assignments to dimensions.

### 6.1 Functional requirements

1. An agreement may reference multiple dimensions.
2. Each dimension may have one or multiple explicitly assigned values according to its configuration.
3. Assignments must reference valid dimension values.
4. Values must belong to the correct dimension and account.
5. Leaf-only restrictions must be enforced.
6. Single-selection restrictions must be enforced.
7. Required dimensions must be validated during agreement registration.
8. Existing assignments must be editable.
9. Assignments must be persisted using stable IDs.
10. Assignment changes must preserve all unrelated agreement properties.
11. Duplicate assignments must be prevented.
12. Authorization must follow existing Application-layer patterns.

### 6.2 Agreement UI

Add a dimension section to the existing agreement creation and editing workflows.

The UI shall dynamically display dimensions configured as available in Agreements.

For each dimension:

- Display its localized name.
- Indicate whether it is required.
- Display values hierarchically.
- Use single-select or multi-select controls as configured.
- Allow parent selection when permitted.
- Preserve selected values during validation failures.
- Display validation errors consistently with existing UI patterns.

The implementation must work for both creating and editing agreements.

### 6.3 Remove legacy selectors

Remove the dedicated Geography and Organization Structure selectors from agreement forms.

Replace them with the generic dimension selectors.

Do not retain parallel legacy classification fields.

Preserve agreement counterparties, organization references and all other unrelated agreement fields.

## 7. Complete Replacement of Legacy Classification Models

The legacy Geography and Organization Structure classification models shall be fully replaced by Dimensions throughout TenantPlatform.

This includes all active consumers, not only Agreements.

### 7.1 Dependency inventory

Before implementation, inspect the entire solution for references to:

- Geographic areas.
- Organization elements.
- Related entity IDs and foreign keys.
- Domain models.
- Application services.
- Infrastructure repositories and EF Core mappings.
- Agreement and Leasing workflows.
- Dashboard queries and analytics.
- Demo-data generators.
- Navigation and administration pages.
- API endpoints and contracts.
- Tests and documentation.

Do not assume these models are used only by Agreements and Leasing.

### 7.2 Migration

For each legacy classification:

1. Create or identify the corresponding dimension.
2. Create dimension values corresponding to legacy records.
3. Preserve parent-child relationships.
4. Preserve account ownership.
5. Establish deterministic mappings from legacy IDs to dimension-value IDs.
6. Migrate all existing references and assignments.
7. Verify that no relationships are lost.
8. Update all application consumers to use Dimensions.
9. Remove obsolete application code and database structures.

Migration must be safe for existing non-demo data.

### 7.3 Removal of obsolete structures

After migration and verification, remove obsolete:

- Geography and Organization Structure domain entities.
- Application services and contracts.
- Infrastructure repositories and mappings.
- Dedicated UI and navigation entries.
- Foreign keys and classification columns.
- Database tables and indexes.
- Demo-data generators for legacy structures.
- Tests that only validate removed behavior.

Replace relevant tests with tests covering the new Dimensions implementation.

Use proper EF Core migrations.

Do not leave unused parallel classification models in the solution.

### 7.4 Data safety

Before dropping legacy tables or columns, verify that all relevant records and relationships have been migrated.

The migration must be transactional where supported and fail safely if required mappings or integrity checks cannot be completed.

Do not silently discard data.

If a legacy dependency cannot be migrated without changing unrelated business semantics, stop the autonomous run and report the blocker instead of deleting data.

Preserve actual Organization entities and unrelated geographic/address data.

## 8. Demo Dimensions

Replace legacy demo Geography and Organization Structure generation with two ordinary dimensions.

Create these for each relevant demo account.

### 8.1 Geografi

| Property | Value |
|---|---|
| Code | GEO |
| Norwegian name | Geografi |
| English name | Geography |
| Multiple selection | No |
| Leaf-only selection | No |
| Active | Yes |
| Available in Agreements | Yes |
| Required in Agreements | No |

### 8.2 Avdeling

| Property | Value |
|---|---|
| Code | DEPT |
| Norwegian name | Avdeling |
| English name | Department |
| Multiple selection | No |
| Leaf-only selection | No |
| Active | Yes |
| Available in Agreements | Yes |
| Required in Agreements | No |

Neither dimension shall automatically be enabled in Leasing.

Existing Leasing dimension configuration must remain unchanged.

### 8.3 Dimension values

Reuse the hierarchies previously generated for Geography and Organization Elements.

Preserve:

- Names.
- Hierarchical relationships.
- Hierarchy depth.
- Relative distribution.
- Stable identifiers or deterministic mappings where practical.

All generated values must belong to the correct account and dimension.

The demo-data initialization must be idempotent.

Repeated application startup must not create duplicate dimensions, values or assignments.

## 9. Demo Agreements

Update the existing generator for approximately 300 demo agreements.

### Requirements

1. Every generated demo agreement shall have one Geografi assignment.
2. Every generated demo agreement shall have one Avdeling assignment.
3. Assignments must reference valid dimension values.
4. Parent nodes may be selected.
5. Preserve existing agreement types, counterparties, statuses, dates, owners and other properties.
6. Preserve existing geographic and organizational distributions as closely as possible.
7. Update existing demo agreements in place where feasible.
8. Do not create duplicate assignments during repeated initialization.
9. Do not delete and recreate existing demo agreements unnecessarily.

The dimensions are optional for normal user registration, even though all demo agreements receive values.

## 10. Agreement Dashboard

Update the agreement distribution widget to support both fixed agreement properties and dynamic dimensions.

### 10.1 Fixed grouping options

Preserve:

- Avtaletype
- Avtalepartner
- Status
- Avtaleansvarlig

### 10.2 Dynamic grouping options

Include all active dimensions configured as available in Agreements.

These must be discovered dynamically from configuration.

Use localized dimension names.

Geografi and Avdeling must appear as ordinary dimensions.

Remove dedicated Geography and Organization Structure grouping implementations.

### 10.3 Hierarchical aggregation

When grouping agreements by a hierarchical dimension, each agreement contributes to:

- Every explicitly assigned dimension value.
- Every ancestor of each explicitly assigned value.

Example:

An agreement assigned to Moss contributes to Moss, Østfold and Norge.

An agreement assigned to both Moss and Fredrikstad contributes to:

- Moss: 1
- Fredrikstad: 1
- Østfold: 1
- Norge: 1

**Within any individual node, an agreement must be counted at most once**, even if several selected values share the same ancestor.

This applies to dimensions allowing multiple selections.

Do not create redundant persisted ancestor assignments.

### 10.4 Hierarchical drill-down

The dashboard must support hierarchical navigation.

For example:

1. Display top-level geographic categories.
2. Expand or drill into Norge.
3. Display regions such as Østfold.
4. Expand Østfold.
5. Display municipalities such as Moss and Fredrikstad.

Support navigating back to higher levels.

Preserve existing dashboard filters.

The selected grouping and drill-down context must remain consistent when filters change.

### 10.5 Counting and percentages

Distinguish between:

- Unique agreements.
- Agreement occurrences within a grouping.
- Hierarchically aggregated node counts.

Rules:

1. Each node counts distinct agreements belonging to that node.
2. An agreement may appear in multiple sibling groups.
3. The sum of sibling counts may exceed the number of unique agreements.
4. Percentages use the sum of occurrences in the currently displayed sibling groups as denominator.
5. Overall agreement totals count unique agreements.
6. Do not sum counts from different hierarchy levels into one denominator.
7. Display a short explanation when multiple selection can cause overlapping counts.

If an agreement is explicitly assigned to a parent node, it contributes to that parent and its ancestors, but not automatically to its descendants.

### 10.6 Agreements without dimension assignments

Agreements without a selected value for the grouping dimension shall be shown in a separate **Ikke angitt** category.

This category must not be included under any hierarchy node.

Its count must represent distinct agreements.

### 10.7 Financial aggregation

Do not introduce financial amount allocation or distribution logic as part of this feature.

The requested hierarchical aggregation concerns agreement counts only.

## 11. Database Migrations

Implement the necessary EF Core migrations for:

- Dimension translations.
- Agreement dimension configuration.
- Agreement dimension assignments.
- Legacy classification data conversion.
- Removal of obsolete Geography and Organization Structure structures.
- Required indexes, foreign keys and constraints.

### Requirements

1. Preserve existing dimension data.
2. Preserve existing agreements.
3. Preserve account isolation.
4. Preserve hierarchy relationships.
5. Preserve migrated classifications.
6. Avoid duplicate assignments.
7. Validate referential integrity.
8. Make migrations safe for existing installations.
9. Do not depend on demo-data initialization to migrate production data.
10. Do not drop legacy structures before successful conversion and validation.

Where a safe migration cannot be completed, fail with a clear diagnostic rather than silently losing data.

## 12. Automated Verification

Implement and run automated tests covering the complete feature.

### 12.1 Dimension configuration

- Available and required settings.
- Required implies available.
- Independent Agreements and Leasing configuration.
- Persistence.
- Authorization and tenant isolation.

### 12.2 Agreement assignments

- Single selection.
- Multiple selection.
- Duplicate prevention.
- Parent-node selection.
- Leaf-only restrictions.
- Required-dimension validation.
- Cross-account rejection.
- Editing existing assignments.
- Preservation of unrelated agreement fields.

### 12.3 Hierarchical aggregation

- Single leaf assignment rolls up to every ancestor.
- Parent assignment rolls up only to ancestors.
- Multiple assignments sharing an ancestor count once at that ancestor.
- Multiple sibling assignments count once in each sibling.
- Hierarchical drill-down.
- Percentage calculations.
- Unassigned agreements.
- Filtered dashboard results.

### 12.4 Localization

- Norwegian names.
- English names.
- Missing translation fallback.
- Migration of existing names.
- Consistent display across modules.

### 12.5 Demo data

- Geografi and Avdeling created correctly.
- Hierarchies preserved.
- Correct dimension configuration.
- Approximately 300 agreements assigned both dimensions.
- Repeated initialization is idempotent.

### 12.6 Legacy migration

- Existing legacy records are converted.
- Hierarchical relationships are preserved.
- Existing agreement references are migrated.
- Other discovered consumers are migrated.
- No unmapped legacy references remain.
- Obsolete tables and columns are removed only after successful conversion.
- Migration fails safely on inconsistent data.

### 12.7 Regression

- Existing Leasing functionality.
- Agreement creation and editing.
- Agreement dashboard.
- Existing authorization.
- Tenant isolation.
- Relevant API and Application contracts.

Build the complete solution and execute all relevant trusted tests.

A successful build alone is not sufficient verification.

## 13. Autonomous Execution

AgentOrchestrator 0.5 shall execute the entire specification as one autonomous development task.

### Requirements

1. No intermediate human approvals.
2. Automatically plan and execute the implementation.
3. Automatically approve decisions within the trusted scope.
4. Record every automatically approved decision and its rationale.
5. Run verification after implementation steps.
6. Attempt bounded automatic repair when builds or tests fail.
7. Re-run verification after repairs.
8. Preserve all trusted path, command and execution restrictions.
9. Do not request expanded permissions during execution.
10. Do not automatically commit, merge, deploy or publish.
11. Keep changes isolated in a managed Git worktree.
12. Produce one consolidated final report.

Internal implementation steps may be used for planning, checkpointing and verification, but must not require user interaction.

### Completion behavior

The run shall finish with one of two outcomes:

**COMPLETED:** All requirements implemented and configured automated verification passed.

**FAILED:** A blocking issue prevented complete implementation. The final report must explain the blocker, attempted resolutions and remaining work.

Do not report a partially implemented feature as completed.

If the task cannot be implemented within trusted limits, fail clearly rather than waiting for approval.

## 14. Final Report

Provide a consolidated report containing:

1. Implementation summary.
2. Architectural changes.
3. Database migration details.
4. Legacy model removal summary.
5. Agreement UI changes.
6. Leasing compatibility.
7. Dashboard changes.
8. Localization implementation.
9. Demo-data changes.
10. Test results.
11. Automatically approved decisions.
12. Rejected or constrained actions.
13. Automatic repair attempts.
14. Remaining limitations.
15. Manual end-to-end verification checklist.
16. Worktree path and instructions for running the completed feature.

The user shall only need to review and test the integrated result after the autonomous run finishes.

## 15. Acceptance Criteria

The feature is accepted when all the following are true:

- Dimensions are the shared classification mechanism across Agreements and Leasing.
- Dimension names and descriptions support localization.
- The Dimensions page includes **Bruk i Avtaler**.
- Agreement dimension availability and required settings work correctly.
- Agreements support dimension assignments using existing selection rules.
- Hierarchical parent and leaf selections work.
- Geografi and Avdeling exist as ordinary dimensions.
- Both are single-select and permit non-leaf selection.
- Both are available but optional in Agreements.
- Existing Geography and Organization Structure classifications have been migrated.
- Obsolete classification models, services, UI and database tables have been removed.
- Actual Organization business entities remain intact.
- The approximately 300 demo agreements have both dimension assignments.
- The dashboard dynamically lists available agreement dimensions.
- Hierarchical roll-up and drill-down work.
- Agreements are counted once per hierarchy node.
- Multiple selection can contribute to multiple sibling categories.
- Dashboard percentages use the currently displayed grouping level.
- Existing Leasing functionality remains unchanged.
- Existing agreements and other business data are preserved.
- Database migrations are safe and verified.
- Relevant automated tests pass.
- The entire development task completes without intermediate human involvement.

---

## 16. Explicit Non-Goals

The following are outside the scope of this feature:

- Replacing actual Organization business entities.
- Removing address or postal information.
- Changing Leasing cost-allocation rules.
- Introducing financial allocation across dimensions.
- Changing agreement lifecycle or approval workflows.
- Introducing new permission models.
- Automatically merging or deploying the completed implementation.
- Creating a second, parallel dimension-classification framework.

The implementation shall focus on consolidating classification functionality into the existing Dimensions framework while preserving all unrelated business behavior.
