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

disposed = false;
const passiveOptions = [];
const passiveInstance = {
    setOption(value) { passiveOptions.push(value); }, resize() {}, dispose() { disposed = true; },
    isDisposed() { return disposed; }
};
const passiveChart = chartModule.createWithLibrary({ clientWidth: 640, clientHeight: 320 }, model,
    { getInstanceByDom: () => null, init: () => passiveInstance }, false);
flushFrames();
assert.equal(passiveOptions[0].tooltip.show, false, "passive chart disables tooltips");
assert.equal(passiveOptions[0].legend.selectedMode, false, "passive chart disables legend toggling");
assert.equal(passiveOptions[0].series[0].silent, true, "passive chart series emits no pointer events");
passiveChart.dispose();

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

const passiveTreeOptions = [];
const passiveTreeHandlers = new Map();
let passiveTreeDisposed = false;
const passiveTreeInstance = {
    setOption(value) { passiveTreeOptions.push(value); }, getOption() { return passiveTreeOptions.at(-1); },
    resize() {}, dispose() { passiveTreeDisposed = true; }, isDisposed() { return passiveTreeDisposed; },
    on(name, callback) { passiveTreeHandlers.set(name, callback); },
    off(name, callback) { if (passiveTreeHandlers.get(name) === callback) passiveTreeHandlers.delete(name); }
};
const passiveSelections = [];
const passiveTree = chartModule.createTreeWithLibrary(treeElement, treeModel,
    { invokeMethodAsync: (...args) => { passiveSelections.push(args); return Promise.resolve(); } }, 9,
    { getInstanceByDom: () => null, init: () => passiveTreeInstance }, false);
flushFrames();
assert.equal(passiveTreeOptions[0].tooltip.show, false, "passive tree disables tooltips");
assert.equal(passiveTreeOptions[0].series[0].roam, false, "passive tree disables pan and zoom");
assert.equal(passiveTreeOptions[0].series[0].silent, true, "passive tree disables node interaction");
passiveTreeHandlers.get("click")({ data: { id: "child" } });
await Promise.resolve();
assert.equal(passiveSelections.length, 0, "passive tree ignores chart callbacks");
passiveTree.dispose();

