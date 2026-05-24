"""
MONARCH Intelligence Report System — Weekly Report Generator
=============================================================
Builds MONARCH_Weekly for the Mon–Fri window ending on a given Friday.
File name: weekly_YYYYMMDD.html  (date = Friday of that week)
"""

from datetime import date, timedelta
from pathlib import Path
from typing import List

from config import ACCT_LABEL
from log_parser import (
    compute_stats, grade_stats, combo_stats, account_stats,
    group_by_session, register_weekly,
)
from date_utils import week_dates, fmt_month_day, fmt_weekday_month_day
from templates import (
    page_wrap, kpi_card, bar_stat_row, grade_tag,
    pnl_fmt, pnl_class, pf_fmt, rr_fmt, now_stamp,
)

HUB_REL = '../CastleBravo.html'


def build_weekly_html(friday: date, all_trades: List[dict],
                      index: dict, reports_dir: Path) -> str:
    """Generate the full HTML string for a weekly summary."""
    days      = week_dates(friday)
    monday    = days[0]
    by_day    = group_by_session(all_trades)

    # All live trades in this Mon–Fri window
    week_trades = []
    for d in days:
        week_trades.extend(t for t in by_day.get(d, []) if t['is_live'])

    stats   = compute_stats(week_trades)
    gstats  = grade_stats(week_trades)
    combos  = combo_stats(week_trades)

    date_range = f"{fmt_month_day(monday)} – {fmt_month_day(friday)}, {friday.year}"

    # ── Badge ──────────────────────────────────────────────────────────────────
    if not week_trades:
        badge = '<span class="badge badge-none">No Trades</span>'
    elif stats['pnl'] >= 0:
        badge = f'<span class="badge badge-pos">{pnl_fmt(stats["pnl"])} Week</span>'
    else:
        badge = f'<span class="badge badge-neg">{pnl_fmt(stats["pnl"])} Week</span>'

    # ── Header ─────────────────────────────────────────────────────────────────
    header = f"""<div class="header">
  <div>
    <h1><span class="monarch-badge">MONARCH</span>Weekly Summary {badge}</h1>
    <div class="meta">Week of {date_range} &nbsp;·&nbsp; MNQ 06-26</div>
  </div>
  <div class="nav"><a href="{HUB_REL}">← Castle Bravo</a></div>
</div>"""

    # ── KPI Grid ───────────────────────────────────────────────────────────────
    if week_trades:
        kpis = f"""<div class="kpi-grid">
  {kpi_card("Week P&L",      pnl_fmt(stats['pnl']),        pnl_class(stats['pnl']),   f"{stats['trades']} trades")}
  {kpi_card("Win Rate",      f"{stats['win_rate']*100:.1f}%", 'green' if stats['win_rate']>=.7 else 'yellow', f"{stats['wins']}W / {stats['losses']}L")}
  {kpi_card("Profit Factor", pf_fmt(stats['profit_factor']), 'green' if stats['profit_factor']>1.5 else ('yellow' if stats['profit_factor']>1 else 'red'), "Target: >1.5")}
  {kpi_card("Avg Win",       f"${stats['avg_win']:.2f}",    'green')}
  {kpi_card("Avg Loss",      f"${stats['avg_loss']:.2f}",   'red')}
  {kpi_card("R:R",           rr_fmt(stats['rr']),           'green' if stats['rr']>=1 else 'red', "Target: >1.0")}
  {kpi_card("Longs",  f"{stats['long_wins']}/{stats['long_trades']}",  pnl_class(stats['long_pnl']),  pnl_fmt(stats['long_pnl']))}
  {kpi_card("Shorts", f"{stats['short_wins']}/{stats['short_trades']}", pnl_class(stats['short_pnl']), pnl_fmt(stats['short_pnl']))}
</div>"""
    else:
        kpis = ('<div class="callout callout-yellow">'
                '<h3>No live trades this week</h3>'
                '<p>NT8 logs found no APEX account activity for this period.</p>'
                '</div>')

    # ── Session Breakdown table ────────────────────────────────────────────────
    day_rows = ''
    for d in days:
        day_trades = [t for t in by_day.get(d, []) if t['is_live']]
        s     = compute_stats(day_trades)
        dname = fmt_weekday_month_day(d)
        fname = f"daily_{d.strftime('%Y%m%d')}.html"
        has_report = (reports_dir / fname).exists()

        if not day_trades:
            day_rows += (f'<tr class="flat-row">'
                         f'<td>{"<a href=" + repr(fname) + ">" if has_report else ""}{dname}{"</a>" if has_report else ""}</td>'
                         f'<td class="muted">—</td><td class="muted">—</td>'
                         f'<td class="muted">—</td><td class="muted">—</td>'
                         f'<td class="muted">No trades</td></tr>')
        else:
            pnl_cls = pnl_class(s['pnl'])
            accts   = ', '.join(sorted({ACCT_LABEL.get(t['account'], '?') for t in day_trades}))
            link_o  = f'<a href="{fname}" style="color:var(--accent)">' if has_report else ''
            link_c  = '</a>' if has_report else ''
            row_cls = 'win-row' if s['pnl'] >= 0 else 'loss-row'
            day_rows += (f'<tr class="{row_cls}">'
                         f'<td>{link_o}{dname}{link_c}</td>'
                         f'<td>{s["trades"]}</td>'
                         f'<td>{s["wins"]}/{s["losses"]}</td>'
                         f'<td class="{pnl_cls}">{pnl_fmt(s["pnl"])}</td>'
                         f'<td class="{pnl_cls}">{s["win_rate"]*100:.0f}%</td>'
                         f'<td class="muted">{accts}</td></tr>')

    day_table = f"""<div class="section"><h2>Session Breakdown</h2>
<table><thead><tr>
  <th>Day</th><th>Trades</th><th>W/L</th><th>P&L</th><th>Win %</th><th>Accounts</th>
</tr></thead><tbody>{day_rows}</tbody></table></div>"""

    # ── Top Wins / Biggest Losses ──────────────────────────────────────────────
    winners_section = ''
    if week_trades:
        sorted_trades = sorted(week_trades, key=lambda t: t['pnl'], reverse=True)

        def mini_rows(ts, limit=3):
            rows = ''
            for t in ts[:limit]:
                d_str   = fmt_weekday_month_day(t['open_dt'].date()) + ' ' + t['open_dt'].strftime('%H:%M')
                pnl_cls = pnl_class(t['pnl'])
                dir_cls = 'long' if t['direction'] == 'Long' else 'short'
                acct_s  = ACCT_LABEL.get(t['account'], '?')
                rows += (f'<tr>'
                         f'<td style="font-size:11px">{d_str}</td>'
                         f'<td>{acct_s}</td>'
                         f'<td><span class="tag tag-{dir_cls}">{t["direction"]}</span></td>'
                         f'<td>{grade_tag(t["grade"])}</td>'
                         f'<td class="{pnl_cls}">{pnl_fmt(t["pnl"])}</td>'
                         f'</tr>')
            return rows or '<tr><td colspan="5" class="muted">None</td></tr>'

        wins_rows   = mini_rows(sorted_trades)
        losses_rows = mini_rows(list(reversed(sorted_trades)))
        col_heads   = '<th>Time</th><th>Acct</th><th>Dir</th><th>Grade</th><th>P&L</th>'
        winners_section = f"""<div class="section two-col">
<div><h2>Biggest Wins</h2>
<table><thead><tr>{col_heads}</tr></thead><tbody>{wins_rows}</tbody></table></div>
<div><h2>Biggest Losses</h2>
<table><thead><tr>{col_heads}</tr></thead><tbody>{losses_rows}</tbody></table></div>
</div>"""

    # ── Grade + Direction Breakdowns ───────────────────────────────────────────
    breakdowns = ''
    if week_trades:
        grade_rows = ''.join(
            bar_stat_row(grade_tag(g), s['wins'], s['trades'], s['pnl'])
            for g, s in gstats.items()
        )
        breakdowns = f"""<div class="section"><h2>Breakdowns</h2><div class="two-col">
<div class="card"><h3>Signal Grade</h3>{grade_rows}</div>
<div class="card"><h3>Direction</h3>
  {bar_stat_row('Long',  stats['long_wins'],  stats['long_trades'],  stats['long_pnl'])}
  {bar_stat_row('Short', stats['short_wins'], stats['short_trades'], stats['short_pnl'])}
</div></div></div>"""

    # ── Footer ─────────────────────────────────────────────────────────────────
    footer = (f'<div class="footer">Generated {now_stamp()} · MONARCH Intelligence Report System · '
              f'<a href="{HUB_REL}" style="color:var(--accent)">← Castle Bravo</a></div>')

    body = '\n'.join([header, kpis, day_table, winners_section, breakdowns, footer])
    return page_wrap(f"MONARCH Weekly — {friday.strftime('%Y-%m-%d')}", body)


def generate_weekly(friday: date, all_trades: List[dict],
                    index: dict, reports_dir: Path) -> Path:
    """Build and write the weekly report. Returns the output path."""
    from log_parser import group_by_session
    days        = week_dates(friday)
    by_day      = group_by_session(all_trades)
    week_trades = []
    for d in days:
        week_trades.extend(t for t in by_day.get(d, []) if t['is_live'])

    print(f"  Weekly {friday} ({len(week_trades)} live trades)")

    html  = build_weekly_html(friday, all_trades, index, reports_dir)
    fname = f"weekly_{friday.strftime('%Y%m%d')}.html"
    out   = reports_dir / fname
    with open(out, 'w', encoding='utf-8') as f:
        f.write(html)

    register_weekly(index, friday, week_trades)
    print(f"  → {out}")
    return out
