# GodZillaKilla — ATM Trading Strategy

**Version:** 1.7.3
**Namespace:** `NinjaTrader.NinjaScript.Strategies.Playr101`
**Author:** Playr101
**Credits:** GreyBeard, ninZa.co, RenkoKings, ES, rbro112

GodZillaKilla is a NinjaTrader 8 strategy that reads signals from the six GodZilla Suite sub-indicators and executes trades using either NT8 ATM templates or strategy-managed Fixed-Ticks orders. It is designed for live and replay trading on any chart type.

---

## Version History

| Version | Summary |
|---|---|
| **1.7.3** | Per-indicator **Require** flags added for both Set 1 and Set 2. When a `Require` flag is enabled, that indicator must be among the signals that fired in the trigger direction — a count that reaches Required Count without the required indicator does not trigger. Defaults to false (no change to existing behavior). HUD signal tracking split into two lines: `Set1 Enabled:` and `Set2 Enabled:` (Set 2 line hidden when Set 2 is disabled). Required indicators are prefixed with `+` on both lines. |
| **1.7.2** | Session PnL reset now fires from `Bars.IsFirstBarOfSession` on the primary bar series at the correct futures session open (e.g. 1700 CST for ES), not from the tick series at midnight. Martingale recovery blocked and `EnableMartingaleOnStopLoss` hidden in FixedTicks mode. `NC_Brush` hidden when `UseNCSignals = false`. NobleCloud Properties panel labels renamed from "NC:" to "NobleCloud:". |
| **1.7.1** | `LogEnabled` defaults to true (required for MONARCH trade ingestion). ATM strategy field now shows a dropdown populated from ATM templates on disk (`FriendlyAtmConverter`). Descriptions added to all Properties panel fields. Namespace import updated to `GreyBeard`. Sub-indicator enums and category attributes moved to namespace scope. |
| **1.7.0** | Reentrancy guard added to `FlattenEverything` (`_flattenLock` / `_flattenInProgress`) to prevent double-flatten on rapid tick sequences. `Account.Positions` iteration wrapped in `lock (Account.Positions)`. |
| **1.6.9** | `gbBarStatus` sub-indicator added with `ShowBarStatusIndicator` property. Dashboard auto-sizing: HUD box width now measured dynamically from text content via `MeasureHudTextWidth()` using DirectWrite TextLayout — eliminates text clipping at all sizes. Properties panel reorganized: "Display" split into "Dashboard Display" / "Indicator Display" / "ATM Marker Display". `IsExitOnSessionCloseStrategy` default changed to true. `UseNCSignals` default changed to false. |
| **1.6.6** | NobleCloud integration as 6th signal source (Set 1 and Set 2). OnOrderUpdate rejection handling for FixedTicks mode. RealtimeErrorHandling changed to StopCancelClose. Defense #3/#8 extended to martingale ATM path. WPF button click handler unsubscribe on disable (defense #5). _tradeMap upgraded to ConcurrentDictionary. FixedTicks PnL baseline for fresh-start accuracy. SafeSignalRead applied to all six signals. Indicator null diagnostic. Open PnL HUD row conditional on UseUnrealizedPnl. Entry/Exit labels anchor to bar High/Low. Confluence stats replace per-group stats. CSV log expanded to 14 columns. |
| 1.6.5 | Fixed CategoryOrder collision (Display/NobleCloud both at 12). Fixed CSV log header to match 14-column output. Fixed martingale close path to use `WriteTradeLogRecord`. Applied Defense #8 `WriteTradeLogRecord` patches to both normal ATM and martingale ATM stale-ID paths. |
| 1.6.4 | Internal bump by Playr101. |
| 1.6.3 | Added NobleCloud (NC) as sixth signal indicator. Defense #8 mid-trade staleness detection. |

---

## Order Management Modes

### ATM Strategy Mode
Submits a market entry order and immediately attaches a pre-configured NT8 ATM template for stop-loss, profit target, and trailing stop management. ATM templates are selected from the NT8 ATM library at configuration time — the ATM Strategy field shows a dropdown populated from templates on disk.

- Entry is via `AtmStrategyCreate` at bar close (pending signal queued on bar 0, executed on bar 1 tick series)
- Supports a one-time **Martingale Recovery** entry in the opposite direction after a stop-loss, using a separate ATM template
- Optional **ATM Plot Markers** draw entry/exit lines and labels directly on the price panel

