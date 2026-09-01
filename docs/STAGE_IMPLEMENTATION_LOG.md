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
| 76 — Move confirmation ownership into sessions | Completed | 2026-09-01 | `755e8355aa7d3b8642e4422e1806671a9ec84770` |
| 77 — Central Official UI Dispatcher | Completed | 2026-09-01 | `71c4a0874c91afe507dd5ea507681f75a983841c` |
| 78 — Global next-due priority queue | Completed | 2026-09-01 | `4cfc0975ce9e210f80dda5c44e9adbdeb3504768` |
| 78.1 — User-selected broker route foundation | Completed | 2026-09-01 | `0bb94d8a3a3218333f2e60cdd1f94ff326730440` |
| 78.2 — Pishro Kaman official UI adapter | In progress — runtime validation pending | — | `8753f08b81ebb389404fae9fd81092dc46c9d6aa`, `9b800fa11ce2fbb7b1e55f0d58f63c95511a1655` |
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

**Status:** Completed on 2026-09-01

**Commit:** `755e8355aa7d3b8642e4422e1806671a9ec84770` (`Move confirmed snapshots into order sessions`)

### Delivered changes

- Added an explicit independent-copy operation to `ConfirmedOrderSnapshot` that preserves the
  confirmed payload, fingerprint, and confirmation timestamp while creating a separate immutable
  object.
- Made `OrderSession` take ownership of that independent copy during construction.
- Renamed the window-level snapshot to `_currentOrderSnapshot` so its scope is explicitly limited
  to Current Order Setup before session creation.
- Detached the Current Order Setup confirmation immediately after a session is created while
  preserving the visible symbol, ISIN, price, quantity, commission, and total-value summary.
- Changed the scheduler entry point to accept only the created `OrderSession`; it reconstructs and
  validates the authoritative order from `session.ConfirmedOrderSnapshot`.
- Added a session identity check that fail-closes if the session snapshot's symbol, ISIN, side,
  price, or total quantity does not match the immutable session fields.
- Routed every scheduled slice through the session-owned snapshot and removed all scheduler-slot
  reads of the mutable Current Order Setup snapshot.
- Stopped schedule cleanup from clearing Current Order Setup confirmation state belonging to a
  later setup, preparing the ownership boundary for future multi-session stages.

### Changed files

- `ConfirmedOrderSnapshot.cs`
- `OrderSession.cs`
- `MainWindow.xaml.cs`

### Preserved behavior and non-goals

- Only one session can execute at a time; concurrent execution remains deferred to Stage 79.
- TSETMC exchange-clock synchronization, freshness checks, and fail-closed timing are unchanged.
- The one-second slot cadence and missed-slot no-burst behavior are unchanged.
- Prime Until Ready behavior is unchanged.
- Existing `sent` and `in-flight` reservation and settlement accounting is unchanged.
- The final submit remains the official EasyTrader UI action; no direct broker API order path was
  introduced.
- Current Order Setup remains populated after Add to Schedule, but its old confirmation is no
  longer authoritative for the created session.

### Verification

- Debug build passed with zero compilation errors.
- Release build passed with zero compilation errors.
- The only build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- Static dependency checks confirmed that `RunScheduledOrderAsync`,
  `DispatchReservedSliceAsync`, and `ExecuteClockDrivenSliceAttemptAsync` now receive the
  `OrderSession` and do not read `_currentOrderSnapshot`.
- Static route checks confirmed that `BuildAtomicScheduledSubmitScript`, Prime Until Ready, and
  the existing `sent`/`in-flight` accounting remain on the same execution path.
- No live order was sent during Stage 76 verification.

## Stage 77 — Central Official UI Dispatcher

**Status:** Completed on 2026-09-01

**Commit:** `71c4a0874c91afe507dd5ea507681f75a983841c` (`Centralize official EasyTrader UI dispatch`)

### Delivered changes

- Added `OfficialOrderUiDispatcher`, a single asynchronous gate for every short operation that
  reads or mutates the shared official EasyTrader order-form DOM.
- Added an explicit access guard to the low-level prepare, clear, trusted-click, and Prime helpers
  so future code cannot call those DOM operations outside the central dispatcher.
- Routed Current Order Setup open/read operations, scheduled pre-warm, atomic scheduled submit,
  Prime Until Ready, the retained legacy guarded submit helper, and prepare-only Dry-Run probes
  through the same dispatcher.
- Kept prepare/verify/submit/cleanup inside one critical section for the active scheduled click,
  then released the dispatcher before separately running Prime Until Ready.
