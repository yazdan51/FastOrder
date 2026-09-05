# FastOrder.ChartViewer

Independent analysis-only PoC host for `FastOrder.ChartTools`.

Architecture:

`WPF shell -> controlled local WebView2 -> offline HTML/JS -> TradingView Lightweight Charts`

The C# host owns position state and validates all edits. JavaScript owns chart rendering and pointer interaction, but sends price/time values rather than storing pixel coordinates as drawing anchors. The only data source is deterministic mock OHLC data generated in-process.

This project has no reference to `FastOrder`, broker pages, authentication, scheduling, order bridges, or order submission. It is intentionally not integrated into the main FastOrder window.
