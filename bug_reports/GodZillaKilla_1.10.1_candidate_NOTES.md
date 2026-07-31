# GodZillaKilla 1.10.1 candidate — naked-watchdog + ATM registration fixes

**File:** `GodZillaKilla_1.10.1_candidate.cs` — paste into NT8 and compile. Not yet applied
to the repo copy of `GodZillaKilla.cs`; do that after this compiles and runs a clean session.

Source: the four fixes from the user bug report of 2026-07-29
(`GodZillaKilla_fix_package/`), ported onto 1.10.0, plus four additions below.

**Diff vs 1.10.0:** 167 lines added, 4 removed, 15 regions. Line endings and final-byte
(no trailing newline) match the repo file, so the diff is reviewable.

---

## What came from the user's patch (unchanged in substance)

All 8 of his hunks applied to 1.10.0 with zero fuzz — none of the 8 methods he touched
changed between 1.9.4 and 1.10.0.

| # | Method | Change |
|---|---|---|
| 1 | `HasWorkingProtectiveOrders` | Accepts `ChangePending`, `ChangeSubmitted`, `CancelPending`, `PartFilled` as live protection. An OCO quantity adjustment no longer reads as naked. |
| 2 | `CheckForNakedPositions` | Grace period after fill/order activity — brackets are not submitted atomically with the entry (351–644 ms measured). |
| 3 | `CheckForNakedPositions` | Double-confirm: first sighting logs `SUSPECTED` and pulls the next check forward; only the second consecutive sighting flattens (`CONFIRMED`). |
| 4 | `IsAtmIdQueryable` (new), `GetAtmStrategyMarketPositionTickCached`, `TryGetAtmMarketPositionSafe` | Do not query a brand-new ATM id before `AtmStrategyCreate` registers it. NT8 logs instead of throwing for a missing id, so `try/catch` never caught this — 6–12 log errors per entry. |

His base file was verified byte-identical to committed 1.9.4 (`e9b0240`), and his
original→patched diff is exactly the 111/2 lines he claimed, with no hidden edits.

## What was added on top

