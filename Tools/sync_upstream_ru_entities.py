#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

ENTRY_RE = re.compile(r"^(-?[A-Za-z0-9][A-Za-z0-9_-]*)\s*=")
ENTITY_TYPE_RE = re.compile(r"^\s*-\s*type:\s*entity\s*$")
ENTITY_ID_RE = re.compile(r"^\s*id:\s*([A-Za-z0-9][A-Za-z0-9_-]*)\s*$")


@dataclass(frozen=True)
class FtlBlock:
    key: str
    file: str
    text: str


@dataclass(frozen=True)
class AuditSummary:
    shared: int
    changed: int
    source_only_live: int
    source_only_dead: int
    target_only_live: int
    target_only_dead: int
    source_duplicates: int
    target_duplicates: int
    desired_files: int
    desired_entries: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Audit and optionally sync upstream ru-RU entity translations into Resources/Locale/ru-RU.")
    parser.add_argument(
        "--source",
        type=Path,
        default=Path("ru-RU/ss14-ru/prototypes/entities"),
        help="Directory containing the upstream entity .ftl files.")
    parser.add_argument(
        "--target",
        type=Path,
        default=Path("Resources/Locale/ru-RU/ss14-ru/prototypes/entities"),
        help="Directory containing the in-use entity .ftl files.")
    parser.add_argument(
        "--prototypes",
        type=Path,
        default=Path("Resources/Prototypes"),
        help="Prototype root used to decide whether ent-* keys are still live.")
    parser.add_argument(
        "--apply",
        action="store_true",
        help="Rewrite the target tree using live upstream keys plus live target-only keys.")
    parser.add_argument(
        "--report-json",
        type=Path,
        help="Optional path to write the audit summary and samples as JSON.")
    return parser.parse_args()


def parse_ftl_tree(root: Path) -> tuple[list[str], dict[str, list[FtlBlock]], dict[str, FtlBlock], dict[str, list[FtlBlock]]]:
    file_order: list[str] = []
    blocks_by_file: dict[str, list[FtlBlock]] = {}
    first_seen: dict[str, FtlBlock] = {}
    duplicates: dict[str, list[FtlBlock]] = defaultdict(list)

    for path in sorted(root.rglob("*.ftl")):
        relative = path.relative_to(root).as_posix()
        file_order.append(relative)
        blocks: list[FtlBlock] = []
        lines = path.read_text(encoding="utf-8").splitlines()
        index = 0

        while index < len(lines):
            match = ENTRY_RE.match(lines[index])
            if not match:
                index += 1
                continue

            key = match.group(1)
            start = index
            index += 1

            while index < len(lines) and not ENTRY_RE.match(lines[index]):
                index += 1

            text = "\n".join(lines[start:index]).strip()
            block = FtlBlock(key=key, file=relative, text=text)
            blocks.append(block)

            if key in first_seen:
                duplicates[key].append(block)
            else:
                first_seen[key] = block

        blocks_by_file[relative] = blocks

    return file_order, blocks_by_file, first_seen, duplicates


def load_live_entity_ids(root: Path) -> set[str]:
    entity_ids: set[str] = set()

    for path in sorted(root.rglob("*.yml")):
        in_entity = False
        for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
            if ENTITY_TYPE_RE.match(line):
                in_entity = True
                continue

            if line.startswith("- type: "):
                in_entity = False
                continue

            if not in_entity:
                continue

            match = ENTITY_ID_RE.match(line)
            if match:
                entity_ids.add(match.group(1))
                in_entity = False

    return entity_ids


def is_live_entity_key(key: str, live_ids: set[str]) -> bool:
    return key.startswith("ent-") and key[4:] in live_ids


def is_dead_entity_key(key: str, live_ids: set[str]) -> bool:
    return key.startswith("ent-") and key[4:] not in live_ids


def build_desired_tree(
    source_file_order: Iterable[str],
    source_blocks_by_file: dict[str, list[FtlBlock]],
    target_file_order: Iterable[str],
    target_blocks_by_file: dict[str, list[FtlBlock]],
    source_entries: dict[str, FtlBlock],
    target_entries: dict[str, FtlBlock],
    live_ids: set[str],
) -> dict[str, list[FtlBlock]]:
    desired: dict[str, list[FtlBlock]] = defaultdict(list)

    for relative in source_file_order:
        for block in source_blocks_by_file.get(relative, []):
            if is_dead_entity_key(block.key, live_ids):
                continue

            desired[relative].append(block)

    for relative in target_file_order:
        for block in target_blocks_by_file.get(relative, []):
            if block.key in source_entries:
                continue

            if not is_live_entity_key(block.key, live_ids):
                continue

            desired[relative].append(block)

    return dict(sorted(desired.items()))


