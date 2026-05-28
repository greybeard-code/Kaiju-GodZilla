# CLAUDE.md – MONARCH Intelligence Report System

Guidance for working with this Python sub-project inside the Kaiju repository.

---

## What This Is

A standalone Windows executable (`MONARCH.exe`) that generates HTML trade reports
from NinjaTrader 8 GodZilla CSV logs. It requires no Python on the target machine
once compiled with Nuitka.

Source lives at `C:\Dev\Kaiju\MONARCH\src\`.
Compiled exe deploys to `%USERPROFILE%\Documents\NinjaTrader 8\MONARCH\MONARCH.exe`.

---

## Python Environment – Critical

The machine runs **pyenv-win** with multiple Python versions:

| Command   | Version | Owns Nuitka? |
|-----------|---------|--------------|
| `python`  | 3.14.3  | No           |
| (pyenv)   | 3.10.5  | Yes – Nuitka is installed here |

**Never invoke Nuitka with the default `python` command.** The build script
derives the correct interpreter from `pip show nuitka`'s `Location:` field
(site-packages → up two dirs → `python.exe`).

Running from source (`python src\monarch.py`) works with any Python – the
3.10.5 restriction applies only to Nuitka compilation.

The canonical build tool is `build.ps1` (PowerShell). `build.bat` is a thin
wrapper that launches `build.ps1` via PowerShell for cmd.exe environments.

---

## Building

```powershell
# One-time: generate the exe icon
python make_icon.py        # auto-installs Pillow; writes monarch.ico

# Compile + deploy
.\build.ps1
```

`build.ps1` does:
1. Locates the Python 3.10.5 interpreter that owns Nuitka
2. Reads `VERSION` from `src/config.py` for exe metadata
3. Cleans previous `dist/`
4. Runs Nuitka `--onefile --windows-console-mode=force --icon monarch.ico` (icon optional)
5. Copies `dist\MONARCH.exe` to `NinjaTrader 8\MONARCH\MONARCH.exe`

`dist/` is in `.gitignore` – never commit it.

**First build only:** Nuitka downloads MinGW-w64 (~120 MB, one-time) if Visual
Studio Build Tools are not already installed. Subsequent builds skip this step.
Build time: ~2–5 min on first compile, ~30–90 sec on rebuilds.

`make_version_file.py` is **no longer used** – `build.ps1` extracts the version
from `config.py` and passes it directly to Nuitka as `--file-version` flags.

### Known build pitfalls

- **C compiler required**: Nuitka compiles Python to C and then to native code.
  On Windows it needs either MSVC (Visual Studio Build Tools) or MinGW-w64.
  With `--assume-yes-for-downloads`, Nuitka auto-downloads MinGW-w64 if MSVC
  is absent. If the download fails, install Build Tools manually:
  `winget install Microsoft.VisualStudio.2022.BuildTools`

- **Nuitka not found under the right Python**: If `pip show nuitka` resolves to
  the wrong interpreter, install it explicitly:
  ```powershell
  & "C:\Users\dcjon\.pyenv\pyenv-win\versions\3.10.5\python.exe" -m pip install nuitka
  ```

- **`$ErrorActionPreference = 'Stop'` and pip**: Native commands writing to stderr
  throw terminating errors under Stop mode. The `pip show` calls in `build.ps1`
  use `2>&1` to merge stderr into stdout – keep this pattern if you modify the script.

- **Quoted exe paths in PowerShell**: Use the call operator `&`:
  ```powershell
  & "C:\path\to\python.exe" -m nuitka ...   # correct
  "C:\path\to\python.exe" -m nuitka ...     # wrong – tries to run a string
  ```

---

## Module Map

```
src/
  monarch.py       Entry point / orchestrator (CLI parsing, run loop)
  config.py        NT8 path detection, account constants, MONARCH dir init
  date_utils.py    Trading-day math, week helpers, missing-report detection
  log_sync.py      Find GodZilla CSVs in NT8 tree, copy to MONARCH/logs/
  log_parser.py    Parse CSVs → trade dicts, compute all statistics
  templates.py     Shared dark-theme CSS + HTML component helpers
  daily_report.py  Build one day's HTML report
  weekly_report.py Build Mon–Fri weekly summary HTML
  hub.py           Build CastleBravo.html hub page
