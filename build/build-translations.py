#!/usr/bin/env python3
"""Compact the versioned JSONL corpus into data/translations.dat, the gzip-JSONL blob the app
embeds as a resource.

Concatenates every data/translations/jsonl/**/*.jsonl line into one gzip stream, keeping ONLY rows
whose ``status`` is packageable (``approved`` or ``gold``). Rows with any other status
(``rejected``, ``needs-review``, ``draft``...) never get applied by the patcher, so shipping them in
the embedded blob is dead weight — they are dropped here.

Run this after a translation update (build/sync-translations.ps1 syncs the raw corpus first), then
re-publish the app so the embedded resource changes. The output (data/translations.dat) IS versioned:
it is the compact source-of-record this repo ships, regenerated from the raw jsonl tree which is
synced locally and git-ignored.

Usage:
    python build/build-translations.py [--source DIR] [--output FILE]
"""
from __future__ import annotations

import argparse
import gzip
import json
import sys
from collections import Counter
from pathlib import Path

# Keep in sync with PackageableStatus.Default in the Pipeline: the statuses the patcher actually
# applies. A row with any other status is never written into an EXD page, so it is excluded here.
PACKAGEABLE_STATUSES = {"approved", "gold"}


def build(source: Path, output: Path) -> int:
    if not source.is_dir():
        raise SystemExit(
            f"Translation source not found: {source}. Run build/sync-translations.ps1 first."
        )

    files = sorted(source.rglob("*.jsonl"), key=lambda p: str(p))
    if not files:
        raise SystemExit(f"No .jsonl files found under {source}.")

    output.parent.mkdir(parents=True, exist_ok=True)

    kept = 0
    skipped = 0
    by_status: Counter[str] = Counter()
    # newline="\n" + utf-8 without BOM matches the previous PowerShell writer byte-for-byte.
    with gzip.open(output, "wt", encoding="utf-8", newline="\n", compresslevel=9) as out:
        for file in files:
            with file.open("r", encoding="utf-8") as fh:
                for line in fh:
                    line = line.strip()
                    if not line:
                        continue
                    try:
                        status = (json.loads(line).get("status") or "").lower()
                    except json.JSONDecodeError as exc:
                        raise SystemExit(f"Invalid JSON in {file}: {exc}") from exc
                    by_status[status or "(none)"] += 1
                    if status not in PACKAGEABLE_STATUSES:
                        skipped += 1
                        continue
                    out.write(line)
                    out.write("\n")
                    kept += 1

    size_mb = output.stat().st_size / (1024 * 1024)
    detail = ", ".join(f"{s}={n}" for s, n in by_status.most_common())
    print(
        f"Wrote {output}: {kept} packageable entries ({size_mb:.2f} MB compressed) "
        f"from {len(files)} files; dropped {skipped} non-packageable. Corpus by status: {detail}."
    )
    return 0


def main(argv: list[str]) -> int:
    repo_root = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", type=Path, default=repo_root / "data" / "translations" / "jsonl")
    parser.add_argument("--output", type=Path, default=repo_root / "data" / "translations.dat")
    args = parser.parse_args(argv)
    return build(args.source.resolve(), args.output.resolve())


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
