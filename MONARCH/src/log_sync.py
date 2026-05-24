"""
MONARCH Intelligence Report System — Log Sync
==============================================
Finds all GodZilla_*.csv files in the NT8 folder tree and copies
any new or updated ones into MONARCH/logs/.

Rules:
  - Include:  GodZilla_*.csv  anywhere under the NT8 root
  - Exclude:  GodZuki_*.csv   (strategy state files, not trade logs)
  - Exclude:  Any file already in MONARCH/logs/ that hasn't changed
              (size + mtime comparison to avoid unnecessary copies)
"""

import shutil
from pathlib import Path
from typing import List, Tuple


def find_source_logs(nt8: Path) -> List[Path]:
    """
    Recursively find all GodZilla_*.csv files under the NT8 root,
    excluding the MONARCH/logs/ destination to prevent self-copies.
    """
    monarch_logs = nt8 / 'MONARCH' / 'logs'
    found = []
    for f in nt8.rglob('GodZilla_*.csv'):
        # Skip files already in MONARCH/logs
        try:
            f.relative_to(monarch_logs)
            continue   # this file IS in the destination, skip it
        except ValueError:
            pass
        if f.is_file():
            found.append(f)
    return sorted(found)


def _needs_copy(src: Path, dst: Path) -> bool:
    """Return True if dst doesn't exist or is older/different from src."""
    if not dst.exists():
        return True
    src_stat = src.stat()
    dst_stat = dst.stat()
    # Copy if size differs or source is newer (>1s tolerance)
    if src_stat.st_size != dst_stat.st_size:
        return True
    if src_stat.st_mtime > dst_stat.st_mtime + 1.0:
        return True
    return False


def sync_logs(nt8: Path, logs_dir: Path) -> Tuple[int, int]:
    """
    Copy new/updated GodZilla CSVs from the NT8 tree into logs_dir.

    Returns:
        (copied_count, skipped_count)
    """
    sources = find_source_logs(nt8)
    copied  = 0
    skipped = 0

    for src in sources:
        dst = logs_dir / src.name
        if _needs_copy(src, dst):
            try:
                shutil.copy2(src, dst)   # copy2 preserves metadata
                copied += 1
            except Exception as e:
                print(f"  [warn] Could not copy {src.name}: {e}")
        else:
            skipped += 1

    return copied, skipped
