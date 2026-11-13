import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

async function moduleFrom(path) {
    const source = await readFile(path, "utf8");
    return import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
}

const frames = new Map();
let nextFrame = 1;
globalThis.requestAnimationFrame = callback => { const id = nextFrame++; frames.set(id, callback); return id; };
globalThis.cancelAnimationFrame = id => frames.delete(id);
const flushFrames = () => {
    const pending = [...frames.values()]; frames.clear(); pending.forEach(callback => callback());
};
globalThis.matchMedia = () => ({ matches: true });
globalThis.document = { documentElement: {} };
globalThis.getComputedStyle = element => element?.isReportGrid ? {
    display: "grid",
    columnGap: "16px",
    rowGap: "16px",
    gridAutoRows: "48px",
    getPropertyValue: name => name === "--report-grid-row-height" ? "3rem" : ""
} : ({ getPropertyValue: () => "" });

const observers = [];
globalThis.ResizeObserver = class {
    constructor(callback) { this.callback = callback; this.disconnected = false; observers.push(this); }
    observe(element) { this.element = element; }
    disconnect() { this.disconnected = true; }
};
const mutationObservers = [];
globalThis.MutationObserver = class {
    constructor(callback) { this.callback = callback; this.disconnected = false; mutationObservers.push(this); }
    observe(element, options) { this.element = element; this.options = options; }
    disconnect() { this.disconnected = true; }
};

const chartModule = await moduleFrom("src/TenantPlatform.Web/wwwroot/js/report-charts.js");
const element = { clientWidth: 0, clientHeight: 0 };
const options = [];
let initCount = 0;
let resizeCount = 0;
let disposed = false;
const instance = {
    setOption(value) { options.push(value); },
    resize() { resizeCount++; },
    dispose() { disposed = true; },
    isDisposed() { return disposed; }
};
const echarts = { getInstanceByDom: () => null, init: () => { initCount++; return instance; } };
const model = {
    kind: 0,
    categories: [{ key: "a", label: "<stored text>" }, { key: "b", label: "B" }],
    series: [{ key: "NOK", label: "NOK", currency: "NOK", values: [10, null] }],
    unit: 1,
    ariaLabel: "Accessible chart",
    nonAdditive: false
};
const chart = chartModule.createWithLibrary(element, model, echarts);
flushFrames();
assert.equal(initCount, 1, "chart creates exactly one ECharts instance");
assert.equal(resizeCount, 0, "zero-sized chart is not resized");
assert.equal(options[0].series[0].data[1].value, null, "missing value remains null in ECharts option");
assert.equal(options[0].series[0].data[0].id, "a", "stable group key reaches the ECharts data item");
assert.equal(options[0].tooltip.renderMode, "richText", "tooltips use canvas rich text instead of stored HTML formatters");
assert.equal(options[0].animation, false, "reduced-motion preference disables animation");
element.clientWidth = 640; element.clientHeight = 320;
observers[0].callback(); flushFrames();
assert.equal(resizeCount, 1, "ResizeObserver resizes after a hidden chart becomes measurable");
chart.update({ ...model, series: [{ ...model.series[0], values: [11, null] }] });
flushFrames();
assert.equal(options.length, 2, "chart update reuses the instance and replaces options");
mutationObservers[0].callback();
assert.equal(options.length, 3, "theme mutation reapplies the current model without creating another instance");
chart.dispose();
assert.equal(observers[0].disconnected, true, "chart dispose disconnects ResizeObserver");
assert.equal(mutationObservers[0].disconnected, true, "chart dispose disconnects theme observer");
assert.equal(disposed, true, "chart dispose releases the ECharts instance");
chart.dispose();

