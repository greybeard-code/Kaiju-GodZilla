"""
MONARCH Intelligence Report System
====================================
Standalone Windows executable entry point.

Compiled with PyInstaller (see build.bat) into a single MONARCH.exe
that requires no Python installation on the target machine.

Usage:
  MONARCH.exe                        Run: sync logs, fill gaps, update hub
  MONARCH.exe --date 2026-05-22      Force-regenerate a specific daily date
  MONARCH.exe --weekly               Force-regenerate this week's summary
  MONARCH.exe --backfill             Generate ALL missing reports (no limit)
  MONARCH.exe --nt8-path "D:\\NT8"   Override NT8 folder location
  MONARCH.exe --dry-run              Show what would be generated, don't write

Weekend guardrail:
  Saturday and Sunday both map to the preceding Friday.
  No blank weekend reports are ever created.
"""

import sys
import argparse
from datetime import date, timedelta
from pathlib import Path

# ── ensure src/ is on the path when running from source ──────────────────────
if getattr(sys, 'frozen', False):
    # Running as compiled exe — PyInstaller sets sys._MEIPASS
    _src = Path(sys.executable).parent
else:
    _src = Path(__file__).parent

if str(_src) not in sys.path:
    sys.path.insert(0, str(_src))

from config import APP_NAME, VERSION, find_nt8_folder, init_monarch_dirs
from log_sync import sync_logs
from log_parser import (
    parse_all_trades, group_by_session,
    load_index, save_index, update_cumulative,
)
from date_utils import (
    get_report_date, is_friday, get_friday_of_week, week_dates,
    get_trading_days_with_data, get_missing_daily_dates,
    get_weeks_with_data, get_missing_weekly_dates,
)
from daily_report import generate_daily
from weekly_report import generate_weekly
from hub import generate_hub


# ── CLI ───────────────────────────────────────────────────────────────────────

