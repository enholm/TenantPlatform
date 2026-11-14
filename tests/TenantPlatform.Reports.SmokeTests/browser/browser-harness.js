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
        if (editable && item === fixture.initial[1]) card.classList.add("report-grid-item-selected");
        card.dataset.reportCardId = compact(item.clientId);
        if (editable) card.tabIndex = 0;
        setLayout(card, item);
        const content = document.createElement("div");
        content.className = "report-grid-content grid-card";
        const chartSurface = document.createElement("div");
        chartSurface.className = "chart-surface report-chart";
        if (!editable || item !== fixture.initial[1]) chartSurface.textContent = "Chart surface";
        content.append(chartSurface);
        card.append(content);
        if (editable && item === fixture.initial[1]) {
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

    const passiveTreeElement = document.createElement("div");
    passiveTreeElement.className = "report-tree-chart";
    passiveTreeElement.style.height = "320px";
    document.body.append(passiveTreeElement);
    const passiveSelections = [];
    const passiveTreeAdapter = await charts.createTree(passiveTreeElement, fixture.tree,
        { invokeMethodAsync: (...args) => { passiveSelections.push(args); return Promise.resolve(); } }, 14, false);
    adapters.push(passiveTreeAdapter);
    await wait(60);
    const passiveTree = echarts.getInstanceByDom(passiveTreeElement);
    const passiveTreeOption = passiveTree.getOption();
    check(!value(passiveTreeOption.tooltip[0].show) && !value(passiveTreeOption.series[0].roam) &&
        value(passiveTreeOption.series[0].silent),
        "passive relationship preview disables tooltip, pan, zoom and node interaction in real ECharts");
    passiveTree.getZr().handler.dispatch("click", { zrX: 10, zrY: 10 });
    await wait(20);
    check(passiveSelections.length === 0, "passive relationship preview emits no node callback");
}

