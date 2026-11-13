# Step 7 isolated browser harness

Run from the repository root:

```bash
node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs
```

The runner builds/executes the Reports smoke fixture mode, starts an ephemeral loopback-only static server, and launches the installed Google Chrome in headless mode with requested desktop (`1440×1000`) and mobile (`390×844`) window sizes. Chrome reports the exact resulting CSS viewport in the output; on macOS Chrome 157 the verified viewports were `1440×913` and `500×757` (the latter is still below the production `768px` mobile breakpoint). It loads the production `report-charts.js`, `report-grid.js`, local ECharts 5.6.0 and relevant repository CSS directly.

The page uses synthetic report/chart DTOs and layout fixtures produced by the real C# `ReportChartMapper`, `ReportLayoutEngine`, and `ReportWorkspaceState`. Theme handling is exercised by changing the Bootstrap color variables and the observed `data-bs-theme` attribute; the repository's local Bootstrap 5.1 file does not contain the Bootstrap 5.3 color-mode variables loaded by the application shell. It does not start TenantPlatform, connect to a database, or verify Blazor InteractiveServer. Pointer events include mouse and synthetic touch-typed `PointerEvent`s; they are not physical-device tests.

Per-viewport JSON results are written to a temporary directory printed by the runner.
