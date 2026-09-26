# Leasing phases 1 and 2

Run from the repository root:

```sh
LEASING_TEST_CONNECTION='Host=localhost;Username=...;Database=tenant_leasing_tests' dotnet run --project tests/TenantPlatform.Leasing.SmokeTests
```

The runner refuses any database name other than `tenant_leasing_tests`. It creates an isolated schema, applies the complete migration chain, seeds test-only records, checks the real PostgreSQL locking behaviour, and removes its own schema in `finally`. It never targets the application database. All database operations use the production `AuditSaveChangesInterceptor`, including full composite-key auditing for allocations. Document storage is a test double; document metadata and access checks use PostgreSQL. Component checks render Blazor markup without a browser.

Coverage: five-year anniversaries, month end/leap years, inclusive acquisition windows and backdated registration, exact/over-limit purchases, concurrent registrations, edits and stable line IDs, stale revisions, cancellation, moves across frameworks, VAT-inclusive/exclusive limits, standalone leases, copied terms, closed frameworks, negative lines, forged line IDs, tenant and owner authorization, documents, history, and rendered editor/detail components.

## Implementation choices

- Account administrators can create/manage all leases in their account. A framework owner manages its acquisitions; an acquisition owner manages that acquisition. Platform admin status alone grants no account access. Parties and responsible users must belong to the selected account. There is no separate leasing sharing model in phase 1.
- All mutations serialize on the account row inside a PostgreSQL transaction, matching the existing financial agreement locking order. The limit is recalculated from registered acquisition purchase totals. No cached capacity counter or repayments are used.
- Start is the purchase date. End is `DateOnly.AddMonths`, clamped to the last day of the target month if necessary, without subtracting a day. Durations are 1–600 whole months.
- Quantity, price and VAT rate use decimals (at most four fractional digits). Net line amounts and line VAT are rounded to two decimals, midpoint away from zero, then summed. Financed amount and limit have two decimals. Phase 1 accepts the same currencies as the existing agreement module.
- New acquisitions copy framework terms through the server's default-generation operation. Changes to framework defaults do not update saved acquisitions. A registered acquisition cannot revert to draft; cancellation is terminal and retains all data and before/after history.
- Manual Closed/Finished status blocks new registered acquisitions. Expiration of the acquisition window alone does not prevent late registration of a purchase whose purchase date is within the window. Existing leases continue independently.
- Framework edits cannot conflict with registered purchase dates, currency or finance company. VAT basis is locked while registered acquisitions exist. Limit reductions cannot go below used capacity.
- Financial/history changes and file metadata are committed atomically. Uploads reuse existing validated agreement storage and file limits. There is no permanent-delete endpoint.
- History exposes timestamp, actor, reason, action and before/after snapshots. No schedules, interest lookup, imports, reservations, credits or asset serials are included.

## Phase 2

The same runner verifies upgrading a populated phase-one schema, dimension uniqueness and hierarchy constraints, leaf/single/multiple selection, required dimensions and legacy warnings, all three allocation modes, decimal quantities and exact net/VAT reconciliation, combinations of dimensions, price/quantity changes, snapshot preservation, explicit reclassification, allocation removal, forged row IDs, access control and cross-tenant references. It also renders the shared register and classification editor. Register checks switch between dimensions and edit forms, add a fourth hierarchy level through the page actions, exclude descendants from parent choices, and move a subtree to the root.

- Shared definitions and their history live under `Dimensions`; Leasing-specific availability/required/allocation rules remain in Leasing. Admins maintain the shared register; authorized acquisition owners classify lines.
- Codes are normalized to uppercase (1–50 ASCII letters, digits, period, hyphen or underscore). Hierarchies have no fixed depth limit. Full paths are derived iteratively; selected values retain stable references plus name/code/path snapshots.
- Common selections are copied to the other lines, preserving each target line's varying dimensions and allocation inputs. Explicit edits validate current rules. Unrelated changes preserve historical selections, including inactive ones and legacy missing requirements.
- One ordered allocation applies to each item. Net amounts use two decimals; percentages/quantities use four. Cumulative rounding differences reconcile net, VAT and quantity exactly. Zero-net amount allocations display no derived percentage or quantity. Incomplete drafts retain inputs without fabricated calculated amounts.
- The new migration is additive, defaults existing lines to unsplit allocation, and creates no fictional classifications. Tests use only disposable schemas; application database migrations are not applied by the test runner.