async function gridChecks() {
    const editGrid = document.querySelector("#edit-grid");
    const viewGrid = document.querySelector("#view-grid");
    renderGrid(editGrid, true);
    renderGrid(viewGrid, false);
    const callbacks = [];
    const selections = [];
    const editorTargets = [];
    let generation = fixture.currentGeneration;
    const selectCard = id => {
        for (const item of editGrid.querySelectorAll(".report-grid-item")) {
            item.classList.remove("report-grid-item-selected");
            item.removeAttribute("aria-current");
            item.querySelector(".report-resize-handle")?.remove();
        }
        if (!id) return;
        const selected = card(editGrid, id);
        selected.classList.add("report-grid-item-selected");
        selected.setAttribute("aria-current", "true");
        const resize = document.createElement("button");
        resize.type = "button";
        resize.className = "report-resize-handle";
        resize.setAttribute("aria-label", "Resize selected card");
        resize.textContent = "↘";
        selected.append(resize);
    };
    const dotnet = {
        async invokeMethodAsync(name, id, column, row, width, height, callbackGeneration) {
            callbacks.push({ name, id, column, row, width, height, generation: callbackGeneration });
            if (name === "SelectCard") {
                const selectionGeneration = column;
                if (selectionGeneration !== generation) return;
                selections.push(id);
                selectCard(id);
                return;
            }
            if (name === "EditCard") {
                if (column === generation) editorTargets.push(id);
                return;
            }
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
    let adapter = gridModule.create(editGrid, dotnet, generation, 12, 3, 3, fixture.maximumResize.height);
    adapters.push(adapter);
    const second = card(editGrid, fixture.initial[1].clientId);
    const first = card(editGrid, fixture.initial[0].clientId);
    const commits = () => callbacks.filter(x => x.name === "CommitLayout");

    const passiveSurface = second.querySelector(".chart-surface");
    const passiveChartAdapter = await charts.create(passiveSurface, fixture.charts.bar, false);
    adapters.push(passiveChartAdapter);
    await wait(60);
    const passiveChart = globalThis.echarts.getInstanceByDom(passiveSurface);
    const passiveOption = passiveChart.getOption();
    check(!value(passiveOption.tooltip[0].show) && !value(passiveOption.legend[0].selectedMode) &&
        passiveOption.series.every(series => value(series.silent)),
        "edit-card chart is a real passive ECharts result with tooltip, legend toggling and series events disabled");

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
            const resize = second.querySelector(".report-resize-handle");
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
        const resize = second.querySelector(".report-resize-handle");
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

    check(editGrid.querySelectorAll(".report-grid-item-selected").length === 1 &&
        editGrid.querySelectorAll(".report-resize-handle").length === 1 &&
        !first.querySelector(".report-resize-handle"),
    "only one card is selected and only that card has a resize handle");

    const beforeClick = commits().length;
    pointer(first, "pointerdown", { pointerId: 5, clientX: 100, clientY: 100 });
    pointer(first, "pointerup", { pointerId: 5, clientX: 100, clientY: 100 });
    await wait(20);
    check(selections.at(-1) === compact(fixture.initial[0].clientId) && first.classList.contains("report-grid-item-selected"),
        "pointer click selects a card by stable id");
    check(commits().length === beforeClick, "click without movement does not change layout");
    check(editorTargets.length === 0, "single click selects without opening the editor");
    editGrid.append(first);
    check(first.classList.contains("report-grid-item-selected") && selections.at(-1) === compact(fixture.initial[0].clientId),
        "selection remains attached to the stable id after DOM reordering");
    editGrid.insertBefore(first, second);
    first.querySelector(".chart-surface").dispatchEvent(new MouseEvent("dblclick", { bubbles: true, cancelable: true }));
    await wait(20);
    check(editorTargets.at(-1) === compact(fixture.initial[0].clientId),
        "double-click over passive card content opens the selected stable id after DOM reordering");

    editGrid.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true }));
    await wait(20);
    check(selections.at(-1) === null && editGrid.querySelectorAll(".report-grid-item-selected").length === 0,
        "clicking empty canvas clears selection");
    pointer(second, "pointerdown", { pointerId: 6, clientX: 400, clientY: 30 });
    pointer(second, "pointerup", { pointerId: 6, clientX: 400, clientY: 30 });
    await wait(20);

    const beforeThreshold = commits().length;
    pointer(second.querySelector(".chart-surface"), "pointerdown", { pointerId: 7, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 7, clientX: 404, clientY: 33 });
    pointer(editGrid, "pointerup", { pointerId: 7, clientX: 404, clientY: 33 });
    await wait(20);
    check(commits().length === beforeThreshold, "movement below the CSS-pixel threshold does not start drag");

    resetGrid(editGrid);
    const chartSurface = second.querySelector(".chart-surface canvas") ?? second.querySelector(".chart-surface");
    pointer(chartSurface, "pointerdown", { pointerId: 11, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 11, clientX: 400 - columnWidth * 6, clientY: 30 });
    pointer(editGrid, "pointerup", { pointerId: 11, clientX: 400 - columnWidth * 6, clientY: 30 });
    await wait(20);
    check(commits().at(-1)?.column === 1 && commits().at(-1)?.id === compact(fixture.initial[1].clientId),
        "mouse drag from chart content selects and snaps the correct card");
    const editsAfterDrag = editorTargets.length;
    chartSurface.dispatchEvent(new MouseEvent("dblclick", { bubbles: true, cancelable: true }));
    await wait(20);
    check(editorTargets.length === editsAfterDrag, "completed drag is not misinterpreted as double-click editing");
    equal(layout(second), { column: 1, row: 1, width: 6, height: 4 },
        "preferred moved card keeps its snapped position");
    equal(layout(first), { column: 7, row: 1, width: 6, height: 4 },
        "production collision fixture relocates the colliding card deterministically");

    resetGrid(editGrid);
    setLayout(second, { column: 1, row: 1, width: 6, height: 4 });
    let resize = second.querySelector(".report-resize-handle");
    pointer(resize, "pointerdown", { pointerId: 12, clientX: 100, clientY: 100 });
    pointer(editGrid, "pointermove", { pointerId: 12, clientX: 5000, clientY: 5000 });
    pointer(editGrid, "pointerup", { pointerId: 12, clientX: 5000, clientY: 5000 });
    await wait(20);
    equal(layout(second), { column: fixture.maximumResize.column, row: fixture.maximumResize.row,
        width: fixture.maximumResize.width, height: fixture.maximumResize.height },
    "resize clamps to the maximum grid bounds");
    const editsAfterResize = editorTargets.length;
    resize.dispatchEvent(new MouseEvent("dblclick", { bubbles: true, cancelable: true }));
    await wait(20);
    check(editorTargets.length === editsAfterResize, "resize handle double-click does not open the editor");
    pointer(resize, "pointerdown", { pointerId: 13, clientX: 100, clientY: 100 });
    pointer(editGrid, "pointermove", { pointerId: 13, clientX: -5000, clientY: -5000 });
    pointer(editGrid, "pointerup", { pointerId: 13, clientX: -5000, clientY: -5000 });
    await wait(20);
    equal(layout(second), { column: fixture.minimumResize.column, row: fixture.minimumResize.row,
        width: fixture.minimumResize.width, height: fixture.minimumResize.height },
    "resize clamps to the minimum interactive size");

    resetGrid(editGrid);
    const beforeCancel = commits().length;
    pointer(chartSurface, "pointerdown", { pointerId: 14, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 14, clientX: 100, clientY: 140 });
    editGrid.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "Escape" }));
    equal(layout(second), { column: 7, row: 1, width: 6, height: 4 },
        "Escape restores the pre-gesture layout");
    check(commits().length === beforeCancel, "Escape does not commit a layout callback");
    pointer(chartSurface, "pointerdown", { pointerId: 15, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 15, clientX: 100, clientY: 140 });
    pointer(editGrid, "pointercancel", { pointerId: 15, clientX: 100, clientY: 140 });
    equal(layout(second), { column: 7, row: 1, width: 6, height: 4 },
        "pointercancel restores the pre-gesture layout");
    check(commits().length === beforeCancel, "pointercancel does not commit a layout callback");

    check([...editGrid.querySelectorAll(".report-grid-item")].every(item =>
        item.querySelectorAll("button:not(.report-resize-handle),select,a,input,[tabindex]").length === 0),
    "editable card content has no focusable or activatable controls behind the passive preview");
    check(getComputedStyle(second).touchAction !== "none" &&
        getComputedStyle(second.querySelector(".report-resize-handle")).touchAction === "none",
    "ordinary touch can scroll while the explicit resize handle owns its gesture");
    const scrollHost = document.querySelector("#scroll-host");
    scrollHost.scrollTop = 120;
    check(scrollHost.scrollTop === 120, "content outside handles remains scrollable");

    check(viewGrid.querySelectorAll(".report-grid-item-selected,.report-resize-handle").length === 0 &&
        [...viewGrid.querySelectorAll(".report-grid-item")].every(x => x.tabIndex < 0),
        "view mode renders no selection, edit focus target or resize handle");
    const viewBefore = layout(card(viewGrid, fixture.initial[1].clientId));
    pointer(card(viewGrid, fixture.initial[1].clientId), "pointerdown", { pointerId: 20, clientX: 400, clientY: 30 });
    pointer(viewGrid, "pointermove", { pointerId: 20, clientX: 50, clientY: 100 });
    pointer(viewGrid, "pointerup", { pointerId: 20, clientX: 50, clientY: 100 });
    equal(layout(card(viewGrid, fixture.initial[1].clientId)), viewBefore,
        "view mode receives no layout mutation");

    first.focus();
    first.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "Enter" }));
    await wait(20);
    check(first.classList.contains("report-grid-item-selected"), "keyboard activation selects the focused report card");
    selectCard(compact(fixture.initial[1].clientId));

    resetGrid(editGrid);
    const palette = document.querySelector("#layout-palette");
    const paletteAction = document.querySelector("#palette-action");
    const paletteEdit = document.querySelector("#palette-edit");
    const paletteDuplicate = document.querySelector("#palette-duplicate");
    const paletteRefresh = document.querySelector("#palette-refresh");
    const paletteDelete = document.querySelector("#palette-delete");
    const paletteTarget = document.querySelector("#palette-target");
    const paletteSelection = document.querySelector("#palette-selection");
    const paletteHandle = palette.querySelector(".report-palette-drag-handle");
    const paletteAdapter = gridModule.createPalette(palette);
    adapters.push(paletteAdapter);
    const moveButton = paletteAction;
    const widerButton = document.querySelector("#palette-reset");
    widerButton.addEventListener("click", () => paletteAdapter.reset());
    const actionTargets = [];
    const selectedStableId = () => editGrid.querySelector(".report-grid-item-selected")?.dataset.reportCardId ?? null;
    paletteEdit.addEventListener("click", () => actionTargets.push({ action: "edit", id: selectedStableId() }));
    paletteRefresh.addEventListener("click", () => actionTargets.push({ action: "refresh", id: selectedStableId() }));
    paletteDuplicate.addEventListener("click", () => {
        const source = editGrid.querySelector(".report-grid-item-selected");
        actionTargets.push({ action: "duplicate", id: source?.dataset.reportCardId ?? null });
        if (!source) return;
        const copy = source.cloneNode(true);
        copy.dataset.reportCardId = "cccccccccccccccccccccccccccccccc";
        copy.querySelector(".report-resize-handle")?.remove();
        editGrid.append(copy);
        selectCard(copy.dataset.reportCardId);
    });
    paletteDelete.addEventListener("click", () => {
        const selected = editGrid.querySelector(".report-grid-item-selected");
        actionTargets.push({ action: "delete", id: selected?.dataset.reportCardId ?? null });
        selected?.remove();
        selectCard(null);
        paletteSelection.focus();
    });
    paletteTarget.addEventListener("change", () => {
        if (!paletteTarget.value) return;
        actionTargets.push({ action: "move", id: selectedStableId(), target: paletteTarget.value });
        selectCard(null);
        paletteSelection.focus();
        paletteTarget.value = "";
    });
    moveButton.addEventListener("click", () => {
        for (const item of fixture.keyboardMoveUp) setLayout(card(editGrid, item.clientId), item);
        document.querySelector("#layout-status").textContent = "Layout updated by keyboard";
    });
    const paletteResize = document.createElement("button");
    paletteResize.type = "button";
    paletteResize.className = "btn btn-outline-secondary btn-sm";
    paletteResize.textContent = "Wider";
    palette.append(paletteResize);
    paletteResize.addEventListener("click", () => {
        for (const item of fixture.keyboardWider) setLayout(card(editGrid, item.clientId), item);
        document.querySelector("#layout-status").textContent = "Layout resized by keyboard";
    });
    const selectedBeforePalette = editGrid.querySelector(".report-grid-item-selected")?.dataset.reportCardId;
    moveButton.focus();
    moveButton.click();
    equal(layout(second), layout(cardFor(fixture.keyboardMoveUp, fixture.initial[1].clientId)),
        "keyboard move control applies the production working-copy fixture");
    paletteResize.focus();
    paletteResize.click();
    equal(layout(second), layout(cardFor(fixture.keyboardWider, fixture.initial[1].clientId)),
        "keyboard resize control applies the production working-copy fixture");
    const focusStyle = getComputedStyle(paletteResize);
    check(document.activeElement === paletteResize && (focusStyle.boxShadow !== "none" || focusStyle.outlineStyle !== "none"),
        "palette keyboard action retains visible focus");
    check(editGrid.querySelector(".report-grid-item-selected")?.dataset.reportCardId === selectedBeforePalette,
        "palette actions retain the selected card");
    check(document.querySelector("#layout-status").textContent.includes("keyboard"),
        "keyboard layout action publishes a status message");

    selectCard(compact(fixture.initial[1].clientId));
    const actionOrder = [...palette.querySelector(".report-palette-card-actions").children].map(x => x.id);
    equal(actionOrder, ["palette-edit", "palette-duplicate", "palette-refresh", "palette-delete"],
        "palette keeps edit, duplicate, refresh and delete in one ordered wrapping action row");
    const actionRow = palette.querySelector(".report-palette-card-actions");
    check(actionRow.scrollWidth <= actionRow.clientWidth + 1 &&
        [...actionRow.children].every(x => x.getBoundingClientRect().right <= actionRow.getBoundingClientRect().right + 1),
        "palette card actions stay inside the available palette width");
    if (innerWidth >= 768)
        check(new Set([...actionRow.children].map(x => Math.round(x.getBoundingClientRect().top))).size === 1,
            "palette card actions share one row at the desktop width");
    check(paletteRefresh.getAttribute("aria-label") === "Refresh preview" &&
        paletteDelete.getAttribute("aria-label") === "Delete" &&
        paletteDelete.getBoundingClientRect().right >= actionRow.getBoundingClientRect().right - 1,
        "icon-only refresh and delete actions stay accessible while delete is right aligned");
    editGrid.append(second);
    paletteEdit.click();
    check(actionTargets.at(-1)?.action === "edit" && actionTargets.at(-1)?.id === compact(fixture.initial[1].clientId),
        "palette edit resolves the selected stable id after DOM reordering");
    paletteDuplicate.click();
    check(actionTargets.at(-1)?.id === compact(fixture.initial[1].clientId) &&
        selectedStableId() === "cccccccccccccccccccccccccccccccc",
        "palette duplicate targets the selected card and selects the independent copy");
    paletteDelete.click();
    check(actionTargets.at(-1)?.id === "cccccccccccccccccccccccccccccccc" && !selectedStableId() &&
        document.activeElement === paletteSelection,
        "palette delete removes the selected copy, clears selection and restores palette focus");
    selectCard(compact(fixture.initial[1].clientId));
    paletteTarget.value = "allowed-target";
    paletteTarget.dispatchEvent(new Event("change", { bubbles: true }));
    check(actionTargets.at(-1)?.action === "move" && actionTargets.at(-1)?.id === compact(fixture.initial[1].clientId) &&
        actionTargets.at(-1)?.target === "allowed-target" && !selectedStableId(),
        "palette side selector targets the selected card, exposes only its allowed target and clears selection");
    selectCard(compact(fixture.initial[1].clientId));

    const layoutBeforePaletteDrag = layout(second);
    const paletteBefore = palette.getBoundingClientRect();
    pointer(paletteHandle, "pointerdown", { pointerId: 24, clientX: paletteBefore.left + 10, clientY: paletteBefore.top + 10 });
    pointer(paletteHandle, "pointermove", { pointerId: 24, clientX: innerWidth + 500, clientY: innerHeight + 500 });
    pointer(paletteHandle, "pointerup", { pointerId: 24, clientX: innerWidth + 500, clientY: innerHeight + 500 });
    const paletteAfter = palette.getBoundingClientRect();
    if (innerWidth >= 768) {
        check(paletteAfter.right <= innerWidth - 7 && paletteAfter.bottom <= innerHeight - 7,
            "movable palette is clamped inside the viewport");
        scrollTo(0, 300);
        await wait(20);
        check(Math.abs(palette.getBoundingClientRect().top - paletteAfter.top) < 1,
            "fixed palette remains visible while the page scrolls");
        scrollTo(0, 0);
        globalThis.dispatchEvent(new Event("resize"));
        check(palette.getBoundingClientRect().right <= innerWidth - 7, "viewport resize reclamps palette position");
    } else {
        check(getComputedStyle(paletteHandle).display === "none" && getComputedStyle(palette).position === "fixed",
            "narrow view uses a fixed compact palette without free-move handle");
    }
    equal(layout(second), layoutBeforePaletteDrag, "palette drag does not mutate report layout");
    widerButton.click();
    if (innerWidth >= 768) check(!palette.style.left && !palette.style.top, "reset action restores default palette placement");

    resetGrid(editGrid);
    const layoutExecutionMarker = globalThis.echarts.getInstanceByDom(document.querySelector("#bar-chart"));
    const chartIdBeforeLayout = layoutExecutionMarker.id;
    pointer(chartSurface, "pointerdown", { pointerId: 30, pointerType: "touch", clientX: 400, clientY: 30 });
    await wait(480);
    pointer(editGrid, "pointermove", { pointerId: 30, pointerType: "touch", clientX: 400 - columnWidth, clientY: 30 });
    pointer(editGrid, "pointerup", { pointerId: 30, pointerType: "touch", clientX: 400 - columnWidth, clientY: 30 });
    await wait(20);
    check(commits().at(-1)?.column === 6, "long-press synthetic touch follows the snapped drag path");
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
    const staleCount = commits().length;
    pointer(chartSurface, "pointerdown", { pointerId: 31, clientX: 400, clientY: 30 });
    pointer(editGrid, "pointermove", { pointerId: 31, clientX: 100, clientY: 90 });
    adapter.dispose();
    pointer(editGrid, "pointerup", { pointerId: 31, clientX: 100, clientY: 90 });
    await wait(20);
    check(commits().length === staleCount && !second.classList.contains("report-grid-gesture"),
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
    ? `PASS (${checks}) report browser harness`
    : `FAIL (${failures}/${checks}) report browser harness`;
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
