import sys, os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

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


def render(fen, white_bottom, path, origin=(310, 140), sq=72, highlight=None, dots=None, floating=None, popup=None,
           canvas=(1600, 1000), decor=True, arrows=None, cursor=None):
    W, H = canvas
    img = Image.new("RGB", (W, H), (38, 36, 33))
    d = ImageDraw.Draw(img)
    if decor:
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
    for file, rank in dots or []:
        col = file if white_bottom else 7 - file
        row = 7 - rank if white_bottom else rank
        x, y = ox + col * sq, oy + row * sq
        light = (rank + file) % 2 == 1
        shade = (210, 211, 186) if light else (107, 134, 77)
        if board[7 - rank][file]:
            d.ellipse([x + 2, y + 2, x + sq - 3, y + sq - 3], outline=shade, width=max(3, sq // 12))
        else:
            r = sq * 0.16
            d.ellipse([x + sq / 2 - r, y + sq / 2 - r, x + sq / 2 + r, y + sq / 2 + r], fill=shade)
    for (ff, fr), (tf, tr) in arrows or []:
        def centre(file, rank):
            col = file if white_bottom else 7 - file
            row = 7 - rank if white_bottom else rank
            return ox + col * sq + sq / 2, oy + row * sq + sq / 2
        (x1, y1), (x2, y2) = centre(ff, fr), centre(tf, tr)
        d.line([x1, y1, x2, y2], fill=(255, 170, 0), width=max(4, sq // 6))
        d.ellipse([x2 - sq * 0.25, y2 - sq * 0.25, x2 + sq * 0.25, y2 + sq * 0.25], fill=(255, 170, 0))
    if floating:
        g, (fx, fy), *rest = floating
        if rest:
            f = font(int(sq * 0.82 * rest[0]))
        if g.isupper():
            d.text((fx, fy), GLYPH_SOLID[g.lower()], font=f, fill=(250, 250, 250), anchor="mm")
            d.text((fx, fy), GLYPH_OUTLINE[g.lower()], font=f, fill=(20, 20, 20), anchor="mm")
        else:
            d.text((fx, fy), GLYPH_SOLID[g], font=f, fill=(25, 25, 25), anchor="mm")
    if cursor:
        cx, cy = cursor
        pts = [(cx, cy), (cx, cy + sq * 0.38), (cx + sq * 0.1, cy + sq * 0.3), (cx + sq * 0.18, cy + sq * 0.46),
               (cx + sq * 0.25, cy + sq * 0.43), (cx + sq * 0.17, cy + sq * 0.28), (cx + sq * 0.3, cy + sq * 0.28)]
        d.polygon(pts, fill=(0, 0, 0), outline=(255, 255, 255))
    if popup:
        box = (ox + 2 * sq, oy + 2 * sq, ox + 6 * sq, oy + 5 * sq)
        if popup == "blur":
            region = img.crop(box).filter(ImageFilter.GaussianBlur(sq * 0.9))
            img.paste(region, box[:2])
        else:
            d.rectangle(box, fill=(49, 46, 43))
            d.text(((box[0] + box[2]) / 2, box[1] + sq * 0.5), "Game Over", fill=(240, 240, 240), anchor="mm")
            d.rectangle([box[0] + 10, box[1] + sq, box[2] - 10, box[1] + sq * 1.6], fill=(106, 160, 60))
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
    midgame = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R"
    render(midgame, True, os.path.join(out, "popup_blank.png"), popup="blank")
    render(midgame, True, os.path.join(out, "popup_blur.png"), popup="blur")
    render(midgame, True, os.path.join(out, "popup_none.png"))
    lifted = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R"
    lift_from = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/4P3/3P1N2/PPP2PPP/RNBQK2R"
    dots = {(1, 4), (0, 5), (3, 4), (4, 5), (5, 6), (1, 2)}
    render(lift_from, True, os.path.join(out, "lift_dots.png"), dots=dots, floating=("B", (310 + 5 * 72 + 36, 140 + 3 * 72 + 20)))
    render(lift_from, True, os.path.join(out, "lift_offgrid.png"), floating=("B", (310 + 8 * 72 + 60, 140 + 3 * 72)))
    render(lift_from, True, os.path.join(out, "lift_dots_only.png"), dots=dots)
    big = dict(origin=(140, 140), sq=140, canvas=(1400, 1400), decor=False)
    bigmid = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R"
    biglift = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/4P3/3P1N2/PPP2PPP/RNBQK2R"
    render(start, True, os.path.join(out, "big_start.png"), **big)
    render(bigmid, True, os.path.join(out, "big_mid.png"), highlight={(3, 1), (3, 2)}, **big)
    render(biglift, True, os.path.join(out, "big_drag.png"), highlight={(3, 1), (3, 2)}, dots=dots,
           floating=("B", (140 + 5 * 140 + 70, 140 + 3 * 140 + 40), 1.35), cursor=(140 + 5 * 140 + 80, 140 + 3 * 140 + 60), **big)
    render(biglift, True, os.path.join(out, "big_dots.png"), highlight={(2, 3), (5, 2), (4, 5)}, dots=dots, **big)
    render(bigmid, True, os.path.join(out, "big_arrows.png"), highlight={(3, 1), (3, 2), (1, 4), (6, 5)},
           arrows=[((4, 3), (4, 6)), ((1, 3), (6, 4))], cursor=(140 + 3 * 140 + 30, 140 + 4 * 140 + 30), **big)
    render(bigmid, True, os.path.join(out, "big_shifted.png"), origin=(260, 90), sq=140, canvas=(1400, 1400), decor=False)
    saved = LIGHT, DARK
    LIGHT, DARK = (240, 217, 181), (181, 136, 99)
    render(bigmid, True, os.path.join(out, "big_other_theme.png"), **big)
    LIGHT, DARK = saved
    with open(os.path.join(out, "expected.txt"), "w") as fh:
        fh.write(mid + "\n")
    print("ok")