```

---

## Accounts

```python
LIVE_ACCOUNTS = {'APEX750470000084', 'APEX750470000085'}
ACCT_LABEL    = {'APEX750470000084': '084', 'APEX750470000085': '085'}
ACCT_GRADE    = {'APEX750470000084': 'G4',  'APEX750470000085': 'G3'}
```

Sim accounts (names starting with `'Sim'`) are parsed from CSVs but flagged
`is_live=False` and excluded from all reports and statistics.

---

## GodZilla CSV Format

Columns written by NinjaTrader 8:

```
OpenTime, Account, Instrument, OpenPrice, Qty, CloseTime,
Trigger, Direction, AtmStrategyName, RealizedPnL,
SignalCombo, UsedSignals, TradeResult, LastTradeLine
```

- Files are named `GodZilla_*.csv`. `GodZuki_*.csv` files are excluded (strategy
  state files, not trade logs).
- `OpenTime` / `CloseTime` format: `%Y-%m-%d %H:%M:%S`
- Deduplication key: `(Account, OpenTime)` – the same trade can appear in multiple
  CSV files if the strategy was restarted.
- `is_fast` flag: trades closed in ≤ 10 seconds are marked fast (ATM take-profit
  hit immediately). These appear with a ⚡ marker in the trade log table.

---

## Trading Day Boundary

**6:00 PM ET = start of a new session.**
Session date = the calendar day on which the session **ends**.

```
Trade open 22:10 on Tuesday   →  session date Wednesday
Trade open 09:45 on Wednesday →  session date Wednesday
```

Weekend guardrail in `get_report_date()`:
- Saturday → preceding Friday
- Sunday   → preceding Friday

`MONARCH.exe` can be scheduled daily (Mon–Sun) without creating blank weekend reports.

---

## Output Folder Structure

```
NinjaTrader 8\
  MONARCH\
    CastleBravo.html          Hub / index page
    config.json               Per-installation settings (optional)
    logs\                     GodZilla CSVs copied here by log_sync
    reports\
      daily_YYYYMMDD.html     One per trading day
      weekly_YYYYMMDD.html    One per Friday (Mon–Fri summary)
      index.json              Internal metadata / cumulative stats
```

- Hub (`CastleBravo.html`) lives one level **above** `reports/`.
- Daily/weekly reports link back to hub via the constant `HUB_REL = '../CastleBravo.html'`
  defined at the top of `daily_report.py` and `weekly_report.py`.
- Hub links to reports via relative paths `reports/daily_YYYYMMDD.html`.

---

## Report Contents

**Daily report** (`daily_YYYYMMDD.html`) sections:
- KPI grid: Net P&L, Win Rate, Profit Factor, Avg Win, Avg Loss, R:R, Longs, Shorts
- Account breakdown (one card per account – all accounts, including Sim)
- Trade log table (time, date, account, direction, duration, grade, KO flag, signals, result, P&L)
- Breakdowns: Direction / Signal Grade / KO Signal (bar charts)
- Signal Combo Analysis (top 10 combos by frequency)
- Cumulative performance (all-time KPIs from index)

**Weekly report** (`weekly_YYYYMMDD.html`) sections:
- Same KPI grid, but aggregated Mon–Fri
- Per-day summary table
- Account breakdown (one card per account)
- Biggest Wins / Biggest Losses tables
- Grade and Direction breakdowns across the week

**Hub** (`CastleBravo.html`) sections:
- All-time cumulative KPI grid (overall + one card per account, dynamic)
- Current Recommendations (from `index['recommendations']` – see below)
- 4-week rolling calendar (clickable cells → daily reports; week totals → weekly reports)
- Recent Sessions table (last 10 days with trade data)

---

## Recommendations System

`index['recommendations']` is a list of dicts written into `reports/index.json`.
Currently populated manually (no auto-generation logic). Structure:

```python
{
    "type":  "critical" | "warn" | "positive" | "info",
    "title": "Short headline",
    "body":  "Longer explanation shown in the hub card."
}
```

Visual rendering in the hub:
- `critical` → red left border
- `warn`     → yellow left border
- `positive` → green left border
- `info`     → blue left border

To add a recommendation, edit `reports/index.json` directly or add generation
logic to `hub.py` / `daily_report.py` that appends to `index['recommendations']`
before `save_index()` is called.

---

## CLI Reference

```
MONARCH.exe                        Standard daily run
MONARCH.exe --backfill             Generate ALL missing reports
MONARCH.exe --date 2026-05-22      Force-regenerate a specific date
MONARCH.exe --weekly               Force-regenerate this week's summary
MONARCH.exe --nt8-path "D:\NT8"   Override NT8 folder location
MONARCH.exe --dry-run              Preview actions, write nothing
MONARCH.exe -d / --daemon          Daemon mode: no pause at exit (Task Scheduler)
MONARCH.exe --version              Print version, author, website, and email
```

`--dry-run` skips log sync and all file writes but still parses trades and
prints what it would have generated. Safe to run at any time.

`-d` / `--daemon` is intended for Task Scheduler. Without it, manual runs pause
for 60 seconds at exit (with countdown) so the user can read the output.

---

## NT8 Folder Detection (`config.py`)

Search order in `find_nt8_folder()`:

1. `--nt8-path` CLI argument
2. `NT8_PATH` environment variable
3. Windows registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Personal`
   + `\NinjaTrader 8`  ← the only reliable source when OneDrive redirects Documents
4. `OneDrive*` subdirectories under `Path.home()`
5. `~/Documents/NinjaTrader 8` and `C:\Users\<user>\Documents\NinjaTrader 8`

