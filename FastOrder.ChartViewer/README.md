# FastOrder.ChartViewer

Independent analysis-only PoC host for `FastOrder.ChartTools`.

Architecture:

`WPF shell -> controlled local WebView2 -> offline HTML/JS -> TradingView Lightweight Charts`

The C# host owns every position, validates/clamps price edits, calculates metrics, and serializes source fields. JavaScript owns chart rendering, selection, the properties panel, and pointer interaction, but sends values rather than storing pixel coordinates as drawing anchors. The only data source is deterministic mock OHLC data generated in-process.

Current product layer:

- selected-position Inputs panel for entry, stop, target, account size, absolute/percent risk, lot size, point value, leverage, and quantity precision;
- immediate C#-validated panel-to-drawing and drag-to-panel synchronization;
- independent create/select/edit/delete behavior for multiple positions;
- Save/Load buttons backed by versioned JSON at `%LOCALAPPDATA%\FastOrder\ChartViewer\positions.v1.json`;
- a clearly labeled, non-authoritative Iran-market mock profile (`TickSize=10`, `QuantityStep=100`, `MinimumQuantity=100`);
- mock realtime candles that continue while drawings exist; update messages carry a diagnostic update/position count.

Save files contain only source-of-truth fields. Quantity, R:R, PnL, and balances are recalculated after load. Loaded horizontal time anchors continue to map through Lightweight Charts during zoom and pan.

This project has no reference to `FastOrder`, broker pages, authentication, scheduling, order bridges, or order submission. It is intentionally not integrated into the main FastOrder window.
