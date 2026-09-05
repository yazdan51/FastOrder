# FastOrder.ChartTools.Tests

These tests cover the verified TradingView formulas and the explicitly documented PoC decisions.

They also cover validated per-position inputs, combined derived metrics, independent multi-position selection state, stale-id rejection, horizontal range editing, quantity precision/step behavior, repeated realtime-style reads, local atomic-save failure behavior, missing/corrupt files, and versioned persistence round trips/rejections. The companion dependency-free Node tests cover canvas hit testing, cursor intent, bar-bound movement/range resizing, and label spacing.

The official TradingView Long/Short Position article does not currently contain a complete worked numerical example. Test vectors in this project are therefore direct arithmetic applications of the published formulas, not claimed TradingView example values.