const windowListeners = new Map();
globalThis.addEventListener = (name, callback) => windowListeners.set(name, callback);
globalThis.removeEventListener = (name, callback) => {
    if (windowListeners.get(name) === callback) windowListeners.delete(name);
};
globalThis.innerWidth = 1200;
globalThis.innerHeight = 800;
const gridModule = await moduleFrom("src/TenantPlatform.Web/wwwroot/js/report-grid.js");
const listeners = new Map();
const styleValues = new Map([
    ["--report-grid-column", "1"], ["--report-grid-row", "1"],
    ["--report-grid-width", "6"], ["--report-grid-height", "4"]
]);
const itemClasses = new Set();
const item = {
    dataset: { reportCardId: "11111111111111111111111111111111" },
    style: {
        getPropertyValue: name => styleValues.get(name),
        setProperty: (name, value) => styleValues.set(name, String(value))
    },
    classList: {
        add: value => itemClasses.add(value),
        remove: value => itemClasses.delete(value),
        contains: value => itemClasses.has(value)
    },
    closest: selector => selector === ".report-grid-item" ? item : null,
    setPointerCapture() {}, hasPointerCapture: () => false, releasePointerCapture() {},
    getBoundingClientRect() {
        const width = Number(styleValues.get("--report-grid-width"));
        const height = Number(styleValues.get("--report-grid-height"));
        const columnTrack = (1200 - 11 * 16) / 12;
        return { width: width * columnTrack + (width - 1) * 16, height: height * 48 + (height - 1) * 16 };
    }
};
const resizeHandle = {
    closest(selector) {
        if (selector === ".report-resize-handle" || selector.includes("button")) return this;
        if (selector === ".report-grid-item") return item;
        return null;
    },
    setPointerCapture() {}, hasPointerCapture: () => false, releasePointerCapture() {}
};
const action = {
    closest(selector) {
        if (selector === ".report-grid-item") return item;
        if (selector.includes("button")) return this;
        return null;
    }
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
const callbacks = [];
const adapter = gridModule.create(grid, { invokeMethodAsync: (...args) => {
    callbacks.push(args);
    if (args[0] === "SelectCard" && args[1]) itemClasses.add("report-grid-item-selected");
} }, 4, 12, 3, 3, 12);
const commits = () => callbacks.filter(x => x[0] === "CommitLayout");
const gridSelections = () => callbacks.filter(x => x[0] === "SelectCard");
const gridObserver = observers.at(-1);
assert.equal(Number.parseFloat(styleValues.get("--report-grid-row-pitch")), 64,
    "published row pitch combines the resolved 48px track and 16px gap");
assert.ok(Math.abs(Number.parseFloat(styleValues.get("--report-grid-column-pitch")) - (1200 + 16) / 12) < 0.01,
    "published column pitch combines responsive track width and gap");
const startEvent = { button: 0, pointerId: 7, pointerType: "mouse", clientX: 0, clientY: 0, target: item, preventDefault() {} };
listeners.get("pointerdown")(startEvent);
await listeners.get("pointerup")({ pointerId: 7, clientX: 0, clientY: 0 });
assert.equal(gridSelections().length, 1, "pointerdown selects the card by stable id");
assert.equal(commits().length, 0, "a click without movement does not commit layout");

listeners.get("pointerdown")({ ...startEvent, pointerId: 8 });
listeners.get("pointermove")({ pointerId: 8, clientX: 4, clientY: 3, preventDefault() {} });
await listeners.get("pointerup")({ pointerId: 8, clientX: 4, clientY: 3 });
assert.equal(commits().length, 0, "movement below the explicit threshold does not start drag");

listeners.get("pointerdown")({ ...startEvent, pointerId: 9 });
listeners.get("pointermove")({ pointerId: 9, clientX: 200, clientY: 96, preventDefault() {} });
await listeners.get("pointerup")({ pointerId: 9, clientX: 200, clientY: 96 });
assert.deepEqual(commits()[0].slice(0, 7), ["CommitLayout", item.dataset.reportCardId, 3, 3, 6, 4, 4],
    "drag from the card surface snaps once and commits one generation-bound layout update");

const resetItem = () => {
    styleValues.set("--report-grid-column", "1"); styleValues.set("--report-grid-row", "1");
    styleValues.set("--report-grid-width", "6"); styleValues.set("--report-grid-height", "4");
};
const resize = async (pointerId, clientX, clientY) => {
    resetItem();
    listeners.get("pointerdown")({ ...startEvent, target: resizeHandle, pointerId });
    listeners.get("pointermove")({ pointerId, clientX, clientY, preventDefault() {} });
    await listeners.get("pointerup")({ pointerId, clientX, clientY });
    return commits().at(-1);
};
let resized = await resize(10, 0, 64);
assert.equal(resized[5], 5, "one complete row pitch increases height by one row");
resized = await resize(11, 0, 192);
assert.equal(resized[5], 7, "three complete row pitches increase height by three rows");
resized = await resize(12, 0, 20);
assert.equal(resized[5], 4, "small vertical movement does not produce a large height jump");
resized = await resize(13, 0, -64);
assert.equal(resized[5], 3, "upward movement snaps by one row and respects the minimum height");
resized = await resize(14, (1200 + 16) / 12, 0);
assert.equal(resized[4], 7, "one horizontal column pitch retains the existing one-column resize behavior");

const beforeAction = commits().length;
listeners.get("pointerdown")({ ...startEvent, target: action, pointerId: 15 });
listeners.get("pointermove")({ pointerId: 15, clientX: 200, clientY: 96, preventDefault() {} });
await listeners.get("pointerup")({ pointerId: 15, clientX: 200, clientY: 96 });
assert.equal(commits().length, beforeAction, "card commands do not start card drag");

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
listeners.get("pointerdown")({ ...startEvent, target: resizeHandle, pointerId: 16 });
listeners.get("pointermove")({ pointerId: 16, clientX: 1200, clientY: 1000, preventDefault() {} });
listeners.get("pointercancel")({ pointerId: 16 });
assert.equal(styleValues.get("--report-grid-width"), "6", "pointercancel restores pre-gesture width");
assert.equal(styleValues.get("--report-grid-height"), "4", "pointercancel restores pre-gesture height");

const paletteListeners = new Map();
const paletteStyles = new Map();
const paletteClasses = new Set();
const paletteHandle = {
    addEventListener: (name, callback) => paletteListeners.set(name, callback),
    removeEventListener: (name, callback) => { if (paletteListeners.get(name) === callback) paletteListeners.delete(name); },
    setPointerCapture() {}, hasPointerCapture: () => false, releasePointerCapture() {}
};
const palette = {
    querySelector: () => paletteHandle,
    getBoundingClientRect: () => ({ left: Number.parseFloat(paletteStyles.get("left")) || 900,
        top: Number.parseFloat(paletteStyles.get("top")) || 80, width: 260, height: 140 }),
    style: {
        get left() { return paletteStyles.get("left") || ""; },
        get top() { return paletteStyles.get("top") || ""; },
        set left(value) { paletteStyles.set("left", value); },
        set top(value) { paletteStyles.set("top", value); },
        set inset(value) { paletteStyles.set("inset", value); },
        removeProperty: name => paletteStyles.delete(name)
    },
    classList: { add: value => paletteClasses.add(value), remove: value => paletteClasses.delete(value) }
};
const paletteAdapter = gridModule.createPalette(palette);
const layoutBeforePalette = new Map(styleValues);
paletteListeners.get("pointerdown")({ button: 0, pointerId: 20, clientX: 950, clientY: 100, preventDefault() {} });
paletteListeners.get("pointermove")({ pointerId: 20, clientX: 4000, clientY: 4000, preventDefault() {} });
paletteListeners.get("pointerup")({ pointerId: 20 });
assert.equal(paletteStyles.get("left"), "932px", "palette drag remains inside the viewport width");
assert.equal(paletteStyles.get("top"), "652px", "palette drag remains inside the viewport height");
assert.deepEqual(styleValues, layoutBeforePalette, "palette drag does not change report layout");
paletteAdapter.reset();
assert.equal(paletteStyles.has("left"), false, "accessible palette reset removes the local position");
paletteAdapter.dispose();

adapter.dispose();
assert.equal(listeners.size, 0, "grid dispose removes all pointer listeners");
assert.equal(paletteListeners.size, 0, "palette dispose removes all pointer listeners");
assert.equal(windowListeners.size, 0, "adapters dispose their window listeners");
assert.equal(gridObserver.disconnected, true, "grid dispose disconnects its geometry ResizeObserver");

console.log("Report JS module checks passed (ECharts lifecycle and pointer-grid mock harness; no real browser rendering)." );
