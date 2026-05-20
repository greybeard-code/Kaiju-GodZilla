# GodZuki — Signal Indicator

**Version:** 1.0.1
**Namespace:** `NinjaTrader.NinjaScript.Indicators.GreyBeard.KingPanaZilla`

GodZuki is the pure indicator version of GodZillaKilla. It reads the same six KingPanaZilla sub-indicators, evaluates the same confluence logic, applies the same EMA filter — but executes no trades. Use it to visually monitor signals on any chart, audit historical signal quality, trigger audio alerts, and log signal history to CSV.

---

## Version History

| Version | Summary |
|---|---|
| **1.0.1** | Fixed nested enum compile errors — `GodZukiSignalOperator`, `GodZukiHudCorner`, `GodZukiHudSize` moved to namespace level. Set 1 and Set 2 now draw independently on the same bar. Set 2 arrow offset increased (22 ticks vs Set 1 at 12 ticks). Group arrow labels changed from numeric suffix to `-S1` / `-S2`. |
| 1.0.0 | Initial release. |

---

## How It Differs from GodZillaKilla

| Feature | GodZillaKilla | GodZuki |
|---|---|---|
| Type | Strategy | Indicator |
| Trades | Yes (ATM or Fixed Ticks) | No |
| Session filters | Yes | No |
| News filter | Yes | No |
| Daily PnL limits | Yes | No |
| Martingale recovery | Yes | No |
| Signal visualization | Yes | Yes |
| Audio alerts | Yes | Yes |
| CSV logging | Trade log | Signal log |
| Data Box outputs | No | Yes (11 plots) |
| Public Series outputs | No | Yes |
| Control panel | Yes | No |

---

## Signal System

GodZuki uses the same signal configuration as GodZillaKilla: six sub-indicators, two independent trigger sets, configurable operators and threshold values per indicator.

### Sub-Indicator Signals
Each of the six indicators exposes a `Signal_Trade` series. GodZuki reads `Signal_Trade[0]` each bar and computes a normalized −1 / 0 / +1 output using the configured comparison operator and value.

### Required Count
With N indicators enabled and Required Count = R, the trigger fires when at least R signals agree in the same direction. Flat signals (0) do not count toward either side. If both long and short counts both reach R on the same bar, the conflict guard suppresses the trigger — no signal fires. Setting Required Count = N requires all enabled signals to agree.

### EMA Filter
Optional short/long EMA filter. When enabled, signals that conflict with the EMA direction are suppressed from visuals, arrows, audio, and the Set1/Set2 output plots. Individual sub-indicator signal values (KO–NC) in the Data Box reflect pre-filter raw values so near-misses remain visible.

### Trigger Sets — Independent Signals
Set 1 and Set 2 are evaluated and drawn **independently on every bar.** Both can produce arrows on the same bar simultaneously. This differs from GodZillaKilla where Set 2 is used only as a trading fallback when Set 1 does not trigger.

---

## Visual Output

### EMA Lines
When `EnableEmaFilter = true`, two EMA lines are plotted directly on the price panel:
- **EMA Short** — DodgerBlue, 2px
- **EMA Long** — HotPink, 2px

### Signal Arrows
Per-indicator and group trigger arrows are drawn on the price panel at configurable tick offsets above (short) or below (long) each signal bar. Rolling 250-bar cleanup prevents draw object pool exhaustion.

### Group Trigger Arrows
Set 1 and Set 2 arrows draw independently at different distances from the bar:

| Set | Arrow offset | Label format |
|---|---|---|
| Set 1 | ArrowOffset + 12 ticks | `GODZUKI-S1` (or custom text `-S1`) |
| Set 2 | ArrowOffset + 22 ticks | `GODZUKI-S2` (or custom text `-S2`) |

The 10-tick gap between Set 1 and Set 2 ensures the arrows and labels are visually distinct when both fire on the same bar.

### Group Trigger Back-Brush
When either Set 1 or Set 2 fires, the bar background is highlighted with a configurable semi-transparent brush. Set 1 takes priority when both fire.

---

## HUD (Dashboard)

The SharpDX overlay panel shows four fixed rows:

```
GodZuki  v1.0.1
─────────────────────────────────────
EMA: ON   21=19843.50 / 50=19856.25    ← green=bullish, red=bearish, dim=off
Set1: ON   Req:2/3                      ← white=active, dim=off
Set2: OFF  Req:3/4                      ← dim when disabled
```

- **EMA row** — shows `ON/OFF` status and live price values for each EMA when the filter is enabled; coloured green (bullish) or red (bearish)
- **Set1/Set2 rows** — show enabled/disabled status and required count threshold (`Req: required/enabled`)

The box height is fixed at 4 rows — no layout shifting as signals change.

Position: `TopLeft` / `TopRight` / `BottomLeft` / `BottomRight` / `Center` / `Hidden`.
Size: `Tiny` / `Small` / `Normal` / `Large` / `Huge`.

---

## Data Box

GodZuki registers 11 `AddPlot` entries visible when hovering over any bar:

| Plot | Index | Value | Filter applied? |
|---|---|---|---|
| EMA Short | Values[0] | Short EMA price | — |
| EMA Long | Values[1] | Long EMA price | — |
| Set1 Signal | Values[2] | −1 / 0 / 1 | Yes (EMA-filtered) |
| Set2 Signal | Values[3] | −1 / 0 / 1 | Yes (EMA-filtered) |
| EMA Dir | Values[4] | 1=bullish / −1=bearish / 0=off | — |
| KO Signal | Values[5] | −1 / 0 / 1 | No (raw) |
| PA Signal | Values[6] | −1 / 0 / 1 | No (raw) |
| TH Signal | Values[7] | −1 / 0 / 1 | No (raw) |
| SJ Signal | Values[8] | −1 / 0 / 1 | No (raw) |
| SU Signal | Values[9] | −1 / 0 / 1 | No (raw) |
| NC Signal | Values[10] | −1 / 0 / 1 | No (raw) |

