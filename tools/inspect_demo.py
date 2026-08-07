#!/usr/bin/env python3
"""Corpus analysis helper: validate a .dem container and date it from its assets.

Deliberately Python and deliberately outside the solution. This is throwaway
corpus archaeology, not part of the parser - the real decoder is C# in
Tf2DemoSalvage.Core (D2). Keeping it separate means nobody mistakes a
one-off measurement script for production code.

Usage:
    python tools/inspect_demo.py walk tools/corpus/demos/z1800.dem
    python tools/inspect_demo.py date tools/corpus/demos/z1800.dem
"""

import re
import struct
import sys
from collections import Counter

HEADER_BYTES = 1072

# democmdinfo_t at demo protocol 3: one Split_t of int32 flags + 6 Vectors.
CMDINFO_BYTES = 76

COMMANDS = {
    1: "dem_signon",
    2: "dem_packet",
    3: "dem_synctick",
    4: "dem_consolecmd",
    5: "dem_usercmd",
    6: "dem_datatables",
    7: "dem_stop",
    8: "dem_stringtables",
}


def read_header(data):
    def text(offset, length=260):
        return data[offset:offset + length].split(b"\0", 1)[0].decode("utf-8", "replace")

    return {
        "stamp": data[0:8],
        "demo_protocol": struct.unpack_from("<i", data, 8)[0],
        "network_protocol": struct.unpack_from("<i", data, 12)[0],
        "server_name": text(16),
        "client_name": text(276),
        "map_name": text(536),
        "game_directory": text(796),
        "playback_time": struct.unpack_from("<f", data, 1056)[0],
        "playback_ticks": struct.unpack_from("<i", data, 1060)[0],
        "playback_frames": struct.unpack_from("<i", data, 1064)[0],
        "signon_length": struct.unpack_from("<i", data, 1068)[0],
    }


def walk(path):
    """Walk the command stream and report whether the layout holds.

    The decisive check is that dem_packet count equals the header's frame count:
    any off-by-one in a payload size drifts and never lands exactly.
    """
    data = open(path, "rb").read()
    header = read_header(data)
    for key, value in header.items():
        print(f"  {key:18} {value}")

    position = HEADER_BYTES
    counts = Counter()
    truncated_at = None

    while position < len(data):
        start = position
        command = data[position]
        if command not in COMMANDS:
            print(f"\n!! unknown command {command} at offset {start}")
            return 1

        # Command header at protocol 3 is 5 bytes: uint8 cmd + int32 tick.
        # Later protocols add a playerSlot byte here.
        if len(data) - position < 5:
            truncated_at = start
            break

        position += 5
        counts[COMMANDS[command]] += 1

        try:
            if command in (1, 2):
                position += CMDINFO_BYTES + 8
                size = struct.unpack_from("<i", data, position)[0]
                position += 4 + size
            elif command == 5:
                position += 4
                size = struct.unpack_from("<i", data, position)[0]
                position += 4 + size
            elif command in (4, 6, 8):
                size = struct.unpack_from("<i", data, position)[0]
                position += 4 + size
            elif command == 7:
                break
        except struct.error:
            truncated_at = start
            break

    print()
    for name, count in counts.most_common():
        print(f"  {name:18} {count}")

    frames = header["playback_frames"]
    packets = counts["dem_packet"]
    print(f"\n  dem_packet {packets} vs header frames {frames}: "
          f"{'MATCH' if packets == frames else 'MISMATCH'}")

    if truncated_at is not None:
        available = len(data) - truncated_at
        command = COMMANDS.get(data[truncated_at], data[truncated_at])
        print(f"  TRUNCATED: {command} header at offset {truncated_at} has "
              f"{available} of 5 bytes")
    return 0


# Valve names seasonal cosmetics after the event year, so assets are self-dating.
# This is the only reliable way to date a demo - protocol numbers are stable for
# years at a time and say nothing about age.
ASSET_YEAR = re.compile(rb"(?:hwn|sum|xms|sbox|spr)(\d{2,4})_", re.IGNORECASE)


def date_demo(path):
    data = open(path, "rb").read()
    years = Counter()

    for raw in ASSET_YEAR.findall(data):
        year = int(raw)
        years[year + 2000 if year < 100 else year] += 1

    if not years:
        print("  no seasonal assets found; cannot date from assets")
        return 1

    for year in sorted(years):
        print(f"  {year}: {years[year]} assets")
    print(f"\n  earliest possible date: {max(years)} or later")
    return 0


def main(argv):
    if len(argv) != 3 or argv[1] not in ("walk", "date"):
        print(__doc__)
        return 2
    return walk(argv[2]) if argv[1] == "walk" else date_demo(argv[2])


if __name__ == "__main__":
    sys.exit(main(sys.argv))