### Fixed Ticks Mode
Strategy-managed entries with configurable quantity, stop-loss ticks, and profit target ticks. Supports optional breakeven logic (move stop to entry ± offset after price moves a set distance in favor).

---

## Signal System

### Sub-Indicators
GodZillaKilla instantiates all six GodZilla Suite sub-indicators at `State.DataLoaded`. Each exposes a `Signal_Trade` series; the strategy reads `Signal_Trade[0]` every bar close.

### Signal Configuration
Each indicator has independent Long and Short threshold values and comparison operators (`Equal`, `GreaterOrEqual`, `GreaterThan`, `LessOrEqual`, `LessThan`, `NotEqual`).

### Trigger Sets
Two independent group trigger sets can be configured:

| Setting | Description |
|---|---|
| **Set 1 Required Count** | Minimum number of enabled Set 1 signals that must agree on the same bar |
| **Set 2 Required Count** | Minimum number of enabled Set 2 signals that must agree |

**Required Count behavior:** With N indicators enabled and Required Count = R, the trigger fires when at least R signals agree in the same direction (long or short). Flat signals (0) are ignored. If both long and short sides both reach R on the same bar, the conflict guard suppresses the trigger. Setting Required Count = N effectively requires all enabled signals to agree.

If both sets fire in conflicting directions on the same bar, no entry is taken.

### Require Flags
Each indicator in Set 1 and Set 2 has a corresponding **Require** flag (`Set 1 Require KingOrderBlock`, `Set 2 Require PANAKanal`, etc.). All default to `false`.

When a Require flag is enabled, that indicator must be **one of the signals that actually fired** in the trigger direction on the entry bar. Meeting the Required Count without the required indicator vetoes the trigger entirely.

**Example:** Set 1 has 6 indicators enabled, Required Count = 3, PANAKanal is required. If Sumo + JumpBoost + ThunderZilla all fire long (`longAgree = 3 = needed`) but PANAKanal did not fire, the trigger is suppressed. PANAKanal must appear among the 3 agreeing signals for the entry to proceed.

Multiple indicators can be required simultaneously — all required indicators must be present in the agreeing set.

### EMA Filter
Optional EMA filter using a short and long period. When enabled:
- Long entries require short EMA > long EMA (bullish trend)
- Short entries require short EMA < long EMA (bearish trend)

---

## Filters

### Session Time Filters
Up to three configurable trading windows (TF1, TF2, TF3) with optional flatten-at-window-end per window. An additional Skip Window suppresses entries during a configurable midday or news window.

### News Filter
Integrates with `gbNewsSignals` for real-time economic calendar blocking. Configurable pre/post block minutes, impact level toggles (High / Medium / Low), and NT8 Alert integration. Live chart only — automatically disabled in Strategy Analyzer and Market Replay.

---

## Risk Management

| Feature | Description |
|---|---|
| **Daily Profit Target** | Flattens all positions and disables entries when total PnL reaches the target |
| **Daily Loss Limit** | Same for loss side |
| **Use Unrealized PnL** | Includes open position PnL in the daily limit calculation |
| **Start Fresh On Enable** | Ignores historical trade PnL when the strategy is enabled in realtime |
| **Martingale On Stop Loss** | Fires one recovery trade in the opposite direction after a stop-loss event |

---

## Defense Mechanisms

GodZillaKilla includes eight layered defenses against NT8 lifecycle edge cases:

| Defense | Trigger | Action |
|---|---|---|
| #1 | Entry order fills before ATM registration | Position adopted; ATM state corrected |
| #2 | Duplicate fill events | Second fill ignored via execution ID tracking |
| #3 | ATM ID never confirmed (registration timeout) | Stale ID cleared after 10 seconds |
| #4 | Draw object pool exhaustion | Rolling 250-bar cleanup of per-prefix draw tags |
| #5 | Naked position at strategy enable | Detects and adopts pre-existing account position |
| #6 | Position inherited from prior session | Captured as baseline; PnL calculated from delta |
| #7 | `TryGetAtmMarketPositionSafe` failure | Falls back to account-level position check |
| #8 | Mid-trade ATM ID goes stale (HDS bounce) | Writes trade log, flattens at account level, resets all ATM state |

