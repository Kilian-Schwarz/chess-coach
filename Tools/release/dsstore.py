import plistlib
import struct
import sys

PAGE = 0x1000
SPACE = 0x2000


def utf16(text):
    return text.encode("utf-16-be")


def record(name, code, kind, value):
    head = struct.pack(">I", len(name)) + utf16(name) + code.encode() + kind.encode()
    if kind == "blob":
        return head + struct.pack(">I", len(value)) + value
    if kind == "long":
        return head + struct.pack(">I", value)
    if kind == "type":
        return head + value.encode()
    raise ValueError(kind)


def icon_location(x, y):
    return struct.pack(">IIIHH", x, y, 0xFFFFFFFF, 0xFFFF, 0)


def window_plist(width, height):
    return plistlib.dumps(
        {
            "WindowBounds": "{{200, 140}, {%d, %d}}" % (width, height),
            "ShowStatusBar": False,
            "ShowToolbar": False,
            "ShowSidebar": False,
            "ShowPathbar": False,
            "ShowTabView": False,
            "ContainerShowSidebar": False,
            "SidebarWidth": 0,
        },
        fmt=plistlib.FMT_BINARY,
    )


def view_plist(icon_size):
    return plistlib.dumps(
        {
            "viewOptionsVersion": 1,
            "backgroundType": 1,
            "backgroundColorRed": 1.0,
            "backgroundColorGreen": 1.0,
            "backgroundColorBlue": 1.0,
            "gridOffsetX": 0.0,
            "gridOffsetY": 0.0,
            "gridSpacing": 100.0,
            "iconSize": float(icon_size),
            "textSize": 13.0,
            "showItemInfo": False,
            "showIconPreview": True,
            "labelOnBottom": True,
            "arrangeBy": "none",
            "scrollPositionX": 0.0,
            "scrollPositionY": 0.0,
        },
        fmt=plistlib.FMT_BINARY,
    )


def build(width, height, icon_size, items):
    records = [
        (".", "bwsp", record(".", "bwsp", "blob", window_plist(width, height))),
        (".", "icvp", record(".", "icvp", "blob", view_plist(icon_size))),
        (".", "vSrn", record(".", "vSrn", "long", 1)),
        (".", "vstl", record(".", "vstl", "type", "icnv")),
    ]
    for name, (x, y) in items.items():
        records.append((name, "Iloc", record(name, "Iloc", "blob", icon_location(x, y))))
    records.sort(key=lambda r: (r[0].lower(), r[1]))
    leaf = struct.pack(">II", 0, len(records)) + b"".join(r[2] for r in records)
    leaf = leaf.ljust(PAGE, b"\0")
    info = struct.pack(">IIIII", 2, 0, len(records), 1, PAGE).ljust(32, b"\0")

    blocks = [None, 0x20 | 5, 0x1000 | 12]
    root_addr = 0x800 | 11
    blocks[0] = root_addr
    offsets = struct.pack(">II", len(blocks), 0) + b"".join(struct.pack(">I", b) for b in blocks)
    offsets += b"\0" * (4 * (256 - len(blocks)))
    toc = struct.pack(">I", 1) + bytes([4]) + b"DSDB" + struct.pack(">I", 1)
    free = {6: [0x40], 7: [0x80], 8: [0x100], 9: [0x200], 10: [0x400]}
    for w in range(13, 32):
        free[w] = [1 << w]
    freelist = b"".join(struct.pack(">I", len(free.get(w, []))) + b"".join(struct.pack(">I", o) for o in free.get(w, [])) for w in range(32))
    root = (offsets + toc + freelist).ljust(0x800, b"\0")

    data = bytearray(4 + SPACE)
    data[0:4] = struct.pack(">I", 1)
    data[4:8] = b"Bud1"
    data[8:20] = struct.pack(">III", 0x800, 0x800, 0x800)
    data[4 + 0x20 : 4 + 0x20 + 32] = info
    data[4 + 0x800 : 4 + 0x800 + len(root)] = root
    data[4 + 0x1000 : 4 + 0x1000 + PAGE] = leaf
    return bytes(data)


if __name__ == "__main__":
    out, app_name = sys.argv[1], sys.argv[2]
    open(out, "wb").write(build(540, 380, 128, {app_name: (140, 170), "Applications": (400, 170)}))
