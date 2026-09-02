# FastOrder — Handoff to Codex
Date: 2026-09-02

## Repository / branch context

- Local project:
  `C:\Users\Hossein-0900\Documents\ChatGPT\Fastorder`
- Repository:
  `yazdan51/FastOrder`
- Working branch:
  `feature/scheduled-split-orders-1s`
- Architecture baseline commit:
  `821b6c4df98d6c03c7f85b5a942136b78cd21f50`
- The branch had previously been pushed through commit:
  `d11cc95`
- IMPORTANT: several later edits were made locally after that push. Do **not** assume GitHub currently contains the latest local source.

---

# Architectural invariants

These must remain unchanged.

1. FastOrder must **not** submit broker orders through a direct broker API.
2. FastOrder must **not** read or use credentials, tokens, cookies, authorization values, or private request bodies.
3. Orders must be submitted only through the broker's **official UI**.
4. For EasyTrader:
   - prepare the official EasyTrader form;
   - use the broker's official submit action;
   - observe resulting network activity only.
5. For Pishro Kaman:
   - the trusted primary origin is:
     `https://kaman.pishrobroker.ir`
   - the only additional trusted origin is:
     `https://mobile.pishrobroker.ir`
   - no wildcard sibling origins.
6. Fail closed if:
   - the official form is ambiguous;
   - symbol identity is missing or mismatched;
   - price or quantity cannot be read/verified;
   - exchange clock is stale/unavailable.
7. A `CLICKED` state must never automatically retry.
8. A network `2xx` response must not be interpreted as exchange fill confirmation.

---

# Stage status before the latest work

Completed:
- Stage 74 — responsive UI
- Stage 75 — session model/list
- Stage 75.1 — TSETMC exchange clock
- Stage 76 — snapshot ownership
- Stage 77 — central official UI dispatcher
- Stage 78 — global next-due queue
- Stage 78.1 — broker-route foundation

Stage 78.2 was originally in progress for Pishro Kaman.

Stage 79 / 80 / 81 had not yet been completed.

---

# Pishro route correction

The primary Pishro route was corrected from mobile to the root Kaman site.

Desired/current `BrokerProfile` behavior:

```csharp
public static BrokerProfile PishroKaman
{
    get;
} = new BrokerProfile(
    PishroKamanId,
    "پیشرو — کمان",
    "https://kaman.pishrobroker.ir/",
    "https://kaman.pishrobroker.ir",
    "kaman.pishrobroker.ir",
    supportsOfficialOrderUiAutomation: true,
    additionalTrustedOrigins: new[]
    {
        "https://mobile.pishrobroker.ir"
    },
    additionalMonitoredHosts: new[]
    {
        "mobile.pishrobroker.ir"
    });
```

Runtime validation confirmed that the application opens Kaman correctly and manual login works.

---

# Stage 78.2 — Pishro Kaman form discovery fix

## What was discovered

Kaman can render BUY and SELL controls at the same time.

Observed BUY inputs:
- `input#price-input`
- `input#count-input`

Observed BUY button class included:
- `sendorder-btn`
- `buy`

Visible BUY button text example:
- `خرید جوانه کوچک`

Observed SELL controls had similar IDs but belonged to a separate SELL scope.

## Fix applied in `PishroKamanOrderUiBridge.cs`

The form locator was changed so that it:

1. finds exactly one visible BUY action;
2. recognizes BUY using class token `buy` plus visible text beginning with `خرید`;
3. climbs to the relevant form/container scope;
4. finds the unique visible `price-input` and `count-input` only inside that BUY scope;
5. avoids mixing BUY and SELL inputs.

This runtime fix was successful.

Observed log:

```text
STATUS: DIALOG_ALREADY_OPEN
REASON: Usable official Pishro buy form is already visible.
```

---

# Kaman instrument verification — ISIN removed as a hard requirement

The Kaman UI does not expose/require ISIN for this flow.

The actual broker interaction is symbol-name based.

## New Kaman rule

For Pishro Kaman:
- instrument identity is verified using the symbol name visible in the official BUY action;
- empty ISIN is allowed;
- if an ISIN is present, it still must be syntactically valid;
- missing or mismatched symbol name still fails closed.

EasyTrader remains ISIN-dependent.

## `PishroKamanOrderUiBridge.cs`

The bridge now derives the symbol from the official BUY button.

Conceptually:

