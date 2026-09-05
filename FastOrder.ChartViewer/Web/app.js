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
    const propertiesPanel = document.getElementById("properties-panel");
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

    const interaction = window.FastOrderPositionInteraction;

    if (!window.LightweightCharts || !interaction) {
        setStatus("کتابخانه‌های محلی نمودار/تعامل بارگذاری نشدند.", true);
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

    function selectPosition(id, options = {}) {
        const { notifyHost = true, announce = true } = options;
        const resolvedId = id && positions.has(id) ? id : null;
        const changed = selectedId !== resolvedId;
        selectedId = resolvedId;
        deleteButton.disabled = !selectedId;

        const selectedPosition = selectedId ? positions.get(selectedId) : null;
        propertiesPanel.dataset.side = selectedPosition?.side ?? "";
        if (announce && selectedPosition) {
            setStatus(`${selectedPosition.side} Position انتخاب شد.`);
        }
        if (notifyHost && changed) {
            postMessage({ type: "selectPosition", id: selectedId });
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
                input.removeAttribute("aria-invalid");
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
        const field = input.dataset.field;
        const value = input instanceof HTMLSelectElement ? input.value : Number(input.value);
        const validValue = interaction.isValidPropertyValue(field, value);

        if (!selectedId || !positions.has(selectedId) || !field || !input.checkValidity() || !validValue) {
            renderProperties();
            input.setAttribute("aria-invalid", "true");
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
        return interaction.nearestBarIndex(barTimes, time);
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

        const orderedPositions = [
            ...Array.from(positions.values()).filter(position => position.id !== selectedId),
            ...Array.from(positions.values()).filter(position => position.id === selectedId)
        ];
        for (const position of orderedPositions) {
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

        context.save();
        context.globalAlpha = selected ? 1 : 0.62;
        context.fillStyle = selected ? "rgba(38, 166, 154, 0.25)" : "rgba(38, 166, 154, 0.16)";
        context.fillRect(leftX, rewardTop, width, rewardHeight);
        context.fillStyle = selected ? "rgba(239, 83, 80, 0.25)" : "rgba(239, 83, 80, 0.16)";
        context.fillRect(leftX, riskTop, width, riskHeight);

        drawLevel(leftX, rightX, targetY, "#26a69a", false, selected);
        drawLevel(leftX, rightX, entryY, "#e7edf5", true, selected);
        drawLevel(leftX, rightX, stopY, "#ef5350", false, selected);

        context.save();
        context.strokeStyle = selected ? "#5ab0ff" : "rgba(171, 190, 209, 0.65)";
        context.lineWidth = selected ? 2 : 1;
        if (selected) {
            context.shadowColor = "rgba(90, 176, 255, 0.72)";
            context.shadowBlur = 8;
        }
        context.strokeRect(leftX, geometry.topY, width, geometry.bottomY - geometry.topY);
        context.restore();

        const priceDigits = inferPriceDigits(position.symbol.tickSize);
        const quantityDigits = Number(position.symbol.quantityPrecision);
        drawSideBadge(position, geometry, priceDigits, selected);

        if (selected) {
            const labelTops = interaction.layoutLabelTops(
                [targetY, entryY, stopY],
                host.clientHeight,
                22,
                3);
            drawLabel(
                rightX,
                labelTops[0],
                `TP ${formatNumber(position.targetPrice, priceDigits)}  +${formatNumber(position.rewardPercent, 2)}%  PnL ${formatNumber(position.profitPnl, 2)}`,
                "#126a61");
            drawLabel(
                rightX,
                labelTops[1],
                `Entry ${formatNumber(position.entryPrice, priceDigits)}  Qty ${formatNumber(position.finalQuantity, quantityDigits)}  R:R ${formatNumber(position.rewardToRiskRatio, 2)}`,
                "#35475a");
            drawLabel(
                rightX,
                labelTops[2],
                `SL ${formatNumber(position.stopPrice, priceDigits)}  -${formatNumber(position.riskPercent, 2)}%  PnL ${formatNumber(position.lossPnl, 2)}`,
                "#862f3a");
            drawHandle(leftX, targetY, "#26a69a");
            drawHandle(leftX, entryY, "#e7edf5");
            drawHandle(leftX, stopY, "#ef5350");
            const middleY = (geometry.topY + geometry.bottomY) / 2;
            drawRangeHandle(leftX, middleY);
            drawRangeHandle(rightX, middleY);
        }

        context.restore();
    }

    function drawLevel(leftX, rightX, y, color, dashed, selected) {
        context.beginPath();
        context.setLineDash(dashed ? [6, 4] : []);
        context.moveTo(leftX, y);
        context.lineTo(rightX, y);
        context.strokeStyle = color;
        context.lineWidth = selected ? 1.6 : 1.1;
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

    function drawRangeHandle(x, y) {
        context.fillStyle = "#101722";
        context.fillRect(x - 5, y - 8, 10, 16);
        context.strokeStyle = "#5ab0ff";
        context.lineWidth = 2;
        context.strokeRect(x - 5, y - 8, 10, 16);
    }

    function drawSideBadge(position, geometry, priceDigits, selected) {
        const label = `${position.side.toUpperCase()}  ${formatNumber(position.entryPrice, priceDigits)}`;
        context.font = `${selected ? "600" : "500"} 11px Segoe UI, sans-serif`;
        const width = context.measureText(label).width + 12;
        const height = 19;
        const x = Math.max(2, Math.min(host.clientWidth - width - 2, geometry.leftX + 7));
        const y = Math.max(2, Math.min(host.clientHeight - height - 2, geometry.topY + 7));
        context.fillStyle = position.side === "Long" ? "#126a61" : "#862f3a";
        context.fillRect(x, y, width, height);
        context.fillStyle = "#ffffff";
        context.textBaseline = "middle";
        context.fillText(label, x + 6, y + height / 2);
    }

    function drawLabel(rightX, top, text, background) {
        context.font = "12px Segoe UI, sans-serif";
        const paddingX = 7;
        const labelWidth = context.measureText(text).width + paddingX * 2;
        const labelHeight = 22;
        const x = Math.min(rightX + 5, Math.max(0, host.clientWidth - labelWidth - 84));
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
        const safeDigits = Number.isInteger(digits)
            ? Math.max(0, Math.min(20, digits))
            : 0;
        return Number(value).toLocaleString("en-US", {
            minimumFractionDigits: safeDigits,
            maximumFractionDigits: safeDigits
        });
    }

    function hitTest(point) {
        return interaction.hitTest(Array.from(positions.values()), geometries, selectedId, point);
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

    function flushPendingDragMessage() {
        if (dragFrame !== null) {
            cancelAnimationFrame(dragFrame);
            dragFrame = null;
        }
        if (pendingDragMessage) {
            postMessage(pendingDragMessage);
            pendingDragMessage = null;
        }
    }

    function discardPendingDragMessage() {
        if (dragFrame !== null) {
            cancelAnimationFrame(dragFrame);
            dragFrame = null;
        }
        pendingDragMessage = null;
    }

    function updateHoverCursor(point) {
        host.style.cursor = interaction.cursorForHit(hitTest(point), activeTool);
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

        if (hit.kind === "select") {
            event.preventDefault();
            event.stopImmediatePropagation();
            selectPosition(hit.id);
            return;
        }

        const position = positions.get(hit.id);
        const pointerTime = chartCoordinateMapper.xToHorizontalValue(point.x);
        const pointerIndex = nearestBarIndex(pointerTime);
        const pointerPrice = chartCoordinateMapper.yToPrice(point.y);
        if (pointerIndex < 0 || (hit.kind !== "range-handle" && pointerPrice === null)) {
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        host.setPointerCapture(event.pointerId);
        selectPosition(hit.id);
        dragState = {
            pointerId: event.pointerId,
            hit,
            pointerPrice: pointerPrice ?? position.entryPrice,
            pointerIndex,
            entryPrice: position.entryPrice,
            startIndex: nearestBarIndex(position.startTime),
            endIndex: nearestBarIndex(position.endTime)
        };
        host.style.cursor = interaction.cursorForHit(hit, activeTool);
    }, true);

    host.addEventListener("pointermove", event => {
        if (!dragState || dragState.pointerId !== event.pointerId) {
            updateHoverCursor(eventPoint(event));
            return;
        }

        event.preventDefault();
        event.stopImmediatePropagation();
        const point = eventPoint(event);

        if (dragState.hit.kind === "range-handle") {
            const currentTime = chartCoordinateMapper.xToHorizontalValue(point.x);
            const currentIndex = nearestBarIndex(currentTime);
            if (currentIndex < 0) {
                return;
            }

            const resizedIndex = interaction.clampResizeIndex(
                dragState.hit.handle,
                currentIndex,
                dragState.startIndex,
                dragState.endIndex,
                barTimes.length);
            queueDragMessage({
                type: "resizePosition",
                id: dragState.hit.id,
                handle: dragState.hit.handle,
                proposedTime: barTimes[resizedIndex]
            });
            return;
        }

        const price = chartCoordinateMapper.yToPrice(point.y);
        if (price === null) {
            return;
        }

        if (dragState.hit.kind === "price-handle") {
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

        const barDelta = interaction.clampMoveDelta(
            dragState.pointerIndex,
            currentIndex,
            dragState.startIndex,
            dragState.endIndex,
            barTimes.length);
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
        flushPendingDragMessage();
        if (host.hasPointerCapture(event.pointerId)) {
            host.releasePointerCapture(event.pointerId);
        }
        dragState = null;
        updateHoverCursor(eventPoint(event));
    }

    function cancelActiveDrag() {
        if (!dragState) {
            return false;
        }

        discardPendingDragMessage();
        if (host.hasPointerCapture(dragState.pointerId)) {
            host.releasePointerCapture(dragState.pointerId);
        }
        dragState = null;
        host.style.cursor = activeTool ? "crosshair" : "default";
        setStatus("تعامل فعال متوقف شد؛ آخرین مقدار معتبر حفظ شد.");
        return true;
    }

    host.addEventListener("pointerup", endDrag, true);
    host.addEventListener("pointercancel", endDrag, true);
    host.addEventListener("pointerleave", event => {
        if (!dragState) {
            updateHoverCursor(eventPoint(event));
        }
    }, true);

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
    positionForm.addEventListener("input", event => {
        if (event.target instanceof HTMLInputElement || event.target instanceof HTMLSelectElement) {
            event.target.removeAttribute("aria-invalid");
        }
    });

    document.addEventListener("keydown", event => {
        const target = event.target;
        const isPropertiesControl = target instanceof Element && Boolean(target.closest("#properties-panel"));
        if (isPropertiesControl) {
            if (event.key === "Escape") {
                event.preventDefault();
                event.stopPropagation();
                renderProperties();
                if (target instanceof HTMLElement) {
                    target.blur();
                }
            }
            return;
        }

        if (event.ctrlKey || event.altKey || event.metaKey) {
            return;
        }

        if (event.key === "Delete" || event.key === "Backspace") {
            event.preventDefault();
            deleteSelected();
        } else if (event.key === "Escape") {
            if (cancelActiveDrag()) {
                event.preventDefault();
            } else if (activeTool) {
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
                selectPosition(message.selectedId, { notifyHost: false, announce: false });
                break;
            case "positionsReplaced": {
                positions.clear();
                for (const position of message.positions) {
                    positions.set(position.id, position);
                }
                selectPosition(message.selectedId, { notifyHost: false, announce: false });
                break;
            }
            case "positionDeleted":
                positions.delete(message.id);
                selectPosition(message.selectedId, { notifyHost: false, announce: false });
                setStatus("Position حذف شد.");
                break;
            case "selectionChanged":
                selectPosition(message.selectedId, { notifyHost: false, announce: false });
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
