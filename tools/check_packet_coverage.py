#!/usr/bin/env python3
"""Report which server-to-client packets have a handler in Client.Main.

Compares the packet specification in ServerPackets/Packets/*_by-server.md
(OpenMU packet docs) with the [PacketHandler(code, sub)] attributes in
Client.Main/Networking and the handlers registered by hand in PacketRouter.

A handler registered with PacketRouter.NoSubCode also covers every sub-code of
that main code, because the router falls back to the main-code-only handler.

Usage:
    python tools/check_packet_coverage.py                 # summary + missing list
    python tools/check_packet_coverage.py --markdown out.md
    python tools/check_packet_coverage.py --include-legacy  # also 075/095/097 variants
    python tools/check_packet_coverage.py --fail-on-missing # exit 1 if anything is missing
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SPEC_DIR = REPO_ROOT / "ServerPackets" / "Packets"
NETWORKING_DIR = REPO_ROOT / "Client.Main" / "Networking"

NO_SUB = None  # main-code-only handler / packet without sub-code

# C1-F3-11-SkillAdded075_by-server.md, C1-3F-2-PlayerShopOpenSuccessful_by-server.md, C2-12-AddCharactersToScope_by-server.md
SPEC_NAME = re.compile(
    r"^(?P<type>C[1-4])-(?P<code>[0-9A-F]{2})(?:-(?P<sub>[0-9A-F]{1,2}))?-(?P<name>[A-Za-z].*)_by-server\.md$"
)
HANDLER_ATTR = re.compile(
    r"\[PacketHandler\(\s*0x(?P<code>[0-9A-Fa-f]{2})\s*,\s*(?:0x(?P<sub>[0-9A-Fa-f]{2})|PacketRouter\.NoSubCode)\s*\)\]"
)
# Packets for old protocol versions (0.75, 0.95, 0.97) that a Season 6 client never receives.
LEGACY_SUFFIX = re.compile(r"(075|095|097)$")

# Handlers registered by hand in PacketRouter.RegisterConnectServerHandlers.
MANUAL_HANDLERS = {
    (0x00, 0x01),  # Hello
    (0xF4, 0x06),  # ServerListResponse
    (0xF4, 0x03),  # ConnectionInfo
}


@dataclass(frozen=True)
class SpecPacket:
    header: str
    code: int
    sub: int | None
    name: str

    @property
    def key(self) -> str:
        sub = f"-{self.sub:02X}" if self.sub is not None else ""
        return f"{self.header}-{self.code:02X}{sub}"

    @property
    def is_legacy(self) -> bool:
        return bool(LEGACY_SUFFIX.search(self.name))


def load_spec() -> list[SpecPacket]:
    packets = []
    for path in sorted(SPEC_DIR.glob("*_by-server.md")):
        match = SPEC_NAME.match(path.name)
        if not match:
            print(f"warning: cannot parse spec file name {path.name}", file=sys.stderr)
            continue
        sub = match.group("sub")
        packets.append(
            SpecPacket(
                header=match.group("type"),
                code=int(match.group("code"), 16),
                sub=int(sub, 16) if sub is not None else NO_SUB,
                name=match.group("name"),
            )
        )
    return packets


def load_handlers() -> dict[tuple[int, int | None], list[str]]:
    handlers: dict[tuple[int, int | None], list[str]] = defaultdict(list)
    for path in sorted(NETWORKING_DIR.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        for match in HANDLER_ATTR.finditer(text):
            sub = match.group("sub")
            key = (int(match.group("code"), 16), int(sub, 16) if sub else NO_SUB)
            line = text.count("\n", 0, match.start()) + 1
            handlers[key].append(f"{path.relative_to(REPO_ROOT).as_posix()}:{line}")
    for key in MANUAL_HANDLERS:
        handlers[key].append("Client.Main/Networking/PacketHandling/PacketRouter.cs (manual)")
    return handlers


def is_covered(packet: SpecPacket, handlers: dict) -> bool:
    if (packet.code, packet.sub) in handlers:
        return True
    # The router falls back to the main-code-only handler for any sub-code.
    return (packet.code, NO_SUB) in handlers


def render_markdown(covered, missing, legacy_skipped, handler_count) -> str:
    total = len(covered) + len(missing)
    pct = 100.0 * len(covered) / total if total else 100.0
    out = [
        "# Pokrycie pakietów S2C (OpenMU spec vs Client.Main)",
        "",
        "Wygenerowane przez `tools/check_packet_coverage.py`.",
        "",
        f"- Pakiety w specyfikacji (bez wariantów 075/095/097): **{total}**",
        f"- Obsłużone: **{len(covered)}** ({pct:.0f}%)",
        f"- Bez handlera: **{len(missing)}**",
        f"- Pominięte warianty legacy: {legacy_skipped}",
        f"- Rejestracje handlerów w porcie: {handler_count}",
        "",
        "## Bez handlera",
        "",
        "| Pakiet | Nazwa |",
        "|---|---|",
    ]
    out += [f"| `{p.key}` | {p.name} |" for p in missing]
    out += ["", "## Obsłużone", "", "| Pakiet | Nazwa |", "|---|---|"]
    out += [f"| `{p.key}` | {p.name} |" for p in covered]
    return "\n".join(out) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--markdown", type=Path, help="write a markdown report to this file")
    parser.add_argument("--include-legacy", action="store_true", help="include 075/095/097 protocol variants")
    parser.add_argument("--fail-on-missing", action="store_true", help="exit with code 1 if any packet is missing")
    args = parser.parse_args()

    if not SPEC_DIR.is_dir():
        print(f"error: spec directory not found: {SPEC_DIR}", file=sys.stderr)
        return 2

    spec = load_spec()
    handlers = load_handlers()

    legacy_skipped = 0
    covered, missing = [], []
    for packet in spec:
        if packet.is_legacy and not args.include_legacy:
            legacy_skipped += 1
            continue
        (covered if is_covered(packet, handlers) else missing).append(packet)

    total = len(covered) + len(missing)
    pct = 100.0 * len(covered) / total if total else 100.0
    print(f"S2C packets in spec: {total} (legacy variants skipped: {legacy_skipped})")
    print(f"Covered: {len(covered)} ({pct:.0f}%), missing: {len(missing)}")
    print()
    for packet in missing:
        print(f"  MISSING {packet.key:<12} {packet.name}")

    if args.markdown:
        args.markdown.parent.mkdir(parents=True, exist_ok=True)
        args.markdown.write_text(
            render_markdown(covered, missing, legacy_skipped, sum(len(v) for v in handlers.values())),
            encoding="utf-8",
        )
        print(f"\nMarkdown report written to {args.markdown}")

    return 1 if args.fail_on_missing and missing else 0


if __name__ == "__main__":
    sys.exit(main())
