function numberStyle(item, name) {
    return Number.parseInt(item.style.getPropertyValue(name), 10);
}

function clamp(value, minimum, maximum) {
    return Math.min(Math.max(value, minimum), maximum);
}

function pixels(value, fallback = 0) {
    const parsed = Number.parseFloat(value);
    return Number.isFinite(parsed) ? parsed : fallback;
}

export function measureGridGeometry(grid, columns) {
    const rect = grid.getBoundingClientRect();
    const style = getComputedStyle(grid);
    const scaleX = grid.offsetWidth > 0 ? rect.width / grid.offsetWidth : 1;
    const scaleY = grid.offsetHeight > 0 && rect.height > 0 ? rect.height / grid.offsetHeight : 1;
    const columnGap = pixels(style.columnGap || style.getPropertyValue("column-gap") || style.gap) * scaleX;
    const rowGap = pixels(style.rowGap || style.getPropertyValue("row-gap") || style.gap) * scaleY;
    const rowTrack = pixels(style.gridAutoRows || style.getPropertyValue("grid-auto-rows"),
        pixels(style.getPropertyValue("--report-grid-row-height"), 48)) * scaleY;
    const columnTrack = Math.max(1, (rect.width - columnGap * (columns - 1)) / columns);
    return {
        display: style.display,
        columnGap,
        rowGap,
        columnTrack,
        rowTrack,
        columnPitch: columnTrack + columnGap,
        rowPitch: rowTrack + rowGap
    };
}

function gestureGeometry(grid, item, start, columns) {
    const measured = measureGridGeometry(grid, columns);
    if (measured.display !== "grid") return measured;
    const itemRect = item.getBoundingClientRect();
    return {
        ...measured,
        // The rendered span is authoritative. This also preserves correctness if
        // a surrounding surface applies a CSS transform.
        columnPitch: start.width > 0 ? (itemRect.width + measured.columnGap) / start.width : measured.columnPitch,
        rowPitch: start.height > 0 ? (itemRect.height + measured.rowGap) / start.height : measured.rowPitch
    };
}

export function create(grid, dotnet, initialGeneration, columns, minimumWidth, minimumHeight, maximumHeight) {
    let generation = initialGeneration;
    let gesture = null;
    let disposed = false;

    const publishGeometry = () => {
        const geometry = measureGridGeometry(grid, columns);
        grid.style.setProperty("--report-grid-column-pitch", `${geometry.columnPitch}px`);
        grid.style.setProperty("--report-grid-row-pitch", `${geometry.rowPitch}px`);
    };
    const resizeObserver = typeof ResizeObserver === "function" ? new ResizeObserver(publishGeometry) : null;
    publishGeometry();
    resizeObserver?.observe(grid);

    const cancel = () => {
        if (!gesture) return;
        gesture.item.style.setProperty("--report-grid-column", gesture.start.column);
        gesture.item.style.setProperty("--report-grid-row", gesture.start.row);
        gesture.item.style.setProperty("--report-grid-width", gesture.start.width);
        gesture.item.style.setProperty("--report-grid-height", gesture.start.height);
        gesture.item.classList.remove("report-grid-gesture");
        gesture = null;
    };

    const complete = async event => {
        if (!gesture || event.pointerId !== gesture.pointerId) return;
        move(event);
        const current = gesture;
        current.item.classList.remove("report-grid-gesture");
        gesture = null;
        try {
            await dotnet.invokeMethodAsync("CommitLayout", current.id,
                current.value.column, current.value.row, current.value.width, current.value.height,
                current.generation);
        } catch { /* circuit navigation/disposal invalidates late callbacks */ }
    };

    const move = event => {
        if (!gesture || event.pointerId !== gesture.pointerId) return;
        const gridRect = grid.getBoundingClientRect();
        const deltaX = event.clientX - gesture.x - (gridRect.left - gesture.gridLeft);
        const deltaY = event.clientY - gesture.y - (gridRect.top - gesture.gridTop);
        const deltaColumn = Math.round(deltaX / gesture.columnPitch);
        const deltaRow = Math.round(deltaY / gesture.rowPitch);
        let value;
        if (gesture.kind === "resize") {
            const width = clamp(gesture.start.width + deltaColumn, minimumWidth, columns - gesture.start.column + 1);
            const height = clamp(gesture.start.height + deltaRow, minimumHeight, maximumHeight);
            value = { ...gesture.start, width, height };
        } else {
            value = {
                ...gesture.start,
                column: clamp(gesture.start.column + deltaColumn, 1, columns - gesture.start.width + 1),
                row: Math.max(1, gesture.start.row + deltaRow)
            };
        }
        gesture.value = value;
        gesture.item.style.setProperty("--report-grid-column", value.column);
        gesture.item.style.setProperty("--report-grid-row", value.row);
        gesture.item.style.setProperty("--report-grid-width", value.width);
        gesture.item.style.setProperty("--report-grid-height", value.height);
    };

    const start = event => {
        if (disposed || event.button !== 0 || gesture) return;
        const handle = event.target.closest(".report-drag-handle,.report-resize-handle");
        if (!handle || !grid.contains(handle)) return;
        const item = handle.closest(".report-grid-item");
        if (!item) return;
        event.preventDefault();
        handle.setPointerCapture(event.pointerId);
        const startValue = {
            column: numberStyle(item, "--report-grid-column"),
            row: numberStyle(item, "--report-grid-row"),
            width: numberStyle(item, "--report-grid-width"),
            height: numberStyle(item, "--report-grid-height")
        };
        const gridRect = grid.getBoundingClientRect();
        const geometry = gestureGeometry(grid, item, startValue, columns);
        gesture = {
            kind: handle.classList.contains("report-resize-handle") ? "resize" : "move",
            pointerId: event.pointerId,
            item,
            id: item.dataset.reportCardId,
            x: event.clientX,
            y: event.clientY,
            gridLeft: gridRect.left,
            gridTop: gridRect.top,
            columnPitch: geometry.columnPitch,
            rowPitch: geometry.rowPitch,
            start: startValue,
            value: startValue,
            generation
        };
        item.classList.add("report-grid-gesture");
    };

    const keydown = event => {
        if (event.key === "Escape" && gesture) {
            event.preventDefault();
            cancel();
        }
    };

    grid.addEventListener("pointerdown", start);
    grid.addEventListener("pointermove", move);
    grid.addEventListener("pointerup", complete);
    grid.addEventListener("pointercancel", cancel);
    globalThis.addEventListener("keydown", keydown);

    return {
        setGeneration(next) { generation = next; },
        dispose() {
            if (disposed) return;
            disposed = true;
            cancel();
            resizeObserver?.disconnect();
            grid.removeEventListener("pointerdown", start);
            grid.removeEventListener("pointermove", move);
            grid.removeEventListener("pointerup", complete);
            grid.removeEventListener("pointercancel", cancel);
            globalThis.removeEventListener("keydown", keydown);
        }
    };
}
