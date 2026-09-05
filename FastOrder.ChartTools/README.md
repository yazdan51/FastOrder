# FastOrder.ChartTools

`FastOrder.ChartTools` contains analysis-only chart drawing state, calculations, coordinate mapping, and interaction transformations.

Dependency boundary:

- `FastOrder` may reference `FastOrder.ChartTools`.
- `FastOrder.ChartTools` must not reference `FastOrder`, `FastOrder.Manager`, WebView2, broker authentication, browser state, scheduling, or order-submission components.
- The library does not click broker controls, send requests, or submit orders.

The current layer deliberately stops before chart-host-specific rendering and pointer-event wiring. Those adapters can be added once the chart surface and its coordinate system are selected.
