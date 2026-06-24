#!/usr/bin/env python3
"""One-way sync of the approved JSONL corpus from the upstream FFXIV-Spanish repo into
data/translations/jsonl.

Translation cadence differs from code cadence, so this is its own step: refresh the corpus without
touching anything else. The vendored libraries under vendor/ are owned code in this repo and are NOT
synced (there is no script for that). Pass --build to also regenerate data/translations.dat
afterwards (runs build/build-translations.py).

The raw corpus tree (data/translations/jsonl) is git-ignored; only the compact data/translations.dat
blob is versioned.

Usage:
    python build/sync-translations.py [--upstream DIR] [--build]
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parent


def sync(upstream: Path) -> int:
    if not upstream.is_dir():
        raise SystemExit(f"Upstream repo not found: {upstream}")

    src = upstream / "data" / "translations" / "jsonl"
    if not src.is_dir():
        raise SystemExit(f"Upstream corpus not found: {src}")

    dst = REPO_ROOT / "data" / "translations" / "jsonl"
    if dst.exists():
        shutil.rmtree(dst)
    dst.mkdir(parents=True, exist_ok=True)

    count = 0
    for jsonl in src.rglob("*.jsonl"):
        target = dst / jsonl.relative_to(src)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(jsonl, target)
        count += 1

    print(f"Synced {count} .jsonl files from upstream into data/translations/jsonl", flush=True)
    return count


def build() -> None:
    script = HERE / "build-translations.py"
    result = subprocess.run([sys.executable, str(script)], check=False)
    if result.returncode != 0:
        raise SystemExit(f"build-translations.py failed (exit {result.returncode})")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--upstream",
        type=Path,
        default=REPO_ROOT.parent / "FFXIV-Spanish",
        help="Path to the upstream FFXIV-Spanish repo (default: sibling of this repo).",
    )
    parser.add_argument(
        "--build",
        action="store_true",
        help="Also run build/build-translations.py (regenerates data/translations.dat) after syncing.",
    )
    args = parser.parse_args(argv)

    sync(args.upstream.resolve())
    if args.build:
        build()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
