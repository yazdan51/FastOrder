# FastOrder Stage Implementation Log

This document is the implementation record for the staged multi-session redesign defined in
[`MULTI_SESSION_ARCHITECTURE.md`](MULTI_SESSION_ARCHITECTURE.md). It records what was actually
delivered in each stage, the preserved safety boundaries, verification evidence, and the Git
commit that contains the implementation.

## Maintenance rules

- Update this document when each implementation stage is completed.
- Keep each stage in a dedicated implementation commit whenever practical.
- Record only verified behavior; planned work must remain explicitly marked as not started.
- For every completed stage, list its scope, changed files, non-goals, verification, and commit.
- Do not treat a UI click, `CLICKED`, Service Worker activity, or HTTP `2xx` as proof that the
  broker accepted or filled an order. EasyTrader's broker order list remains authoritative.
- Never document or store token, cookie, authorization header, or private request-body values.

## Status summary

| Stage | Status | Completed | Implementation commit |
| --- | --- | --- | --- |
| 74 — Responsive UI shell | Completed | 2026-09-01 | `4c670098d6e204510f5488b96df4b01a259798b2` |
| 75 — Session model + session list UI | Completed | 2026-09-01 | `b5cfd60ecc580a9069f89a931b6ae77a5ab3e776` |
| 75.1 — Exchange-synchronized scheduler clock | Completed | 2026-09-01 | `35d004d4b777ddb3c2fa48f81293e822c5e155d0` |
| 76 — Move confirmation ownership into sessions | Not started | — | — |
| 77 — Central Official UI Dispatcher | Not started | — | — |
| 78 — Global next-due priority queue | Not started | — | — |
| 79 — Enable concurrent active sessions | Not started | — | — |
| 80 — Conflict detection | Not started | — | — |
| 81 — UX polish | Not started | — | — |

## Stage 74 — Responsive UI shell

**Status:** Completed on 2026-09-01

**Commit:** `4c670098d6e204510f5488b96df4b01a259798b2` (`Implement responsive main window layout`)

### Delivered changes

- Rebuilt the main WPF window around a responsive `Grid` layout.
- Added adjustable splitters between the control panel, EasyTrader WebView, and log area.
- Made the left control area scrollable so controls remain reachable at smaller window sizes.
- Allowed the WebView to stretch with the available window space.
- Added persisted window position, dimensions, state, and splitter sizes.
- Corrected restored window bounds when a saved position is no longer visible on the current
  monitor configuration.

### Changed files

- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `Properties/Settings.settings`
- `Properties/Settings.Designer.cs`

### Preserved behavior and non-goals

- Scheduler timing and the one-second dispatch cadence were not changed.
- Prime Until Ready behavior was not changed.
- `sent` and `in-flight` accounting were not changed.
- The official EasyTrader UI submission path remained unchanged.
- No direct broker API order path was introduced.

### Verification

- Debug build passed with zero compilation errors.
- Release build passed with zero compilation errors.
- The only build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- Normal and maximized window layouts were visually checked.
- Splitter and window-geometry persistence were checked across application restarts.

## Stage 75 — Session model + session list UI

**Status:** Completed on 2026-09-01

**Commit:** `b5cfd60ecc580a9069f89a931b6ae77a5ab3e776` (`Implement Stage 75 order session UI`)

### Delivered changes

- Added the `OrderSession` presentation model with immutable identity, order, schedule, and
  confirmed-snapshot data plus observable state and progress properties.
- Added `OrderSessionState` values for Draft, Confirmed, Waiting, PreWarming, Ready, Running,
  Paused, Completed, Canceled, and Failed.
- Added a responsive Current Order Setup summary for symbol, ISIN, price, total quantity,
  commission, and final total.
- Renamed the final action to `افزودن به زمان‌بندی` (Add to Schedule).
- Kept Current Order Setup populated after a session is added.
- Added an adjustable session area and table showing creation sequence, symbol, state, total,
  sent, in-flight, remaining, slice/max quantity, start time, next time, HTTP status, and latest
  status.
- Created a session only after final confirmation and revalidation of the confirmed snapshot.
- Mirrored the existing scheduler's state and accounting into the session row for visibility.
- Kept the Stage 75 limitation explicit in the UI: only one session is executable at a time.

### Changed files

- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `OrderSession.cs` (new)
- `OrderSessionState.cs` (new)

### Preserved behavior and non-goals

