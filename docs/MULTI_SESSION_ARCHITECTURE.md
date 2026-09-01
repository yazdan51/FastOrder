# FastOrder Multi-Session Architecture and UI Design

**Status:** Approved design baseline before implementation  
**Target branch:** `feature/scheduled-split-orders-1s`  
**Baseline commit:** `821b6c4df98d6c03c7f85b5a942136b78cd21f50`  
**Date:** 2026-08-31

---

## 1. Purpose

This document defines the agreed architecture and user-experience design for evolving FastOrder from a single active scheduled order into a multi-session, multi-symbol order scheduler while preserving the safety properties of the current one-second scheduler.

The redesign must solve two practical problems:

1. an active schedule currently disables too much of the UI, preventing preparation of another order for the same or another symbol;
2. the current layout requires repeated manual resizing to keep FastOrder controls and the EasyTrader WebView usable.

The target combines:

- independent order sessions;
- an explicit user-selected broker route;
- separate official-UI adapters for EasyTrader and Pishro Kaman;
- one safe dispatcher for the shared broker WebView;
- a global next-due scheduling queue;
- responsive WPF layout;
- persistent window and splitter geometry;
- independent session controls;
- user-level and technical logging;
- strict symbol/ISIN/price/quantity verification before every final submit.

---

## 2. Current execution model to preserve

FastOrder does **not** directly submit an order to the broker API.

Current supported path:

```text
FastOrder
  -> prepares official EasyTrader order form
  -> clicks official EasyTrader "Send Buy" button
  -> EasyTrader sends its own POST
  -> FastOrder observes network request/response
```

So:

```text
UI CLICK -> EasyTrader -> Broker API
```

Direct broker order POST from FastOrder is outside this redesign.

No direct credentials, tokens, cookies, authorization values, or custom direct order submission are to be added.

---

## 2.1. User-selected broker route and multi-broker scenario

The user selects the broker before login and order preparation. The supported broker profiles are:

- EasyTrader: `https://d.easytrader.ir/`;
- Pishro Kaman: `https://kaman.pishrobroker.ir/trading-view/IRO9MSMI0D81`.

The selection is persisted between application launches and is always visible in the header and
session list. Changing the selected broker:

- is blocked while a schedule or live submission is active;
- clears the current confirmation and current setup because they belong to the previous broker;
- clears broker-session evidence such as observed request/response state;
- navigates only to the selected profile's official HTTPS route;
- never mutates the immutable broker identity of an already-created session.

Every confirmed snapshot and `OrderSession` is bound to an immutable `BrokerId`. A session may be
prepared, validated, primed, and submitted only by the adapter for that same broker. DOM selectors,
trusted origins, network-observation rules, and verification scripts must be broker-specific and
must never be silently reused across brokers.

The target broker-neutral scenario is:

```text
Select broker
  -> navigate to that broker's official HTTPS route
  -> user signs in manually
  -> broker adapter validates the exact trusted origin
  -> open/read the official order form
  -> local order confirmation
  -> bind confirmed payload + BrokerId into an immutable snapshot
  -> final live confirmation
  -> schedule from the fresh TSETMC exchange clock
  -> global next-due queue
  -> central Official UI Dispatcher
  -> broker-specific immediate final verification
  -> click that broker's official submit control
  -> user verifies the result in that broker's official order list
```

The application continues to use one shared WebView and one central dispatcher. Until a future
explicit multiple-WebView design is approved, concurrent sessions must belong to the currently
selected broker; FastOrder must not navigate across brokers while a session is active.

The Pishro integration is deliberately staged. Stage 78.1 provides profile selection, official
navigation, broker-bound snapshots/sessions, and a read-only structural compatibility probe. The
probe may inspect only non-secret DOM structure and labels; it must not read field values, tokens,
cookies, storage, request bodies, or headers, and it must not click or submit. Stage 78.2 adds a
separate strict Pishro adapter and progressive control activation. The adapter accepts only an
exact trusted origin, an ISIN in the official route, one unambiguous visible price/quantity pair,
and one unambiguous official buy action. It remains fail-closed on any DOM ambiguity, and logged-in
runtime validation is required before Stage 78.2 is marked complete. No private endpoint is guessed.

---

## 3. Stable scheduler properties

The following behavior is considered stable and must not be weakened:

- one-second clock slots;
- scheduled start, pre-warm, slot, and end-window decisions use a fresh exchange clock synchronized
  from the public HTTPS TSETMC market server, not the Windows wall clock;
