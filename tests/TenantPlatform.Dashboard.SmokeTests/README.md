Run `dotnet run --project tests/TenantPlatform.Dashboard.SmokeTests` from the repository root.

These checks use an isolated EF in-memory database, fixed time and rendered Blazor components. They do not connect to the application database or apply migrations. PostgreSQL translation of the priority query is checked without opening a connection.

Coverage includes inclusive day 0/90 boundaries, day 91 exclusion, independent dates, status/archive eligibility, expiry/notice priority, title tie-breaking, five-item limit, timezone/DST, account and agreement access, hierarchy grouping and descendants, filtered overview counts, labels and numeric legend, and each widget's loading/error/retry/empty states.

Dashboard scope: upcoming deadlines use active agreements, matching reminder eligibility. Expired means an active or expired agreement with EndDate before the account's current calendar date. Draft, terminated and archived agreements do not require attention. Insights include all accessible non-archived agreements, regardless of status.

Optional: set `DASHBOARD_PREVIEW_PATH` to export rendered test-fixture markup for a visual check using the application's Bootstrap and app.css files. Browser layout and keyboard interaction still need a browser; the component checks are not a browser end-to-end test.
