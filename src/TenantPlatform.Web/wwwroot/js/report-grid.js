function numberStyle(item, name) {
    return Number.parseInt(item.style.getPropertyValue(name), 10);
}

function clamp(value, minimum, maximum) {
    return Math.min(Math.max(value, minimum), maximum);
}

export function create(grid, dotnet, initialGeneration, columns, minimumWidth, minimumHeight, maximumHeight) {
    let generation = initialGeneration;
    let gesture = null;
    let disposed = false;

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
        const columnWidth = gridRect.width / columns;
        const rowHeight = Number.parseFloat(getComputedStyle(grid).getPropertyValue("--report-grid-row-height")) || 48;
        const deltaColumn = Math.round((event.clientX - gesture.x) / columnWidth);
        const deltaRow = Math.round((event.clientY - gesture.y) / rowHeight);
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
        gesture = {
            kind: handle.classList.contains("report-resize-handle") ? "resize" : "move",
            pointerId: event.pointerId,
            item,
            id: item.dataset.reportCardId,
            x: event.clientX,
            y: event.clientY,
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
            grid.removeEventListener("pointerdown", start);
            grid.removeEventListener("pointermove", move);
            grid.removeEventListener("pointerup", complete);
            grid.removeEventListener("pointercancel", cancel);
            globalThis.removeEventListener("keydown", keydown);
        }
    };
}