- scheduling fails closed when the exchange clock is unavailable or stale;
- no burst catch-up for missed slots;
- no waiting for previous HTTP response before creating the next slot;
- no waiting for previous UI dispatch before creating the next slot;
- `sent + in-flight <= total`;
- after official UI reports `CLICKED`, that slice is locally committed as sent and is never auto-retried;
- pre-click failures may release their reservation;
- cancellation stops future slots but lets launched dispatches settle;
- internal scheduler errors stop future slots and settle launched dispatches before cleanup;
- `CLICKED` and HTTP `2xx` are not interpreted as broker fill/execution;
- EasyTrader order list remains authoritative for final broker outcome.

Validated one-symbol behavior currently includes:

```text
PRE-WARM -> PREPARED
CLICKED -> NEXT FORM PRIME: READY
next 1-second slot -> CLICKED
```

for ten consecutive slices.

---

## 4. Core architecture: session isolation instead of global lock

The application must move from one global active-order state to multiple independent sessions.

```text
ActiveOrderSessions

Session A -> symbol A
Session B -> symbol B
Session C -> symbol C
```

An active session must not disable the whole order-entry workflow.

The user must be able to:

- keep Session A running;
- prepare another order for the same symbol;
- select another symbol in EasyTrader;
- read and confirm that symbol;
- create Session B;
- pause/cancel one session without affecting unrelated sessions.

---

## 5. Current Order Setup vs Order Session

The left-side order panel is **not cleared** after `Add to Schedule`.

It represents a reusable current working setup:

```text
CurrentOrderDraft
```

Creating a session copies the confirmed values into an independent session snapshot.

Example:

```text
Current setup
Symbol = FMLI
Price = 21150
Quantity = 10000
```

Create Session A.

Then user changes:

```text
Price = 21200
Quantity = 5000
```

Session A remains unchanged. Session B receives the new values.

Multiple sessions for the same symbol are valid.

If another symbol is selected in EasyTrader and its form is read, the same Current Order Setup panel updates in place with the new:

- Symbol
- ISIN
- Price
- Quantity
- Commission
- Total Value

The panel is not recreated or cleared unnecessarily.

---

## 6. Proposed `OrderSession`

Conceptual model:

```text
OrderSession
{
    SessionId
    CreationSequence
    BrokerId

    SymbolName
    SymbolIsin
    Side
    Price

    TotalQuantity
    MaxQuantityPerOrder

    StartTime
    EndTime

    SentQuantity
    InFlightQuantity
    RemainingQuantity
    ClickedOrderCount

    ConfirmedOrderSnapshot

    State
    LastStatus
    LastHttpStatus
    LastError

    CancellationTokenSource

    CreatedAt
    CompletedAt
}
```

Important ownership rule:

> After a session is created, its confirmed data must not depend on a mutable global `_confirmedOrderSnapshot`.

---

## 7. Session state machine

Recommended states:

```text
Draft
 -> Confirmed
 -> Waiting
 -> PreWarming
 -> Ready
 -> Running
 -> Completed
```

Additional states:

```text
Paused
Canceled
Failed
```

Pause:

```text
stop new slices for this session
-> settle launched dispatches
-> Paused
```

Resume:

```text
continue from next valid future slot
-> do not replay missed slots
-> no burst catch-up
```

Cancel:

```text
stop future slices for this session only
-> settle launched dispatches
-> Canceled
```

A session-local failure must not stop unrelated sessions.

---

## 8. Central Official UI Dispatcher

All sessions share one EasyTrader WebView and one official order form.

Therefore two sessions must never manipulate the EasyTrader DOM concurrently.

```text
Session A --\
Session B ----> OfficialOrderUiDispatcher -> EasyTrader WebView
Session C --/
```

The dispatcher owns only a short critical section.

Typical atomic UI operation:

```text
Acquire dispatcher
-> select/verify symbol
-> verify ISIN
-> open official BUY form
-> locate correct form root
-> set price
-> set quantity
-> verify symbol/ISIN/price/quantity again
-> click final official submit exactly once
-> release dispatcher
```

This is a short UI critical section, not a whole-application lock.

---

## 9. Mandatory verification immediately before submit

Before every final submit, verify:

```text
Expected Symbol
Expected ISIN
Expected Price
Expected Quantity
```

If any mismatch exists:

```text
DO NOT CLICK FINAL SUBMIT
```

Only the affected session should fail/stop.

Example:

```text
SESSION B FAILED
Reason: active instrument/form mismatch
```

Other sessions continue.

The dispatcher must not assume the previously selected EasyTrader symbol is still active because the user may interact manually between automated operations.

