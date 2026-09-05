# FastOrder.ChartTools

`FastOrder.ChartTools` contains analysis-only chart drawing state, TradingView-referenced calculations, market normalization abstractions, coordinate mapping, and interaction transformations.

Dependency boundary:

- `FastOrder` may reference `FastOrder.ChartTools`.
- `FastOrder.ChartTools` must not reference `FastOrder`, `FastOrder.Manager`, WebView2, broker authentication, browser state, scheduling, or order-submission components.
- The library does not click broker controls, send requests, or submit orders.
- Iran-market rules are provided as symbol metadata; the library does not guess exchange rules.

`TRADINGVIEW_REFERENCE.md` records the verified formulas, unresolved behavior, and explicit PoC decisions. Chart-host-specific pointer wiring remains outside this library.
