let echartsPromise;

async function ensureECharts() {
    if (!echartsPromise) {
        echartsPromise = import("/lib/echarts/echarts.min.js").then(() => {
            if (!globalThis.echarts) throw new Error("Apache ECharts did not initialise.");
            return globalThis.echarts;
        });
    }
    return echartsPromise;
}

function option(model) {
    const categories = model.categories.map(category => ({ id: category.key, value: category.label }));
    const styles = getComputedStyle(document.documentElement);
    const textColor = styles.getPropertyValue("--bs-body-color").trim() || undefined;
    const borderColor = styles.getPropertyValue("--bs-border-color").trim() || undefined;
    const common = {
        animation: !globalThis.matchMedia?.("(prefers-reduced-motion: reduce)").matches,
        aria: { enabled: true, decal: { show: true }, label: { description: model.ariaLabel } },
        backgroundColor: "transparent",
        textStyle: { color: textColor },
        tooltip: { trigger: model.kind === 2 ? "item" : "axis", renderMode: "richText", confine: true }
    };

    if (model.kind === 2) {
        const series = model.series[0];
        return {
            ...common,
            legend: { type: "scroll", bottom: 0, textStyle: { color: textColor } },
            series: [{
                id: series.key,
                name: series.label,
                type: "pie",
                radius: ["48%", "72%"],
                avoidLabelOverlap: true,
                label: { color: textColor },
                data: model.categories.map((category, index) => ({
                    id: category.key,
                    name: category.label,
                    value: series.values[index]
                }))
            }]
        };
    }

    const chartType = model.kind === 1 ? "line" : "bar";
    return {
        ...common,
        legend: { type: "scroll", bottom: 0, textStyle: { color: textColor } },
        grid: { left: 16, right: 16, top: 32, bottom: 56, containLabel: true },
        xAxis: { type: "category", data: categories, axisLabel: { hideOverlap: true, color: textColor }, axisLine: { lineStyle: { color: borderColor } } },
        yAxis: { type: "value", axisLabel: { color: textColor }, splitLine: { lineStyle: { color: borderColor } } },
        series: model.series.map(series => ({
            id: series.key,
            name: series.label,
            type: chartType,
            data: model.categories.map((category, index) => ({ id: category.key, value: series.values[index] })),
            connectNulls: false,
            showSymbol: chartType === "line",
            emphasis: { focus: "series" }
        }))
    };
}

export async function create(element, model) {
    const echarts = await ensureECharts();
    return createWithLibrary(element, model, echarts);
}

export function createWithLibrary(element, model, echarts) {
    let instance = echarts.getInstanceByDom(element);
    if (!instance) instance = echarts.init(element, null, { renderer: "canvas" });
    let disposed = false;
    let currentModel = model;
    let frame = 0;
    const resize = () => {
        if (disposed || frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            if (!disposed && element.clientWidth > 0 && element.clientHeight > 0) instance.resize();
        });
    };
    const observer = new ResizeObserver(resize);
    observer.observe(element);
    const themeObserver = typeof MutationObserver === "undefined" ? null : new MutationObserver(() => {
        if (!disposed) instance.setOption(option(currentModel), { notMerge: true, lazyUpdate: false });
    });
    themeObserver?.observe(document.documentElement, { attributes: true, attributeFilter: ["data-bs-theme"] });
    instance.setOption(option(currentModel), { notMerge: true, lazyUpdate: false });
    resize();

    return {
        update(nextModel) {
            if (disposed) return;
            currentModel = nextModel;
            instance.setOption(option(currentModel), { notMerge: true, lazyUpdate: false });
            resize();
        },
        resize,
        dispose() {
            if (disposed) return;
            disposed = true;
            observer.disconnect();
            themeObserver?.disconnect();
            if (frame) cancelAnimationFrame(frame);
            if (!instance.isDisposed()) instance.dispose();
        }
    };
}
