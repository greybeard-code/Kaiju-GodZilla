GodZillaKilla - Bug fixes, 29 July 2026
=======================================

Four defects were found and fixed after the strategy was silently disabled
mid-session by NinjaTrader on two separate occasions. Full analysis, log
evidence and reasoning are in the PDF.

FILES
-----
GodZillaKilla_BugReport_2026-07-29.pdf
    6-page technical report: root cause of each defect, log extracts with
    millisecond timestamps, the fix applied, and verification data.
    Read this first.

GodZillaKilla.cs
    The patched source file. Drop-in replacement.

GodZillaKilla_ORIGINAL_before_fixes.cs
    The file exactly as it was before any change, for reference/rollback.

GodZillaKilla_naked-watchdog-and-atm-registration.patch
    Unified diff between the two files above. This is the quickest way to
    review what changed: 111 lines added, 2 removed.
    Apply with:  patch GodZillaKilla.cs < <this file>


SUMMARY OF THE FOUR FIXES
-------------------------
#1  HasWorkingProtectiveOrders() rejected transitional order states
    (ChangePending / ChangeSubmitted / CancelPending / PartFilled), so a
    normal OCO quantity adjustment made a protected position look naked.

#2  CheckForNakedPositions() had no grace period after an entry fill. ATM
    brackets are not submitted atomically with the entry - the gap reaches
    644 ms - and the watchdog could fire inside that window.

#3  The watchdog took an irreversible action (flatten + strategy shutdown)
    on a single racy observation. It now requires a second confirmation
    5 seconds later.

#4  ATM position was polled before AtmStrategyCreate had registered the ID,
    producing 6-12 NT8 errors per entry. Note: NT8 does not throw for this,
    it logs and returns Flat, so try/catch never caught it.

The reason #1 and #2 were session-ending rather than cosmetic: the watchdog
reacts by calling Account.Flatten(), and on NT8 that also disables every
NinjaScript strategy running on the account.


WHAT WAS NOT TOUCHED
--------------------
No signal or trading logic. Every method in the file was hashed against the
original: 221 methods analysed, 8 differ, and all 8 are listed in the report.
All 28 methods on the signal/entry/exit path are byte-identical, including
OnBarUpdate, ComputeBarSignalSnapshot, BuildSignalTriggerName, the trading
and flatten time windows, the daily limits, the martingale recovery,
AtmStrategyCreate and FlattenNakedAccountPosition itself.


VERIFICATION
------------
Recompiled 29 July 14:35. Verified over a 5-hour live session on the NT8
internal simulator: 130 filled entries, 0 unwanted flatten/disable events,
0 ATM registration errors, 0 NT8 errors or warnings. The exact condition
that caused the 06:50:54 incident recurred 111 times with no action taken.


CAVEAT
------
All timing measurements come from the NinjaTrader internal simulator.
A live broker connection will have a different - probably longer - bracket
latency tail. The three new constants (NakedGraceSeconds = 5,
NakedReCheckSeconds = 5, AtmRegistrationQuietSeconds = 2.0) are hard-coded
and should be re-measured before running on a funded account. Making them
user properties is suggested at the end of the report.
