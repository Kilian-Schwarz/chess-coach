import sys, os
from PIL import Image, ImageDraw, ImageFont

FONT_PATHS = ["/System/Library/Fonts/Apple Symbols.ttf", "/Library/Fonts/Arial Unicode.ttf",
              "/System/Library/Fonts/Supplemental/Arial Unicode.ttf"]
GLYPH_SOLID = {"k": "♚", "q": "♛", "r": "♜", "b": "♝", "n": "♞", "p": "♟"}
GLYPH_OUTLINE = {"k": "♔", "q": "♕", "r": "♖", "b": "♗", "n": "♘", "p": "♙"}
LIGHT, DARK = (235, 236, 208), (119, 149, 86)


def font(size):
    for p in FONT_PATHS:
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    raise SystemExit("no font with chess glyphs")


def parse_fen(fen):
    rows = fen.split()[0].split("/")
    board = []
    for row in rows:
        r = []
        for ch in row:
            if ch.isdigit():
                r += [None] * int(ch)
            else:
                r.append(ch)
        board.append(r)
    return board


def render(fen, white_bottom, path, origin=(310, 140), sq=72, highlight=None):
    W, H = 1600, 1000
    img = Image.new("RGB", (W, H), (38, 36, 33))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 220, H], fill=(28, 27, 25))
    for i in range(12):
        d.rectangle([20, 30 + i * 60, 200, 70 + i * 60], fill=(60 + i * 5, 58, 55))
    d.rectangle([1000, 140, 1500, 716], fill=(48, 46, 43))
    d.text((1020, 160), "Game vs Opponent123", fill=(220, 220, 220))
    for i in range(20):
        d.text((1020, 200 + i * 22), f"{i+1}. e4 e5 Nf3 Nc6", fill=(180, 180, 180))

    f = font(int(sq * 0.82))
    board = parse_fen(fen)
    ox, oy = origin
    for row in range(8):
        for col in range(8):
            rank = 7 - row if white_bottom else row
            file = col if white_bottom else 7 - col
            light = (rank + file) % 2 == 1
            color = LIGHT if light else DARK
            if highlight and (file, rank) in highlight:
                color = (245, 246, 130) if light else (185, 202, 67)
            x, y = ox + col * sq, oy + row * sq
            d.rectangle([x, y, x + sq - 1, y + sq - 1], fill=color)
            p = board[7 - rank][file]
            if p:
                g = p.lower()
                cx, cy = x + sq / 2, y + sq / 2 + sq * 0.04
                if p.isupper():
                    d.text((cx, cy), GLYPH_SOLID[g], font=f, fill=(250, 250, 250), anchor="mm")
                    d.text((cx, cy), GLYPH_OUTLINE[g], font=f, fill=(20, 20, 20), anchor="mm")
                else:
                    d.text((cx, cy), GLYPH_SOLID[g], font=f, fill=(25, 25, 25), anchor="mm")
            if col == 0:
                d.text((x + 3, y + 2), str(rank + 1), fill=DARK if light else LIGHT)
            if row == 7:
                d.text((x + sq - 10, y + sq - 14), "abcdefgh"[file], fill=DARK if light else LIGHT)
    img.save(path)


SEQUENCE = {
    "seq_1": ("rnbqkbnr/pppppppp/8/8/8/5N2/PPPPPPPP/RNBQKB1R", {(6, 0), (5, 2)}),
    "seq_2": ("rnbqkbnr/ppp1pppp/8/3p4/8/5N2/PPPPPPPP/RNBQKB1R", {(3, 6), (3, 4)}),
    "seq_3": ("rnbqkbnr/ppp1pppp/8/3p4/8/5NP1/PPPPPP1P/RNBQKB1R", {(6, 1), (6, 2)}),
    "seq_4": ("rnbqkbnr/pp2pppp/8/2pp4/8/5NP1/PPPPPP1P/RNBQKB1R", {(2, 6), (2, 4)}),
    "seq_lift": ("rnbqkbnr/pp2pppp/8/2pp4/8/5NP1/PPPPPP1P/RNBQK2R", {(5, 0)}),
    "seq_plain_4": ("rnbqkbnr/pp2pppp/8/2pp4/8/5NP1/PPPPPP1P/RNBQKB1R", None),
}


EARLY = {
    "early_d4_white": ("rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR", True),
    "early_e4e5_black": ("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR", False),
    "early_nf3nc6_white": ("r1bqkbnr/pppppppp/2n5/8/8/5N2/PPPPPPPP/RNBQKB1R", True),
    "early_four_plies_black": ("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R", False),
}


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"
    mid = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R"
    render(start, True, os.path.join(out, "start_white.png"))
    render(mid, True, os.path.join(out, "mid_white.png"), highlight={(3, 1), (3, 2)})
    render(start, False, os.path.join(out, "start_black.png"), origin=(420, 90), sq=80)
    render(mid, False, os.path.join(out, "mid_black.png"), origin=(420, 90), sq=80)
    for name, (fen, white_bottom) in EARLY.items():
        render(fen, white_bottom, os.path.join(out, name + ".png"))
    for name, (fen, hl) in SEQUENCE.items():
        render(fen, True, os.path.join(out, name + ".png"), highlight=hl)
    with open(os.path.join(out, "expected.txt"), "w") as fh:
        fh.write(mid + "\n")
    print("ok")
