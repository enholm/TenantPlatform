# Isolated report browser harness

Run from the repository root:

```bash
node tests/TenantPlatform.Reports.SmokeTests/browser/run-browser-harness.mjs
```

The runner builds/executes the Reports smoke fixture mode, starts an ephemeral loopback-only static server, and launches the installed Google Chrome in headless mode with requested desktop (`1440×1000`) and mobile (`390×844`) window sizes. Chrome reports the exact resulting CSS viewport in the output; on macOS Chrome 157 the verified viewports were `1440×913` and `500×757` (the latter is still below the production `768px` mobile breakpoint). It loads the production `report-charts.js`, `report-grid.js`, local ECharts 5.6.0 and relevant repository CSS directly.

The page uses synthetic report/chart DTOs and layout fixtures produced by the real C# `ReportChartMapper`, `ReportLayoutEngine`, and `ReportWorkspaceState`. Theme handling is exercised by changing the Bootstrap color variables and the observed `data-bs-theme` attribute; the repository's local Bootstrap 5.1 file does not contain the Bootstrap 5.3 color-mode variables loaded by the application shell. It does not start TenantPlatform, connect to a database, or verify Blazor InteractiveServer. Pointer events include mouse and synthetic touch-typed `PointerEvent`s; they are not physical-device tests.

The grid checks also exercise the current edit interaction model: one stable-ID selection, selection clearing on empty canvas, double-click editing over passive content, double-click suppression after drag/resize, whole-card drag after a movement threshold, interactive-control exclusions, selected-card-only resize, keyboard selection, cards without embedded action controls, and the shared movable/resettable palette. A real local ECharts canvas is mounted as passive edit-card content; tooltip, legend changes and series events are disabled while card selection, double-click, drag and resize still work over the canvas. A passive tree separately verifies disabled tooltip/pan/zoom/node callbacks. The palette harness covers the ordered edit/duplicate/refresh/delete action row, allowed-page movement, layout actions, stable targeting after DOM reorder, copy selection, cleared selection/focus, Escape/pointercancel/dispose, responsive geometry, scroll handling and synthetic long-press touch drag. The layout actions use the production C# workspace fixtures for their expected layouts, while the harness itself remains a static DOM/JavaScript integration test. It does not prove Blazor rerender/circuit behaviour, editor tab state, service calls, application navigation or persistence.

The presentation checks exercise left, centred, right and hidden title chrome, the accessible name and retained warning in hidden mode, chart resize without a new ECharts instance when title chrome is hidden or restored, and the responsive three-field editor row. KPI, table and relationship-tree component markup is covered by the Reports smoke renderer; those component types are not hosted through a live Blazor circuit in this static harness.

Per-viewport JSON results are written to a temporary directory printed by the runner.
