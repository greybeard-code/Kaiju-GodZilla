"""
MONARCH Intelligence Report System — Configuration
====================================================
Path detection, folder initialisation, and shared constants.
"""

import os
import json
from pathlib import Path

# ── Branding ──────────────────────────────────────────────────────────────────
VERSION  = "1.0.0"
APP_NAME = "MONARCH Intelligence Report System"
HUB_FILE = "CastleBravo.html"

# ── Account constants ─────────────────────────────────────────────────────────
LIVE_ACCOUNTS = {'APEX750470000084', 'APEX750470000085'}
ACCT_LABEL    = {'APEX750470000084': '084', 'APEX750470000085': '085'}
ACCT_GRADE    = {'APEX750470000084': 'G4',  'APEX750470000085': 'G3'}
SIM_PREFIXES  = ('Sim',)   # account names starting with these are ignored


def is_live(account: str) -> bool:
    return account in LIVE_ACCOUNTS


def is_sim(account: str) -> bool:
    return any(account.startswith(p) for p in SIM_PREFIXES)


# ── NT8 folder detection ──────────────────────────────────────────────────────

def _windows_documents_path() -> Path | None:
    """
    Ask Windows where the current user's Documents folder actually lives.
    This is the authoritative source — it reflects OneDrive redirection,
    folder moves, and any other shell customisation.
    Returns None on non-Windows or if the registry read fails.
    """
    try:
        import winreg
        key = winreg.OpenKey(
            winreg.HKEY_CURRENT_USER,
            r'Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders',
        )
        docs, _ = winreg.QueryValueEx(key, 'Personal')
        winreg.CloseKey(key)
        return Path(docs)
    except Exception:
        return None


def _candidate_documents_dirs() -> list[Path]:
    """
    Build an ordered list of directories to search for NinjaTrader 8.
    Covers: registry-reported Documents, OneDrive (personal and business),
    literal Documents, and all user profiles as a last resort.
    """
    home     = Path.home()
    username = os.environ.get('USERNAME') or os.environ.get('USER') or ''
    seen     = set()
    candidates: list[Path] = []

    def add(p: Path):
        if p not in seen:
            seen.add(p)
            candidates.append(p)

    # 1. Registry — the only truly reliable source on Windows
    reg_docs = _windows_documents_path()
    if reg_docs:
        add(reg_docs)

    # 2. OneDrive variants (personal and business/tenant)
    #    OneDrive folder names vary: "OneDrive", "OneDrive - Personal",
    #    "OneDrive - CompanyName", etc.
    for entry in home.iterdir() if home.exists() else []:
        if entry.is_dir() and entry.name.lower().startswith('onedrive'):
            add(entry / 'Documents')

    # 3. Literal Documents (no OneDrive, or as a fallback)
    add(home / 'Documents')
    add(Path(f'C:/Users/{username}/Documents'))

    return candidates


def find_nt8_folder(override: str = None) -> Path:
    """
    Locate the NinjaTrader 8 Documents folder.

    Search order:
      1. --nt8-path CLI argument
      2. NT8_PATH environment variable
      3. Windows registry Shell Folders (handles OneDrive redirection)
      4. OneDrive sub-folders under the user's home directory
      5. Literal Documents paths
    """
    # 1. Explicit override
    if override:
        p = Path(override)
        if p.exists():
            return p
        raise FileNotFoundError(f"NT8 path not found: {override}")

    # 2. Environment variable
    env_path = os.environ.get('NT8_PATH')
    if env_path:
        p = Path(env_path)
        if p.exists():
            return p

    # 3-5. Walk candidate Documents directories
    for docs_dir in _candidate_documents_dirs():
        candidate = docs_dir / 'NinjaTrader 8'
        if candidate.exists():
            return candidate

    raise FileNotFoundError(
        "NinjaTrader 8 folder not found. "
        "Searched: Documents, OneDrive/Documents, registry Shell Folders. "
        "Use --nt8-path to specify the path, "
        "or set the NT8_PATH environment variable."
    )


# ── MONARCH folder structure ──────────────────────────────────────────────────

def init_monarch_dirs(nt8: Path):
    """
    Create the MONARCH folder structure inside the NT8 directory.

    Returns:
        (monarch_dir, logs_dir, reports_dir)
    """
    monarch = nt8 / 'MONARCH'
    logs    = monarch / 'logs'
    reports = monarch / 'reports'
    for d in (monarch, logs, reports):
        d.mkdir(parents=True, exist_ok=True)
    return monarch, logs, reports


# ── Per-installation config file ─────────────────────────────────────────────

def load_local_config(monarch_dir: Path) -> dict:
    """Load optional config.json from the MONARCH folder."""
    cfg_file = monarch_dir / 'config.json'
    if cfg_file.exists():
        try:
            with open(cfg_file, encoding='utf-8') as f:
                return json.load(f)
        except Exception:
            pass
    return {}


def save_local_config(monarch_dir: Path, cfg: dict):
    with open(monarch_dir / 'config.json', 'w', encoding='utf-8') as f:
        json.dump(cfg, f, indent=2)