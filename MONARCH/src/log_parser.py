"""
MONARCH Intelligence Report System — Log Parser
================================================
Parses GodZilla_*.csv trade files into normalised trade dicts and
computes all statistics used by the report generators.

CSV columns (as written by NinjaTrader):
  OpenTime, Account, Instrument, OpenPrice, Qty, CloseTime,
  Trigger, Direction, AtmStrategyName, RealizedPnL,
  SignalCombo, UsedSignals, TradeResult, LastTradeLine
"""

import csv
import re
from datetime import datetime, timedelta
from pathlib import Path
from collections import defaultdict
from typing import List, Dict, Tuple

from config import LIVE_ACCOUNTS, ACCT_LABEL, ACCT_GRADE
from date_utils import trading_day_for


# ── Individual field helpers ──────────────────────────────────────────────────

def parse_grade(trigger: str, used_signals: str) -> str:
    """Extract G3 / G4 / G5 from Trigger or UsedSignals."""
    for field in (trigger, used_signals):
        m = re.search(r'G(\d)', field)
        if m:
            return f"G{m.group(1)}"
    return 'G?'


def has_ko(used_signals: str) -> bool:
    return 'KO' in used_signals.upper()


def parse_duration(open_dt: datetime, close_dt: datetime) -> str:
    secs = int((close_dt - open_dt).total_seconds())
    if secs < 60:
        return f"{secs}s"
    if secs < 3600:
        m, s = divmod(secs, 60)
        return f"{m}m {s}s"
    h, rem = divmod(secs, 3600)
    return f"{h}h {rem // 60}m"


def is_fast_trade(open_dt: datetime, close_dt: datetime) -> bool:
    """True for trades closed in ≤10 seconds (ATM TP hit immediately)."""
    return (close_dt - open_dt).total_seconds() <= 10


def clean_combo(combo: str) -> str:
    """Strip SET1-G?- prefix from signal combo."""
    return re.sub(r'^SET1-G\d-?', '', combo)


# ── File scanning & parsing ───────────────────────────────────────────────────

def parse_all_trades(logs_dir: Path) -> List[dict]:
    """
    Parse every GodZilla_*.csv in logs_dir.
    Returns a list of trade dicts sorted by open_dt ascending.
    Sim accounts (Sim101, Sim102, SimKhahn, etc.) are included in the list
    but flagged with is_live=False so callers can filter them out.
    """
    trades: List[dict] = []
    seen_keys: set = set()  # deduplicate across files with same name

    for fpath in sorted(logs_dir.glob('GodZilla_*.csv')):
        _parse_file(fpath, trades, seen_keys)

    trades.sort(key=lambda t: t['open_dt'])
    return trades


def _parse_file(fpath: Path, trades: List[dict], seen_keys: set):
    try:
        with open(fpath, encoding='utf-8-sig') as f:
            reader = csv.DictReader(f)
            for row in reader:
                if not row.get('OpenTime') or not row.get('Account'):
                    continue
                try:
                    open_dt  = datetime.strptime(row['OpenTime'].strip(),  '%Y-%m-%d %H:%M:%S')
                    close_dt = datetime.strptime(row['CloseTime'].strip(), '%Y-%m-%d %H:%M:%S')
                except ValueError:
                    continue

                acct    = row['Account'].strip().strip('"')
                trigger = row.get('Trigger', '').strip()
                used    = row.get('UsedSignals', '').strip()
                combo   = row.get('SignalCombo', '').strip()

                # Dedup: same account + open time = same trade
                key = (acct, open_dt)
                if key in seen_keys:
                    continue
                seen_keys.add(key)

                try:
                    pnl = float(row['RealizedPnL'].strip())
                except (ValueError, KeyError):
                    pnl = 0.0

                trades.append({
                    'open_dt':    open_dt,
                    'close_dt':   close_dt,
                    'session':    trading_day_for(open_dt),
                    'account':    acct,
                    'instrument': row.get('Instrument', '').strip(),
                    'open_price': _safe_float(row.get('OpenPrice')),
                    'qty':        _safe_int(row.get('Qty')),
                    'direction':  row.get('Direction', '').strip(),
                    'atm':        row.get('AtmStrategyName', '').strip(),
                    'pnl':        pnl,
                    'result':     row.get('TradeResult', '').strip().upper(),
                    'trigger':    trigger,
                    'used':       used,
                    'combo':      combo,
                    'combo_clean': clean_combo(combo),
                    'grade':      parse_grade(trigger, used),
                    'has_ko':     has_ko(used),
                    'duration':   parse_duration(open_dt, close_dt),
                    'is_fast':    is_fast_trade(open_dt, close_dt),
                    'is_live':    acct in LIVE_ACCOUNTS,
                    'source':     fpath.name,
                })
    except Exception as e:
        print(f"  [warn] Could not parse {fpath.name}: {e}")


def _safe_float(v) -> float:
    try:
        return float(v) if v else 0.0
    except (ValueError, TypeError):
        return 0.0


def _safe_int(v) -> int:
    try:
        return int(v) if v else 0
    except (ValueError, TypeError):
        return 0


# ── Grouping ──────────────────────────────────────────────────────────────────

def group_by_session(trades: List[dict]) -> Dict:
    groups = defaultdict(list)
    for t in trades:
        groups[t['session']].append(t)
    return dict(groups)


# ── Statistics ────────────────────────────────────────────────────────────────

_EMPTY_STATS = {
    'trades': 0, 'pnl': 0.0,
    'wins': 0, 'losses': 0, 'win_rate': 0.0,
    'profit_factor': 0.0, 'avg_win': 0.0, 'avg_loss': 0.0, 'rr': 0.0,
    'long_trades': 0, 'long_wins': 0, 'long_pnl': 0.0,
    'short_trades': 0, 'short_wins': 0, 'short_pnl': 0.0,
    'gross_win': 0.0, 'gross_loss': 0.0,
}


