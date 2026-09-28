# Leasing phase 4: orders and reservations

Purchase orders are internal planned purchases under a framework. `/leasing/purchase-orders` is separate from `/leasing/orders`, which continues to list standalone leasing agreements. The framework details page links to its orders and documents proposed limit changes.

## Financial rules

Available capacity is the framework limit minus registered acquisition usage (including the existing explicit credit-note release policy) minus the remaining reservations on approved orders. All financial writes use the existing account-row PostgreSQL lock inside a transaction. Order consumption, acquisition registration, invoice effects and the final capacity check commit together or roll back together.

Each order line declares quantity or amount-share fulfilment. Amount shares range from zero to one, with four-decimal precision. The final delivery consumes the last reserved cent through cumulative rounding. Realisation records retain the approved net/VAT released for that portion and the actual acquisition line amounts. Price changes affect remaining portions only; earlier realisation snapshots remain intact. Quantity-based deliveries must have the same acquisition quantity as the explicitly selected scope. Overdelivery requires an approved amendment first.

An acquisition belongs to at most one order. Each acquisition item can realise one order line; split an acquisition item when separate order-line links are needed. An order line can be realised by many acquisition items and acquisitions. Imported invoice lines that match existing items only document the acquisition; they cannot consume an order again. Order invoice totals count only invoice lines for the linked acquisition items, including approved credit notes with their signs.

Approved orders retain their current reservation while a proposal awaits approval. Approval revalidates the proposal against current fulfilment under the account lock. Rejection discards the proposal; cancellation and closure retain acquisition/invoice records and release only the remainder. Credit notes never reopen reservations. Reversal marks the affected scope as unreserved; the separate, explicit reopening action requires approval rights, an open acquisition window and sufficient capacity. Failed reopening leaves the unreserved scope visible.

New approvals and reservation reopenings require today's account-local date to be inside the acquisition window. Existing reservations are retained after expiry and marked for review. Registration still validates the actual purchase date, preserving historical in-period registration. Expected delivery and order dates never start the lease.

## Authorization and documents

Framework owners and account administrators can register orders; assigned order owners can edit their orders and register linked purchases. `LeasingOrderApprover` and `LeasingLimitApprover` are distinct account-wide roles, assignable through existing user administration. Account administrators have both rights. These roles grant no platform-wide rights. Approval follows the existing single-action pattern; there is no existing prohibition on self-approval, so an authorised proposer may approve their own proposal.

A limit change records the previous/new amounts, reason, optional document reference, proposer/approver and timestamps. Ordinary framework editing cannot change the limit. Approval rechecks the current limit and current used-plus-reserved amount. Future and backdated changes are not supported.

Order attachments use the existing validated agreement document storage and authenticated leasing download endpoint. Order events and realisation links retain actor and timestamps. Request IDs make successful retries return the original result; revision checks reject conflicting stale operations. Orders have no permanent-delete operation.

Order classifications reuse phase-two dimension choices and allocation validation, stored as proposal/snapshot JSON on each order line. They may be incomplete. Delivery suggestions scale quantity/net allocations to the actual delivery, rounding the final row to the exact total. Registration uses the existing required-dimension checks and stores independent acquisition classification snapshots.

## Database and verification

`AddLeasingOrdersAndReservations` adds orders, lines, realisations, events and limit proposals, and adds an optional order parent to leasing documents. It does not update historical acquisition amounts or create historical orders/reservations. Apply through the normal deployment/startup migration process; implementation verification uses only the disposable PostgreSQL test database.

Run:

```sh
dotnet build TenantPlatform.sln
LEASING_TEST_CONNECTION='Host=localhost;Username=...;Database=tenant_leasing_tests' dotnet run --project tests/TenantPlatform.Leasing.SmokeTests
```

The existing phase 1–3 regression suite also runs. Phase-four checks cover approvals and exact limits, concurrent order/acquisition approvals, partial quantity and amount-share realisation, price variances, full consumption, amendment revalidation, ordinary purchases competing with reservations, existing-acquisition linking, partial invoicing, credit/reversal behaviour, explicit reopening, limit proposals, expiry, owner/approver separation, account isolation, rounding, allocation scaling, documents and Blazor rendering. Migrations run in an isolated schema which the runner removes afterwards.

No supplier dispatch, currency conversion, payment scheduling, automatic reservation expiry, general workflow engine or accounting functionality is introduced.
