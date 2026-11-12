Run `dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests` from the repository root.

These checks use an isolated EF in-memory database, fixed time and rendered Blazor components. They do not connect to the application database or apply migrations. PostgreSQL translation of the priority query is checked without opening a connection.

Coverage includes inclusive day 0/90 boundaries, day 91 exclusion, independent dates, status/archive eligibility, expiry/notice priority, title tie-breaking, five-item limit, timezone/DST, account and agreement access, four fixed groupings, dynamic account-scoped dimension catalog, unique ancestor roll-up, overlapping siblings/direct assignments, historical values, four-level drill/back/leaf navigation, authorized context refresh, stale asynchronous responses, localization, full 300-agreement GEO/DEPT production-demo comparison against the frozen reference, filtered overview counts, labels and numeric legend, and each widget's loading/error/retry/empty states.

Dashboard scope: upcoming deadlines use active agreements, matching reminder eligibility. Expired means an active or expired agreement with EndDate before the account's current calendar date. Draft, terminated and archived agreements do not require attention. Insights include all accessible non-archived agreements, regardless of status.

Optional: set `DASHBOARD_PREVIEW_PATH` to export rendered test-fixture markup for a visual check using the application's Bootstrap and app.css files. Browser layout and keyboard interaction still need a browser; the component checks are not a browser end-to-end test.

The component checks render Razor and call actual handler methods via reflection on the renderer dispatcher; they do not dispatch browser DOM events. The new hierarchy aggregator runs in production code, and service authorization/projections run against EF InMemory. These checks do not prove PostgreSQL query execution or relational constraints.
