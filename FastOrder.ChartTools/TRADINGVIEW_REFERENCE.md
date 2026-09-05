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

## UNRESOLVED in the official reference

- Default Stop and Target offsets when a drawing is first created.
- Exact handle-crossing behavior when Stop, Entry, or Target crosses another level.
- Tie-breaking rules for tick normalization and quantity-step rounding.
- How quantity precision interacts with venue-specific minimums and step sizes.
- Instrument-specific interpretation and data source for Point Value and Lot Size.
- A complete numerical worked example suitable for exact test-vector reuse.

## PoC implementation decisions

These behaviors are not represented as TradingView defaults:

- Long defaults: Stop 1% below Entry and Target 2% above Entry.
- Short defaults: Stop 1% above Entry and Target 2% below Entry.
- Invalid level crossing is clamped to one supplied tick from the neighboring level.
- Prices normalize to the supplied symbol tick; midpoint ties round away from zero.
- Final quantity is floored to the supplied quantity step so it does not exceed either raw constraint.
- A final quantity below the supplied minimum quantity becomes zero.

Iran-market metadata is deliberately supplied by the caller. The adapter does not hardcode exchange rules because tick size, quantity step, and other symbol rules can vary and must come from a verified market-data source.