def compute_stats(trades: List[dict]) -> dict:
    if not trades:
        return dict(_EMPTY_STATS)

    wins   = [t for t in trades if t['result'] == 'WIN']
    losses = [t for t in trades if t['result'] == 'LOSS']
    longs  = [t for t in trades if t['direction'] == 'Long']
    shorts = [t for t in trades if t['direction'] == 'Short']

    pnl        = sum(t['pnl'] for t in trades)
    gross_win  = sum(t['pnl'] for t in wins)
    gross_loss = abs(sum(t['pnl'] for t in losses))
    avg_win    = gross_win  / len(wins)   if wins   else 0.0
    avg_loss   = gross_loss / len(losses) if losses else 0.0

    if gross_loss > 0:
        pf = gross_win / gross_loss
        rr = avg_win   / avg_loss
    elif gross_win > 0:
        pf = float('inf')
        rr = float('inf')
    else:
        pf = rr = 0.0

    return {
        'trades':        len(trades),
        'pnl':           pnl,
        'wins':          len(wins),
        'losses':        len(losses),
        'win_rate':      len(wins) / len(trades),
        'profit_factor': pf,
        'avg_win':       avg_win,
        'avg_loss':      avg_loss,
        'rr':            rr,
        'gross_win':     gross_win,
        'gross_loss':    gross_loss,
        'long_trades':   len(longs),
        'long_wins':     sum(1 for t in longs  if t['result'] == 'WIN'),
        'long_pnl':      sum(t['pnl'] for t in longs),
        'short_trades':  len(shorts),
        'short_wins':    sum(1 for t in shorts if t['result'] == 'WIN'),
        'short_pnl':     sum(t['pnl'] for t in shorts),
    }


def grade_stats(trades: List[dict]) -> Dict[str, dict]:
    by_grade = defaultdict(list)
    for t in trades:
        by_grade[t['grade']].append(t)
    return {g: compute_stats(ts) for g, ts in sorted(by_grade.items())}


def ko_stats(trades: List[dict]) -> Tuple[dict, dict]:
    with_ko    = [t for t in trades if t['has_ko']]
    without_ko = [t for t in trades if not t['has_ko']]
    return compute_stats(with_ko), compute_stats(without_ko)


def combo_stats(trades: List[dict]) -> List[dict]:
    by_combo = defaultdict(list)
    for t in trades:
        by_combo[t['combo_clean']].append(t)
    result = []
    for combo, ts in sorted(by_combo.items(), key=lambda x: -len(x[1])):
        s = compute_stats(ts)
        result.append({'combo': combo, **s})
    return result


def account_stats(trades: List[dict]) -> Dict[str, dict]:
    by_acct = defaultdict(list)
    for t in trades:
        by_acct[t['account']].append(t)
    return {a: compute_stats(ts) for a, ts in by_acct.items()}


# ── Index management ──────────────────────────────────────────────────────────

import json
from datetime import date


def load_index(reports_dir: Path) -> dict:
    index_file = reports_dir / 'index.json'
    if index_file.exists():
        try:
            with open(index_file, encoding='utf-8') as f:
                return json.load(f)
        except Exception:
            pass
    return {
        'reports':     {'daily': {}, 'weekly': {}},
        'cumulative':  {},
        'recommendations': [],
    }


def save_index(index: dict, reports_dir: Path):
    index_file = reports_dir / 'index.json'
    with open(index_file, 'w', encoding='utf-8') as f:
        json.dump(index, f, indent=2, default=str)


def update_cumulative(index: dict, all_trades: List[dict]):
    """Recompute all-time cumulative stats from live trades and write into index."""
    live    = [t for t in all_trades if t['is_live']]
    stats   = compute_stats(live)
    by_acct = account_stats(live)
    s084    = by_acct.get('APEX750470000084', compute_stats([]))
    s085    = by_acct.get('APEX750470000085', compute_stats([]))

    index['cumulative'] = {
        'pnl':            stats['pnl'],
        'trades':         stats['trades'],
        'wins':           stats['wins'],
        'losses':         stats['losses'],
        'win_rate':       stats['win_rate'],
        'profit_factor':  stats['profit_factor'],
        'avg_win':        stats['avg_win'],
        'avg_loss':       stats['avg_loss'],
        'rr':             stats['rr'],
        'pnl_084':        s084['pnl'],
        'trades_084':     s084['trades'],
        'pnl_085':        s085['pnl'],
        'trades_085':     s085['trades'],
        'last_updated':   date.today().isoformat(),
    }


def register_daily(index: dict, session_date: date, day_trades: List[dict]):
    live  = [t for t in day_trades if t['is_live']]
    stats = compute_stats(live)
    index['reports']['daily'][session_date.isoformat()] = {
        'file':     f"daily_{session_date.strftime('%Y%m%d')}.html",
        'pnl':      stats['pnl'],
        'trades':   stats['trades'],
        'wins':     stats['wins'],
        'losses':   stats['losses'],
        'accounts': sorted({ACCT_LABEL.get(t['account'], t['account']) for t in live}),
    }


def register_weekly(index: dict, friday: date, week_trades: List[dict]):
    stats = compute_stats(week_trades)
    index['reports']['weekly'][friday.isoformat()] = {
        'file':       f"weekly_{friday.strftime('%Y%m%d')}.html",
        'pnl':        stats['pnl'],
        'trades':     stats['trades'],
        'wins':       stats['wins'],
        'losses':     stats['losses'],
        'week_start': (friday - __import__('datetime').timedelta(days=4)).isoformat(),
        'week_end':   friday.isoformat(),
    }
