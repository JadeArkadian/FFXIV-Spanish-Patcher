#!/usr/bin/env python3
"""Compact the versioned JSONL corpus into data/translations.dat, the gzip-JSONL blob the app
embeds as a resource.

Two reductions, both lossless for the patcher (the full record stays in the upstream corpus):

1. Row filter: keep ONLY rows the pipeline can actually package, mirroring its ``Packageable``
   check — ``status`` in {approved, gold}, a non-empty ``target`` and a complete source key
   (``sheet`` + ``rowId``). Rows with any other status (``rejected``, ``needs-review``...), an empty
   target or an incomplete key never get applied, so shipping them is dead weight.
2. Field projection: emit ONLY the fields the runtime reads (``source``, ``target``, ``status`` and
   ``sourceKey`` = sheet/rowId/field/exdPath). Dropping the upstream provenance metadata the patcher
   never touches — ``hash`` (random hex, nearly incompressible), ``id``, ``category``, ``translator``,
   ``reviewer``, ``notes``, ``context``, ``subRowId`` — shrinks the gzip blob by ~65%. The
   TranslationEntry model deserializes fine; the omitted fields default to empty/null.

Run this after a translation update (build/sync-translations.py syncs the raw corpus first), then
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

# Compact JSON: no spaces after separators. The .NET reader parses this identically.
_COMPACT = (",", ":")


def project(entry: dict) -> str:
    """Project a corpus row to the minimal record the patcher's pipeline reads."""
    sk = entry.get("sourceKey") or {}
    record = {
        "source": entry.get("source", ""),
        "target": entry.get("target", ""),
        "status": entry.get("status"),
        "sourceKey": {
            "sheet": sk.get("sheet"),
            "rowId": sk.get("rowId"),
            "field": sk.get("field"),
            "exdPath": sk.get("exdPath"),
        },
    }
    return json.dumps(record, ensure_ascii=False, separators=_COMPACT)


def is_packageable(entry: dict) -> bool:
    """Mirror of the pipeline's Packageable check (status already validated by the caller)."""
    if not (entry.get("target") or "").strip():
        return False
    sk = entry.get("sourceKey") or {}
    return bool((sk.get("sheet") or "").strip()) and sk.get("rowId") is not None


def build(source: Path, output: Path) -> int:
    if not source.is_dir():
        raise SystemExit(
            f"Translation source not found: {source}. Run build/sync-translations.py first."
        )

    files = sorted(source.rglob("*.jsonl"), key=lambda p: str(p))
    if not files:
        raise SystemExit(f"No .jsonl files found under {source}.")

    output.parent.mkdir(parents=True, exist_ok=True)

    kept = 0
    skipped = 0
    skipped_incomplete = 0
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
                        entry = json.loads(line)
                    except json.JSONDecodeError as exc:
                        raise SystemExit(f"Invalid JSON in {file}: {exc}") from exc
                    status = (entry.get("status") or "").lower()
                    by_status[status or "(none)"] += 1
                    if status not in PACKAGEABLE_STATUSES:
                        skipped += 1
                        continue
                    if not is_packageable(entry):
                        skipped_incomplete += 1
                        continue
                    out.write(project(entry))
                    out.write("\n")
                    kept += 1

    size_mb = output.stat().st_size / (1024 * 1024)
    detail = ", ".join(f"{s}={n}" for s, n in by_status.most_common())
    print(
        f"Wrote {output}: {kept} packageable entries ({size_mb:.2f} MB compressed) from {len(files)} "
        f"files; dropped {skipped} by status + {skipped_incomplete} empty-target/incomplete-key. "
        f"Corpus by status: {detail}."
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
