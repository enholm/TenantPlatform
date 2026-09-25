# Leasing phase 1

Run from the repository root:

```sh
LEASING_TEST_CONNECTION='Host=localhost;Username=...;Database=tenant_leasing_tests' dotnet run --project tests/TenantPlatform.Leasing.SmokeTests
```

The runner refuses any database name other than `tenant_leasing_tests`. It creates an isolated schema, applies the complete migration chain, seeds test-only records, checks the real PostgreSQL locking behaviour, and removes its own schema in `finally`. It never targets the application database. Document storage is a test double; document metadata and access checks use PostgreSQL. Component checks render Blazor markup without a browser.

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
- History exposes timestamp, actor, reason, action and before/after snapshots. No schedules, interest lookup, imports, reservations, credits, asset serials or cost allocation are included.
