# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

## Author

**GreyBeard** — greybeard@greybeardconsulting.net — [greybeardconsulting.net](https://greybeardconsulting.net)

---

## What This Is

NinjaScript C# code for NinjaTrader 8 (NT8). There is no build system, test runner, or linter — all compilation happens inside NT8's built-in editor. The only way to validate code is to paste it into NT8 and compile it there.

**Files must be loaded into NT8** via `Tools → Edit NinjaScript → Indicators` or `Strategies`. NT8 compiles the entire namespace together, so all files in `NinjaTrader.NinjaScript.Indicators.GreyBeard` are compiled as one unit.

---

## Namespaces

| Namespace | Contents |
|---|---|
| `NinjaTrader.NinjaScript.Indicators.GreyBeard` | All six sub-indicators, GodZuki, and their helper types |
| `NinjaTrader.NinjaScript.Strategies.Playr101` | GodZillaKilla strategy |

**Never change the namespaces.** NT8 uses them for internal serialization of saved chart templates and ATM settings. Renaming breaks all existing user configurations silently.

The default namespace for all new indicators is `NinjaTrader.NinjaScript.Indicators.GreyBeard`. New strategies go under `NinjaTrader.NinjaScript.Strategies.GreyBeard`.

---

## Directory Structure

```
Kaiju/
├── *.cs              Active NinjaScript source — all indicators and strategies
├── CLAUDE.md         This file
├── README.md         Suite overview
├── documents/        Markdown reference docs only — not loaded by NT8
├── bug_reports/      Incoming user bug reports, patches, and porting notes — reports and diffs only, source copies are gitignored
├── originals/        Pre-edit file snapshots kept before major changes — never load into NT8
├── old versions/     Historical releases for rollback reference — never load alongside current files (namespace conflicts)
└── sound files/      WAV files for audio alerts — must be copied to NT8's sounds directory to appear in the properties picker
└── MONARCH/          MONARCH Reporting system for GodZillaKilla
```

---

## Compile Order Dependency

The six sub-indicators must be compiled and present in NT8 **before** GodZillaKilla or GodZuki will compile. If a sub-indicator is missing or broken, both consumers fail with unhelpful errors.

Compile order: sub-indicators → GodZuki → GodZillaKilla.

---

## NT8 Lifecycle — Critical

NT8 calls `OnStateChange()` with sequential states. Each has strict rules:

| State | What to do |
|---|---|
| `SetDefaults` | Set property defaults only. No indicator instantiation, no file I/O, no API calls. |
| `DataLoaded` | Instantiate child indicators via factory methods (e.g. `gbKingOrderBlock(...)`). Create `Series<double>`. Open log files. |
| `Realtime` | Live trading begins. ATM operations only valid here. |
| `Terminated` | Dispose everything: SharpDX resources, `StreamWriter`, event handler unsubscriptions. |

Never call `AddChartIndicator()` outside `DataLoaded`. Never create `Series<double>` outside `DataLoaded`.

**Required defaults for every indicator** — set these in `State.SetDefaults`:
```csharp
ShowTransparentPlotsInDataBox = true;  // signal plots appear in the NT8 Data Box without drawing visible lines
IsSuspendedWhileInactive      = false; // indicator stays active and updating even when the chart tab is not visible
```
Omitting either will cause signal values to disappear from the Data Box or stop updating on background charts.

---

## Signal_Trade Contract

Each sub-indicator exposes a `Signal_Trade` series. **The backing `Values[n]` index varies per indicator — never assume `Values[0]`:**

| Indicator | Signal_Trade backing |
|---|---|
| gbKingOrderBlock | `Values[0]` |
| gbPANAKanal | `Values[4]` |
| gbThunderZilla | `Values[3]` |
| gbSuperJumpBoost | `Values[1]` |
| gbSumoPullback | `Values[1]` |
| gbNobleCloud | `Values[2]` |

Always read via the `Signal_Trade` property, never `Values[n]` directly from a consumer.

All signal reads in GodZillaKilla and GodZuki go through `SafeSignalRead(Func<double> getter, string src)` which catches exceptions and returns 0.0 on failure. Use this wrapper whenever reading `Signal_Trade[0]`.

---

## AddChartIndicator Warning

`AddChartIndicator()` registers the indicator as a secondary data series on the chart. This causes NT8 to call `OnBarUpdate()` once per bar per indicator (not just once for the primary series). GodZillaKilla gates on `BarsInProgress != 0` at the top of `OnBarUpdate` to skip secondary-series callbacks.

For daily-resolution or timezone indicators, `AddChartIndicator` can trigger secondary series with different bar counts — this causes index misalignment. These indicators must be bootstrapped manually without `AddChartIndicator`.

---

## Threading Model

NT8 has two relevant threads:

- **Data thread** — runs `OnBarUpdate`, `OnMarketData`, `OnExecutionUpdate`, `OnOrderUpdate`. All trading logic lives here.
- **UI thread** — runs `OnRender`, `OnRenderTargetChanged`. All SharpDX drawing lives here.

**Rules:**
- Never call SharpDX draw methods from the data thread.
- Never call NT8 order/ATM methods from `OnRender`.
- `_tradeMap` is `ConcurrentDictionary` because `SystemPerformance` callbacks can fire from background threads.
- `Account.Positions` iteration requires `lock (Account.Positions)` — see `IsAtmMidTradeStale()` for the correct pattern.
- HUD data is passed from data thread to UI thread via plain string fields. This is safe because strings are immutable (torn reads produce a valid old or new string, never garbage).

---

## SharpDX Resource Rules

SharpDX brushes and text formats are owned by the UI thread and tied to the `RenderTarget`.

- All `SharpDX.Direct2D1.*` and `SharpDX.DirectWrite.*` objects must be created in `OnRender` or `OnRenderTargetChanged`, never in `OnStateChange` or `OnBarUpdate`.
- `OnRenderTargetChanged` is called on device loss and chart resize. Dispose all SharpDX resources there and recreate them on next `OnRender`.
- Gate recreation on `!object.ReferenceEquals(RenderTarget, _lastSeenRenderTarget)` to skip duplicate calls with the same target.
- Call `DisposeSharpDxResources()` in `State.Terminated`.

**SharpDX / WPF type collision:** `Brush`, `Color`, `FontStyle`, `FontWeight` exist in both namespaces. The alias block at the top of each file resolves this — always keep it:
```csharp
using Brush     = System.Windows.Media.Brush;
using Color     = System.Windows.Media.Color;
using FontStyle = SharpDX.DirectWrite.FontStyle;
using FontWeight = SharpDX.DirectWrite.FontWeight;
```

**WPF brushes** (used for `Draw.*` calls and `BackBrush`) must be frozen before use: call `brush.Freeze()` or use `MakeFrozenBrush()`. Unfrozen brushes passed to NT8 draw calls cause cross-thread exceptions.

---

## Dynamic Property Panel (ICustomTypeDescriptor)

Both GodZillaKilla and GodZuki implement `ICustomTypeDescriptor` to show/hide properties in the NT8 Properties panel based on other property values (e.g., NC display properties are hidden when `UseNCSignals = false`).

- Property removal is handled by `RemoveProperties(PropertyDescriptorCollection col, params string[] names)`.
- Names passed to `RemoveProperties` are the **C# property names** (e.g., `"ShowNCSignalArrows"`), not Display Name strings.
- Properties decorated with `[RefreshProperties(RefreshProperties.All)]` trigger a full panel refresh when changed — required for any property that controls visibility of others.
- `[NinjaScriptProperty]` is required on all properties that must persist in saved chart templates.

---

## Draw Tag Rolling Cleanup

Every `Draw.ArrowUp`, `Draw.ArrowDown`, and `Draw.Text` call with a unique-per-bar tag holds a WPF geometry reference indefinitely. Over a multi-hour session this exhausts the WPF draw object pool ("Not Enough Quota" error).

The pattern used throughout: generate a tag with `CurrentBar` embedded, then remove the tag from `DRAW_TAG_KEEP` bars ago (250 bars ≈ 2 hours on 30s bars):

```csharp
private const int DRAW_TAG_KEEP = 250;
// In DrawSignalArrow():
int oldBar = CurrentBar - DRAW_TAG_KEEP;
RemoveDrawObject(prefix + oldBar);
Draw.ArrowUp(this, prefix + CurrentBar, false, ...);
```

Always follow this pattern for any new per-bar draw calls.

---

## ATM Strategy Lifecycle and Defenses

GodZillaKilla wraps NT8's ATM API in eight defenses against documented NT8 edge cases:

| Defense | Problem | Solution |
|---|---|---|
| #3 | `AtmStrategyCreate` callback never fires; ID stays set forever causing log floods | Clear `atmStrategyId` after `ATM_REGISTRATION_TIMEOUT_SEC` (10s) via `EvictStaleAtmIdsIfTimedOut()` |
| #4 | Draw object pool exhaustion | DRAW_TAG_KEEP rolling cleanup (see above) |
| #5 | WPF button click handlers accumulate across enable/disable cycles | Explicitly `-=` all click handlers in `State.Terminated` and null the button refs |
| #8 | ATM ID goes stale mid-trade after HDS bounce | `IsAtmMidTradeStale()` detects ATM-says-Flat vs Account-still-open mismatch; **double-confirms over `AtmStaleConfirmSeconds` (2s)**, then `WriteTradeLogRecord` and `FlattenNakedAccountPosition` |

`EvictStaleAtmIdsIfTimedOut()` runs on every tick (called from `OnBarUpdate`). It checks both the normal ATM ID and the martingale ATM ID.

**Never use `FlattenEverything` to close an ATM-managed position when the ATM ID may be dead.** It calls `AtmStrategyClose` (a no-op on an ID NT8 has lost) and `ExitLong`/`ExitShort` gated on `Position.MarketPosition` — which is always Flat in ATM mode, because the position belongs to the ATM and not to the strategy. All that actually executes is `Account.Cancel` on the working orders, so it strips the brackets off a position it leaves open. `FlattenNakedAccountPosition` is the path that closes it (via `Account.Flatten`), at the cost of NT8 disabling the strategy. `FlattenEverything` remains correct for the normal paths — daily limits, trading-window close, CLOSE ALL — where the ATM ID is alive and `AtmStrategyClose` works.

**Both watchdogs double-confirm before acting.** The naked-position watchdog and Defense #8 each take an irreversible action (account flatten, which NT8 turns into a strategy shutdown) off an inherently racy observation — orders in flight, ATM state propagating asynchronously. Each arms a suspect flag on the first sighting and only acts if the condition still holds on a second look. Any new check that flattens or tears down trade state should follow the same pattern.

---

## PnL Accounting

Two modes with separate accounting paths:

**ATM mode:** `totalRealizedPnL` is accumulated via `lastAtmRealizedPnL` on each trade close detected in `OnBarUpdate`. `TryGetAtmRealizedPnlSafe` reads the realized value from the ATM before clearing the ID.

**FixedTicks mode:** `totalRealizedPnL` is synced from `SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit` via `TrySyncFixedTicksPnlFromSystemPerformanceThrottled`. A baseline (`fixedPerformanceRealizedBaseline`) is captured at enable time when `StartFreshOnEnable = true` so historical trades don't inflate session PnL.

`dailyRealizedPnL = totalRealizedPnL - sessionStartTotalRealizedPnL` is recalculated each tick. `totalRunningPnL` adds unrealized PnL when `UseUnrealizedPnl = true`.

---

## Key Gotchas

- **`SimpleFont` size:** Pass size in the constructor only — `new SimpleFont("Agency Fb", 20) { Bold = true }`. Setting both the constructor arg and `{ Size = N }` causes the property to override the constructor silently.
- **`IsExitOnSessionCloseStrategy = true`** (with `ExitOnSessionCloseSeconds = 30`) — NT8's built-in session-close auto-exit is kept ON as a backstop. The strategy's own TF/daily-limit `FlattenEverything` paths normally flatten earlier; this ensures no position is carried overnight if one of those gates is missed (strategy disabled mid-day, TF3 EndTime set away from session close, etc.).
- **`RealtimeErrorHandling = StopCancelClose`** — order rejections surface to `OnOrderUpdate` instead of being swallowed. The `OnOrderUpdate` override handles FixedTicks entry and protective order rejections.
- **Enums at class level vs namespace level:** GodZillaKilla defines its enums inside the class (they are not shared). GodZuki defines `GodZukiSignalOperator`, `GodZukiHudCorner`, `GodZukiHudSize` at namespace level to avoid resolution conflicts with NT8's cross-file compilation.
- **`Account.Positions` is not a thread-safe collection** — always `lock (Account.Positions)` before iterating it from any path that can be called off the data thread (e.g., `OnOrderUpdate`).
- **`GetAtmStrategy*` does not throw for a missing ATM ID — it logs and returns `Flat`.** NT8 writes `'GetAtmStrategyMarketPosition' method error: ATM strategy ID '...' does not exist` at level 3 and returns a default. Every `try/catch` and `Try...Safe()` wrapper around these calls is therefore dead code for that failure, and at tick rate it floods the log (6–12 errors per entry were observed before the fix). The only remedy is to not make the call: `IsAtmIdQueryable()` gates the query until the `AtmStrategyCreate` callback confirms registration. **Route every new ATM position query through it.** Corollary: a `Flat` return never proves the position is closed — hence the double-confirm on both watchdogs.
- **`Account.Flatten()` disables every NinjaScript strategy on the account.** NT8 does this in the same millisecond, whoever calls it — the strategy's own watchdog, or the user hitting Close/Flatten in Chart Trader. It is the only reliable way to close an ATM-managed position with a dead ATM ID, so it cannot simply be removed from the emergency paths; treat any new call site as a deliberate trading halt. `FlattenEverything` does *not* call it, which is why the daily limits, the trading-window close, and the CLOSE ALL button leave the strategy armed.
- **`OrderState` has more live states than `Working`/`Accepted`/`Submitted`.** A healthy OCO bracket spends real time in `ChangePending`, `ChangeSubmitted`, `CancelPending`, and `PartFilled` — an OCO quantity adjustment after a partial fill hits all of them. Treating those as "no protection" is what made `HasWorkingProtectiveOrders` report a fully-bracketed position as naked. Any code that decides whether protection exists must accept the transitional states.