---

## 10. Global next-due queue

Single-session priming prepares the next slice of the same session. Multi-session mode must instead prime the globally next-due slice.

Conceptual structure:

```text
PriorityQueue<ScheduledSlice>
```

Ordering:

1. TargetTime
2. optional Priority
3. SessionCreationSequence

Example:

```text
10:00:01.000 -> FMLI
10:00:01.400 -> KHODRO
10:00:02.000 -> FMLI
10:00:02.400 -> KHODRO
```

After processing a slice:

```text
dequeue processed slice
-> enqueue next eligible slice for that session
-> find globally earliest due slice
-> prime that slice
```

This replaces blind same-symbol priming.

---

## 11. Equal target times and contention

One WebView cannot perform two official submissions at the exact same instant.

Deterministic order:

```text
TargetTime
-> Priority
-> SessionCreationSequence
```

If two sessions collide, the second may be delayed by the dispatcher duration.

This must be visible to the user.

FastOrder should warn about timing overlap but must not silently modify user schedule times.

Optional future enhancement: suggest a user-approved offset such as 300–500 ms.

---

## 12. Responsive main-window layout

The redesigned window must eliminate repeated manual resize/minimize/maximize work.

Recommended structure:

```text
+--------------------------------------------------------------------------------+
| FastOrder | EasyTrader Connected | Active Sessions | Next Due | Current Clock   |
+------------------------+-------------------------------------------------------+
| CURRENT ORDER SETUP    |                                                       |
|                        |                                                       |
| Symbol                 |                   EASYTRADER WEBVIEW                  |
| ISIN                   |                                                       |
| Price                  |                                                       |
| Total Quantity         |                                                       |
| Slice Quantity         |                                                       |
| Start / End            |                                                       |
|                        |                                                       |
| Open Form              |                                                       |
| Read / Confirm         |                                                       |
| Add to Schedule        |                                                       |
+------------------------+-------------------------------------------------------+
| ACTIVE / WAITING SESSIONS                                                       |
| FMLI   Running    6000/10000    Next 10:30:07     Pause Cancel Details          |
| KHODRO Waiting    0/5000        Next 10:30:07.5   Cancel Details                |
+--------------------------------------------------------------------------------+
| User Log | Technical Log                                                        |
+--------------------------------------------------------------------------------+
```

Implementation principles:

- WPF `Grid`, not fixed absolute layout;
- WebView stretches to available space;
- left panel has a sensible minimum width;
- session area height is adjustable;
- `GridSplitter` between major areas;
- usable at minimum 1366x768;
- scales naturally to Full HD and larger;
- avoid routine modal dialogs where possible.

---

## 13. Persist window/splitter layout

Persist:

```text
Window Width
Window Height
Window Left
Window Top
WindowState
Left/Right Splitter Position
Top/Bottom Splitter Position
```

If last closed maximized, reopen maximized.

If monitor topology changes, invalid off-screen coordinates must be corrected.

Goal:

> User arranges the application once and normally does not need repeated resizing in later runs.

---

## 14. Current Order Setup behavior

After `Add to Schedule`:

```text
CurrentOrderDraft
-> copy into independent OrderSession
-> keep CurrentOrderDraft visible
```

The user may then modify:

- quantity;
- slice size;
- start/end times;
- price, subject to appropriate reconfirmation;

and create another session for the same symbol.

If EasyTrader changes symbol, the existing setup panel updates in place after reading the official form.

The panel should visibly distinguish:

- values read and confirmed from EasyTrader;
- locally modified values requiring reconfirmation;
- stale values because the active EasyTrader instrument changed.

---

## 15. Session table

Recommended columns:

| Column | Meaning |
|---|---|
| Symbol | Instrument |
| State | Waiting / Ready / Running / Paused / Failed / Completed |
| Total | Requested total quantity |
| Sent | Locally committed after `CLICKED` |
| Remaining | Total - Sent - InFlight |
| Slice | Max quantity per slice |
| Start | Start time |
| Next | Next target time |
| Actions | Pause / Resume / Cancel / Details |

State should use text and optional icon/color; color must not be the only indicator.

Suggested visual semantics:

```text
Waiting    neutral
Ready      blue
Running    green
Paused     amber
Failed     red
Completed  subdued green
Canceled   gray
```

---

## 16. Per-session controls

Recommended:

```text
Pause
Resume
Cancel
Details
```

They operate only on the selected session.

There should be no global "disable everything while any session is active" behavior.

Only a control currently executing its own short operation may be temporarily disabled.

