# TradingView Long/Short Position reference boundary

Primary source: https://www.tradingview.com/support/solutions/43000475660-how-to-use-long-and-short-position-drawing-tools/

## Verified formulas

For a long position:

- `QtyRisk = (RiskSize / ((EntryPrice - StopPrice) * PointValue)) / LotSize`
- `QtyLvg = (AccountSize * Leverage / EntryPrice) * PointValue / LotSize`
- `Qty = min(QtyRisk, QtyLvg)`
- `TP offset = ProfitPrice - EntryPrice`
- `TP percent = TP offset / EntryPrice * 100`
- `SL offset = EntryPrice - StopPrice`
- `SL percent = SL offset / EntryPrice * 100`
- `R:R = TP offset / SL offset`
- `Profit PnL = TP offset * Qty * PointValue * LotSize`
- `Loss PnL = (StopPrice - EntryPrice) * Qty * PointValue * LotSize`

For a short position:

- `QtyRisk = (RiskSize / ((StopPrice - EntryPrice) * PointValue)) / LotSize`
- `QtyLvg = (AccountSize * Leverage / EntryPrice) * PointValue / LotSize`
- `Qty = min(QtyRisk, QtyLvg)`
- `TP offset = EntryPrice - ProfitPrice`
- `TP percent = TP offset / EntryPrice * 100`
- `SL offset = StopPrice - EntryPrice`
- `SL percent = SL offset / EntryPrice * 100`
- `R:R = TP offset / SL offset`
- `Profit PnL = TP offset * Qty * PointValue * LotSize`
- `Loss PnL = (EntryPrice - StopPrice) * Qty * PointValue * LotSize`

`RiskSize` is the absolute risk value, or `RiskPercent / 100 * AccountSize` when percentage mode is selected.

## Verified input/property concepts

The official article states that the properties dialog accepts account size, risk as either an absolute amount or account percentage, lot size, leverage, entry price, profit level, stop level, and quantity precision. Point Value appears in the official size and PnL formulas, but the article does not define its instrument-specific source or say that it is universally user-editable.

## UNRESOLVED in the official reference

- Default Stop and Target offsets when a drawing is first created.
- Exact handle-crossing behavior when Stop, Entry, or Target crosses another level.
- Tie-breaking rules for tick normalization and quantity-step rounding.
- How quantity precision interacts with venue-specific minimums and step sizes.
- Instrument-specific interpretation and data source for Point Value and Lot Size.
- A complete numerical worked example suitable for exact test-vector reuse.
- Whether official properties apply continuously while typing or only after the dialog's OK action. ChartViewer intentionally applies valid committed field edits immediately.
- Persistence schema, multi-position selection ordering, and local save location; these are ChartViewer product choices, not TradingView behaviors.

## PoC implementation decisions

These behaviors are not represented as TradingView defaults:

- Long defaults: Stop 1% below Entry and Target 2% above Entry.
- Short defaults: Stop 1% above Entry and Target 2% below Entry.
- Invalid level crossing is clamped to one supplied tick from the neighboring level.
- Prices normalize to the supplied symbol tick; midpoint ties round away from zero.
- Final quantity is floored to the supplied quantity step so it does not exceed either raw constraint.
- A final quantity below the supplied minimum quantity becomes zero.
- Quantity precision defaults to the decimal scale of quantity step. An explicitly lower precision than the configured step is rejected.
- Each position snapshots its own sizing inputs and mock symbol metadata so edits cannot affect another drawing.
- JSON schema version `1` persists ids, symbol/timeframe, direction, price/time anchors, sizing inputs, and normalization metadata; derived metrics are omitted and recalculated.

## ChartViewer UX decisions

These interaction choices are local product decisions and are not claimed as undocumented TradingView behavior:

- The selected drawing is rendered last with a stronger outline; only it exposes draggable price and horizontal-range handles.
- A first click on an unselected drawing selects it without moving it. A selected line, body, or handle begins the corresponding interaction.
- Empty-chart pointer input deselects without blocking Lightweight Charts pan/zoom behavior.
- Horizontal edge dragging changes only persisted time anchors and preserves Entry, Stop, and Target prices.
- Labels for the selected drawing are spaced and clamped to the visible chart. Non-selected drawings use a compact side/entry badge to reduce overlap.
- `Escape` stops the active interaction at its latest C#-validated state; it does not implement an undo transaction.
- Schema version `1` is strict. Future changes require an explicit version bump and tested migration; silent best-effort migration is intentionally unsupported.

Iran-market metadata is deliberately supplied by the caller. The ChartViewer demo uses a visible `IR_DEMO_MOCK` profile only to demonstrate tick normalization and quantity floor-to-step behavior. It is non-authoritative and does not claim actual exchange or symbol rules. The adapter does not hardcode exchange rules because tick size, quantity step, and other symbol rules can vary and must come from a verified market-data source.
