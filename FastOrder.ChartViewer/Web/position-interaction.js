(function initializePositionInteraction(root, factory) {
    "use strict";

    const api = factory();
    if (typeof module === "object" && module.exports) {
        module.exports = api;
    }

    root.FastOrderPositionInteraction = api;
})(typeof globalThis === "object" ? globalThis : window, () => {
    "use strict";

    const positiveNumberFields = new Set([
        "entryPrice",
        "stopPrice",
        "targetPrice",
        "accountSize",
        "riskValue",
        "lotSize",
        "pointValue",
        "leverage"
    ]);

    function nearestBarIndex(barTimes, time) {
        if (!Array.isArray(barTimes) || barTimes.length === 0 || !Number.isFinite(time)) {
            return -1;
        }

        let low = 0;
        let high = barTimes.length - 1;
        while (low < high) {
            const middle = Math.floor((low + high) / 2);
            if (barTimes[middle] < time) {
                low = middle + 1;
            } else {
                high = middle;
            }
        }

        if (low > 0 && Math.abs(barTimes[low - 1] - time) <= Math.abs(barTimes[low] - time)) {
            return low - 1;
        }

        return low;
    }

    function hitTest(positions, geometries, selectedId, point, tolerance = 8) {
        const selected = selectedId ? positions.find(position => position.id === selectedId) : null;
        const ordered = selected
            ? [selected, ...positions.filter(position => position.id !== selectedId).reverse()]
            : positions.slice().reverse();

        for (const position of ordered) {
            const geometry = geometries.get(position.id);
            if (!geometry) {
                continue;
            }

            const isSelected = position.id === selectedId;
            const insideX = point.x >= geometry.leftX - tolerance &&
                point.x <= geometry.rightX + tolerance;
            const insideY = point.y >= geometry.topY - tolerance &&
                point.y <= geometry.bottomY + tolerance;

            if (isSelected) {
                const middleY = (geometry.topY + geometry.bottomY) / 2;
                if (Math.abs(point.y - middleY) <= tolerance + 2) {
                    if (Math.abs(point.x - geometry.leftX) <= tolerance) {
                        return { id: position.id, kind: "range-handle", handle: "StartEdge" };
                    }
                    if (Math.abs(point.x - geometry.rightX) <= tolerance) {
                        return { id: position.id, kind: "range-handle", handle: "EndEdge" };
                    }
                }

                if (insideX) {
                    if (Math.abs(point.y - geometry.targetY) <= tolerance) {
                        return { id: position.id, kind: "price-handle", handle: "Target" };
                    }
                    if (Math.abs(point.y - geometry.entryY) <= tolerance) {
                        return { id: position.id, kind: "price-handle", handle: "Entry" };
                    }
                    if (Math.abs(point.y - geometry.stopY) <= tolerance) {
                        return { id: position.id, kind: "price-handle", handle: "Stop" };
                    }
                }

                if (insideX && insideY) {
                    return { id: position.id, kind: "body" };
                }
            } else if (insideX && (
                insideY ||
                Math.abs(point.y - geometry.targetY) <= tolerance ||
                Math.abs(point.y - geometry.entryY) <= tolerance ||
                Math.abs(point.y - geometry.stopY) <= tolerance)) {
                return { id: position.id, kind: "select" };
            }
        }

        return null;
    }

    function cursorForHit(hit, activeTool) {
        if (activeTool) {
            return "crosshair";
        }
        if (!hit) {
            return "default";
        }

        switch (hit.kind) {
            case "price-handle":
                return "ns-resize";
            case "range-handle":
                return "ew-resize";
            case "body":
                return "move";
            case "select":
                return "pointer";
            default:
                return "default";
        }
    }

    function isValidPropertyValue(field, value) {
        if (field === "riskMode") {
            return value === "PercentOfAccount" || value === "Absolute";
        }
        if (field === "quantityPrecision") {
            return Number.isInteger(value) && value >= 0 && value <= 28;
        }

        return positiveNumberFields.has(field) && Number.isFinite(value) && value > 0;
    }

    function clampMoveDelta(pointerIndex, currentIndex, startIndex, endIndex, barCount) {
        if (![pointerIndex, currentIndex, startIndex, endIndex, barCount].every(Number.isInteger) ||
            barCount < 1) {
            throw new TypeError("Bar indexes and count must be valid integers.");
        }

        const requestedDelta = currentIndex - pointerIndex;
        const minimumDelta = -startIndex;
        const maximumDelta = barCount - 1 - endIndex;
        return Math.max(minimumDelta, Math.min(maximumDelta, requestedDelta));
    }

    function clampResizeIndex(handle, currentIndex, startIndex, endIndex, barCount) {
        if (![currentIndex, startIndex, endIndex, barCount].every(Number.isInteger) || barCount < 2) {
            throw new TypeError("Bar indexes and count must be valid integers.");
        }

        if (handle === "StartEdge") {
            return Math.max(0, Math.min(endIndex - 1, currentIndex));
        }
        if (handle === "EndEdge") {
            return Math.max(startIndex + 1, Math.min(barCount - 1, currentIndex));
        }

        throw new RangeError("A horizontal edge handle is required.");
    }

    function layoutLabelTops(desiredCenters, viewportHeight, labelHeight = 22, gap = 3) {
        if (!Array.isArray(desiredCenters) || !Number.isFinite(viewportHeight) || viewportHeight <= 0) {
            return [];
        }

        const maximumTop = Math.max(0, viewportHeight - labelHeight);
        const entries = desiredCenters
            .map((center, index) => ({ index, top: Math.max(0, Math.min(maximumTop, center - labelHeight / 2)) }))
            .sort((left, right) => left.top - right.top);

        for (let index = 1; index < entries.length; index++) {
            entries[index].top = Math.max(entries[index].top, entries[index - 1].top + labelHeight + gap);
        }

        if (entries.length > 0 && entries[entries.length - 1].top > maximumTop) {
            const shift = entries[entries.length - 1].top - maximumTop;
            for (const entry of entries) {
                entry.top -= shift;
            }
        }

        if (entries.length > 0 && entries[0].top < 0) {
            const shift = -entries[0].top;
            for (const entry of entries) {
                entry.top += shift;
            }
        }

        const result = new Array(entries.length);
        for (const entry of entries) {
            result[entry.index] = entry.top;
        }
        return result;
    }

    return Object.freeze({
        nearestBarIndex,
        hitTest,
        cursorForHit,
        isValidPropertyValue,
        clampMoveDelta,
        clampResizeIndex,
        layoutLabelTops
    });
});