---

## 17. Manual EasyTrader use during automation

Manual interaction remains possible while the dispatcher is idle.

During the short dispatcher critical section, show a temporary overlay over the WebView, for example:

```text
Submitting FMLI...
```

The overlay prevents the user from changing the DOM during the critical operation and disappears immediately after release.

This is not a global application lock.

---

## 18. Header/status bar

Recommended fields:

```text
EasyTrader: Connected / Not Ready
Session observed: Valid / Unknown
UI Dispatcher: Idle / Busy
Active sessions: N
Next due: SYMBOL @ TIME
Exchange clock (TSETMC): HH:mm:ss.fff / SYNC / STALE
```

---

## 19. Logging

Provide two views.

### User Log

Concise:

```text
10:30:01.005 FMLI -> Clicked
10:30:01.410 KHODRO -> Clicked
10:30:02.000 FMLI -> Ready
10:30:02.400 KHODRO -> Waiting for shared UI
```

### Technical Log

Detailed:

```text
PREPARED
DIALOG_OPEN_REQUESTED
NEXT FORM PRIME READY
HTTP POST OBSERVED
HTTP STATUS 200
DOM verification status
```

Sensitive credentials remain omitted.

---

## 20. Session Details

Recommended information:

```text
Session ID
Symbol
ISIN
Side
Price
Total Quantity
Slice Quantity
Created Time
Start Time
End Time
Current State
Sent Quantity
In-Flight Quantity
Remaining Quantity
Clicked Count
Last UI Status
Last Observed HTTP Status
Last Error
```

Include event history:

```text
10:30:00.005 CLICKED
10:30:01.009 CLICKED
10:30:02.004 CLICKED
```

---

## 21. Error isolation

### Session-local errors

Examples:

- symbol mismatch;
- ISIN mismatch;
- form not found for one session;
- one-session cancellation;
- invalid local state.

Action:

```text
stop/fail affected session only
```

### Shared infrastructure errors

Examples:

- WebView process failure;
- EasyTrader origin unavailable;
- dispatcher cannot access WebView;
- application shutdown.

These may require safe shutdown of all active sessions.

---

## 22. Safety invariants

Non-negotiable:

1. No direct broker API credentials are read or stored.
2. No direct custom order POST is introduced.
3. Final submission uses the selected broker's official UI adapter. EasyTrader retains its
   validated adapter; Pishro uses its separate strict adapter and fails closed unless every
   required visible control, route ISIN, value, and preparation nonce is unambiguous.
4. Symbol, ISIN, price, and quantity are revalidated immediately before every final click.
5. Each session has independent sent/in-flight accounting.
6. No session exceeds its configured total quantity.
7. A `CLICKED` slice is never automatically retried.
8. Missed slots are skipped, not burst-replayed.
9. Cancellation cannot corrupt accounting of launched dispatches.
10. Failure in one session cannot silently mutate another.
11. Shared WebView manipulation is serialized through the dispatcher.
12. Broker execution/fill is never inferred only from `CLICKED` or HTTP `2xx`.
13. No scheduled click starts when the TSETMC exchange-clock sample is unavailable or stale.
14. Every confirmed snapshot and session has one immutable `BrokerId`.
15. A broker route cannot be changed while scheduling or a live submission is active.
16. The active page origin must exactly match the selected broker profile's trusted origin.
17. Broker-specific selectors and scripts cannot be reused for another broker without explicit
    validation.

---

## 23. Implementation phases

Implementation status, completed changes, verification evidence, and Git commits are maintained
in [`STAGE_IMPLEMENTATION_LOG.md`](STAGE_IMPLEMENTATION_LOG.md).

### Stage 74 — Responsive UI shell

- redesign `MainWindow.xaml`;
- responsive `Grid`;
- splitters;
- stretch WebView;
- persist geometry;
- no scheduler behavior changes.

### Stage 75 — Session model + session list UI

- add `OrderSession`;
- add session state enum;
- add session table;
- Current Order Setup remains populated after Add to Schedule;
- initially only one executable session if needed.

### Stage 75.1 — Exchange-synchronized scheduler clock

- synchronize read-only from the public HTTPS TSETMC market server;
- advance synchronized time with a monotonic clock;
- use exchange time for confirmation, pre-warm, slot, missed-slot, and end-window decisions;
- show exchange time and freshness in the UI;
- fail closed when exchange time is unavailable or stale;
- preserve the official EasyTrader UI path, Prime Until Ready, and sent/in-flight accounting.

### Stage 76 — Move confirmation ownership into sessions

