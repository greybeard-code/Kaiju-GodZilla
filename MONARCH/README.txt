MONARCH Intelligence Report System
====================================
Standalone Windows trade report generator for NinjaTrader 8 / GodZilla strategy.
No Python installation required on the target machine once compiled.


QUICK START
-----------
1. Copy this folder to C:\Dev\Kaiju\MONARCH  (or wherever you prefer)
2. Open a terminal in this folder
3. Run:  build.bat          <- compiles MONARCH.exe (one-time)
4. Run:  dist\MONARCH.exe   <- generates today's reports


FOLDER STRUCTURE CREATED AUTOMATICALLY
---------------------------------------
After first run, MONARCH creates this inside your NinjaTrader 8 folder:

  NinjaTrader 8\
    MONARCH\
      CastleBravo.html       <- Hub / index page (open this in your browser)
      logs\                  <- GodZilla CSV files are copied here
      reports\
        daily_YYYYMMDD.html  <- One per trading day
        weekly_YYYYMMDD.html <- One per Friday (Mon-Fri summary)
        index.json           <- Internal metadata


USAGE
-----
MONARCH.exe                         Normal daily run
MONARCH.exe --backfill              Generate ALL missing reports
MONARCH.exe --date 2026-05-22       Regenerate a specific date
MONARCH.exe --weekly                Force-regenerate this week's summary
MONARCH.exe --nt8-path "D:\NT8"     Override NT8 folder location
MONARCH.exe --dry-run               Preview what would be generated
MONARCH.exe --version               Show version


SCHEDULING (Windows Task Scheduler)
-------------------------------------
Recommended schedule:  Mon-Fri at 5:00 PM
Action:  Start a program
Program: C:\Dev\Kaiju\MONARCH\dist\MONARCH.exe

Friday 5pm will automatically generate both the daily AND weekly summary.
Saturday/Sunday runs map to Friday — safe to schedule daily without gaps.


TRADING DAY RULES
------------------
- Session boundary: 6:00 PM ET = start of new session
- Session date = END of the session (next calendar day for evening trades)
  Example:  Trade at 10:22 PM on Tuesday  ->  Wednesday's report
            Trade at  9:45 AM on Wednesday ->  Wednesday's report
- Saturday run = Friday's report
- Sunday run   = Friday's report


SOURCE FILES  (src\)
---------------------
monarch.py       Entry point / orchestrator (CLI args, run loop)
config.py        NT8 path detection, account constants
date_utils.py    Trading day math, week helpers, missing-report detection
log_sync.py      Find & copy GodZilla CSVs to MONARCH\logs\
log_parser.py    Parse CSVs into trade dicts, compute stats
templates.py     Shared CSS theme and HTML component helpers
daily_report.py  Build one day's HTML report
weekly_report.py Build Mon-Fri weekly summary HTML
hub.py           Build CastleBravo.html hub page


BUILDING FROM SOURCE
---------------------
Requirements: Python 3.10+ with pip
  pip install pyinstaller
  python src\monarch.py        <- run directly from source (no build needed)
  build.bat                    <- compile to dist\MONARCH.exe


ACCOUNTS
---------
APEX750470000084  (G4 threshold, active since May 18)
APEX750470000085  (G3 threshold, active since May 21)
Sim accounts (Sim101, Sim102, SimKhahn) are parsed but excluded from reports.


VERSION HISTORY
---------------
1.0.0  Initial release — modular rewrite of generate_report.py
       New: standalone exe, auto log sync, backfill, Sat/Sun guardrail,
            CastleBravo hub, MONARCH branding