- Preserved the immediate final verification in `BuildSubmitScript`: nonce, symbol, ISIN, price,
  and quantity are rechecked before the official submit button is clicked exactly once.
- Added session-local fail-closed handling for final symbol, ISIN, price, or quantity mismatch;
  the affected session becomes `Failed`, its reservation is released, and no later slot for that
  session is launched.
- Made dispatcher failure isolation explicit: every operation releases the gate in `finally`, and
  a failed operation does not poison later queued work.
- Added non-sensitive dispatcher diagnostics for queue wait and operation duration.
- Added a temporary WebView overlay only while the dispatcher owns the critical section, blocking
  manual DOM changes during that short operation and disappearing immediately after release.

### Changed files

- `OfficialOrderUiDispatcher.cs` (new)
- `MainWindow.xaml`
- `MainWindow.xaml.cs`

### Preserved behavior and non-goals

- Only one session is executable at a time; concurrent session execution remains deferred to
  Stage 79.
- The global next-due priority queue and cross-session priming remain deferred to Stage 78.
- TSETMC exchange-clock synchronization, freshness checks, and fail-closed timing are unchanged.
- The scheduler's one-second target-slot generation and missed-slot no-burst rule are unchanged.
- Prime Until Ready keeps its existing retry deadline and form-preparation behavior.
- Existing `sent` and `in-flight` reservation and settlement accounting remains authoritative.
- A slice is moved to `sent` only after the existing `CLICKED` result; a verification mismatch is
  not counted as sent and is not automatically retried.
- The final action remains the official EasyTrader UI click; no direct broker API order path or
  credential access was introduced.

### Verification

- Debug build passed with zero compilation errors.
- Release build passed with zero compilation errors.
- The only build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- An isolated eight-operation concurrency probe observed `max-active=1`.
- The probe injected one expected operation failure, confirmed that the gate was released, and
  successfully ran a subsequent operation with `pending=0`.
- A second access probe confirmed that low-level DOM access is rejected outside the dispatcher and
  succeeds while the dispatcher owns the operation.
- Static route checks confirmed that scheduled atomic submit still calls
  `BuildAtomicScheduledSubmitScript`, which composes the existing prepare and immediate guarded
  `BuildSubmitScript` verification path.
- No live order was sent during Stage 77 verification.

## Stage 78 — Global next-due priority queue

**Status:** Completed on 2026-09-01

**Commit:** `4cfc0975ce9e210f80dda5c44e9adbdeb3504768` (`Schedule slices through global next-due queue`)

### Delivered changes

- Added a thread-safe `GlobalNextDueQueue` backed by `PriorityQueue` and limited it to at most one
  future eligible slice per session.
- Added deterministic slice ordering by target exchange time, explicit numeric priority, session
  creation sequence, and a defensive per-session slice sequence as the final tie-breaker.
- Migrated the active scheduler loop from its local `nextSlot` variable to dequeueing the globally
  earliest due slice.
- Enqueued the next eligible slice before launching the current UI dispatch, allowing a completed
  official click to prime the actual globally next-due order.
- Replaced blind same-order priming with a global queue lookup that rebuilds and validates the
  order from the selected session-owned confirmed snapshot.
- Preserved missed-slot behavior by skipping elapsed one-second targets before enqueueing the next
  eligible slice; elapsed targets are never burst-replayed.
- Removed a session's queued slice immediately when its window ends, cancellation or an internal
  error stops scheduling, its total is fully sent, or final cleanup runs.
- Kept priming best-effort so a prime failure cannot replace an already confirmed `CLICKED` result
  or corrupt `sent`/`in-flight` settlement.
- Kept the Stage 78 execution boundary explicit: the queue and scheduler source support global
  ordering, but only the currently active session is executable until Stage 79.

### Changed files

- `GlobalNextDueQueue.cs` (new)
- `MainWindow.xaml.cs`

### Preserved behavior and non-goals

- Concurrent active-session execution was not enabled; it remains Stage 79 scope.
- Schedule timing, pre-warm, slot eligibility, missed-slot detection, and end-window checks remain
  based on the fail-closed TSETMC exchange clock.
- The one-second target cadence and no-burst missed-slot rule remain unchanged.
- The central Official UI Dispatcher remains the only route for EasyTrader DOM operations.
- Immediate symbol, ISIN, price, and quantity verification remains mandatory before every final
  official click.
- Existing `sent` and `in-flight` reservation and settlement accounting remains authoritative.
- A slice is committed as sent only after `CLICKED`; HTTP activity is not treated as broker fill or
  execution.