const treeElement = { clientWidth: 720, clientHeight: 360 };
const treeOptions = [];
const treeHandlers = new Map();
let treeDisposed = false;
let treeResizeCount = 0;
let treeCurrentOption = null;
const treeInstance = {
    setOption(value) { treeCurrentOption = value; treeOptions.push(value); },
    getOption() { return treeCurrentOption; },
    resize() { treeResizeCount++; },
    dispose() { treeDisposed = true; },
    isDisposed() { return treeDisposed; },
    on(name, callback) { treeHandlers.set(name, callback); },
    off(name, callback) { if (treeHandlers.get(name) === callback) treeHandlers.delete(name); }
};
const treeEcharts = { getInstanceByDom: () => null, init: () => treeInstance };
const selections = [];
const treeModel = {
    root: { key: "root", name: "Account <script>", context: null, selected: false,
        children: [{ key: "child", name: "Same name", context: "Agreement", selected: true, children: [] }] },
    ariaLabel: "Relationship tree"
};
const tree = chartModule.createTreeWithLibrary(treeElement, treeModel,
    { invokeMethodAsync: (...args) => { selections.push(args); return Promise.resolve(); } }, 7, treeEcharts);
flushFrames();
assert.equal(treeOptions[0].series[0].type, "tree", "relationship adapter creates an ECharts tree series");
assert.equal(treeOptions[0].series[0].orient, "LR", "relationship tree is oriented left to right");
assert.equal(treeOptions[0].series[0].roam, true, "relationship tree enables pan and zoom");
assert.equal(treeOptions[0].tooltip.renderMode, "richText", "tree tooltips do not interpret stored names as HTML");
assert.equal(treeOptions[0].series[0].data[0].id, "root", "typed stable tree key reaches ECharts data");
treeCurrentOption.series[0].zoom = 1.4;
treeCurrentOption.series[0].center = [120, 80];
tree.update({ ...treeModel, root: { ...treeModel.root, selected: true } }, 8);
flushFrames();
assert.equal(treeOptions.at(-1).series[0].zoom, 1.4, "tree update preserves zoom");
assert.deepEqual(treeOptions.at(-1).series[0].center, [120, 80], "tree update preserves pan centre");
treeHandlers.get("click")({ data: { id: "child" } });
await Promise.resolve();
assert.deepEqual(selections[0], ["SelectTreeNode", "child", 8],
    "tree click reports stable identity with the current generation");
tree.dispose();
assert.equal(treeHandlers.size, 0, "tree dispose removes the click handler");
assert.equal(treeDisposed, true, "tree dispose releases the ECharts instance");
assert.ok(treeResizeCount >= 1, "tree participates in ResizeObserver lifecycle");

const windowListeners = new Map();
globalThis.addEventListener = (name, callback) => windowListeners.set(name, callback);
globalThis.removeEventListener = (name, callback) => {
    if (windowListeners.get(name) === callback) windowListeners.delete(name);
};
const gridModule = await moduleFrom("src/TenantPlatform.Web/wwwroot/js/report-grid.js");
const listeners = new Map();
const styleValues = new Map([
    ["--report-grid-column", "1"], ["--report-grid-row", "1"],
    ["--report-grid-width", "6"], ["--report-grid-height", "4"]
]);
const item = {
    dataset: { reportCardId: "11111111111111111111111111111111" },
    style: {
        getPropertyValue: name => styleValues.get(name),
        setProperty: (name, value) => styleValues.set(name, String(value))
    },
    classList: { add() {}, remove() {} },
    getBoundingClientRect() {
        const width = Number(styleValues.get("--report-grid-width"));
        const height = Number(styleValues.get("--report-grid-height"));
        const columnTrack = (1200 - 11 * 16) / 12;
        return { width: width * columnTrack + (width - 1) * 16, height: height * 48 + (height - 1) * 16 };
    }
};
const handle = {
    resize: false,
    closest(selector) {
        if (selector.includes("report-drag-handle")) return this;
        if (selector === ".report-grid-item") return item;
        return null;
    },
    classList: { contains: value => value === "report-resize-handle" && handle.resize },
    setPointerCapture() {}
};
const grid = {
    isReportGrid: true,
    offsetWidth: 1200,
    offsetHeight: 640,
    style: { setProperty: (name, value) => styleValues.set(name, value) },
    addEventListener: (name, callback) => listeners.set(name, callback),
    removeEventListener: (name, callback) => { if (listeners.get(name) === callback) listeners.delete(name); },
    contains: () => true,
    getBoundingClientRect: () => ({ width: 1200, height: 640, left: 0, top: 0 })
};
const commits = [];
const adapter = gridModule.create(grid, { invokeMethodAsync: (...args) => { commits.push(args); } }, 4, 12, 3, 3, 12);
const gridObserver = observers.at(-1);
assert.equal(Number.parseFloat(styleValues.get("--report-grid-row-pitch")), 64,
    "published row pitch combines the resolved 48px track and 16px gap");