- `OrderSession` is a presentation/read model in this stage; it does not drive dispatch or
  accounting.
- The existing confirmed snapshot remains the scheduler's source of truth until Stage 76.
- Scheduler timing and the one-second dispatch cadence were not changed.
- Prime Until Ready behavior was not changed.
- Existing `sent` and `in-flight` accounting remained authoritative.
- The official EasyTrader UI submission path remained authoritative and unchanged.
- Concurrent executable sessions were not enabled.

### Verification

- Debug build passed with zero compilation errors.
- Release build passed with zero compilation errors.
- The only build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- A startup smoke check confirmed that the WPF window became visible at `1280x720` and that the
  WebView initialized.
- Temporary diagnostic output used for the smoke check was removed before the commit.

## Stage 75.1 — Exchange-synchronized scheduler clock

**Status:** Completed on 2026-09-01

**Commit:** `35d004d4b777ddb3c2fa48f81293e822c5e155d0` (`Use exchange clock for scheduled order timing`)

### Delivered changes

- Added `ExchangeClock`, synchronized read-only through the public HTTPS TSETMC market-overview
  endpoint at `cdn.tsetmc.com` without authentication or broker-session data.
- Derived the exchange clock from the TSETMC HTTP `Date` response header and converted it to the
  Tehran time zone.
- Used three initial samples and selected the sample with the lowest round-trip time before final
  confirmation and again immediately after confirmation.
- Advanced a successful sample with `Stopwatch` so Windows wall-clock changes cannot shift an
  armed schedule.
- Added a visible TSETMC clock and `SYNC`/`STALE` state to the main-window header.
- Added the synchronized exchange time and source to the final confirmation window.
- Built the entered start and end timestamps from the TSETMC calendar date and Tehran offset,
  rather than `DateTime.Today` or `DateTimeOffset.Now`.
- Routed pre-warm waiting, slot start, missed-slot skipping, and end-window checks through the
  exchange clock.
- Refreshed the exchange clock every three seconds while a real schedule is active.
- Made scheduling fail closed: if no valid sample exists or the last valid sample is older than
  ten seconds, no new slot is created.
- Preserved already-launched dispatch settlement after a clock failure so `sent`/`in-flight`
  accounting remains consistent.

### Changed files

- `ExchangeClock.cs` (new)
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `LiveOrderConfirmationWindow.xaml`
- `LiveOrderConfirmationWindow.xaml.cs`

### Preserved behavior and non-goals

- The one-second slot cadence and missed-slot no-burst rule remain unchanged.
- Prime Until Ready behavior remains unchanged.
- Existing `sent` and `in-flight` accounting remains authoritative.
- The final submit still uses the official EasyTrader UI click path.
- No token, cookie, authorization value, private request body, or direct broker API order path was
  introduced.
- The source is the public TSETMC HTTPS server clock, not a private matching-engine clock or an
  undocumented authenticated broker endpoint.
- The HTTP `Date` header has one-second resolution. Estimated uncertainty is recorded as 500ms
  plus half of the measured round-trip time; the implementation does not claim millisecond-level
  exchange-core precision.

### Verification

- Debug build passed with zero compilation errors.
- Release build passed with zero compilation errors.
- The only normal build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- A live read-only TSETMC request returned HTTP `200`, a valid `Date` header, and a valid
  `marketOverview` object.
- Debug startup smoke test displayed the synchronized clock with a measured RTT of approximately
  `52ms`.
- Release startup smoke test displayed the synchronized clock with a measured RTT of approximately
  `7ms`.
- Both smoke tests stopped at the EasyTrader login page; no credentials were entered, no order
  form was changed, and no order was submitted.

## Stage 76 — Move confirmation ownership into sessions

**Status:** Not started

Planned scope from the approved architecture:

- give every created session its own independent confirmed snapshot;
- remove created-session dependence on one mutable global confirmation;
- keep Current Order Setup independent from existing sessions.

No Stage 76 implementation is claimed by this document yet.

## Template for future stages

Copy this structure when completing Stage 76 and later stages:

```markdown
## Stage NN — Name

**Status:** Completed on YYYY-MM-DD

**Commit:** `full-commit-hash` (`commit subject`)

### Delivered changes

- Verified implementation change.

### Changed files

- `path/to/file`

### Preserved behavior and non-goals

- Safety boundary or deliberately deferred behavior.

### Verification

- Build, automated test, smoke test, or manual verification evidence.
```
