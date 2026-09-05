(() => {
    "use strict";

    const host = document.getElementById("chart-host");
    const canvas = document.getElementById("drawing-overlay");
    const context = canvas.getContext("2d");
    const longButton = document.getElementById("long-tool");
    const shortButton = document.getElementById("short-tool");
    const deleteButton = document.getElementById("delete-tool");
    const saveButton = document.getElementById("save-tool");
    const loadButton = document.getElementById("load-tool");
    const statusOutput = document.getElementById("interaction-status");
    const symbolName = document.getElementById("symbol-name");
    const realtimeStatus = document.getElementById("realtime-status");
    const positionForm = document.getElementById("position-form");
    const noSelection = document.getElementById("no-selection");
    const selectedPositionSide = document.getElementById("selected-position-side");
    const propertyInputs = new Map(
        Array.from(positionForm.querySelectorAll("[data-field]"))
            .map(input => [input.dataset.field, input]));
    const metricOutputs = Object.freeze({
        riskDistance: document.getElementById("risk-distance"),
        rewardDistance: document.getElementById("reward-distance"),
        rewardRisk: document.getElementById("reward-risk"),
        riskAmount: document.getElementById("risk-amount"),
        riskQuantity: document.getElementById("risk-quantity"),
        leverageQuantity: document.getElementById("leverage-quantity"),
        finalQuantity: document.getElementById("final-quantity"),
        profitPnl: document.getElementById("profit-pnl"),
        lossPnl: document.getElementById("loss-pnl"),
        balanceTp: document.getElementById("balance-tp"),
        balanceSl: document.getElementById("balance-sl"),
        metadataSymbol: document.getElementById("metadata-symbol"),
        metadataTick: document.getElementById("metadata-tick"),
        metadataQuantity: document.getElementById("metadata-quantity")
    });

    const positions = new Map();
    const geometries = new Map();
    const barTimes = [];
    let selectedId = null;
    let activeTool = null;
    let dragState = null;
    let pendingDragMessage = null;
    let dragFrame = null;

    if (!window.LightweightCharts) {
        setStatus("کتابخانه نمودار محلی بارگذاری نشد.", true);
        return;
    }

    const chart = LightweightCharts.createChart(host, {
        layout: {
            background: { type: LightweightCharts.ColorType.Solid, color: "#101722" },
            textColor: "#b9c7d6",
            attributionLogo: false
        },
        grid: {
            vertLines: { color: "#1c2938" },
            horzLines: { color: "#1c2938" }
        },
        crosshair: {
            mode: LightweightCharts.CrosshairMode.Normal
        },
        rightPriceScale: {
            borderColor: "#34475c"
        },
        timeScale: {
            borderColor: "#34475c",
            timeVisible: true,
            secondsVisible: false,
            rightOffset: 4
        },
        handleScale: {
            axisPressedMouseMove: true,
            mouseWheel: true,
            pinch: true
        },
        handleScroll: {
            horzTouchDrag: true,
            mouseWheel: true,
            pressedMouseMove: true,
            vertTouchDrag: true
        }
    });

    const candleSeries = chart.addSeries(LightweightCharts.CandlestickSeries, {
        upColor: "#26a69a",
        downColor: "#ef5350",
        borderUpColor: "#26a69a",
        borderDownColor: "#ef5350",
        wickUpColor: "#26a69a",
        wickDownColor: "#ef5350",
        priceFormat: {
            type: "price",
            precision: 0,
            minMove: 1
        }
    });

    const chartCoordinateMapper = Object.freeze({
        horizontalValueToX: value => chart.timeScale().timeToCoordinate(value),
        xToHorizontalValue: x => chart.timeScale().coordinateToTime(x),
        priceToY: price => candleSeries.priceToCoordinate(price),
        yToPrice: y => candleSeries.coordinateToPrice(y)
    });

    const resizeObserver = new ResizeObserver(entries => {
        const entry = entries[0];
        if (!entry) {
            return;
        }

        const width = Math.max(1, Math.floor(entry.contentRect.width));
        const height = Math.max(1, Math.floor(entry.contentRect.height));
        chart.resize(width, height);
        resizeCanvas(width, height);
        renderDrawings();
    });
    resizeObserver.observe(host);

    chart.timeScale().subscribeVisibleLogicalRangeChange(renderDrawings);

    function resizeCanvas(width, height) {
        const ratio = window.devicePixelRatio || 1;
        canvas.width = Math.floor(width * ratio);
        canvas.height = Math.floor(height * ratio);
        canvas.style.width = `${width}px`;
        canvas.style.height = `${height}px`;
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
    }

    function postMessage(message) {
        if (!window.chrome?.webview) {
            setStatus("پل C# و JavaScript در دسترس نیست.", true);
            return;
        }

        window.chrome.webview.postMessage(message);
    }

    function setStatus(message, isError = false) {
        statusOutput.textContent = message;
        statusOutput.classList.toggle("error", isError);
    }

    function setActiveTool(tool) {
        activeTool = activeTool === tool ? null : tool;
        longButton.setAttribute("aria-pressed", String(activeTool === "Long"));
        shortButton.setAttribute("aria-pressed", String(activeTool === "Short"));
        host.style.cursor = activeTool ? "crosshair" : "default";
        setStatus(activeTool ? `روی نمودار برای ساخت ${activeTool} کلیک کنید.` : "یک ابزار را انتخاب کنید.");
    }

    function selectPosition(id) {
        selectedId = id;
        deleteButton.disabled = !id;
        if (id && positions.has(id)) {
            const side = positions.get(id).side;
            setStatus(`${side} Position انتخاب شد.`);
        }
        renderProperties();
        renderDrawings();
    }

    function deleteSelected() {
        if (selectedId) {
            postMessage({ type: "deletePosition", id: selectedId });
        }
    }

    function renderProperties() {
        const position = selectedId ? positions.get(selectedId) : null;
        positionForm.hidden = !position;
        noSelection.hidden = Boolean(position);
        selectedPositionSide.textContent = position
            ? `${position.side} · ${position.id.slice(0, 8)}`
            : "No selection";

        if (!position) {
            return;
        }

        const inputValues = {
            entryPrice: position.entryPrice,
            stopPrice: position.stopPrice,
            targetPrice: position.targetPrice,
            accountSize: position.accountSize,
            riskMode: position.riskMode,
            riskValue: position.riskValue,
            lotSize: position.symbol.lotSize,
            pointValue: position.symbol.pointValue,
            leverage: position.leverage,
            quantityPrecision: position.symbol.quantityPrecision
        };
        for (const [field, value] of Object.entries(inputValues)) {
            const input = propertyInputs.get(field);
            if (input) {
                input.value = String(value);
            }
        }

        const priceDigits = inferPriceDigits(position.symbol.tickSize);
        const quantityDigits = Number(position.symbol.quantityPrecision);
        metricOutputs.riskDistance.textContent =
            `${formatNumber(position.riskPerUnit, priceDigits)} (${formatNumber(position.riskPercent, 2)}%)`;
        metricOutputs.rewardDistance.textContent =
            `${formatNumber(position.rewardPerUnit, priceDigits)} (${formatNumber(position.rewardPercent, 2)}%)`;
        metricOutputs.rewardRisk.textContent = formatNumber(position.rewardToRiskRatio, 2);
        metricOutputs.riskAmount.textContent = formatNumber(position.riskAmount, 2);
        metricOutputs.riskQuantity.textContent = formatNumber(position.riskLimitedQuantity, quantityDigits);
        metricOutputs.leverageQuantity.textContent = formatNumber(position.leverageLimitedQuantity, quantityDigits);
        metricOutputs.finalQuantity.textContent = formatNumber(position.finalQuantity, quantityDigits);
        metricOutputs.profitPnl.textContent = formatNumber(position.profitPnl, 2);
        metricOutputs.lossPnl.textContent = formatNumber(position.lossPnl, 2);
        metricOutputs.balanceTp.textContent = formatNumber(position.accountBalanceAfterTp, 2);
        metricOutputs.balanceSl.textContent = formatNumber(position.accountBalanceAfterSl, 2);
        metricOutputs.metadataSymbol.textContent =
            `${position.symbol.symbolId} · ${position.timeframe}`;
        metricOutputs.metadataTick.textContent = formatNumber(position.symbol.tickSize, priceDigits);
        metricOutputs.metadataQuantity.textContent =
            `${formatNumber(position.symbol.quantityStep, quantityDigits)} / ${formatNumber(position.symbol.minimumQuantity, quantityDigits)}`;
    }

    function submitPropertyEdit(input) {
        if (!selectedId || !positions.has(selectedId) || !input.checkValidity()) {
            setStatus("مقدار ورودی معتبر نیست.", true);
            return;
        }

        const field = input.dataset.field;
        const value = input instanceof HTMLSelectElement ? input.value : Number(input.value);
        if (!field || (typeof value === "number" && !Number.isFinite(value))) {
            setStatus("مقدار ورودی معتبر نیست.", true);
            return;
        }

        postMessage({
            type: "editPosition",
            id: selectedId,
            field,
            value
        });
    }

    function nearestBarIndex(time) {
        if (!barTimes.length || typeof time !== "number") {
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

    function eventPoint(event) {
        const bounds = host.getBoundingClientRect();
        return {
            x: event.clientX - bounds.left,
            y: event.clientY - bounds.top
        };
    }

    function createPositionAt(point) {
        const entryPrice = chartCoordinateMapper.yToPrice(point.y);
        const clickTime = chartCoordinateMapper.xToHorizontalValue(point.x);
        const clickIndex = nearestBarIndex(clickTime);
        if (entryPrice === null || clickIndex < 0) {
            setStatus("این نقطه به قیمت/زمان معتبر نگاشت نشد.", true);
            return;
        }

        const widthInBars = Math.min(8, Math.max(1, barTimes.length - 1));
        const startIndex = Math.min(clickIndex, barTimes.length - 1 - widthInBars);
        const endIndex = startIndex + widthInBars;
        postMessage({
            type: "createPosition",
            side: activeTool,
            entryPrice,
            startTime: barTimes[startIndex],
            endTime: barTimes[endIndex]
        });
        setActiveTool(null);
    }

    function geometryFor(position) {
        const leftX = chartCoordinateMapper.horizontalValueToX(position.startTime);
        const rightX = chartCoordinateMapper.horizontalValueToX(position.endTime);
        const targetY = chartCoordinateMapper.priceToY(position.targetPrice);
        const entryY = chartCoordinateMapper.priceToY(position.entryPrice);
        const stopY = chartCoordinateMapper.priceToY(position.stopPrice);

        if ([leftX, rightX, targetY, entryY, stopY].some(value => value === null || !Number.isFinite(value))) {
            return null;
        }

        return {
            leftX: Math.min(leftX, rightX),
            rightX: Math.max(leftX, rightX),
            targetY,
            entryY,
            stopY,
            topY: Math.min(targetY, entryY, stopY),
            bottomY: Math.max(targetY, entryY, stopY)
        };
    }

    function renderDrawings() {
        const width = host.clientWidth;
        const height = host.clientHeight;
        context.clearRect(0, 0, width, height);
        geometries.clear();

        for (const position of positions.values()) {
            const geometry = geometryFor(position);
            if (!geometry) {
                continue;
            }

            geometries.set(position.id, geometry);
            drawPosition(position, geometry, position.id === selectedId);
        }
    }

    function drawPosition(position, geometry, selected) {
        const { leftX, rightX, targetY, entryY, stopY } = geometry;
        const rewardTop = Math.min(targetY, entryY);
        const rewardHeight = Math.abs(targetY - entryY);
        const riskTop = Math.min(stopY, entryY);
        const riskHeight = Math.abs(stopY - entryY);
        const width = rightX - leftX;

        context.fillStyle = "rgba(38, 166, 154, 0.22)";
        context.fillRect(leftX, rewardTop, width, rewardHeight);
        context.fillStyle = "rgba(239, 83, 80, 0.22)";
        context.fillRect(leftX, riskTop, width, riskHeight);

        drawLevel(leftX, rightX, targetY, "#26a69a", false);
        drawLevel(leftX, rightX, entryY, "#e7edf5", true);
        drawLevel(leftX, rightX, stopY, "#ef5350", false);

        context.strokeStyle = selected ? "#5ab0ff" : "rgba(171, 190, 209, 0.65)";
        context.lineWidth = selected ? 1.5 : 1;
        context.strokeRect(leftX, geometry.topY, width, geometry.bottomY - geometry.topY);

        const priceDigits = inferPriceDigits(position.symbol.tickSize);
        const quantityDigits = Number(position.symbol.quantityPrecision);
        drawLabel(
            rightX,
            targetY,
            `TP ${formatNumber(position.targetPrice, priceDigits)}  +${formatNumber(position.rewardPercent, 2)}%  PnL ${formatNumber(position.profitPnl, 2)}`,
            "#126a61");
        drawLabel(
            rightX,
            entryY,
            `Entry ${formatNumber(position.entryPrice, priceDigits)}  Qty ${formatNumber(position.finalQuantity, quantityDigits)}  R:R ${formatNumber(position.rewardToRiskRatio, 2)}`,
            "#35475a");
        drawLabel(
            rightX,
            stopY,
            `SL ${formatNumber(position.stopPrice, priceDigits)}  -${formatNumber(position.riskPercent, 2)}%  PnL ${formatNumber(position.lossPnl, 2)}`,
            "#862f3a");

        if (selected) {
            drawHandle(leftX, targetY, "#26a69a");
            drawHandle(leftX, entryY, "#e7edf5");
            drawHandle(leftX, stopY, "#ef5350");
        }
    }

    function drawLevel(leftX, rightX, y, color, dashed) {
        context.beginPath();
        context.setLineDash(dashed ? [6, 4] : []);
        context.moveTo(leftX, y);
        context.lineTo(rightX, y);
        context.strokeStyle = color;
        context.lineWidth = 1.25;
        context.stroke();
        context.setLineDash([]);
    }

    function drawHandle(x, y, color) {
        context.beginPath();
        context.arc(x, y, 5, 0, Math.PI * 2);
        context.fillStyle = "#101722";
        context.fill();
        context.strokeStyle = color;
        context.lineWidth = 2;
        context.stroke();
    }

    function drawLabel(rightX, y, text, background) {
        context.font = "12px Segoe UI, sans-serif";
        const paddingX = 7;
        const labelWidth = context.measureText(text).width + paddingX * 2;
        const labelHeight = 22;
        const x = Math.min(rightX + 5, Math.max(0, host.clientWidth - labelWidth - 84));
        const top = Math.max(0, Math.min(host.clientHeight - labelHeight, y - labelHeight / 2));
        context.fillStyle = background;
        context.fillRect(x, top, labelWidth, labelHeight);
        context.fillStyle = "#ffffff";
        context.textBaseline = "middle";
        context.fillText(text, x + paddingX, top + labelHeight / 2);
    }

    function inferPriceDigits(tickSize = window.symbolMetadata?.tickSize ?? 1) {
        const tick = Number(tickSize);
        if (!Number.isFinite(tick) || Number.isInteger(tick)) {
            return 0;
        }

        return Math.min(8, tick.toFixed(8).replace(/0+$/, "").split(".")[1]?.length ?? 0);
    }

    function formatNumber(value, digits) {
        return Number(value).toLocaleString("en-US", {
            minimumFractionDigits: digits,
            maximumFractionDigits: digits
        });
    }

    function hitTest(point) {
        const entries = Array.from(positions.values()).reverse();
        for (const position of entries) {
            const geometry = geometries.get(position.id);
            if (!geometry) {
                continue;
            }

            const insideX = point.x >= geometry.leftX - 7 && point.x <= geometry.rightX + 7;
            if (insideX) {
                if (Math.abs(point.y - geometry.targetY) <= 7) {
                    return { id: position.id, kind: "handle", handle: "Target" };
                }
                if (Math.abs(point.y - geometry.entryY) <= 7) {
                    return { id: position.id, kind: "handle", handle: "Entry" };
                }
                if (Math.abs(point.y - geometry.stopY) <= 7) {
                    return { id: position.id, kind: "handle", handle: "Stop" };
                }
            }

            if (point.x >= geometry.leftX && point.x <= geometry.rightX &&
                point.y >= geometry.topY && point.y <= geometry.bottomY) {
                return { id: position.id, kind: "body" };
            }
        }

        return null;
    }

    function queueDragMessage(message) {
        pendingDragMessage = message;
        if (dragFrame !== null) {
            return;
        }

        dragFrame = requestAnimationFrame(() => {
            dragFrame = null;
            if (pendingDragMessage) {
                postMessage(pendingDragMessage);
                pendingDragMessage = null;
            }
        });
    }

    host.addEventListener("pointerdown", event => {
        if (event.button !== 0) {
            return;
        }

        const point = eventPoint(event);
        if (activeTool) {
            event.preventDefault();
            event.stopImmediatePropagation();
            createPositionAt(point);
            return;
        }

        const hit = hitTest(point);
        if (!hit) {
            selectPosition(null);
            return;
        }

        const position = positions.get(hit.id);
        const pointerPrice = chartCoordinateMapper.yToPrice(point.y);
        const pointerTime = chartCoordinateMapper.xToHorizontalValue(point.x);
        const pointerIndex = nearestBarIndex(pointerTime);
        if (pointerPrice === null || pointerIndex < 0) {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        host.setPointerCapture(event.pointerId);
        selectPosition(hit.id);
        dragState = {
            pointerId: event.pointerId,
            hit,
            pointerPrice,
            pointerIndex,
            entryPrice: position.entryPrice,
            startIndex: nearestBarIndex(position.startTime),
            endIndex: nearestBarIndex(position.endTime)
        };
        host.style.cursor = hit.kind === "body" ? "move" : "ns-resize";
    }, true);

    host.addEventListener("pointermove", event => {
        if (!dragState || dragState.pointerId !== event.pointerId) {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        const point = eventPoint(event);
        const price = chartCoordinateMapper.yToPrice(point.y);
        if (price === null) {
            return;
        }

        if (dragState.hit.kind === "handle") {
            queueDragMessage({
                type: "updatePosition",
                id: dragState.hit.id,
                handle: dragState.hit.handle,
                proposedPrice: price
            });
            return;
        }

        const currentTime = chartCoordinateMapper.xToHorizontalValue(point.x);
        const currentIndex = nearestBarIndex(currentTime);
        if (currentIndex < 0) {
            return;
        }

        const requestedDelta = currentIndex - dragState.pointerIndex;
        const minimumDelta = -dragState.startIndex;
        const maximumDelta = barTimes.length - 1 - dragState.endIndex;
        const barDelta = Math.max(minimumDelta, Math.min(maximumDelta, requestedDelta));
        queueDragMessage({
            type: "movePosition",
            id: dragState.hit.id,
            proposedEntryPrice: dragState.entryPrice + (price - dragState.pointerPrice),
            proposedStartTime: barTimes[dragState.startIndex + barDelta]
        });
    }, true);

    function endDrag(event) {
        if (!dragState || dragState.pointerId !== event.pointerId) {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        if (host.hasPointerCapture(event.pointerId)) {
            host.releasePointerCapture(event.pointerId);
        }
        dragState = null;
        host.style.cursor = activeTool ? "crosshair" : "default";
    }

    host.addEventListener("pointerup", endDrag, true);
    host.addEventListener("pointercancel", endDrag, true);

    longButton.addEventListener("click", () => setActiveTool("Long"));
    shortButton.addEventListener("click", () => setActiveTool("Short"));
    deleteButton.addEventListener("click", deleteSelected);
    saveButton.addEventListener("click", () => postMessage({ type: "savePositions" }));
    loadButton.addEventListener("click", () => postMessage({ type: "loadPositions" }));
    positionForm.addEventListener("change", event => {
        const input = event.target;
        if (input instanceof HTMLInputElement || input instanceof HTMLSelectElement) {
            submitPropertyEdit(input);
        }
    });

    document.addEventListener("keydown", event => {
        if (event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement) {
            if (event.key === "Escape") {
                event.target.blur();
            }
            return;
        }

        if (event.key === "Delete" || event.key === "Backspace") {
            event.preventDefault();
            deleteSelected();
        } else if (event.key === "Escape") {
            if (activeTool) {
                setActiveTool(null);
            } else {
                selectPosition(null);
            }
        } else if (event.key.toLowerCase() === "l") {
            setActiveTool("Long");
        } else if (event.key.toLowerCase() === "s") {
            setActiveTool("Short");
        }
    });

    window.addEventListener("error", event => {
        postMessage({
            type: "clientError",
            message: String(event.message || "Unknown JavaScript runtime error").slice(0, 500)
        });
    });

    window.addEventListener("unhandledrejection", event => {
        postMessage({
            type: "clientError",
            message: String(event.reason || "Unhandled JavaScript rejection").slice(0, 500)
        });
    });

    window.chrome?.webview?.addEventListener("message", event => {
        const message = event.data;
        switch (message?.type) {
            case "initialize":
                window.symbolMetadata = message.symbol;
                symbolName.textContent = message.symbol.symbolId;
                barTimes.splice(0, barTimes.length, ...message.bars.map(bar => bar.time));
                candleSeries.applyOptions({
                    priceFormat: {
                        type: "price",
                        precision: inferPriceDigits(message.symbol.tickSize),
                        minMove: Number(message.symbol.tickSize)
                    }
                });
                candleSeries.setData(message.bars);
                chart.timeScale().fitContent();
                setStatus(`Long یا Short را انتخاب کنید. ذخیره محلی: ${message.persistenceFileName}`);
                renderDrawings();
                break;
            case "positionState":
                positions.set(message.position.id, message.position);
                selectPosition(message.position.id);
                break;
            case "positionsReplaced": {
                const previousSelection = selectedId;
                positions.clear();
                for (const position of message.positions) {
                    positions.set(position.id, position);
                }
                const replacementSelection = positions.has(previousSelection)
                    ? previousSelection
                    : positions.keys().next().value ?? null;
                selectPosition(replacementSelection);
                break;
            }
            case "positionDeleted":
                positions.delete(message.id);
                if (selectedId === message.id) {
                    selectPosition(null);
                } else {
                    renderDrawings();
                }
                setStatus("Position حذف شد.");
                break;
            case "barUpdate":
                candleSeries.update(message.bar);
                if (!barTimes.includes(message.bar.time)) {
                    barTimes.push(message.bar.time);
                }
                realtimeStatus.textContent =
                    `Realtime mock: ${message.updateCount} · Positions: ${message.positionCount}`;
                renderDrawings();
                break;
            case "bridgeError":
                setStatus(message.message, true);
                break;
            case "operationStatus":
                setStatus(message.message);
                break;
            default:
                break;
        }
    });

    postMessage({ type: "ready" });
})();
