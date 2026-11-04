# Opening data

Source: https://github.com/lichess-org/chess-openings (CC0 1.0), files `a.tsv` to `e.tsv` (columns eco, name, pgn).

Regenerate:

    mkdir -p build/openings-src
    for f in a b c d e; do curl -s -o build/openings-src/$f.tsv "https://raw.githubusercontent.com/lichess-org/chess-openings/master/$f.tsv"; done
    python3 -m venv build/venv && build/venv/bin/pip install chess
    build/venv/bin/python Tools/openings/convert.py

Output: `ChessCoach/Resources/openings.tsv`. Data lines: `EPD<TAB>ECO<TAB>name`, UTF-8, LF, sorted by EPD, no header. It holds only the final position of each PGN line in the source (intermediate positions have lines of their own there). If several lines reach the same position, the one with the most half-moves wins.

## EPD convention (Swift and C# must build it identically)

Four fields separated by exactly one space (the first four FEN fields), no move counters:

1. Piece placement as in FEN (rank 8 first, digits for empty squares, `/` between ranks).
2. Side to move: `w` or `b`.
3. Castling rights in the order `K Q k q`, `-` if none. The rights come from the move history (king or rook moved means right lost). Without a history, only an approximation: king on e1/e8 and rook on its corner.
4. En passant target square (for example `e3`) only if the last move was a pawn double step AND an enemy pawn stands directly beside the moved pawn (same rank, adjacent file). Pins and checks are not considered (simplified rule, not python-chess `legal`). Otherwise `-`.

Example after 1.e4: `rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq -`.