**Defense #8 detail:** Fires inside `EvictStaleAtmIdsIfTimedOut` on every tick. Detects a mismatch between the ATM reporting Flat and the account still holding a position. `WriteTradeLogRecord` is called **before** clearing `_atmPositionConfirmed` — both the normal ATM path and the martingale ATM path are protected. The estimated PnL from `dailyUnrealizedPnL` is used for the forced-close log record.

**FlattenEverything reentrancy guard (v1.7.0+):** `_flattenInProgress` flag and `_flattenLock` prevent double-flatten when rapid ticks fire the method concurrently. The inner check inside the lock ensures thread safety under NT8's mixed threading model.

---

## Dashboard (HUD)

The SharpDX overlay panel shows:
- Strategy name and version
- Master arm status (ENABLED / DISABLED) with L / S / REV sub-status
- Session status (IN SESSION / OUT OF SESSION)
- News filter status (if enabled)
- Strategy PnL, Daily PnL, Open PnL (Open PnL row visible only when Use Unrealized PnL is enabled)
- Risk target and loss limit settings
- Current trade status (IDLE / IN POSITION)
- Last trade summary with PnL
- Optional signal tracking stats (per-indicator win/loss counts and confluence combo stats)
  - **Set1 Enabled** row — lists active Set 1 indicators; required indicators are prefixed with `+` (e.g. `KO, +PA, TH`)
  - **Set2 Enabled** row — same for Set 2; hidden when Set 2 is disabled

The HUD box auto-sizes to fit its content — width is measured dynamically using DirectWrite TextLayout so text is never clipped regardless of font size or dashboard size setting.

Position: configurable (`TopLeft` / `TopRight` / `BottomLeft` / `BottomRight` / `Center` / `Hidden`).
Size: `Tiny` / `Small` / `Normal` / `Large` / `Huge`.

---

## Control Panel

An on-chart WPF button panel (ARM LONG / ARM SHORT / REV / AUTO / CLOSE) allows realtime manual control of:
- Arming long and/or short entries independently
- Toggling auto-arm (enables/disables all automated entries and clears L/S/REV state on disable)
- Toggling reverse-on-opposite-signal behaviour
- Immediately flattening all positions and cancelling orders

---

## Audio Alerts

- Individual signal alerts — fires when a single sub-indicator signal passes the filter
- Group trigger alerts — fires on a Set 1 or Set 2 confluence trigger
- Both have independent sound file selection and per-bar deduplication

---

## CSV Trade Log

When `LogEnabled = true` (default), a CSV file is created at `State.DataLoaded`:

**Filename:** `GodZilla_[AccountName]_YYYYMMDD_HHmmss.csv`

**Columns (14):**
`OpenTime, Account, Instrument, OpenPrice, Qty, CloseTime, Trigger, Direction, AtmStrategyName, RealizedPnL, SignalCombo, UsedSignals, TradeResult, LastTradeLine`

One row is written per closed trade. Defense #8 forced-close events also write a log row using the estimated unrealized PnL at time of detection. These CSV files are read by the MONARCH Intelligence Report System to generate daily and weekly performance reports.

---

## Key Properties Quick Reference

| Category | Key Properties |
|---|---|
| ATM Parameters | `OrderMode`, `AtmStrategy`, `MartingaleAtmStrategy`, `FixedOrderQuantity`, `FixedStopLossTicks`, `FixedProfitTargetTicks` |
| Signals | `GroupTriggerSet1RequiredCount`, `UseKOSignals`…`UseNCSignals`, `RequireKOSignal`…`RequireNCSignal`, `KO_LongOperator`…`NC_ShortValue`, `G2_RequireKOSignal`…`G2_RequireNCSignal` |
| Filters | `EnableEmaFilter`, `EmaShortPeriod`, `EmaLongPeriod`, `EnableNewsFilter` |
| Session | `EnableTF1`…`EnableTF3`, `StartTime1`…`EndTime3`, `EnableSkipTimeWindow` |
| Risk | `EnableDailyProfitTarget`, `DailyProfitTarget`, `EnableDailyLossLimit`, `DailyLossLimit` |
| Dashboard Display | `ShowDashboard`, `DashboardPosition`, `DashboardSize` |
| Indicator Display | `ShowBarStatusIndicator` |
| ATM Marker Display | `ShowEntryExitMarkers` |
| Audio Alerts | `EnableSignalAudioAlerts`, `IndividualSignalAlertSound`, `GroupSignalAlertSound` |
| Logging | `LogEnabled`, `EnableDebug` |

---

← [README.md](../README.md)