def write_tree(root: Path, desired_tree: dict[str, list[FtlBlock]]) -> None:
    desired_paths = {root / relative for relative in desired_tree}

    for path in sorted(root.rglob("*.ftl")):
        if path not in desired_paths:
            path.unlink()

    for relative, blocks in desired_tree.items():
        destination = root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        body = "\n\n".join(block.text for block in blocks).rstrip() + "\n"
        destination.write_text(body, encoding="utf-8")

    empty_directories = sorted(
        (path for path in root.rglob("*") if path.is_dir()),
        key=lambda path: len(path.parts),
        reverse=True,
    )

    for directory in empty_directories:
        if any(directory.iterdir()):
            continue
        directory.rmdir()


def build_report_payload(
    summary: AuditSummary,
    source_entries: dict[str, FtlBlock],
    target_entries: dict[str, FtlBlock],
    live_ids: set[str],
) -> dict[str, object]:
    shared_keys = sorted(set(source_entries) & set(target_entries))
    changed_keys = [key for key in shared_keys if source_entries[key].text != target_entries[key].text]
    source_only_keys = sorted(set(source_entries) - set(target_entries))
    target_only_keys = sorted(set(target_entries) - set(source_entries))

    return {
        "summary": summary.__dict__,
        "samples": {
            "changed": changed_keys[:50],
            "source_only_live": [key for key in source_only_keys if is_live_entity_key(key, live_ids)][:50],
            "source_only_dead": [key for key in source_only_keys if is_dead_entity_key(key, live_ids)][:50],
            "target_only_live": [key for key in target_only_keys if is_live_entity_key(key, live_ids)][:50],
            "target_only_dead": [key for key in target_only_keys if is_dead_entity_key(key, live_ids)][:50],
        },
    }


def main() -> int:
    args = parse_args()

    source_file_order, source_blocks_by_file, source_entries, source_duplicates = parse_ftl_tree(args.source)
    target_file_order, target_blocks_by_file, target_entries, target_duplicates = parse_ftl_tree(args.target)
    live_ids = load_live_entity_ids(args.prototypes)

    shared_keys = set(source_entries) & set(target_entries)
    changed_keys = {key for key in shared_keys if source_entries[key].text != target_entries[key].text}
    source_only_keys = set(source_entries) - set(target_entries)
    target_only_keys = set(target_entries) - set(source_entries)

    desired_tree = build_desired_tree(
        source_file_order=source_file_order,
        source_blocks_by_file=source_blocks_by_file,
        target_file_order=target_file_order,
        target_blocks_by_file=target_blocks_by_file,
        source_entries=source_entries,
        target_entries=target_entries,
        live_ids=live_ids,
    )

    summary = AuditSummary(
        shared=len(shared_keys),
        changed=len(changed_keys),
        source_only_live=sum(1 for key in source_only_keys if is_live_entity_key(key, live_ids)),
        source_only_dead=sum(1 for key in source_only_keys if is_dead_entity_key(key, live_ids)),
        target_only_live=sum(1 for key in target_only_keys if is_live_entity_key(key, live_ids)),
        target_only_dead=sum(1 for key in target_only_keys if is_dead_entity_key(key, live_ids)),
        source_duplicates=len(source_duplicates),
        target_duplicates=len(target_duplicates),
        desired_files=len(desired_tree),
        desired_entries=sum(len(blocks) for blocks in desired_tree.values()),
    )

    print(f"Shared keys: {summary.shared}")
    print(f"Changed keys: {summary.changed}")
    print(f"Source-only live keys: {summary.source_only_live}")
    print(f"Source-only dead keys: {summary.source_only_dead}")
    print(f"Target-only live keys: {summary.target_only_live}")
    print(f"Target-only dead keys: {summary.target_only_dead}")
    print(f"Source duplicate keys: {summary.source_duplicates}")
    print(f"Target duplicate keys: {summary.target_duplicates}")
    print(f"Desired files after merge: {summary.desired_files}")
    print(f"Desired entries after merge: {summary.desired_entries}")

    if args.report_json is not None:
        payload = build_report_payload(summary, source_entries, target_entries, live_ids)
        args.report_json.parent.mkdir(parents=True, exist_ok=True)
        args.report_json.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"JSON report written to: {args.report_json}")

    if args.apply:
        write_tree(args.target, desired_tree)
        print(f"Target tree synced: {args.target}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