- The final submit remains the official EasyTrader UI path; no direct broker order POST or
  credential access was introduced.

### Verification

- Debug build passed with zero compilation errors.
- Release build passed with zero compilation errors.
- The only build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- An in-memory four-session queue probe confirmed ordering by earlier target, then priority, then
  session creation sequence; the observed creation-sequence order was `2, 4, 1, 3`.
- The same probe confirmed that a duplicate future slice for one session is rejected and that
  session cleanup removes the queued slice without leaving queue entries.
- Static route checks confirmed that the scheduler loop consumes `_globalNextDueQueue`, global
  priming is active, and the previous blind same-order prime call is absent.
- The repository currently has no automated test project; the isolated queue probe was compiled
  and executed in memory without adding test-only production files.
- No live order was sent during Stage 78 verification.

## Stage 78.1 — User-selected broker route foundation

**Status:** Completed on 2026-09-01

**Commit:** `0bb94d8a3a3218333f2e60cdd1f94ff326730440` (`Add user-selected broker routing foundation`)

### Delivered changes

- Added an explicit broker selector for EasyTrader and Pishro Kaman and persisted the selected
  broker across application launches.
- Added validated broker profiles containing the stable broker id, display name, official HTTPS
  route, exact trusted origin, monitored host, and official-order-UI capability state.
- Navigated login and browser actions through the selected broker profile rather than a fixed
  EasyTrader URL.
- Blocked broker switching while a schedule or live submission is active.
- Cleared Current Order Setup confirmation and broker-session network evidence after a permitted
  broker change so stale data cannot cross the broker boundary.
- Bound `ConfirmedOrderSnapshot` fingerprints to both the broker id and confirmed payload.
- Added immutable broker identity and display name to every `OrderSession` and displayed the
  broker in the session table.
- Added scheduler and session validation that rejects a broker mismatch before any broker UI
  operation.
- Added a Pishro compatibility probe that validates the exact selected origin and reports only
  sanitized structural DOM attributes and visible action labels.
- Kept the Pishro order-form bridge fail-closed until its logged-in DOM contract is validated;
  navigation and the read-only compatibility probe are available, but open/read/prepare/click
  order operations are not yet enabled.
- Updated network filtering for the known broker hosts while retaining value-free header-name and
  status observation.

### Changed files