```js
const buySymbolName = form => {
    if (!form || !(form.buyAction instanceof HTMLElement))
        return "";

    const text = norm(
        form.buyAction.textContent ||
        form.buyAction.getAttribute("value") ||
        form.buyAction.getAttribute("aria-label"));

    return norm(text.replace(/^خرید(?:\s+|$)/, ""));
};
```

Prepare / submit / ensure / read paths for Kaman use this symbol name instead of requiring ISIN discovery.

---

# Reading price and quantity

A Kaman-specific input reader was added so price/quantity can be read from:

- `input.value`
- `value` attribute
- `data-value`
- `aria-valuenow`

This was necessary because the first read attempt reached:

```text
STATUS: ORDER_VALUES_NOT_READY
```

After the change, the flow advanced beyond that error.

---

# Commission and total value are not required for Kaman

The Kaman BUY form does not show commission or total order value, and the practical BUY payload only needs:
- symbol
- price
- quantity

Therefore Kaman must **not** fail because commission / total value are absent.

## `PishroKamanOrderUiBridge.cs`

For Kaman `read()`:
- read symbol name;
- read price;
- read quantity;
- return success without requiring commission or total.

Expected values:
- `commissionAmount = ""`
- `totalValue = ""`

## `MainWindow.xaml.cs`

`TryBuildPayloadFromOfficialForm` was changed so that:

- Pishro Kaman may have empty ISIN;
- Kaman does not require `CommissionAmount` from the broker form;
- Kaman does not require `TotalValue` from the broker form;
- local gross value can be calculated from:
  `price * quantity`
- Kaman commission rate may be represented as `0`.

EasyTrader validation behavior should remain unchanged.

## `OrderSubmissionValidator.cs`

Added broker-aware validation:
- EasyTrader still requires a valid non-empty ISIN and positive commission rate.
- Pishro Kaman:
  - allows empty ISIN;
  - if ISIN exists, it must still be valid;
  - allows commission `0`.

---

# Important runtime success — Kaman read + local preparation

The following test succeeded:

```text
ORDER READ FROM BROKER FORM: پیشرو — کمان
SYMBOL: جوانه کوچک
ISIN:
PRICE: 30291
QUANTITY: 500
SIDE: BUY
COMMISSION AMOUNT (FROM BROKER FORM):
TOTAL VALUE (FROM BROKER FORM):
HTTP POST: NOT SENT
```

Immediately afterward:

```text
LOCAL ORDER PREPARATION
RESULT: LOCALLY READY
PAYLOAD VALIDATION: PASSED
PAYLOAD FINGERPRINT: 07B923C2645B0244
TRUSTED OFFICIAL FORM OBSERVED: YES
DIRECT API CREDENTIALS: NOT ACCESSED
LIVE SUBMISSION: REQUIRES FINAL CONFIRMATION
HTTP POST: NOT SENT
```

This confirms:
- symbol read succeeded;
- price read succeeded;
- quantity read succeeded;
- ISIN is intentionally empty for Kaman;
- broker-form commission/total are intentionally empty;
- payload validation passed;
- no direct API POST was sent.

---

# Dry-Run button enable fix

Problem:
- `Dry-Run` remained disabled after a successful `Read & Confirm`.

Cause:
- `_currentOrderSnapshot` was created, but the Dry-Run button state was not refreshed afterward.

Fix:
after the snapshot is created, enable `OrderUiDryRunTimingButton` when all required conditions are true.

Conceptually:

```csharp
OrderUiDryRunTimingButton.IsEnabled =
    _selectedBroker.SupportsOfficialOrderUiAutomation &&
    !_scheduledOrderActive &&
    !_liveSubmissionInProgress &&
    _currentOrderSnapshot != null;
```

---

# Stage 78.2 Dry-Run result — SUCCESS

The Kaman Prepare-only Dry-Run succeeded with all 10 probes.

Observed summary:

```text
EASYTRADER PREPARE-ONLY DRY-RUN
PROBES: 10
TARGET INTERVAL: 1000 ms
FORM FIND + VALUE SET: YES
FINAL SUBMIT CLICK: NO
ORDER POST CREATED BY DRY-RUN: NO
DIRECT API CREDENTIALS: NOT ACCESSED
DRY-RUN SETUP STATUS: PREPARED
```

All probes returned:

```text
STATUS: PREPARED
```

Timing examples:

```text
#1  REQUEST JITTER MS: 0.0
#2  REQUEST JITTER MS: 8.6
#3  REQUEST JITTER MS: 11.6
#4  REQUEST JITTER MS: 9.6
#5  REQUEST JITTER MS: 8.8
#6  REQUEST JITTER MS: 7.2
#7  REQUEST JITTER MS: 4.2
#8  REQUEST JITTER MS: 6.1
#9  REQUEST JITTER MS: 2.4
#10 REQUEST JITTER MS: 1.0
```

Dry-Run final result:

```text
FINAL SUBMIT CLICK: NO
ORDER POST CREATED BY DRY-RUN: NO
```

Conclusion:
**Stage 78.2 is functionally successful for Kaman Prepare-only Dry-Run.**

Minor cosmetic issue:
the log title still says:

```text
EASYTRADER PREPARE-ONLY DRY-RUN
```

It should eventually become broker-aware, for example:

```text
PISHRO KAMAN PREPARE-ONLY DRY-RUN
```

---

# Live order confirmation window layout fix

File:
`LiveOrderConfirmationWindow.xaml`

Problem:
- on the user's display, the scheduling section was hidden;
- fields for start time / end time / max quantity were pushed below the visible area.

Original window had:
- fixed height;
- `ResizeMode="NoResize"`;
- no scrollable main content.

## Layout changes

The window was changed so that:

- main content is inside a vertical `ScrollViewer`;
- bottom action buttons remain fixed;
- the window is resizable;
- initial dimensions are slightly larger;
- scheduling fields remain accessible even with Windows DPI scaling.

Important:
A temporary XAML compile error occurred because `TextBlock.TextWrapping` was incorrectly attached directly to `CheckBox`.

That was corrected by using a `TextBlock` as the `CheckBox` content:

```xml
<CheckBox x:Name="FinalConfirmationCheckBox"
          Margin="0,0,0,8"
          FontWeight="SemiBold"
          Checked="FinalConfirmationCheckBox_Changed"
          Unchecked="FinalConfirmationCheckBox_Changed"
          AutomationProperties.Name="تأیید ارسال واقعی سفارش">
    <TextBlock Text="اطلاعات، حجم کل، سقف هر سفارش و بازه زمانی را بررسی کردم و ارسال پله‌ای سفارش‌ها را تأیید می‌کنم."
               TextWrapping="Wrap"/>
</CheckBox>
```

Another build failure occurred because a temporary file
`LiveOrderConfirmationWindow_FIXED_v2.xaml`
was left in the project directory.

Because it used the same:

```xml
x:Class="FastOrder.LiveOrderConfirmationWindow"
```

WPF compiled both files and generated duplicate fields / `InitializeComponent`.

The fix was to delete the temporary XAML file and clean `obj` / `bin`.

General rule:
**Never leave temporary `.cs` or `.xaml` copies inside the project folder when they define the same class.**

---

# Current exchange clock runtime evidence

Observed:

```text
EXCHANGE CLOCK: 2026-09-02 08:51:15.503 +03:30
EXCHANGE CLOCK SOURCE: TSETMC
EXCHANGE CLOCK ESTIMATED UNCERTAINTY MS: 503
```

So the TSETMC-backed exchange clock was active at runtime.

---

# Current live-submission blocker

After the user opened the real confirmation window and confirmed it, the application produced:

```text
LIVE ORDER SUBMISSION
RESULT: BLOCKED
REASON: خطای داخلی در مسیر کنترل‌شده رخ داد.
HTTP POST: NOT SENT
DIRECT API CREDENTIALS: NOT ACCESSED
```

This means the controlled live path failed before successful scheduling/submission.

No direct order POST occurred.

---

# Diagnostic changes added to `MainWindow.xaml.cs`

To identify the current live-path failure, additional trace logging was prepared.

The latest intended diagnostic trace points are:

```text
LIVE FLOW TRACE: CONFIRMATION WINDOW OPENING
LIVE FLOW TRACE: CONFIRMATION WINDOW CLOSED; RESULT=TRUE/FALSE
LIVE FLOW TRACE: CREATING SESSION; START=...; END=...; MAX_QTY=...
LIVE FLOW TRACE: SESSION CREATED; ID=...
LIVE FLOW TRACE: ENTERING SCHEDULER
LIVE FLOW TRACE: SCHEDULER RETURNED
```

The exception catch was also changed so it can log:

```text
LIVE ORDER INTERNAL ERROR
EXCEPTION TYPE: ...
EXCEPTION MESSAGE: ...
INNER EXCEPTION: ...
STACK TRACE: ...
FINAL SUBMIT CLICK: NO
HTTP POST: NOT SENT
```

Sensitive values must still not be logged.

---

# Current observed behavior after the latest diagnostic edit

The user reports:

> بعد از تایید ارسال سفارش هیچ سفارشی فرستاده نمی شود و در log هم چیزی نمی بینم

Meaning:
- after pressing the confirmation button, no order is sent;
- the expected new live-flow trace messages are not visible.

This is now the **immediate debugging target**.

---

# Files most relevant for Codex to inspect first

Priority order:

1. `MainWindow.xaml.cs`
2. `LiveOrderConfirmationWindow.xaml`
3. `LiveOrderConfirmationWindow.xaml.cs`
4. `PishroKamanOrderUiBridge.cs`
5. `OrderSubmissionValidator.cs`
6. `OrderSession.cs`
7. `GlobalNextDueQueue.cs`
8. `OfficialUiDispatcher` implementation
9. `BrokerProfile.cs`

Also inspect:
- `STAGE_IMPLEMENTATION_LOG.md`
- `MULTI_SESSION_ARCHITECTURE.md`

---

# Immediate next debugging steps for Codex

## 1. Verify local source really contains the trace changes

Search locally:

```powershell
rg -n "LIVE FLOW TRACE|LIVE ORDER INTERNAL ERROR" MainWindow.xaml.cs
```

If not found, the last diagnostic file was never actually copied into `MainWindow.xaml.cs`.

This is highly plausible because earlier several downloaded temporary files were not actually replacing the project file.

## 2. Verify no duplicate temporary files remain

Run:

```powershell
Get-ChildItem -File *.cs,*.xaml |
    Where-Object {
        $_.Name -match 'FIXED|DIAG|TRACE|FINAL|NO_DUP'
    } |
    Select-Object Name
```

Remove or rename such files outside the project if they define project classes.

Then:

```powershell
Remove-Item -Recurse -Force .\obj,.\bin -ErrorAction SilentlyContinue
dotnet build -c Debug
```

## 3. Inspect `SendLiveOrderButton_Click`

Confirm the exact path after:

```csharp
confirmationWindow.ShowDialog()
```

Verify that:
- `DialogResult == true` is observed;
- no early `return` occurs;
- session creation runs;
- scheduler invocation runs;
- UI/log dispatcher is not swallowing writes.

## 4. Inspect `LiveOrderConfirmationWindow.xaml.cs`

Current expected behavior:
- `SubmitButton_Click`
- calls `TryCreateSchedule(...)`
- assigns:
  - `ScheduledStartAt`
  - `ScheduledEndAt`
  - `MaxQuantityPerOrder`
- sets:
  `DialogResult = true`

If `DialogResult` is not returning `true`, check:
- validation failure path;
- fresh TSETMC clock requirement;
- time parsing;
- start/end range;
- max quantity validation.

## 5. Do not begin Stage 79 concurrency changes yet

Before introducing multi-session concurrency, first make sure the **single-session real controlled path** can:
- confirm;
- create a session;
- enter scheduler;
- reach the intended official UI action path.

Otherwise Stage 79 will compound the bug.

---

# Important known Stage 79 blocker in existing code

Historically `RunScheduledOrderAsync` contained a Stage 78 restriction similar to:

```csharp
if (!ReferenceEquals(
    nextDueSlice.Session,
    session))
{
    throw new InvalidOperationException(
        "Stage 78 only schedules the active session; " +
        "concurrent session execution starts in Stage 79.");
}
```

And the live send handler had guards based on:

```csharp
_scheduledOrderActive
_liveSubmissionInProgress
```

Do not simply delete those checks.

Before true Stage 79 concurrency, ownership of:
- cancellation;
- sent/in-flight counters;
- scheduling coordinator;
- next-due queue;
- WebView serialization

must be moved to a proper multi-session coordinator.

---

# Known compile/build hygiene issue

The project uses SDK-style wildcard source inclusion.

Therefore:
- any extra `.cs` file in the project directory is compiled;
- any extra `.xaml` file with the same `x:Class` is compiled.

Temporary filenames such as:

```text
MainWindow_FIXED_v2.xaml.cs
MainWindow_FINAL_NO_DUP.xaml.cs
PishroKamanOrderUiBridge_DIAG.cs
PishroKamanOrderUiBridge_DIAG2.cs
LiveOrderConfirmationWindow_FIXED_v2.xaml
```

must **not** remain in the project folder after replacement.

---

# Current build warning

The recurring warning:

```text
NU1900:
Error occurred while getting package vulnerability data:
Unable to load the service index for source
https://api.nuget.org/v3/index.json
```

is non-blocking and has not been the cause of the functional failures.

---

# Suggested Codex starting prompt

Use this with Codex:

```text
Continue FastOrder from the current local working tree.

Read:
- MULTI_SESSION_ARCHITECTURE.md
- STAGE_IMPLEMENTATION_LOG.md
- FASTORDER_HANDOFF_TO_CODEX_2026-09-02.md

Important invariants:
- no direct broker order API POST;
- no token/cookie/credential access;
- official broker UI only;
- fail closed on ambiguity;
- do not auto-retry after official submit click.

Pishro Kaman Stage 78.2 is already validated:
- official BUY form discovery works;
- symbol-name verification works without requiring ISIN;
- price and quantity are read correctly;
- commission and total value are not required for Kaman;
- payload validation passes;
- Prepare-only Dry-Run passed 10/10 probes.

Current blocker:
after confirming the real LiveOrderConfirmationWindow, no order is sent and the user sees no new trace log.

First:
1. inspect the actual local MainWindow.xaml.cs and confirm whether the LIVE FLOW TRACE instrumentation is present;
2. inspect SendLiveOrderButton_Click and the code immediately after ShowDialog();
3. inspect LiveOrderConfirmationWindow.xaml.cs;
4. identify why control does not visibly reach session creation/scheduler;
5. preserve all architecture/security invariants;
6. do not start Stage 79 multi-session concurrency until the single-session live controlled path reaches the scheduler reliably.

Prefer modifying complete files, building, and reporting exact changed files and runtime test steps.
```

---

# Current overall state

**Stable / validated**
- EasyTrader existing path
- Pishro Kaman root route
- Kaman official BUY form discovery
- Kaman symbol-name identity
- Kaman price/quantity read
- broker-aware payload validation
- Kaman no-ISIN path
- Kaman no-commission/no-total form path
- Local payload preparation
- Kaman Prepare-only Dry-Run
- TSETMC exchange clock
- confirmation-window scheduling fields visible after layout change

**Still unresolved**
- real controlled single-session flow after confirmation
- why no scheduler/live-flow trace is visible
- Stage 79 multi-session concurrency
- Stage 80 conflict detection
- Stage 81 UX completion

---

# Post-handoff resolution — 2026-09-02

The actual local source was inspected after this handoff was written.

- The promised `LIVE FLOW TRACE` checkpoints were not present in `MainWindow.xaml.cs`; only the
  final internal-error block had been added.
- `LiveOrderConfirmationWindow` correctly validated the schedule, assigned
  `ScheduledStartAt`/`ScheduledEndAt`/`MaxQuantityPerOrder`, set `DialogResult = true`, and returned
  control to `SendLiveOrderButton_Click`.
- The first deterministic post-confirmation failure was `OrderSession`: its constructor still
  rejected an empty `SymbolIsin` for every broker even though the validated Kaman flow uses the
  visible official BUY symbol name and intentionally permits an empty ISIN.
- Commit `1b31c7d8039e5c6d50bf22299b71dbe8d57b417b` made the session identity check broker-aware,
  preserved EasyTrader's ISIN requirement, rejected unknown broker identities, and added eight
  non-sensitive trace checkpoints through scheduler entry/return.
- Isolated Debug and Release builds passed with zero errors and zero warnings. A focused regression
  probe accepted Kaman with empty ISIN and rejected both EasyTrader with empty ISIN and an unknown
  broker.
- No live order was submitted during this verification. The older running Debug executable must be
  replaced with the new build, and one user-controlled runtime confirmation must reach
  `SESSION CREATED` and `ENTERING SCHEDULER` before Stage 79 begins.

## Final user-controlled runtime outcome

The user subsequently confirmed that all intended scheduled orders were sent and were registered
in Kaman's official broker order list. This closes the single-session scheduler-entry blocker and
completes Stage 78.2. The evidence is recorded as broker registration/acceptance, not as proof that
the exchange filled or executed the orders. Stage 79 concurrency remains not started.