- independent session snapshots;
- remove dependency of created sessions on one mutable global confirmation;
- current setup independent from existing sessions.

### Stage 77 — Central Official UI Dispatcher

- serialize EasyTrader DOM operations;
- short critical section;
- mandatory immediate pre-submit verification;
- isolate dispatcher failures.

### Stage 78 — Global next-due priority queue

- global scheduling across sessions;
- global next-due priming;
- deterministic tie-breaking.

### Stage 78.1 — User-selected broker route foundation

- add persisted EasyTrader/Pishro broker selection;
- navigate only to the selected broker's official HTTPS route;
- bind confirmed snapshots and sessions to an immutable broker identity;
- add a sanitized, read-only structural compatibility probe;
- keep Pishro order-form operations fail-closed until its DOM contract is validated.

### Stage 78.2 — Pishro Kaman official UI adapter

- validate the logged-in Pishro DOM through sanitized structural evidence;
- implement separate Pishro open/read/prepare/verify/click scripts;
- keep Open and Read enabled after Pishro selection, while Prepare and Add to Schedule remain
  disabled until a successful read and explicit local confirmation;
- submit only through Pishro's official visible UI;
- preserve the exchange-clock scheduler, Prime Until Ready, global queue, central dispatcher, and
  sent/in-flight accounting unchanged;
- do not add direct API submission or credential access.

### Stage 79 — Enable concurrent active sessions

- several waiting/running sessions;
- independent pause/resume/cancel;
- shared dispatcher only during UI operation.

### Stage 80 — Conflict detection

- detect overlapping target times;
- warning for expected UI contention;
- optional user-approved timing offset suggestions.

### Stage 81 — UX polish

- User Log / Technical Log;
- Session Details;
- improved header;
- session event history.

---

## 24. Acceptance criteria

The redesign is complete when:

- the user explicitly selects EasyTrader or Pishro before login and order preparation;
- the selected broker is visible and persists across application launches;
- broker selection cannot change while scheduling or a live submission is active;
- each session displays and retains its immutable broker identity;
- each broker uses its own validated trusted origin, selectors, and official-UI adapter;
- Pishro supports the same open/read/prepare/verify/click workflow through its own official UI
  adapter after Stage 78.2 validation;
- no cross-broker selector reuse or silent broker navigation is possible;
- one session can run while user prepares another;
- multiple sessions for the same symbol are supported;
- sessions for different symbols are supported;
- Add to Schedule does not clear Current Order Setup;
- reading another symbol updates the same setup panel in place;
- later setup edits do not mutate already-created sessions;
- one session can be canceled without stopping unrelated sessions;
- two sessions never manipulate EasyTrader DOM concurrently;
- every final submit revalidates instrument and values;
- globally next-due slice is primed;
- timing conflicts are deterministic and visible;
- UI works at 1366x768;
- layout persists across launches;
- repeated manual resize is unnecessary;
- current stable one-second behavior remains testable;
- scheduled start is based on a fresh TSETMC exchange-clock sample rather than the Windows wall
  clock;
- an unavailable or stale exchange clock prevents creation of a new scheduled click;
- no unintended direct broker API path is introduced.

---

## 25. Approved design decisions

The following decisions are approved as the baseline:

- the user selects the active broker profile explicitly;
- broker selection is persisted, but an active session cannot be moved to another broker;
- each broker has a separate official-UI adapter and exact trusted origin;
- confirmed snapshots and sessions are cryptographically bound to their `BrokerId` through the
  confirmation fingerprint;
- multi-session architecture uses independent `OrderSession` objects;
- EasyTrader execution remains official UI click, not direct order API;
- one central dispatcher protects the shared WebView;
- an active session no longer globally disables the application;
- Current Order Setup remains populated after `Add to Schedule`;
- multiple sessions for the same symbol are allowed;
- reading another symbol updates the current setup panel in place;
- existing sessions are independent of later current-setup edits;
- multi-session priming uses the globally next-due slice;
- scheduled timing uses a fail-closed, monotonic TSETMC exchange clock;
- responsive layout and persisted geometry are functional requirements;
- implementation is incremental, starting with UI/session structure before enabling true simultaneous multi-symbol execution.

---

## 26. Important implementation warning

Simply re-enabling the currently disabled buttons is **not** sufficient.

Without session isolation and a central UI dispatcher, two symbols could race while modifying the
same broker order form. Likewise, reusing one broker's selectors on another broker could target an
unrelated control.

Therefore global UI re-enablement must be implemented together with the session/dispatcher architecture described in this document.