Raises `FileNotFoundError` with a helpful message if none of the above succeed.

---

## Statistics (`log_parser.py`)

`compute_stats(trades)` returns:

| Key               | Description                                 |
|-------------------|---------------------------------------------|
| `pnl`             | Net P&L (sum of RealizedPnL)                |
| `trades`          | Total trade count                           |
| `wins` / `losses` | Count by TradeResult                        |
| `win_rate`        | wins / trades (0–1)                         |
| `profit_factor`   | gross_win / gross_loss                      |
| `avg_win`         | Average P&L of winning trades               |
| `avg_loss`        | Average absolute loss of losing trades      |
| `rr`              | avg_win / avg_loss (reward:risk ratio)      |
| `gross_win`       | Sum of all winning trade P&L                |
| `gross_loss`      | Absolute sum of all losing trade P&L        |
| `long_*` / `short_*` | Trades / wins / P&L split by direction  |

Additional breakdown functions:
- `grade_stats(trades)` – dict keyed by grade string (G3, G4, G5)
- `ko_stats(trades)` – tuple `(with_ko_stats, without_ko_stats)`
- `combo_stats(trades)` – list sorted by frequency, each item includes all stats keys plus `combo`
- `account_stats(trades)` – dict keyed by full APEX account string

---

## Icon Generation (`make_icon.py`)

Run once before building (the generated `monarch.ico` is committed to the repo,
so this only needs to be re-run if you want to redesign the icon).

```powershell
python make_icon.py    # auto-installs Pillow if missing
```

Design: dark indigo background, rounded rect with indigo border, large white "M"
with teal glow, small teal dot in lower-right corner. Sizes: 16, 32, 48, 256 px.
Colors match the CSS custom properties in `templates.py`.

`build.ps1` passes `--icon monarch.ico` to PyInstaller when the file exists;
silently skips it if absent.

---

## Exe Version Info (`make_version_file.py`)

Run once before building, or let `build.ps1` call it automatically.

```powershell
python make_version_file.py    # writes version_info.txt
```

Reads `VERSION`, `AUTHOR`, `EMAIL`, `WEBSITE` from `src/config.py` and writes
`version_info.txt` in PyInstaller's `VSVersionInfo` format. This file is passed
to PyInstaller via `--version-file` and embeds metadata into the exe's
**Properties → Details** tab in Windows Explorer.

`version_info.txt` is in `.gitignore` – it is regenerated on every build from
the constants in `config.py`. To bump the version, change `VERSION` in
`config.py` only.

---

## Running from Source

```powershell
cd C:\Dev\Kaiju\MONARCH
python src\monarch.py                  # standard run
python src\monarch.py --backfill       # fill all gaps
python src\monarch.py --dry-run        # preview only
```

Any Python version works for running from source. The entry point adds `src/`
to `sys.path` automatically and detects frozen vs. source execution via
`sys.frozen`.

---

## Windows Task Scheduler

Recommended: Mon–Fri at 5:00 PM. Use `--daemon` (`-d`) so the window doesn't
linger after the scheduled run completes.

```
Action    : Start a program
Program   : C:\Users\<user>\Documents\NinjaTrader 8\MONARCH\MONARCH.exe
Arguments : -d
```

Friday 5 PM automatically generates both the daily and the weekly summary.
Saturday/Sunday runs are safe (map to Friday, no blank reports created).

Manual runs (no `-d`) pause for 60 seconds after completion so the output
stays visible. Press Enter at any time to dismiss early.

---

## Git Workflow Note

The Cowork sandbox can stage files (`git add`) but cannot write to `.git/`
directly, so **commits and pushes must be run from a local terminal**:

```powershell
cd C:\Dev\Kaiju
Remove-Item ".git\index.lock" -Force -ErrorAction SilentlyContinue
git commit -m "your message"
git push
```

If `git add` was already run by the sandbox and you see an `index.lock` error,
the `Remove-Item` line above clears it.

---

## Version History

| Version | Notes |
|---------|-------|
| 1.0.3   | Castle Bravo 4-week calendar now anchors to the current week's Friday instead of the most recent past Friday — current week no longer disappears Mon–Thu. |
| 1.0.2   | Symbol Breakdown section on daily, weekly, and Castle Bravo — stats grouped by base ticker (MNQ, NQ, ES, etc.). Instrument label derived from log data; no contract-roll maintenance. |
| 1.0.1   | Any GodZilla_* file supported; account derived from filename; all accounts (APEX + Sim) included in reports; per-account KPI breakdown in daily, weekly, and hub; daemon mode (`-d`); 60s countdown pause on manual runs; `--version` shows author/web/email; exe Properties metadata via `make_version_file.py`. |
| 1.0.0   | Initial release – modular rewrite of generate_report.py. Standalone exe, auto log sync, backfill, Sat/Sun guardrail, CastleBravo hub, MONARCH branding, icon support. |
