#!/usr/bin/env python3
"""Audit sound usage between the port and the original client sources.

The original client registers sounds with LoadWaveFile(SOUND_*, L"Data\\Sound\\...").
The port references audio by relative path string literals. This script compares
both against the files shipped in the game Data directory.
"""

from __future__ import annotations

import argparse
import collections
import re
import sys
from pathlib import Path

LOAD_WAVE_RE = re.compile(r'LoadWaveFile\s*\(\s*(SOUND_[A-Z0-9_]+)\s*,\s*L"([^"]+)"')
PORT_SOUND_RE = re.compile(r'"((?:Sound|Music)/[^"\r\n]+\.(?:wav|mp3|ogg|MP3))"')


def repo_root() -> Path:
    return Path(__file__).resolve().parent.parent


def normalize(path_value: str) -> str:
    value = path_value.strip().replace("\\", "/")
    while "//" in value:
        value = value.replace("//", "/")
    if value.lower().startswith("data/"):
        value = value[len("data/") :]
    return value.lstrip("/")


def parse_original(source_dir: Path) -> dict[str, dict[str, set[str]]]:
    entries: dict[str, dict[str, set[str]]] = collections.defaultdict(lambda: collections.defaultdict(set))
    for cpp in sorted(source_dir.glob("*.cpp")):
        text = cpp.read_text(encoding="utf-8", errors="ignore")
        for sound_id, path_value in LOAD_WAVE_RE.findall(text):
            entries[sound_id][cpp.name].add(normalize(path_value))
    return entries


def parse_port(port_dir: Path) -> dict[str, list[str]]:
    refs: dict[str, list[str]] = collections.defaultdict(list)
    for cs in sorted(port_dir.rglob("*.cs")):
        text = cs.read_text(encoding="utf-8", errors="ignore")
        for match in PORT_SOUND_RE.finditer(text):
            value = match.group(1)
            if "{" in value:
                continue
            refs[normalize(value)].append(str(cs.relative_to(repo_root())))
    return refs


def build_data_index(data_dir: Path) -> dict[str, str]:
    index: dict[str, str] = {}
    if not data_dir.is_dir():
        return index
    for file in data_dir.rglob("*"):
        if file.is_file():
            index[str(file.relative_to(data_dir)).replace("\\", "/").lower()] = file.name
    return index


def split_case(path_value: str, data_dir: Path) -> str | None:
    current = data_dir
    for part in Path(path_value).parts:
        if not current.is_dir():
            return None
        exact = next((child for child in current.iterdir() if child.name == part), None)
        if exact is None:
            return None
        current = exact
    return current.name if current.is_file() else None


def status_for(path_value: str, data_index: dict[str, str], data_dir: Path) -> str:
    key = path_value.lower()
    if key not in data_index:
        return "missing"
    if split_case(path_value, data_dir) is None:
        return "case"
    return "ok"


def print_matrix(
    title: str,
    values: list[tuple[str, str]],
    limit: int,
) -> None:
    print(f"\n{title}: {len(values)}")
    displayed = values if limit <= 0 else values[:limit]
    for path_value, origin in displayed:
        print(f"  {path_value}  <- {origin}")
    if limit > 0 and len(values) > limit:
        print(f"  ... and {len(values) - limit} more")


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, default=Path(r"C:\Games\MU_Red_1_20_61_Full\Data"))
    parser.add_argument("--source", type=Path, default=repo_root() / "SourceMain5.2" / "source")
    parser.add_argument("--port", type=Path, default=repo_root() / "Client.Main")
    parser.add_argument("--limit", type=int, default=40)
    parser.add_argument("--strict", action="store_true", help="Return non-zero when data errors exist.")
    args = parser.parse_args()

    if not args.source.is_dir() or not args.port.is_dir():
        print(f"Source or port directory not found: {args.source} / {args.port}", file=sys.stderr)
        return 2

    original = parse_original(args.source)
    port = parse_port(args.port)
    port_extra = parse_port(repo_root() / "Client.Data")
    for path_value, files in port_extra.items():
        port[path_value].extend(files)

    data_index = build_data_index(args.data)
    if not data_index:
        print(f"Data directory not found or empty: {args.data}", file=sys.stderr)
        return 2

    original_paths: dict[str, set[str]] = collections.defaultdict(set)
    for sound_id, files in original.items():
        for paths in files.values():
            for path_value in paths:
                original_paths[path_value].add(sound_id)

    port_paths = {path_value.lower(): path_value for path_value in port}
    original_lower = {path_value.lower() for path_value in original_paths}

    missing_in_port = sorted(
        (path_value, ", ".join(sorted(original_paths[path_value])))
        for path_value in original_paths
        if path_value.lower() not in port_paths
    )
    extra_in_port = sorted(
        (original_path, ", ".join(sorted(set(port[original_path]))))
        for key, original_path in port_paths.items()
        if key not in original_lower
    )

    data_errors: list[tuple[str, str]] = []
    port_data_errors: list[tuple[str, str]] = []
    # Check every literal independently. Lowercasing is appropriate for the
    # comparison above, but collapsing paths here hides bad casing when another
    # occurrence happens to use the correct spelling (notably on Linux).
    for path_value in sorted(set(original_paths) | set(port)):
        state = status_for(path_value, data_index, args.data)
        sources = []
        if path_value in original_paths:
            sources.append(", ".join(sorted(original_paths[path_value])))
        if path_value in port:
            sources.append(", ".join(sorted(set(port[path_value]))))
        if state != "ok":
            entry = (f"[{state}] {path_value}", " | ".join(sources))
            data_errors.append(entry)
            if path_value in port:
                port_data_errors.append(entry)

    print(f"Original LoadWaveFile entries: {len(original)} ids / {len(original_paths)} files")
    print(f"Port audio references: {len(port_paths)} files")
    print(f"Data index: {len(data_index)} files")

    print_matrix("Original sounds not referenced by the port", missing_in_port, args.limit)
    print_matrix("Port references not registered by the original", extra_in_port, args.limit)
    print_matrix("Files missing from Data or with mismatched case", data_errors, args.limit)

    print(f"Port reference data errors: {len(port_data_errors)}")
    if port_data_errors and args.strict:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
