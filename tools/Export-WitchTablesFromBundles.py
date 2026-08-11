#!/usr/bin/env python3
"""Refresh a Witch runtime-table snapshot directly from installed Addressables bundles.

The game adds derived KeyWords rows at runtime.  A previous runtime export is used as
the template for those derived rows and for stable field ordering; every native table
field is replaced with the value extracted from the current game bundles.
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import glob
import io
import json
import os
import re
import sys
from collections import OrderedDict

try:
    import UnityPy
except ImportError as exc:
    raise SystemExit(
        "UnityPy is required. Install it with: python -m pip install --user UnityPy"
    ) from exc


TABLE_BUNDLES = OrderedDict(
    [
        ("Card", "card"),
        ("CardPack", "cardpack"),
        ("Career", "career"),
        ("Enemy", "enemy"),
        ("EnemyCard", "enemycard"),
        ("EnchTag", "enchtag"),
        ("Buff", "buff"),
        ("Level", "level"),
        ("Partner", "partner"),
        ("PartnerCard", "partnercard"),
        ("Relic", "relic"),
        ("Bless", "blessing"),
        ("Hard", "hard"),
    ]
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-root", required=True)
    parser.add_argument("--previous-export", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--game-build", required=True)
    return parser.parse_args()


def bundle_root(game_root: str) -> str:
    data_directories = glob.glob(os.path.join(game_root, "*_Data"))
    if len(data_directories) != 1:
        raise RuntimeError(
            f"expected one *_Data directory under {game_root}, found {len(data_directories)}"
        )
    return os.path.join(
        data_directories[0],
        "StreamingAssets",
        "aa",
        "StandaloneWindows64",
        "dataconfig_assets_dataconfigs",
    )


def find_bundle(root: str, kind: str, name: str) -> str | None:
    matches = glob.glob(os.path.join(root, kind, f"{name}_*.bundle"))
    if not matches:
        return None
    if len(matches) != 1:
        raise RuntimeError(f"expected one {kind}/{name} bundle, found {len(matches)}")
    return matches[0]


def read_bundle_rows(root: str, bundle_name: str) -> list[OrderedDict[str, str]]:
    rows_by_id: OrderedDict[str, OrderedDict[str, str]] = OrderedDict()
    for kind in ("data", "text"):
        path = find_bundle(root, kind, bundle_name)
        if path is None:
            continue
        environment = UnityPy.load(path)
        for obj in environment.objects:
            if obj.type.name != "TextAsset":
                continue
            asset = obj.read()
            prefix = str(asset.m_Name)
            reader = csv.DictReader(io.StringIO(str(asset.m_Script).lstrip("\ufeff")))
            for index, source in enumerate(reader):
                # Every game CSV has a human-readable schema explanation on row 1.
                if index == 0:
                    continue
                authored_id = (source.get("Id") or "").strip()
                source_id = authored_id.lstrip("*")
                if not source_id:
                    continue
                full_id = (
                    source_id
                    if source_id.lower().startswith(prefix.lower() + "_")
                    else prefix + "_" + source_id
                )
                row = rows_by_id.setdefault(full_id, OrderedDict())
                for key, value in source.items():
                    if key:
                        row[key] = value or ""
                row["Id"] = full_id
                # The loader removes a leading `*` from runtime table IDs, but the
                # marker still distinguishes internal/non-pool rows in the source
                # CSV.  Preserve both forms so documentation can filter by the
                # authored marker while runtime joins continue to use `Id`.
                row["SourceId"] = (
                    authored_id
                    if authored_id.lower().startswith(prefix.lower() + "_")
                    else prefix + "_" + authored_id
                )
    if not rows_by_id:
        raise RuntimeError(f"no table rows extracted for {bundle_name}")
    return list(rows_by_id.values())


def merge_with_template(
    current: list[OrderedDict[str, str]], previous: list[dict[str, str]]
) -> list[OrderedDict[str, str]]:
    previous_by_id = {row["Id"]: row for row in previous}
    merged: list[OrderedDict[str, str]] = []
    for current_row in current:
        row = OrderedDict(previous_by_id.get(current_row["Id"], {}))
        row.update(current_row)
        merged.append(row)
    return merged


def read_direct_keywords(root: str) -> list[OrderedDict[str, str]]:
    return read_bundle_rows(root, "keywordsdic")


def refresh_derived_keyword(
    target: OrderedDict[str, str], source: dict[str, str], include_icon: bool
) -> None:
    for key, value in source.items():
        if key.startswith("Name"):
            target[key.replace("Name", "Keywords")] = value
        elif key.startswith("Description"):
            target[key] = re.sub(r"\(\{.*?\}\)", "", value)
        elif include_icon and key == "Icon":
            target[key] = value


def refresh_keywords(
    previous: list[dict[str, str]], tables: dict[str, list[OrderedDict[str, str]]], root: str
) -> list[OrderedDict[str, str]]:
    direct = read_direct_keywords(root)
    direct_by_id = {row["Id"]: row for row in direct}
    source_maps = {
        "BuffKeyword_": ({row["Id"]: row for row in tables["Buff"]}, True),
        "CardKeyword_": ({row["Id"]: row for row in tables["Card"]}, True),
        "EnchTag_": ({row["Id"]: row for row in tables["EnchTag"]}, False),
    }
    result: list[OrderedDict[str, str]] = []
    seen: set[str] = set()
    for old in previous:
        keyword_id = old["Id"]
        row = OrderedDict(old)
        if keyword_id in direct_by_id:
            row.update(direct_by_id[keyword_id])
        else:
            for prefix, (source_by_id, include_icon) in source_maps.items():
                if not keyword_id.startswith(prefix):
                    continue
                source = source_by_id.get(keyword_id[len(prefix) :])
                if source is not None:
                    refresh_derived_keyword(row, source, include_icon)
                break
        result.append(row)
        seen.add(keyword_id)

    for row in direct:
        if row["Id"] not in seen:
            result.append(row)
    return result


def main() -> int:
    args = parse_args()
    root = bundle_root(os.path.abspath(args.game_root))
    with open(args.previous_export, "r", encoding="utf-8-sig") as stream:
        previous = json.load(stream, object_pairs_hook=OrderedDict)

    tables: OrderedDict[str, list[OrderedDict[str, str]]] = OrderedDict()
    previous_tables = previous["Tables"]
    for table_name, bundle_name in TABLE_BUNDLES.items():
        extracted = read_bundle_rows(root, bundle_name)
        tables[table_name] = merge_with_template(extracted, previous_tables[table_name])

    # Preserve the native runtime table order: KeyWords sits between EnemyCard and EnchTag.
    ordered_tables: OrderedDict[str, list[OrderedDict[str, str]]] = OrderedDict()
    for table_name in ("Card", "CardPack", "Career", "Enemy", "EnemyCard"):
        ordered_tables[table_name] = tables[table_name]
    ordered_tables["KeyWords"] = refresh_keywords(previous_tables["KeyWords"], tables, root)
    for table_name in (
        "EnchTag",
        "Buff",
        "Level",
        "Partner",
        "PartnerCard",
        "Relic",
        "Bless",
        "Hard",
    ):
        ordered_tables[table_name] = tables[table_name]

    document = OrderedDict(
        [
            ("GameBuild", "v" + args.game_build.lstrip("vV")),
            ("ExportedAtUtc", dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")),
            ("ExportSource", "installed-addressables+previous-runtime-derived-keywords"),
            ("Tables", ordered_tables),
        ]
    )
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(document, stream, ensure_ascii=False, indent=2)
        stream.write("\n")

    counts = ", ".join(f"{name}={len(rows)}" for name, rows in ordered_tables.items())
    print(f"Witch table snapshot exported: {counts}")
    print(os.path.abspath(args.output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
