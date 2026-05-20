# GodZilla Suite — NinjaTrader 8

**Namespace:** `NinjaTrader.NinjaScript.Indicators.GreyBeard.KingPanaZilla`

A NinjaTrader 8 trading system built around six specialized signal indicators unified under a common namespace and signal contract. The suite has three layers: a set of purpose-built sub-indicators that generate numeric `Signal_Trade` outputs, a pure signal indicator (GodZuki) for visual monitoring, and a fully automated ATM trading strategy (GodZillaKilla) that acts on those signals.

---

## Components

### GodZillaKilla — ATM Trading Strategy
*Current version: 1.6.6*

Automated NinjaTrader 8 strategy that reads signals from all six GodZilla Suite sub-indicators and executes ATM or Fixed-Ticks trades based on configurable confluence rules. Includes session filters, EMA filter, news filter, daily PnL limits, martingale recovery, and a full SharpDX dashboard.

→ [GodZillaKilla.md](documents/GodZillaKilla.md)

---

### GodZuki — Signal Indicator
*Current version: 1.0.3*

Pure signal indicator version of GodZillaKilla. No trading — add GodZuki to any chart to visualize the same confluence signals, trigger audio alerts, log signal history to CSV, and expose all signal values in the NT8 Data Box. Signal Set 1 and Set 2 draw independently on the same bar. Useful for monitoring, backtesting signal quality, and driving custom strategies via public `Series<double>` outputs.

→ [GodZuki.md](documents/GodZuki.md)

---

### GodZilla Indicators — Signal Engine
*Six sub-indicators powering both GodZillaKilla and GodZuki*

| Indicator | Short Name | What it detects |
|---|---|---|
| gbKingOrderBlock | KO | Institutional order blocks via BOS/CHoCH structure breaks |
| gbPANAKanal | PA | Adaptive Keltner channel trend, breaks, and pullbacks |
| gbThunderZilla | TH | Dual-system trend + multi-oscillator pullback and slowdown |
| gbSuperJumpBoost | SJ | ATR-derived multi-level supply/demand zones |
| gbSumoPullback | SU | Multi-MA cloud pullback pattern detector |
| gbNobleCloud | NC | Kernel-envelope cloud with re-entry trade signals |

All six expose a `Signal_Trade` series using a consistent **−1 / 0 / +1** (or extended integer) numeric contract that GodZillaKilla and GodZuki read directly.

→ [Indicators.md](documents/Indicators.md)

---

## Quick Start

1. Compile the six sub-indicators first — they must be present in the `KingPanaZilla` namespace before GodZillaKilla or GodZuki will compile.
2. Add **GodZuki** to a chart to verify signal output before enabling live trading.
3. Add **GodZillaKilla** to the same chart and configure your ATM template, signal set, and session times.

---

## File Index

| File | Purpose |
|---|---|
| `GodZillaKilla.cs` | ATM trading strategy (v1.6.6) |
| `GodZuki.cs` | Signal visualization indicator (v1.0.3) |
| `gbKingOrderBlock.cs` | KO sub-indicator |
| `gbPANAKanal.cs` | PA sub-indicator |
| `gbThunderZilla.cs` | TH sub-indicator |
| `gbSuperJumpBoost.cs` | SJ sub-indicator |
| `gbSumoPullback.cs` | SU sub-indicator |
| `gbNobleCloud.cs` | NC sub-indicator |
| `gbBarStatus.cs` | Bar status utility indicator |
| `NewsSignals.cs` | Economic calendar signal source |