assert.ok(Math.abs(Number.parseFloat(styleValues.get("--report-grid-column-pitch")) - (1200 + 16) / 12) < 0.01,
    "published column pitch combines responsive track width and gap");
const startEvent = { button: 0, pointerId: 7, clientX: 0, clientY: 0, target: handle, preventDefault() {} };
listeners.get("pointerdown")(startEvent);
listeners.get("pointermove")({ pointerId: 7, clientX: 200, clientY: 96 });
await listeners.get("pointerup")({ pointerId: 7, clientX: 200, clientY: 96 });
assert.deepEqual(commits[0].slice(0, 7), ["CommitLayout", item.dataset.reportCardId, 3, 3, 6, 4, 4],
    "pointer gesture snaps once and commits one generation-bound layout update");

handle.resize = true;
const resetItem = () => {
    styleValues.set("--report-grid-column", "1"); styleValues.set("--report-grid-row", "1");
    styleValues.set("--report-grid-width", "6"); styleValues.set("--report-grid-height", "4");
};
const resize = async (pointerId, clientX, clientY) => {
    resetItem();
    listeners.get("pointerdown")({ ...startEvent, pointerId });
    listeners.get("pointermove")({ pointerId, clientX, clientY });
    await listeners.get("pointerup")({ pointerId, clientX, clientY });
    return commits.at(-1);
};
let resized = await resize(8, 0, 64);
assert.equal(resized[5], 5, "one complete row pitch increases height by one row");
resized = await resize(9, 0, 192);
assert.equal(resized[5], 7, "three complete row pitches increase height by three rows");
resized = await resize(10, 0, 20);
assert.equal(resized[5], 4, "small vertical movement does not produce a large height jump");
resized = await resize(11, 0, -64);
assert.equal(resized[5], 3, "upward movement snaps by one row and respects the minimum height");
resized = await resize(12, (1200 + 16) / 12, 0);
assert.equal(resized[4], 7, "one horizontal column pitch retains the existing one-column resize behavior");

const narrowGrid = {
    ...grid, offsetWidth: 600,
    getBoundingClientRect: () => ({ width: 600, height: 640, left: 0, top: 0 })
};
const wideGeometry = gridModule.measureGridGeometry(grid, 12);
const narrowGeometry = gridModule.measureGridGeometry(narrowGrid, 12);
assert.equal(wideGeometry.rowPitch, narrowGeometry.rowPitch,
    "row geometry remains stable across canvas widths");
assert.ok(narrowGeometry.columnPitch < wideGeometry.columnPitch && narrowGeometry.columnTrack > 0,
    "column geometry follows the actual canvas width");

resetItem();
listeners.get("pointerdown")({ ...startEvent, pointerId: 13 });
listeners.get("pointermove")({ pointerId: 13, clientX: 1200, clientY: 1000 });
listeners.get("pointercancel")({ pointerId: 13 });
assert.equal(styleValues.get("--report-grid-width"), "6", "pointercancel restores pre-gesture width");
assert.equal(styleValues.get("--report-grid-height"), "4", "pointercancel restores pre-gesture height");
adapter.dispose();
assert.equal(listeners.size, 0, "grid dispose removes all pointer listeners");
assert.equal(windowListeners.size, 0, "grid dispose removes the Escape listener");
assert.equal(gridObserver.disconnected, true, "grid dispose disconnects its geometry ResizeObserver");

console.log("Report JS module checks passed (ECharts lifecycle and pointer-grid mock harness; no real browser rendering)." );
