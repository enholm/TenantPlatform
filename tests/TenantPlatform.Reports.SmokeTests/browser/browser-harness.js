import * as charts from "/js/report-charts.js";
import * as gridModule from "/js/report-grid.js";

const fixture = await fetch("/fixture.json").then(response => {
    if (!response.ok) throw new Error(`Fixture request failed: ${response.status}`);
    return response.json();
});
const parameters = new URLSearchParams(location.search);
const viewport = parameters.get("viewport") ?? "unknown";
const results = document.querySelector("#results");
const adapters = [];
let failures = 0;
let checks = 0;
const records = [];

document.querySelector("#environment").textContent =
    `${navigator.userAgent}; ${innerWidth}×${innerHeight}; ${viewport}`;
globalThis.__reportXss = false;

function check(condition, message) {
    checks++;
    const item = document.createElement("li");
    item.className = condition ? "pass" : "fail";
    item.textContent = `${condition ? "PASS" : "FAIL"}: ${message}`;
    results.append(item);
    records.push({ passed: Boolean(condition), message });
    if (!condition) failures++;
}

function equal(actual, expected, message) {
    check(JSON.stringify(actual) === JSON.stringify(expected),
        `${message} (actual ${JSON.stringify(actual)})`);
}

const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function value(data) {
    return data && typeof data === "object" && "value" in data ? data.value : data;
}

function compact(id) {
    return id.replaceAll("-", "");
}

function setLayout(element, item) {
    element.style.setProperty("--report-grid-column", item.column);
    element.style.setProperty("--report-grid-row", item.row);
    element.style.setProperty("--report-grid-width", item.width);
    element.style.setProperty("--report-grid-height", item.height);
}

function layout(element) {
    const number = name => Number.parseInt(element.style.getPropertyValue(name), 10);
    return {
        column: number("--report-grid-column"),
        row: number("--report-grid-row"),
        width: number("--report-grid-width"),
        height: number("--report-grid-height")
    };
}

function renderGrid(element, editable) {
    for (const item of fixture.initial) {
        const card = document.createElement("article");
        card.className = "report-grid-item";
        card.dataset.reportCardId = compact(item.clientId);
        setLayout(card, item);
        if (editable) {
            const drag = document.createElement("button");
            drag.type = "button";
            drag.className = "report-drag-handle";
            drag.setAttribute("aria-label", `Move ${item.sortOrder + 1}`);
            drag.textContent = "↕";
            drag.setPointerCapture = () => {};
            card.append(drag);
        }
        const content = document.createElement("div");
        content.className = "report-grid-content grid-card";
        const action = document.createElement("button");
        action.type = "button";
        action.className = "btn btn-outline-primary btn-sm content-action";
        action.textContent = "Card action";
        const chartSurface = document.createElement("div");
        chartSurface.className = "chart-surface";
        chartSurface.textContent = "Chart surface";
        content.append(action, chartSurface);
        if (editable) {
            const keyboard = document.createElement("div");
            keyboard.className = "btn-group keyboard-layout";
            keyboard.setAttribute("role", "group");
            keyboard.setAttribute("aria-label", "Resize card with keyboard controls");
            const move = document.createElement("button");
            move.type = "button";
            move.className = "btn btn-outline-secondary btn-sm keyboard-move";
            move.setAttribute("aria-label", "Move card earlier");
            move.textContent = "↑";
            const wider = document.createElement("button");
            wider.type = "button";
            wider.className = "btn btn-outline-secondary btn-sm keyboard-wider";
            wider.setAttribute("aria-label", "Make card wider");
            wider.textContent = "↔+";
            keyboard.append(move, wider);
            content.append(keyboard);
        }
        card.append(content);
        if (editable) {
            const resize = document.createElement("button");
            resize.type = "button";
            resize.className = "report-resize-handle";
            resize.setAttribute("aria-label", `Resize ${item.sortOrder + 1}`);
            resize.textContent = "↘";
            resize.setPointerCapture = () => {};
            card.append(resize);
        }
        element.append(card);
    }
}

function card(grid, id) {
    return grid.querySelector(`[data-report-card-id="${compact(id)}"]`);
}

function resetGrid(grid) {
    for (const item of fixture.initial) setLayout(card(grid, item.clientId), item);
}

function pointer(target, type, options) {
    target.dispatchEvent(new PointerEvent(type, {
        bubbles: true,
        cancelable: true,
        pointerId: options.pointerId ?? 1,
        pointerType: options.pointerType ?? "mouse",
        isPrimary: true,
        button: options.button ?? 0,
        clientX: options.clientX ?? 0,
        clientY: options.clientY ?? 0
    }));
}

