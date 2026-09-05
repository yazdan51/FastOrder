# FastOrder.ChartTools

`FastOrder.ChartTools` contains analysis-only chart drawing state, TradingView-referenced calculations, market normalization abstractions, coordinate mapping, interaction transformations, multi-position workspace state, and versioned JSON persistence contracts.

Dependency boundary:

- `FastOrder` may reference `FastOrder.ChartTools`.
- `FastOrder.ChartTools` must not reference `FastOrder`, `FastOrder.Manager`, WebView2, broker authentication, browser state, scheduling, or order-submission components.
- The library does not click broker controls, send requests, or submit orders.
- Iran-market rules are provided as symbol metadata; the library does not guess exchange rules.
- `PositionAnalysisState` is the per-drawing source of truth for levels, risk/sizing inputs, timeframe, and symbol metadata. Derived metrics are calculated on demand and are never persisted.
- `PositionWorkspace` keeps multiple positions isolated by `Guid` and validates transactional replacement during load.
- `PositionDocumentSerializer` currently writes schema version `1`, rejects unknown fields/versions and duplicate ids, and caps input documents at 1 MiB.

`TRADINGVIEW_REFERENCE.md` records the verified formulas, unresolved behavior, and explicit PoC decisions. Chart-host-specific pointer wiring and local file I/O remain outside this library.