**1. Three constants became properties** (his own suggestion — all his latency data came
from the NT8 internal simulator, and a live broker's bracket tail is unmeasured):

| Property | Group | Default | Range |
|---|---|---|---|
| Naked Watchdog Grace (sec) | Risk Management | 5 | 1–120 |
| Naked Watchdog Re-Check (sec) | Risk Management | 5 | 1–120 |
| ATM Registration Quiet (sec) | ATM Parameters | 2.0 | 0.1–30.0 |

Additive to saved chart templates — existing charts pick up the defaults. The ATM one is
hidden in FixedTicks mode via `ModifyOrderManagementProperties`. All three are used only
in runtime expressions (never in an attribute or `case` label), so the `const` → property
conversion is safe.

**2. Martingale hole closed in `IsAtmIdQueryable`.** `HandleAtmExecution` and the poll-open
path both set `isAtmStrategyCreated` — never `martingaleAtmStrategyCreated` — even when the
open trade *is* the martingale leg. So a martingale fill processed before its create
callback would report "not queryable" for up to the quiet window while the position is
open, and two callers read `Flat` as a state change: the close-detection poll
(`ProcessMartingaleAtmTradeClose` on a live position) and `IsAtmMidTradeStale`
(Defense #8 → `FlattenEverything`). Fixed by returning `true` for
`_openAtmTrade.AtmId` up front — a confirmed open trade proves NT8 registered the id.
The normal leg was already safe (`_openAtmTrade != null` implies `isAtmStrategyCreated`).

**3. Manual buttons stamp the grace period.** 1.10.0's SL/TP buttons call
`AtmStrategyChangeStopTarget` on *every* stop in a loop, so all protective stops enter
`ChangePending`/`ChangeSubmitted` at once — the exact window that read as naked before this
fix. `ProcessManualTradeCommands` now stamps `_lastFillActivityUtc` before dispatching, so
a click can never cost a session even if the widened state list misses something.

**4. Version → 1.10.1.**

---

## Test checklist

1. **Compile.** If NT8 rejects an `OrderState` member name, it will be one of the four in
   `HasWorkingProtectiveOrders` — delete that single line and recompile; each is independent.
2. **Log noise.** Take an entry, check the NT8 log for
   `'GetAtmStrategyMarketPosition' method error: ATM strategy ID '...' does not exist`.
   Expect zero (was 6–12 per entry).
3. **Manual buttons under load.** With a position open, spam **SL ▲** and **TP ▼**, then
   let a 30s watchdog tick land. Expect no `SUSPECTED`, no flatten, no disable.
4. **Watchdog still works.** ✅ **PASSED** — Playback101, 2026-07-13 02:05:20/26. A genuinely
   naked Long 4 produced `NAKED POSITION SUSPECTED` → 6s → `NAKED POSITION CONFIRMED` →
   flatten → strategy disabled (expected NT8 behavior on account flatten). The
   double-confirm hardened the watchdog without switching it off.
5. **Playback.** ✅ **Answered** — the `AtmStrategyCreate` callback *does* fire in Playback
   (`DIAG:ATM_CALLBACK ... NoError`, with `isAtmCreated=True` at `age=0.1s` on the next
   poll). So the quiet window is ended by the callback and never gates anything there; the
   feared fast-forward entry-detection delay does not occur. No need to lower
   **ATM Registration Quiet (sec)**.
6. **Martingale.** Take a losing trade that triggers recovery; confirm one `TRADE OPEN` and
   one `TRADE CLOSE` for the martingale leg, no phantom close, no Defense #8 line.

## Found during testing — pre-existing, NOT introduced by this change

**Defense #8 false-fires and its recovery cannot close an ATM position.** Observed
Playback101, 2026-07-13 02:05:01–02:05:26:

1. `02:05:01` clean entry — ATM created, callback `NoError`, `POLL OPEN Long 4@29660.00`.
2. `02:05:06` `MID-TRADE ATM STALENESS DETECTED` — `GetAtmStrategyMarketPosition` returned
   Flat ~5s after a confirmed-open position while the account still held Long 4.
   `IsAtmMidTradeStale` acts on a **single** observation: `EvictStaleAtmIdsIfTimedOut` has
   no age guard and no re-check on that branch — the same design weakness the bug report
   called out as Bug #3 for the naked watchdog.
3. Defense #8 called `FlattenEverything`, which in ATM mode does: `AtmStrategyClose` (a
   no-op — the premise is that NT8 lost the id), then `ExitLong`/`ExitShort` gated on
   `Position.MarketPosition`, which is **Flat in ATM mode** (log: `strategyPos=Flat qty=0`),
   then `Account.Cancel` on every working order. Net effect: **brackets cancelled, position
   untouched** — it manufactured the naked position. Defense #8's own comment ("ExitLong/Short
   + Account.Cancel ... both work independently of the dead ATM ID") is wrong for ATM mode.
4. `02:05:20/26` the naked watchdog caught it and flattened for real. The watchdog was the
   backstop that saved the account.

Why this is not from the port: `isAtmCreated=True` at the time, so `IsAtmIdQueryable`
returned true and the query went through to NT8 — NT8 itself returned Flat, the guard did
not manufacture it. Neither the user's patch nor the additions touch
`EvictStaleAtmIdsIfTimedOut` or `FlattenEverything`. On 1.10.0 the outcome would have been
identical, ~5s sooner, logged as `NAKED POSITION DETECTED`.

One honest exposure change: after Defense #8 strips the brackets, the naked position now
persists up to (30s poll + re-check) rather than up to 30s. Measured here: 14s + 6s.

**`HUD snapshot error: Index was outside the bounds of the array.`** — caught, capped at 3
prints, gated on `EnableDebug`, non-fatal. Fired during teardown after the disable. Separate
low-priority issue.

## Known gaps left alone (deliberately)

- **`Account.Flatten()` still disables the strategy** on a watchdog trip. Deferred as its
  own change: it is the only thing that reliably closes an ATM-managed position when the
  ATM id is dead, which is exactly the naked scenario. `AtmStrategyClose` is a no-op on a
  dead id and `ExitLong`/`ExitShort` only work on strategy-managed positions, so replacing
  it needs an explicit `Account.CreateOrder`/`Submit` market order plus `Account.Cancel`,
  with double-close risk if the ATM revives.
- **`CollectAtmBracketOrders`** still accepts only `Working`/`Accepted`, so a second nudge
  during an in-flight change is dropped with `no live ATM bracket orders found`. Widening
  it is not obviously correct — NT8 will likely reject a change on an order already in
  `ChangePending`.
- **`CancelSubmitted`** (and `TriggerPending`, if the template uses simulated stops) are not
  in the accepted-state list. Both are candidates if a false `SUSPECTED` shows up during an
  OCO cancel — each is a one-line addition, and both make the watchdog *more* permissive,
  so they are a risk call, not a cleanup.
- **Worst-case exposure** on a genuinely naked position is now grace + re-check ≈ 10s at the
  defaults, up from ~0s. The watchdog only polls every 30s regardless.
- **Docs not yet updated** — `documents/GodZillaKilla.md` (Version History row, Defense
  Mechanisms) and `CLAUDE.md` (the "NT8 logs instead of throwing for a missing ATM id, so
  `try/catch` is dead code there" gotcha) are pending until this is confirmed working.

## MONARCH impact: none

`MONARCH/src/log_parser.py` reads only `GodZilla_*.csv`, never the Output window, so the
`NAKED POSITION DETECTED` → `SUSPECTED`/`CONFIRMED` rename is invisible to it. CSV columns
and `WriteTradeLogRecord` are untouched.

## Note if you ever apply his `.patch` directly

GNU `patch` rewrote the whole file as LF (his patch file is LF, the source is CRLF). The
candidate has been converted back to CRLF to match. His own `GodZillaKilla.cs` is CRLF, so
he applied his edits in an editor rather than with `patch`.