- `BrokerCompatibilityProbe.cs` (new)
- `BrokerProfile.cs` (new)
- `ConfirmedOrderSnapshot.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `OrderSession.cs`
- `Properties/Settings.Designer.cs`
- `Properties/Settings.settings`

### Preserved behavior and non-goals

- The existing EasyTrader official-UI submission route remains operational and broker-specific.
- Pishro live order automation is not enabled in this stage; its selectors and submit contract
  will not be guessed.
- No token, cookie, authorization value, private field value, request body, or browser storage is
  read or logged by the compatibility probe.
- No direct broker API order path or credential access was introduced.
- TSETMC exchange-clock timing, one-second slots, the missed-slot no-burst rule, the global
  next-due queue, Prime Until Ready, the central dispatcher, and sent/in-flight accounting were
  not changed.
- Concurrent active-session execution remains Stage 79 scope.
- No live order was sent during Stage 78.1 verification.

### Verification

- Debug build passed with zero compilation errors.
- Release build passed with zero compilation errors.
- The only build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- A static probe-safety scan confirmed that the compatibility probe contains no cookie, local or
  session storage, field-value, `fetch`, `XMLHttpRequest`, or click access.
- An in-memory broker-foundation probe confirmed two registered profiles, accepted Pishro's exact
  trusted origin, rejected a lookalike origin, produced distinct fingerprints for the same payload
  under different brokers, and preserved broker identity in an independent snapshot copy.
- A WPF UI Automation smoke check found the broker selector and compatibility-probe button in the
  ready main window; no WebView interaction was performed.
- `git diff --check` passed before the implementation commit.

## Stage 78.2 — Pishro Kaman official UI adapter

**Status:** In progress; control lifecycle and strict adapter implemented on 2026-09-01, logged-in
runtime validation pending

**Implementation commit:** `8753f08b81ebb389404fae9fd81092dc46c9d6aa`
(`Enable Pishro order workflow controls`)

**Runtime-fix commit:** `9b800fa11ce2fbb7b1e55f0d58f63c95511a1655`
(`Discover Pishro ISIN from active form`)

### Delivered changes

- Removed the permanent Pishro capability gate that disabled every order-workflow control.
- Enabled `Open current symbol form` and `Read and confirm form` immediately after Pishro is
  selected and while no schedule or live submission is active.
- Preserved progressive safety: `Prepare locally`, `Add to Schedule`, and Dry-Run remain disabled
  until the visible official form is read successfully and the user explicitly confirms the
  captured order.
- Corrected the shared active-schedule control lifecycle so broker navigation, form mutation,
  reload, and order setup stay disabled while a schedule is active and return to their valid
  broker-specific states afterward.
- Added `BrokerOfficialOrderUiBridge`, which selects exactly one broker adapter and prevents
  accidental reuse of EasyTrader scripts for Pishro.
- Added a separate Pishro Kaman adapter for open/read/ensure/prepare/submit/atomic-submit/clear
  operations through the official visible UI.
- Required Pishro's exact HTTPS origin, one unambiguous active ISIN, an unambiguous visible price
  input, quantity input, commission, total value, and buy action before the workflow can advance.
- Expanded active-instrument discovery after runtime evidence showed that Pishro removes the ISIN
  from `pathname` after navigation: the adapter now checks the full URL, attributes and short
  visible text in the located order form, and explicit active/selected element metadata in that
  priority order.
- Added a distinct `INSTRUMENT_AMBIGUOUS` stop result when a selected source exposes more than one
  ISIN; the adapter never chooses the first candidate silently.
- Bound Pishro preparation and final submission through the existing per-attempt nonce and
  revalidated active ISIN, price, quantity, and action immediately before a single official click.
- Used a successful read of the visible form on the exact trusted Pishro origin as non-sensitive
  broker-access evidence; no credential value is inspected.
- Generalized user-facing official-UI error messages to identify the selected broker rather than
  incorrectly referring to EasyTrader.

### Changed files

- `BrokerOfficialOrderUiBridge.cs` (new)
- `PishroKamanOrderUiBridge.cs` (new)
- `BrokerProfile.cs`
- `MainWindow.xaml.cs`
- `OfficialOrderUiBridge.cs`

### Preserved behavior and non-goals

- EasyTrader retains its existing selectors and official-UI submission implementation.
- The Pishro adapter does not reuse EasyTrader selectors and rejects ambiguous or missing DOM
  structure before a final click.
- No direct broker API order path was added.
- No token, cookie, authorization value, request body, browser storage, or password is read or
  stored.
- TSETMC exchange-clock scheduling, one-second slots, missed-slot no-burst behavior, Prime Until
  Ready, the global next-due queue, the central dispatcher, and sent/in-flight accounting remain
  unchanged.
- Pishro multi-symbol switching is not guessed; if exactly one active ISIN cannot be proven or it
  differs from the confirmed snapshot, the operation stops.
- No live order was sent during this implementation or verification.

### Verification

- Debug and Release builds passed with zero compilation errors.
- The only build warning was `NU1900`, caused by the unavailable NuGet vulnerability feed.
- A generated-script routing probe confirmed that the Pishro script contains only the Pishro
  trusted origin and no EasyTrader order selector, while the EasyTrader script contains no Pishro
  bridge state.
- A static safety scan found no cookie, local/session storage, `fetch`, `XMLHttpRequest`,
  authorization-value, or request-body access in the Pishro adapter.
- A Windows UI Automation smoke test selected Pishro and confirmed that Open and Read are enabled,
  while Prepare and Add to Schedule correctly remain disabled before form confirmation.
- The smoke test did not interact with the Pishro webpage and closed the test application after
  inspecting control state.
- The first logged-in runtime attempt failed safely with `INSTRUMENT_NOT_VERIFIED` and explicitly
  reported `HTTP POST: NOT SENT`; this evidence identified the obsolete pathname-only assumption.
- Updated Debug and Release builds passed in isolated temporary output folders while the user's
  currently running Debug executable remained untouched.
- A generated-script probe confirmed full-URL, order-form, and active-selection ISIN discovery,
  explicit ambiguity handling, and removal of pathname-only discovery.
- `git diff --check` passed before the implementation commit.

### Required runtime validation before completion

- Sign in manually through the official Pishro page.
- Open one official buy form and enter a non-sensitive test price and quantity.
- Run Read and Confirm, then a prepare-only Dry-Run; do not approve a live order for validation.
- Record the sanitized status codes and confirm the exact DOM contract or tighten the adapter as
  needed.

## Template for future stages

Copy this structure when completing Stage 78.2 and later stages:

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