def parse_args():
    p = argparse.ArgumentParser(
        prog='MONARCH',
        description=f'{APP_NAME} v{VERSION}',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""Examples:
  MONARCH.exe                      # standard daily run
  MONARCH.exe --weekly             # force this week's summary
  MONARCH.exe --date 2026-05-22    # regenerate a specific date
  MONARCH.exe --backfill           # fill all missing reports
  MONARCH.exe --nt8-path "D:\\NT8" # custom NT8 location
""")
    p.add_argument('--date',     metavar='YYYY-MM-DD',
                   help='Force-generate report for this date (daily + weekly if Friday)')
    p.add_argument('--weekly',   action='store_true',
                   help='Force-generate this week\'s summary report')
    p.add_argument('--backfill', action='store_true',
                   help='Generate all missing daily and weekly reports')
    p.add_argument('--nt8-path', metavar='PATH',
                   help='Path to NinjaTrader 8 Documents folder')
    p.add_argument('--dry-run',  action='store_true',
                   help='Show what would be generated without writing files')
    p.add_argument('--version',  action='version', version=f'%(prog)s {VERSION}')
    return p.parse_args()


# ── Main orchestrator ─────────────────────────────────────────────────────────

def main():
    args = parse_args()

    print(f"\n{'='*60}")
    print(f"  {APP_NAME}  v{VERSION}")
    print(f"{'='*60}\n")

    # ── 1. Locate NT8 folder ───────────────────────────────────────────────────
    try:
        nt8 = find_nt8_folder(getattr(args, 'nt8_path', None))
    except FileNotFoundError as e:
        print(f"[ERROR] {e}")
        sys.exit(1)
    print(f"  NT8 folder : {nt8}")

    # ── 2. Initialise MONARCH directory structure ──────────────────────────────
    monarch_dir, logs_dir, reports_dir = init_monarch_dirs(nt8)
    print(f"  MONARCH    : {monarch_dir}")

    # ── 3. Sync log files ──────────────────────────────────────────────────────
    print("\n[SYNC] Copying GodZilla log files...")
    if not args.dry_run:
        copied, skipped = sync_logs(nt8, logs_dir)
        print(f"  Copied: {copied}  |  Already current: {skipped}")
    else:
        print("  (dry-run — skipped)")

    # ── 4. Parse all trades ────────────────────────────────────────────────────
    print("\n[PARSE] Reading log files...")
    all_trades  = parse_all_trades(logs_dir)
    live_trades = [t for t in all_trades if t['is_live']]
    print(f"  Total: {len(all_trades)} trades  |  Live: {len(live_trades)}  |  Sim: {len(all_trades)-len(live_trades)}")

    if not all_trades:
        print("\n  No trade data found. Nothing to generate.")
        _pause_if_double_clicked()
        return

    # ── 5. Load index and update cumulative stats ──────────────────────────────
    index = load_index(reports_dir)
    update_cumulative(index, all_trades)

    # ── 6. Determine target date (apply Sat/Sun → Friday guardrail) ────────────
    if args.date:
        try:
            target_date = date.fromisoformat(args.date)
        except ValueError:
            print(f"[ERROR] Invalid date format '{args.date}'. Use YYYY-MM-DD.")
            sys.exit(1)
    else:
        target_date = get_report_date()   # applies weekend guardrail

    current_friday = get_friday_of_week(target_date)

    print(f"\n[DATE]  Report date : {target_date}  |  Week ending : {current_friday}")

    # ── 7. Decide which reports to generate ───────────────────────────────────
    trading_days  = get_trading_days_with_data(all_trades)
    all_weeks     = get_weeks_with_data(trading_days)

    daily_to_generate  = []
    weekly_to_generate = []

    if args.backfill:
        # All missing dailies
        daily_to_generate = get_missing_daily_dates(reports_dir, trading_days)
        # All missing weeklies
        weekly_to_generate = get_missing_weekly_dates(reports_dir, all_weeks)
        print(f"\n[BACKFILL] Missing daily reports  : {len(daily_to_generate)}")
        print(f"[BACKFILL] Missing weekly reports : {len(weekly_to_generate)}")

    # Always regenerate today's daily (catches intraday updates)
    if target_date not in daily_to_generate:
        daily_to_generate.append(target_date)

    # Weekly: generate if it's Friday, or forced, or backfill already covers it
    if args.weekly and current_friday not in weekly_to_generate:
        weekly_to_generate.append(current_friday)
    elif is_friday(target_date) and current_friday not in weekly_to_generate:
        weekly_to_generate.append(current_friday)

    # Sort chronologically
    daily_to_generate  = sorted(set(daily_to_generate))
    weekly_to_generate = sorted(set(weekly_to_generate))

    # ── 8. Generate daily reports ──────────────────────────────────────────────
    if daily_to_generate:
        print(f"\n[DAILY] Generating {len(daily_to_generate)} daily report(s)...")
        for d in daily_to_generate:
            if args.dry_run:
                has_data = any(t['session'] == d and t['is_live'] for t in all_trades)
                print(f"  (dry-run) Would generate daily_{d.strftime('%Y%m%d')}.html"
                      f"  — {'has data' if has_data else 'no trades'}")
            else:
                generate_daily(d, all_trades, index, reports_dir)

    # ── 9. Generate weekly reports ─────────────────────────────────────────────
    if weekly_to_generate:
        print(f"\n[WEEKLY] Generating {len(weekly_to_generate)} weekly report(s)...")
        for friday in weekly_to_generate:
            if args.dry_run:
                print(f"  (dry-run) Would generate weekly_{friday.strftime('%Y%m%d')}.html")
            else:
                generate_weekly(friday, all_trades, index, reports_dir)

    # ── 10. Regenerate hub ─────────────────────────────────────────────────────
    print("\n[HUB] Updating Castle Bravo...")
    if not args.dry_run:
        generate_hub(all_trades, index, monarch_dir, reports_dir)
        save_index(index, reports_dir)

    # ── Done ───────────────────────────────────────────────────────────────────
    print(f"\n{'='*60}")
    if args.dry_run:
        print("  Dry run complete — no files written.")
    else:
        hub_path = monarch_dir / 'CastleBravo.html'
        print(f"  Done. Open your hub:")
        print(f"  {hub_path}")
    print(f"{'='*60}\n")

    _pause_if_double_clicked()


def _pause_if_double_clicked():
    """
    When the exe is launched by double-clicking rather than from a terminal,
    the window would vanish immediately. Detect this and pause so the user
    can read the output.
    """
    import os
    # sys.stdin.isatty() is False when piped or no console attached
    # On Windows, if launched from Explorer the parent process is explorer.exe
    try:
        if sys.stdin and sys.stdin.isatty():
            return   # running in a real terminal — no need to pause
    except Exception:
        pass
    # Pause for double-click launches
    try:
        input("\nPress Enter to close...")
    except Exception:
        pass


if __name__ == '__main__':
    main()
