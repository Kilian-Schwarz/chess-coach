import csv, sys, os, io
import chess, chess.pgn

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "build", "openings-src")
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "ChessCoach", "Resources", "openings.tsv")


def epd(board: chess.Board) -> str:
    placement = board.board_fen()
    side = "w" if board.turn else "b"
    castling = board.castling_xfen()
    ep = "-"
    last = board.peek() if board.move_stack else None
    if last is not None:
        piece = board.piece_at(last.to_square)
        if piece and piece.piece_type == chess.PAWN and abs(last.to_square - last.from_square) == 16:
            f, r = chess.square_file(last.to_square), chess.square_rank(last.to_square)
            for df in (-1, 1):
                nf = f + df
                if 0 <= nf < 8:
                    q = board.piece_at(chess.square(nf, r))
                    if q and q.piece_type == chess.PAWN and q.color == board.turn:
                        ep = chess.square_name((last.to_square + last.from_square) // 2)
    return f"{placement} {side} {castling} {ep}"


entries = {}
for letter in "abcde":
    with open(os.path.join(src, f"{letter}.tsv"), encoding="utf-8") as fh:
        for row in csv.DictReader(fh, delimiter="\t"):
            game = chess.pgn.read_game(io.StringIO(row["pgn"]))
            board = game.board()
            plies = 0
            for mv in game.mainline_moves():
                board.push(mv)
                plies += 1
            key = epd(board)
            old = entries.get(key)
            if old is None or plies > old[0]:
                entries[key] = (plies, row["eco"], row["name"])

os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf-8", newline="\n") as fh:
    for key in sorted(entries):
        _, eco, name = entries[key]
        fh.write(f"{key}\t{eco}\t{name}\n")
print(len(entries), "positions ->", out)