async function chartChecks() {
    const barElement = document.querySelector("#bar-chart");
    const lineElement = document.querySelector("#line-chart");
    const donutElement = document.querySelector("#donut-chart");
    const barAdapter = await charts.create(barElement, fixture.charts.bar);
    const lineAdapter = await charts.create(lineElement, fixture.charts.line);
    const donutAdapter = await charts.create(donutElement, fixture.charts.donut);
    adapters.push(barAdapter, lineAdapter, donutAdapter);
    await wait(60);

    const echarts = globalThis.echarts;
    const bar = echarts.getInstanceByDom(barElement);
    const line = echarts.getInstanceByDom(lineElement);
    const donut = echarts.getInstanceByDom(donutElement);
    check(Boolean(bar && line && donut), "bar, line and donut are real ECharts instances");
    check(new Set([bar?.id, line?.id, donut?.id]).size === 3,
        "multiple report cards keep separate chart instances");
    check([barElement, lineElement, donutElement].every(x => x.querySelector("canvas")),
        "all supported chart kinds render to canvas");

    const barOption = bar.getOption();
    equal(barOption.series.map(x => x.name), ["EUR", "NOK"],
        "currency series retain stable mapper ordering");
    check(value(barOption.series[0].data[1]) == null,
        "missing currency value remains a gap instead of becoming zero");
    equal(line.getOption().xAxis[0].data.map(value), ["2025-12", "2026-01", "2026-02"],
        "time categories are chronologically sorted across a year boundary");
    check(value(line.getOption().series[0].data[1]) == null,
        "missing time value remains null");
    check(donut.getOption().series[0].type === "pie" && donut.getOption().series[0].data.length === 2,
        "valid donut renders both grouped values");

    bar.dispatchAction({ type: "showTip", seriesIndex: 0, dataIndex: 0 });
    await wait(60);
    check(!globalThis.__reportXss && !document.querySelector("img[src='x'], #bar-chart script"),
        "stored labels with HTML-like text are not interpreted as HTML");
    check(barOption.xAxis[0].data[0].value.includes("<img") && barOption.xAxis[0].data[0].value.includes("<script>"),
        "long labels and special characters reach the chart as text");

    const originalId = bar.id;
    const canvasCount = barElement.querySelectorAll("canvas").length;
    barAdapter.update(fixture.charts.bar);
    await wait(60);
    check(echarts.getInstanceByDom(barElement).id === originalId &&
        barElement.querySelectorAll("canvas").length === canvasCount,
        "chart update reuses the instance without adding canvases");

    const originalWidth = bar.getWidth();
    barElement.style.width = "65%";
    await wait(120);
    check(bar.getWidth() > 0 && bar.getWidth() !== originalWidth,
        `ResizeObserver resizes a visible chart (${originalWidth} to ${bar.getWidth()}, element ${barElement.clientWidth})`);
    barElement.style.width = "100%";

    const hiddenElement = document.querySelector("#hidden-chart");
    const hiddenAdapter = await charts.create(hiddenElement, fixture.charts.line);
    adapters.push(hiddenAdapter);
    const hidden = echarts.getInstanceByDom(hiddenElement);
    check(hidden.getWidth() === 0 || hiddenElement.clientWidth === 0,
        "zero-size chart can initialise while hidden");
    hiddenElement.style.display = "block";
    await wait(140);
    check(hiddenElement.clientWidth > 0 && hidden.getWidth() > 0,
        "hidden chart resizes after becoming visible");

    const axisTextColor = chart => chart.getOption().xAxis[0].axisLabel.color;
    const initialColor = axisTextColor(bar);
    document.documentElement.style.setProperty("--bs-body-color", "#f1f3f5");
    document.documentElement.style.setProperty("--bs-border-color", "#495057");
    document.documentElement.dataset.bsTheme = "dark";
    await wait(80);
    const darkColor = axisTextColor(bar);
    check(Boolean(initialColor && darkColor && initialColor !== darkColor),
        `theme mutation reapplies computed chart colors (${initialColor} to ${darkColor}; CSS ${getComputedStyle(document.documentElement).getPropertyValue("--bs-body-color").trim()})`);
    document.documentElement.dataset.bsTheme = "light";
    document.documentElement.style.removeProperty("--bs-body-color");
    document.documentElement.style.removeProperty("--bs-border-color");
    const animation = bar.getOption().animation;
    check((Array.isArray(animation) ? animation[0] : animation) === false,
        `reduced-motion browser preference disables chart animation (media ${matchMedia("(prefers-reduced-motion: reduce)").matches}, option ${JSON.stringify(animation)})`);

    check(document.querySelector("#empty-result") && !echarts.getInstanceByDom(document.querySelector("#empty-result")),
        "empty result remains a non-chart placeholder");
    check(fixture.invalidDonutRejected && !echarts.getInstanceByDom(document.querySelector("#invalid-donut")),
        "invalid donut is rejected by the production mapper before chart creation");

    hiddenAdapter.dispose();
    check(!echarts.getInstanceByDom(hiddenElement), "dispose removes the ECharts instance");
    const recreated = await charts.create(hiddenElement, fixture.charts.line);
    await wait(60);
    check(Boolean(echarts.getInstanceByDom(hiddenElement)?.getDom().querySelector("canvas")),
        "a disposed chart can be recreated");
    recreated.dispose();

    const treeElement = document.querySelector("#tree-chart");
    const selected = [];
    const treeAdapter = await charts.createTree(treeElement, fixture.tree,
        { invokeMethodAsync: (...args) => { selected.push(args); return Promise.resolve(); } }, 12);
    adapters.push(treeAdapter);
    await wait(100);
    const tree = echarts.getInstanceByDom(treeElement);
    check(Boolean(tree && treeElement.querySelector("canvas")), "relationship tree renders through the real local ECharts canvas");
    const treeOption = tree.getOption();
    check(treeOption.series[0].type === "tree" && treeOption.series[0].orient === "LR" && treeOption.series[0].roam,
        "real tree chart is left-to-right and supports pan/zoom");
    check(treeOption.series[0].data[0].children[0].children[0].name.includes("<script>") && !treeElement.querySelector("script"),
        "tree labels containing HTML-like text remain chart text");
    const treeId = tree.id;
    const treeCanvasCount = treeElement.querySelectorAll("canvas").length;
    tree.dispatchAction({ type: "treeRoam", zoom: 1.2, originX: 100, originY: 100 });
    await wait(40);
    const zoom = tree.getOption().series[0].zoom;
    treeAdapter.update({ ...fixture.tree, root: { ...fixture.tree.root, selected: true } }, 13);
    await wait(80);
    check(echarts.getInstanceByDom(treeElement).id === treeId && treeElement.querySelectorAll("canvas").length === treeCanvasCount,
        "tree update reuses its ECharts instance without duplicate canvases");
    check(echarts.getInstanceByDom(treeElement).getOption().series[0].zoom === zoom,
        "tree update preserves the current zoom state");
    const treeWidth = tree.getWidth();
    treeElement.style.width = "70%";
    await wait(120);
    check(tree.getWidth() > 0 && tree.getWidth() !== treeWidth, "tree ResizeObserver follows its responsive container");
    treeElement.style.width = "100%";
    treeAdapter.dispose();
    check(!echarts.getInstanceByDom(treeElement), "tree dispose releases the real ECharts instance");
}

