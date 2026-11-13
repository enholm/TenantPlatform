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

let activeGestureOwner = null;

function claimGesture(owner) {
    if (activeGestureOwner && activeGestureOwner !== owner) return false;
    activeGestureOwner = owner;
    return true;
}

function releaseGesture(owner) {
    if (activeGestureOwner === owner) activeGestureOwner = null;
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
        columnPitch: start.width > 0 ? (itemRect.width + measured.columnGap) / start.width : measured.columnPitch,
        rowPitch: start.height > 0 ? (itemRect.height + measured.rowGap) / start.height : measured.rowPitch
    };
}

const interactiveSelector = "button,a,input,select,textarea,label,summary,[contenteditable='true'],.report-resize-handle";

export function create(grid, dotnet, initialGeneration, columns, minimumWidth, minimumHeight, maximumHeight) {
    let generation = initialGeneration;
    let gesture = null;
    let pending = null;
    let suppressClick = false;
    let disposed = false;
    const owner = Symbol("report-grid");
    const moveThreshold = 6;
    const touchScrollThreshold = 8;
    const touchHoldMilliseconds = 450;

    const publishGeometry = () => {
        const geometry = measureGridGeometry(grid, columns);
        grid.style.setProperty("--report-grid-column-pitch", `${geometry.columnPitch}px`);
        grid.style.setProperty("--report-grid-row-pitch", `${geometry.rowPitch}px`);
    };
    const resizeObserver = typeof ResizeObserver === "function" ? new ResizeObserver(publishGeometry) : null;
    publishGeometry();
    resizeObserver?.observe(grid);

    const notifySelection = id => {
        try { void Promise.resolve(dotnet.invokeMethodAsync("SelectCard", id, generation)).catch(() => {}); }
        catch { }
    };

    const clearPending = () => {
        if (!pending) return;
        if (pending.holdTimer) clearTimeout(pending.holdTimer);
        pending = null;
    };

    const restore = current => {
        current.item.style.setProperty("--report-grid-column", current.start.column);
        current.item.style.setProperty("--report-grid-row", current.start.row);
        current.item.style.setProperty("--report-grid-width", current.start.width);
        current.item.style.setProperty("--report-grid-height", current.start.height);
    };

    const endGesture = current => {
        current.item.classList.remove("report-grid-gesture");
        try {
            if (current.item.hasPointerCapture?.(current.pointerId)) current.item.releasePointerCapture(current.pointerId);
        } catch { }
        releaseGesture(owner);
    };

    const cancel = () => {
        clearPending();
        if (!gesture) return;
        const current = gesture;
        gesture = null;
        restore(current);
        endGesture(current);
    };

    const update = event => {
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

    const activate = (candidate, event) => {
        if (disposed || gesture || !candidate || !claimGesture(owner)) return false;
        if (candidate.holdTimer) clearTimeout(candidate.holdTimer);
        const start = {
            column: numberStyle(candidate.item, "--report-grid-column"),
            row: numberStyle(candidate.item, "--report-grid-row"),
            width: numberStyle(candidate.item, "--report-grid-width"),
            height: numberStyle(candidate.item, "--report-grid-height")
        };
        const gridRect = grid.getBoundingClientRect();
        const geometry = gestureGeometry(grid, candidate.item, start, columns);
        gesture = {
            kind: candidate.kind,
            pointerId: candidate.pointerId,
            item: candidate.item,
            id: candidate.item.dataset.reportCardId,
            x: candidate.x,
            y: candidate.y,
            gridLeft: gridRect.left,
            gridTop: gridRect.top,
            columnPitch: geometry.columnPitch,
            rowPitch: geometry.rowPitch,
            start,
            value: start,
            generation
        };
        pending = null;
        candidate.item.classList.add("report-grid-gesture");
        try { candidate.item.setPointerCapture(candidate.pointerId); } catch { }
        globalThis.getSelection?.()?.removeAllRanges?.();
        event?.preventDefault?.();
        return true;
    };

    const complete = async event => {
        if (pending && event.pointerId === pending.pointerId) clearPending();
        if (!gesture || event.pointerId !== gesture.pointerId) return;
        update(event);
        const current = gesture;
        gesture = null;
        endGesture(current);
        suppressClick = current.kind === "move";
        if (suppressClick) setTimeout(() => { suppressClick = false; }, 0);
        try {
            await dotnet.invokeMethodAsync("CommitLayout", current.id,
                current.value.column, current.value.row, current.value.width, current.value.height,
                current.generation);
        } catch { }
    };

    const start = event => {
        if (disposed || event.button !== 0 || gesture || pending || activeGestureOwner) return;
        const item = event.target.closest?.(".report-grid-item");
        if (!item || !grid.contains(item)) return;

        if (!item.classList.contains("report-grid-item-selected")) notifySelection(item.dataset.reportCardId);

        const resize = event.target.closest?.(".report-resize-handle");
        if (resize && grid.contains(resize)) {
            activate({ kind: "resize", pointerId: event.pointerId, item, x: event.clientX, y: event.clientY }, event);
            return;
        }
        if (event.target.closest?.(interactiveSelector)) return;

        pending = {
            kind: "move",
            pointerId: event.pointerId,
            pointerType: event.pointerType || "mouse",
            item,
            x: event.clientX,
            y: event.clientY,
            holdTimer: null
        };
        if (pending.pointerType === "touch") {
            const candidate = pending;
            candidate.holdTimer = setTimeout(() => {
                if (pending === candidate) activate(candidate);
            }, touchHoldMilliseconds);
        }
    };

    const move = event => {
        if (gesture) {
            if (event.pointerId !== gesture.pointerId) return;
            event.preventDefault();
            update(event);
            return;
        }
        if (!pending || event.pointerId !== pending.pointerId) return;
        const distance = Math.hypot(event.clientX - pending.x, event.clientY - pending.y);
        if (pending.pointerType === "touch") {
            if (distance >= touchScrollThreshold) clearPending();
            return;
        }
        if (distance >= moveThreshold && activate(pending, event)) update(event);
    };

    const pointerCancel = event => {
        if (pending && event.pointerId === pending.pointerId) clearPending();
        if (gesture && event.pointerId === gesture.pointerId) cancel();
    };

    const click = event => {
        if (suppressClick && event.target.closest?.(".report-grid-item")) {
            suppressClick = false;
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }
        if (event.target === grid) notifySelection(null);
    };

    const keydown = event => {
        if (event.key === "Escape" && (gesture || pending)) {
            event.preventDefault();
            cancel();
            return;
        }
        if (event.key !== "Enter" && event.key !== " ") return;
        const item = event.target.closest?.(".report-grid-item");
        if (!item || event.target !== item || !grid.contains(item)) return;
        event.preventDefault();
        notifySelection(item.dataset.reportCardId);
    };

    grid.addEventListener("pointerdown", start, true);
    grid.addEventListener("pointermove", move, true);
    grid.addEventListener("pointerup", complete, true);
    grid.addEventListener("pointercancel", pointerCancel, true);
    grid.addEventListener("click", click, true);
    grid.addEventListener("keydown", keydown);

    return {
        setGeneration(next) {
            if (generation !== next) cancel();
            generation = next;
        },
        dispose() {
            if (disposed) return;
            disposed = true;
            cancel();
            resizeObserver?.disconnect();
            grid.removeEventListener("pointerdown", start, true);
            grid.removeEventListener("pointermove", move, true);
            grid.removeEventListener("pointerup", complete, true);
            grid.removeEventListener("pointercancel", pointerCancel, true);
            grid.removeEventListener("click", click, true);
            grid.removeEventListener("keydown", keydown);
        }
    };
}

export function createPalette(palette) {
    const owner = Symbol("report-palette");
    const handle = palette.querySelector(".report-palette-drag-handle");
    let gesture = null;
    let disposed = false;

    const bounds = (left, top) => {
        const rect = palette.getBoundingClientRect();
        return {
            left: clamp(left, 8, Math.max(8, globalThis.innerWidth - rect.width - 8)),
            top: clamp(top, 8, Math.max(8, globalThis.innerHeight - rect.height - 8))
        };
    };

    const place = (left, top) => {
        const next = bounds(left, top);
        palette.style.inset = "auto";
        palette.style.left = `${next.left}px`;
        palette.style.top = `${next.top}px`;
    };

    const reset = () => {
        palette.style.removeProperty("inset");
        palette.style.removeProperty("left");
        palette.style.removeProperty("top");
    };

    const restore = current => {
        if (current.hadInlinePosition) place(current.startLeft, current.startTop);
        else reset();
    };

    const finish = restoreStart => {
        if (!gesture) return;
        const current = gesture;
        gesture = null;
        if (restoreStart) restore(current);
        palette.classList.remove("report-palette-gesture");
        try {
            if (handle.hasPointerCapture?.(current.pointerId)) handle.releasePointerCapture(current.pointerId);
        } catch { }
        releaseGesture(owner);
    };

    const start = event => {
        if (disposed || event.button !== 0 || gesture || !claimGesture(owner)) return;
        const rect = palette.getBoundingClientRect();
        gesture = {
            pointerId: event.pointerId,
            x: event.clientX,
            y: event.clientY,
            left: rect.left,
            top: rect.top,
            startLeft: rect.left,
            startTop: rect.top,
            hadInlinePosition: Boolean(palette.style.left || palette.style.top)
        };
        event.preventDefault();
        handle.setPointerCapture(event.pointerId);
        palette.classList.add("report-palette-gesture");
    };

    const move = event => {
        if (!gesture || event.pointerId !== gesture.pointerId) return;
        event.preventDefault();
        place(gesture.left + event.clientX - gesture.x, gesture.top + event.clientY - gesture.y);
    };

    const up = event => {
        if (gesture && event.pointerId === gesture.pointerId) finish(false);
    };

    const pointerCancel = event => {
        if (gesture && event.pointerId === gesture.pointerId) finish(true);
    };

    const keydown = event => {
        if (event.key === "Escape" && gesture) {
            event.preventDefault();
            finish(true);
        }
    };

    const resize = () => {
        if (gesture) finish(true);
        if (palette.style.left || palette.style.top) {
            const rect = palette.getBoundingClientRect();
            place(rect.left, rect.top);
        }
    };

    handle.addEventListener("pointerdown", start);
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", up);
    handle.addEventListener("pointercancel", pointerCancel);
    globalThis.addEventListener("keydown", keydown);
    globalThis.addEventListener("resize", resize);

    return {
        reset,
        dispose() {
            if (disposed) return;
            disposed = true;
            finish(true);
            handle.removeEventListener("pointerdown", start);
            handle.removeEventListener("pointermove", move);
            handle.removeEventListener("pointerup", up);
            handle.removeEventListener("pointercancel", pointerCancel);
            globalThis.removeEventListener("keydown", keydown);
            globalThis.removeEventListener("resize", resize);
        }
    };
}
