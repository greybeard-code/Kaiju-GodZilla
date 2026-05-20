# GodZillaKilla — ATM Trading Strategy

**Version:** 1.6.5
**Namespace:** `NinjaTrader.NinjaScript.Strategies.Playr101`
**Author:** Playr101
**Credits:** GreyBeard, ninZa.co, RenkoKings, ES, rbro999

GodZillaKilla is a NinjaTrader 8 strategy that reads signals from the six KingPanaZilla sub-indicators and executes trades using either NT8 ATM templates or strategy-managed Fixed-Ticks orders. It is designed for live and replay trading on any chart type.

---

## Version History

| Version | Summary |
|---|---|
| **1.6.5** | Fixed CategoryOrder collision (Display/NobleCloud both at 12). Fixed CSV log header to match 14-column output. Fixed martingale close path to use `WriteTradeLogRecord`. Applied Defense #8 `WriteTradeLogRecord` patches to both normal ATM and martingale ATM stale-ID paths. |
| 1.6.4 | Internal bump by Playr101. |
| 1.6.3 | Added NobleCloud (NC) as sixth signal indicator. Defense #8 mid-trade staleness detection. |

---

## Order Management Modes

### ATM Strategy Mode
Submits a market entry order and immediately attaches a pre-configured NT8 ATM template for stop-loss, profit target, and trailing stop management. ATM templates are selected from the NT8 ATM library at configuration time.

- Entry is via `AtmStrategyCreate` at bar close (pending signal queued on bar 0, executed on bar 1 tick series)
- Supports a one-time **Martingale Recovery** entry in the opposite direction after a stop-loss, using a separate ATM template
- Optional **ATM Plot Markers** draw entry/exit lines and labels directly on the price panel

### Fixed Ticks Mode
Strategy-managed entries with configurable quantity, stop-loss ticks, and profit target ticks. Supports optional breakeven logic (move stop to entry ± offset after price moves a set distance in favor).

---

## Signal System

### Sub-Indicators
GodZillaKilla instantiates all six KPZ sub-indicators at `State.DataLoaded`. Each exposes a `Signal_Trade` series; the strategy reads `Signal_Trade[0]` every bar close.

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

---

## Dashboard (HUD)

The SharpDX overlay panel shows:
- Strategy name and version
- Master arm status (ENABLED / DISABLED) with L / S / REV sub-status
- Session status (IN SESSION / OUT OF SESSION)
- News filter status (if enabled)
- Strategy PnL, Daily PnL, Open PnL
- Risk target and loss limit settings
- Current trade status (IDLE / IN POSITION)
- Last trade summary with PnL
- Optional signal tracking stats (per-indicator win/loss counts and confluence combo stats)

Position: configurable (`TopLeft` / `TopRight` / `BottomLeft` / `BottomRight` / `Center` / `Hidden`).
Size: `Tiny` / `Small` / `Normal` / `Large` / `Huge`.

---

## Control Panel

An on-chart WPF button panel (ARM LONG / ARM SHORT / REV / AUTO / CLOSE) allows realtime manual control of:
- Arming long and/or short entries independently
- Toggling auto-arm (enables/disables all automated entries)
- Toggling reverse-on-opposite-signal behaviour
- Immediately flattening all positions and cancelling orders

---

## Audio Alerts

- Individual signal alerts — fires when a single sub-indicator signal passes the filter
- Group trigger alerts — fires on a Set 1 or Set 2 confluence trigger
- Both have independent sound file selection and per-bar deduplication

---

## CSV Trade Log

When `LogEnabled = true`, a CSV file is created at `State.DataLoaded`:

**Filename:** `GodZilla_[AccountName]_YYYYMMDD_HHmmss.csv`

**Columns (14):**
`OpenTime, Account, Instrument, OpenPrice, Qty, CloseTime, Trigger, Direction, AtmStrategyName, RealizedPnL, SignalCombo, UsedSignals, TradeResult, LastTradeLine`

One row is written per closed trade. Defense #8 forced-close events also write a log row using the estimated unrealized PnL at time of detection.

---

## Key Properties Quick Reference

| Category | Key Properties |
|---|---|
| ATM Parameters | `OrderMode`, `AtmStrategy`, `MartingaleAtmStrategy`, `FixedOrderQuantity`, `FixedStopLossTicks`, `FixedProfitTargetTicks` |
| Signals | `GroupTriggerSet1RequiredCount`, `UseKOSignals`…`UseNCSignals`, `KO_LongOperator`…`NC_ShortValue` |
| Filters | `EnableEmaFilter`, `EmaShortPeriod`, `EmaLongPeriod`, `EnableNewsFilter` |
| Session | `EnableTF1`…`EnableTF3`, `StartTime1`…`EndTime3`, `EnableSkipTimeWindow` |
| Risk | `EnableDailyProfitTarget`, `DailyProfitTarget`, `EnableDailyLossLimit`, `DailyLossLimit` |
| Display | `ShowDashboard`, `DashboardPosition`, `DashboardSize`, `ShowEntryExitMarkers` |
| Audio | `EnableSignalAudioAlerts`, `IndividualSignalAlertSound`, `GroupSignalAlertSound` |
| Logging | `LogEnabled`, `EnableDebug` |

---

← [README.md](README.md)