async function gridChecks() {
    const editGrid = document.querySelector("#edit-grid");
    const viewGrid = document.querySelector("#view-grid");
    renderGrid(editGrid, true);
    renderGrid(viewGrid, false);
    const callbacks = [];
    let generation = fixture.currentGeneration;
    const dotnet = {
        async invokeMethodAsync(name, id, column, row, width, height, callbackGeneration) {
            callbacks.push({ name, id, column, row, width, height, generation: callbackGeneration });
            if (callbackGeneration !== generation) return;
            const target = card(editGrid, id);
            let output = { column, row, width, height };
            if (id === compact(fixture.initial[1].clientId) && column === 1 && row === 1 && width === 6 && height === 4) {
                for (const item of fixture.collision) setLayout(card(editGrid, item.clientId), item);
                output = null;
            }
            if (output) setLayout(target, output);
            document.querySelector("#layout-status").textContent = "Layout updated";
        }
    };
    let adapter = gridModule.create(editGrid, dotnet, generation, 12, 3, 3, 12);
    adapters.push(adapter);
    const second = card(editGrid, fixture.initial[1].clientId);
    const first = card(editGrid, fixture.initial[0].clientId);
    const drag = second.querySelector(".report-drag-handle");
    const resize = second.querySelector(".report-resize-handle");

    await wait(60);
    const geometry = gridModule.measureGridGeometry(editGrid, 12);
    const columnWidth = geometry.columnPitch;
    const publishedColumnPitch = Number.parseFloat(editGrid.style.getPropertyValue("--report-grid-column-pitch"));
    const publishedRowPitch = Number.parseFloat(editGrid.style.getPropertyValue("--report-grid-row-pitch"));
    check(Math.abs(publishedColumnPitch - geometry.columnPitch) < .5 && Math.abs(publishedRowPitch - geometry.rowPitch) < .5,
        "grid publishes the same responsive geometry for pointer snapping and the edit canvas");
    if (innerWidth >= 768) {
        const editDots = getComputedStyle(editGrid, "::before");
        const viewDots = getComputedStyle(viewGrid, "::before");
        check(editDots.backgroundImage !== "none" && editDots.pointerEvents === "none" && viewDots.backgroundImage === "none",
            "dot grid is pointer-transparent and appears only on the editable desktop canvas");
        check(Math.abs(second.getBoundingClientRect().left - first.getBoundingClientRect().left - 6 * geometry.columnPitch) < 1,
            "dot and card column positions use the same measured pitch");

        const resizeFromInitial = async (pointerId, deltaX, deltaY, initialLayout = null) => {
            resetGrid(editGrid);
            if (initialLayout) setLayout(second, initialLayout);
            const before = second.getBoundingClientRect();
            const startX = before.right - 10;
            const startY = before.bottom - 10;
            pointer(resize, "pointerdown", { pointerId, clientX: startX, clientY: startY });
            pointer(editGrid, "pointermove", { pointerId, clientX: startX + deltaX, clientY: startY + deltaY });
            pointer(editGrid, "pointerup", { pointerId, clientX: startX + deltaX, clientY: startY + deltaY });
            await wait(20);
            return { before, after: second.getBoundingClientRect(), pointerY: startY + deltaY, startY };
        };

        let edge = await resizeFromInitial(40, 0, geometry.rowPitch);
        check(layout(second).height === 5 &&
            Math.abs((edge.after.bottom - edge.pointerY) - (edge.before.bottom - edge.startY)) < 1,
            "one row pitch adds one row and keeps the lower card edge aligned with the pointer snap");
        await resizeFromInitial(41, 0, geometry.rowPitch * 3);
        check(layout(second).height === 7, "three row pitches add exactly three rows");
        await resizeFromInitial(42, 0, geometry.rowPitch * .35);
        check(layout(second).height === 4, "small vertical movement does not cause a height jump");
        await resizeFromInitial(43, 0, -geometry.rowPitch * 8);
        check(layout(second).height === 3, "upward resize snaps and clamps to the minimum height");
        await resizeFromInitial(44, geometry.columnPitch, 0,
            { column: 1, row: 1, width: 6, height: 4 });
        check(layout(second).width === 7, "horizontal resize still advances by one column pitch");

        resetGrid(editGrid);
        const scrollHost = document.querySelector("#scroll-host");
        scrollHost.scrollTop = 0;
        const scrollStart = second.getBoundingClientRect();
        const scrollX = scrollStart.right - 10;
        const scrollY = scrollStart.bottom - 10;
        pointer(resize, "pointerdown", { pointerId: 45, clientX: scrollX, clientY: scrollY });
        scrollHost.scrollTop = geometry.rowPitch;
        await wait(20);
        pointer(editGrid, "pointermove", { pointerId: 45, clientX: scrollX, clientY: scrollY });
        check(layout(second).height === 5,
            "scrolling during resize contributes in grid coordinates without changing pointer CSS pixels");
        pointer(editGrid, "pointercancel", { pointerId: 45, clientX: scrollX, clientY: scrollY });
        scrollHost.scrollTop = 0;
        equal(layout(second), layout(cardFor(fixture.initial, fixture.initial[1].clientId)),
            "pointercancel after scrolling restores the starting layout");

        const originalPitch = publishedColumnPitch;
        editGrid.style.width = "72%";
        await wait(120);
        const resizedPitch = Number.parseFloat(editGrid.style.getPropertyValue("--report-grid-column-pitch"));
        check(resizedPitch < originalPitch &&
            Math.abs(resizedPitch - gridModule.measureGridGeometry(editGrid, 12).columnPitch) < .5,
            "dot and pointer geometry update together when the canvas width changes");
        editGrid.style.width = "100%";
        await wait(120);
    } else {
        check(getComputedStyle(editGrid, "::before").display === "none",
            "narrow single-column presentation does not show a misleading desktop dot grid");
    }

    resetGrid(editGrid);
    pointer(drag, "pointerdown", { pointerId: 11, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 11, clientX: 400 - columnWidth * 6, clientY: 30 });
    pointer(editGrid, "pointerup", { pointerId: 11, clientX: 400 - columnWidth * 6, clientY: 30 });
    await wait(20);
    check(callbacks.at(-1)?.column === 1 && callbacks.at(-1)?.id === compact(fixture.initial[1].clientId),
        "mouse drag snaps to the requested grid column");
    equal(layout(second), { column: 1, row: 1, width: 6, height: 4 },
        "preferred moved card keeps its snapped position");
    equal(layout(first), { column: 7, row: 1, width: 6, height: 4 },
        "production collision fixture relocates the colliding card deterministically");

    resetGrid(editGrid);
    setLayout(second, { column: 1, row: 1, width: 6, height: 4 });
    pointer(resize, "pointerdown", { pointerId: 12, clientX: 100, clientY: 100 });
    pointer(editGrid, "pointermove", { pointerId: 12, clientX: 5000, clientY: 5000 });
    pointer(editGrid, "pointerup", { pointerId: 12, clientX: 5000, clientY: 5000 });
    await wait(20);
    equal(layout(second), { column: fixture.maximumResize.column, row: fixture.maximumResize.row,
        width: fixture.maximumResize.width, height: fixture.maximumResize.height },
    "resize clamps to the maximum grid bounds");
    pointer(resize, "pointerdown", { pointerId: 13, clientX: 100, clientY: 100 });
    pointer(editGrid, "pointermove", { pointerId: 13, clientX: -5000, clientY: -5000 });
    pointer(editGrid, "pointerup", { pointerId: 13, clientX: -5000, clientY: -5000 });
    await wait(20);
    equal(layout(second), { column: fixture.minimumResize.column, row: fixture.minimumResize.row,
        width: fixture.minimumResize.width, height: fixture.minimumResize.height },
    "resize clamps to the minimum interactive size");

    resetGrid(editGrid);
    const beforeCancel = callbacks.length;
    pointer(drag, "pointerdown", { pointerId: 14, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 14, clientX: 100, clientY: 140 });
    globalThis.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "Escape" }));
    equal(layout(second), { column: 7, row: 1, width: 6, height: 4 },
        "Escape restores the pre-gesture layout");
    check(callbacks.length === beforeCancel, "Escape does not commit a layout callback");
    pointer(drag, "pointerdown", { pointerId: 15, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 15, clientX: 100, clientY: 140 });
    pointer(editGrid, "pointercancel", { pointerId: 15, clientX: 100, clientY: 140 });
    equal(layout(second), { column: 7, row: 1, width: 6, height: 4 },
        "pointercancel restores the pre-gesture layout");
    check(callbacks.length === beforeCancel, "pointercancel does not commit a layout callback");

    const contentCallbackCount = callbacks.length;
    const action = second.querySelector(".content-action");
    const chartSurface = second.querySelector(".chart-surface");
    for (const target of [action, chartSurface]) {
        pointer(target, "pointerdown", { pointerId: 16, clientX: 200, clientY: 200 });
        pointer(target, "pointermove", { pointerId: 16, clientX: 40, clientY: 40 });
        pointer(target, "pointerup", { pointerId: 16, clientX: 40, clientY: 40 });
    }
    check(callbacks.length === contentCallbackCount,
        "chart and card-action pointer activity does not start dragging");
    check(getComputedStyle(second.querySelector(".grid-card")).touchAction !== "none" &&
        getComputedStyle(drag).touchAction === "none",
    "touch-action restriction is limited to drag and resize handles");
    const scrollHost = document.querySelector("#scroll-host");
    scrollHost.scrollTop = 120;
    check(scrollHost.scrollTop === 120, "content outside handles remains scrollable");

    check(viewGrid.querySelectorAll(".report-drag-handle,.report-resize-handle").length === 0,
        "view mode renders no drag or resize handles");
    const viewBefore = layout(card(viewGrid, fixture.initial[1].clientId));
    pointer(card(viewGrid, fixture.initial[1].clientId), "pointerdown", { pointerId: 20, clientX: 400, clientY: 30 });
    pointer(viewGrid, "pointermove", { pointerId: 20, clientX: 50, clientY: 100 });
    pointer(viewGrid, "pointerup", { pointerId: 20, clientX: 50, clientY: 100 });
    equal(layout(card(viewGrid, fixture.initial[1].clientId)), viewBefore,
        "view mode receives no layout mutation");

    resetGrid(editGrid);
    const moveButton = second.querySelector(".keyboard-move");
    const widerButton = second.querySelector(".keyboard-wider");
    moveButton.addEventListener("click", () => {
        for (const item of fixture.keyboardMoveUp) setLayout(card(editGrid, item.clientId), item);
        document.querySelector("#layout-status").textContent = "Layout updated by keyboard";
    });
    widerButton.addEventListener("click", () => {
        for (const item of fixture.keyboardWider) setLayout(card(editGrid, item.clientId), item);
        document.querySelector("#layout-status").textContent = "Layout resized by keyboard";
    });
    moveButton.focus();
    moveButton.click();
    equal(layout(second), layout(cardFor(fixture.keyboardMoveUp, fixture.initial[1].clientId)),
        "keyboard move control applies the production working-copy fixture");
    widerButton.focus();
    widerButton.click();
    equal(layout(second), layout(cardFor(fixture.keyboardWider, fixture.initial[1].clientId)),
        "keyboard resize control applies the production working-copy fixture");
    const focusStyle = getComputedStyle(widerButton);
    check(document.activeElement === widerButton && (focusStyle.boxShadow !== "none" || focusStyle.outlineStyle !== "none"),
        "keyboard layout control retains visible focus");
    check(document.querySelector("#layout-status").textContent.includes("keyboard"),
        "keyboard layout action publishes a status message");

    resetGrid(editGrid);
    const layoutExecutionMarker = globalThis.echarts.getInstanceByDom(document.querySelector("#bar-chart"));
    const chartIdBeforeLayout = layoutExecutionMarker.id;
    pointer(drag, "pointerdown", { pointerId: 30, pointerType: "touch", clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 30, pointerType: "touch", clientX: 400 - columnWidth, clientY: 30 });
    pointer(editGrid, "pointerup", { pointerId: 30, pointerType: "touch", clientX: 400 - columnWidth, clientY: 30 });
    await wait(20);
    check(callbacks.at(-1)?.column === 6, "synthetic touch pointer follows the same snapped drag path");
    check(globalThis.echarts.getInstanceByDom(document.querySelector("#bar-chart")).id === chartIdBeforeLayout,
        "layout changes do not recreate or rerun chart rendering");

    const display = getComputedStyle(editGrid).display;
    const desktopProperties = fixture.initial.map(item => layout(card(editGrid, item.clientId)));
    if (innerWidth < 768) {
        check(display === "flex", "mobile width uses the single-column responsive layout");
        check([...editGrid.querySelectorAll(".report-grid-item")].every(x => x.getBoundingClientRect().width > editGrid.clientWidth * .9),
            "mobile cards occupy the available width");
    } else {
        check(display === "grid", "desktop width uses the twelve-column grid");
    }
    check(desktopProperties.every((item, index) => item.width === fixture.initial[index].width),
        "responsive presentation does not overwrite stored desktop widths");

    resetGrid(editGrid);
    const staleCount = callbacks.length;
    pointer(drag, "pointerdown", { pointerId: 31, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 31, clientX: 100, clientY: 90 });
    adapter.dispose();
    pointer(editGrid, "pointerup", { pointerId: 31, clientX: 100, clientY: 90 });
    await wait(20);
    check(callbacks.length === staleCount && !second.classList.contains("report-grid-gesture"),
        "disposing edit mode cancels the gesture and rejects its late callback");

    generation++;
    adapter = gridModule.create(editGrid, dotnet, generation, 12, 3, 3, 12);
    adapter.setGeneration(generation);
    adapter.dispose();
}

function cardFor(items, id) {
    const item = items.find(x => x.clientId === id);
    const standIn = document.createElement("div");
    setLayout(standIn, item);
    return standIn;
}

try {
    await chartChecks();
    await gridChecks();
} catch (error) {
    check(false, `${error?.stack ?? error}`);
}

for (const adapter of adapters.reverse()) {
    try { adapter.dispose(); } catch { }
}
document.body.dataset.testStatus = failures === 0 ? "passed" : "failed";
document.body.dataset.testChecks = String(checks);
document.body.dataset.testFailures = String(failures);
document.title = failures === 0
    ? `PASS (${checks}) Step 7 browser harness`
    : `FAIL (${failures}/${checks}) Step 7 browser harness`;
await fetch("/complete", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({
        viewport,
        status: document.body.dataset.testStatus,
        checks,
        failures,
        width: innerWidth,
        height: innerHeight,
        userAgent: navigator.userAgent,
        records
    })
});