`ShowTransparentPlotsInDataBox = true` is set so signal plots appear in the Data Box without drawing visible lines on the chart.

**Set1/Set2 vs individual signals:** Set1 and Set2 reflect the EMA-filtered result — 0 when the EMA filter blocks the group trigger — matching what arrows show. Individual KO–NC values always show the raw computed signal, so near-misses are visible even when the EMA filter suppressed the group trigger.

**Note:** If Set1 or Set2 show non-zero in the Data Box but no arrow appears on the chart, check that `Group: Show Trigger Arrows = true` in the Display properties and that `GroupTriggerBrush` is not set to a transparent colour.

---

## Public Series Outputs

All signal plots are exposed as typed public properties for use by external strategies or indicators:

```csharp
Series<double> Set1Signal  // Values[2] — EMA-filtered Set 1 group result
Series<double> Set2Signal  // Values[3] — EMA-filtered Set 2 group result
Series<double> EmaSignal   // Values[4] — EMA filter direction
Series<double> KOSignal    // Values[5] — KingOrderBlock raw signal
Series<double> PASignal    // Values[6] — PANAKanal raw signal
Series<double> THSignal    // Values[7] — ThunderZilla raw signal
Series<double> SJSignal    // Values[8] — SuperJumpBoost raw signal
Series<double> SUSignal    // Values[9] — SumoPullback raw signal
Series<double> NCSignal    // Values[10] — NobleCloud raw signal
```

**Usage from a consuming strategy:**
```csharp
var gz = GodZuki(/* params */);
if (gz.Set1Signal[0] == 1)   // Set 1 long trigger this bar
if (gz.Set2Signal[0] == -1)  // Set 2 short trigger this bar
if (gz.EmaSignal[0]  == 1)   // EMA filter is bullish
if (gz.PASignal[1]   == -1)  // PANAKanal was short last bar
```

All series support historical lookback via `[n]` indexing.

---

## Audio Alerts

When `EnableSignalAudioAlerts = true`:
- **Individual alerts** — fires when a single sub-indicator signal passes the EMA filter, deduped to once per bar per direction per indicator
- **Group alerts** — fires independently for Set 1 and Set 2; both can alert on the same bar

Both have independent WAV file selection. Deduplication uses a `CurrentBar:DIRECTION` stamp per alert key.

---

## CSV Signal Log

When `LogEnabled = true`, a CSV file is created at `State.DataLoaded`:

**Filename:** `GodZuki_[AccountName]_YYYYMMDD_HHmmss.csv`

Account name is read from the chart's ChartTrader account at load time. Falls back to `NoAccount` if unavailable.

**Columns:**
`DateTime, Instrument, Set1, Set2, EMA, KO, PA, TH, SJ, SU, NC`

**Write trigger:** One row per bar when any signal other than EMA fires (KO, PA, TH, SJ, SU, NC, Set1, or Set2 is non-zero). EMA column is always included as a status field showing filter direction at time of signal.

**DateTime** uses `Time[0]` (bar time) for correct timestamps during Market Replay and playback.

**Example:**
```
DateTime,Instrument,Set1,Set2,EMA,KO,PA,TH,SJ,SU,NC
2026-05-17 09:14:00,NQ 06-26,1,0,1,0,1,1,0,0,0
2026-05-17 09:35:00,NQ 06-26,-1,-1,-1,0,-1,-1,0,0,0
```

---

## Debug Output

Enable `EnableDebug = true` to see Output window diagnostics:

```
[GodZuki] DataLoaded | Instr=NQ 06-26 | Signals=[PA,TH,SJ] | Set1Req=2 | Set2=OFF | EMA=ON (21/50) | Log=ON
[GodZuki] CSV log opened | Acct=Sim101 | C:\...\GodZuki_Sim101_20260517_143022.csv
[GodZuki] Bar=1842 09:14:00 | KO=0 PA=1 TH=1 SJ=0 SU=0 NC=0
[GodZuki] Bar=1842 | Set1=LONG[PA+TH] OK | Set2=FLAT
[GodZuki] Bar=1843 | EMA filter BLOCKED signal(s) | EMA=BEARISH (19821.50/19844.25)
[GodZuki] Bar=1842 | AUDIO | PANAKanal LONG | Alert1.wav
[GodZuki] Bar=1842 | AUDIO | Group Trigger Set1 LONG | Alert2.wav
[GodZuki] Bar=1842 | CSV | Set1=1 Set2=0 EMA=1 KO=0 PA=1 TH=1 SJ=0 SU=0 NC=0
```

---

## Indicator Settings

All six sub-indicator parameters are exposed in the **Indicator Settings** section (hidden by default — toggle `Show Indicator Settings = true`). Parameter names and grouping exactly match GodZillaKilla for cross-reference.

See [Indicators.md](Indicators.md) for full parameter documentation.

---

## Compile Notes

The following types are defined at **namespace level** (not nested inside the class) to avoid type resolution errors when compiled alongside other KingPanaZilla indicators:

- `GodZukiSignalOperator` — comparison operator enum for signal thresholds
- `GodZukiHudCorner` — HUD position enum
- `GodZukiHudSize` — HUD size enum

GodZuki has **no dependency on GodZillaKilla**. It requires only the six KPZ sub-indicators (`gbKingOrderBlock`, `gbPANAKanal`, `gbThunderZilla`, `gbSuperJumpBoost`, `gbSumoPullback`, `gbNobleCloud`) and standard NT8/SharpDX framework types.

---

← [README.md](README.md)
