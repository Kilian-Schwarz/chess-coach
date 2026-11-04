import SwiftUI

struct ChessBoardView: View {
    let placement: Placement
    let whiteAtBottom: Bool
    var highlight: (file: Int, rank: Int)?
    var arrow: (from: (file: Int, rank: Int), to: (file: Int, rank: Int))?
    var interactive = true
    var onMove: (UCIMove) -> Void = { _ in }
    var legal: (Int, Int, Int, Int) -> Bool = { _, _, _, _ in true }

    @State private var selected: (file: Int, rank: Int)?
    @State private var dragFrom: (file: Int, rank: Int)?
    @State private var dragPoint: CGPoint?

    private let light = Color(red: 0.93, green: 0.88, blue: 0.78)
    private let dark = Color(red: 0.45, green: 0.58, blue: 0.40)

    var body: some View {
        GeometryReader { geo in
            let size = min(geo.size.width, geo.size.height)
            let sq = size / 8
            ZStack(alignment: .topLeading) {
                ForEach(0..<64, id: \.self) { i in
                    let col = i % 8, row = i / 8
                    let s = Placement.chessSquare(col: col, row: row, whiteAtBottom: whiteAtBottom)
                    Rectangle()
                        .fill((col + row) % 2 == 0 ? light : dark)
                        .overlay(marker(s))
                        .frame(width: sq, height: sq)
                        .offset(x: CGFloat(col) * sq, y: CGFloat(row) * sq)
                }
                ForEach(0..<64, id: \.self) { i in
                    let col = i % 8, row = i / 8
                    let s = Placement.chessSquare(col: col, row: row, whiteAtBottom: whiteAtBottom)
                    if let p = placement.squares[s.rank][s.file], !(dragFrom.map { $0 == s } ?? false) {
                        PieceGlyph(piece: p, size: sq)
                            .offset(x: CGFloat(col) * sq, y: CGFloat(row) * sq)
                    }
                }
                if let a = arrow {
                    let f = center(a.from, sq), t = center(a.to, sq)
                    ArrowShape(from: f, to: t, width: sq * 0.18)
                        .fill(Color.blue.opacity(0.7))
                }
                if let d = dragFrom, let pt = dragPoint, let p = placement.squares[d.rank][d.file] {
                    PieceGlyph(piece: p, size: sq * 1.2)
                        .position(pt)
                }
                coordinateLabels(sq)
            }
            .frame(width: size, height: size)
            .contentShape(Rectangle())
            .gesture(interactive ? drag(sq) : nil)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(Text("Chessboard"))
        }
        .aspectRatio(1, contentMode: .fit)
    }

    private func center(_ s: (file: Int, rank: Int), _ sq: CGFloat) -> CGPoint {
        let c = Placement.screenSquare(file: s.file, rank: s.rank, whiteAtBottom: whiteAtBottom)
        return CGPoint(x: (CGFloat(c.col) + 0.5) * sq, y: (CGFloat(c.row) + 0.5) * sq)
    }

    @ViewBuilder
    private func marker(_ s: (file: Int, rank: Int)) -> some View {
        if let h = highlight, h == s {
            Color.yellow.opacity(0.45)
        } else if let sel = selected, sel == s {
            Color.yellow.opacity(0.5)
        } else if let sel = selected, legal(sel.file, sel.rank, s.file, s.rank) {
            Circle().fill(Color.black.opacity(0.18)).padding(18)
        }
    }

    private func coordinateLabels(_ sq: CGFloat) -> some View {
        ZStack(alignment: .topLeading) {
            ForEach(0..<8, id: \.self) { i in
                let file = whiteAtBottom ? i : 7 - i
                Text(String(Array("abcdefgh")[file]))
                    .font(.system(size: max(8, sq * 0.16)))
                    .foregroundStyle(.black.opacity(0.55))
                    .offset(x: CGFloat(i) * sq + sq * 0.82, y: sq * 7.8)
                let rank = whiteAtBottom ? 7 - i : i
                Text(String(rank + 1))
                    .font(.system(size: max(8, sq * 0.16)))
                    .foregroundStyle(.black.opacity(0.55))
                    .offset(x: sq * 0.05, y: CGFloat(i) * sq + sq * 0.03)
            }
        }
        .allowsHitTesting(false)
    }

    private func square(at p: CGPoint, _ sq: CGFloat) -> (file: Int, rank: Int)? {
        let col = Int(p.x / sq), row = Int(p.y / sq)
        guard (0..<8).contains(col), (0..<8).contains(row), p.x >= 0, p.y >= 0 else { return nil }
        return Placement.chessSquare(col: col, row: row, whiteAtBottom: whiteAtBottom)
    }

    private func drag(_ sq: CGFloat) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { v in
                guard dragFrom == nil, let s = square(at: v.startLocation, sq) else {
                    if dragFrom != nil { dragPoint = v.location }
                    return
                }
                if placement.squares[s.rank][s.file] != nil, hypot(v.translation.width, v.translation.height) > 4 {
                    dragFrom = s
                    dragPoint = v.location
                }
            }
            .onEnded { v in
                let start = square(at: v.startLocation, sq)
                let end = square(at: v.location, sq)
                defer { dragFrom = nil; dragPoint = nil }
                guard let end else { selected = nil; return }
                if let from = dragFrom {
                    selected = nil
                    attempt(from, end)
                } else if let sel = selected {
                    if sel == end { selected = nil }
                    else if placement.squares[end.rank][end.file].map({ Placement.color(of: $0) }) == placement.squares[sel.rank][sel.file].map({ Placement.color(of: $0) }) {
                        selected = end
                    } else {
                        selected = nil
                        attempt(sel, end)
                    }
                } else if let start, placement.squares[start.rank][start.file] != nil {
                    selected = start
                }
            }
    }

    private func attempt(_ from: (file: Int, rank: Int), _ to: (file: Int, rank: Int)) {
        guard from != to else { return }
        var promotion: Character?
        if let p = placement.squares[from.rank][from.file], Character(p.lowercased()) == "p", to.rank == 0 || to.rank == 7 { promotion = "q" }
        onMove(UCIMove(fromFile: from.file, fromRank: from.rank, toFile: to.file, toRank: to.rank, promotion: promotion))
    }
}

struct PieceGlyph: View {
    let piece: Character
    let size: CGFloat

    private var glyph: String {
        let map: [Character: String] = ["k": "\u{265A}", "q": "\u{265B}", "r": "\u{265C}", "b": "\u{265D}", "n": "\u{265E}", "p": "\u{265F}"]
        return (map[Character(piece.lowercased())] ?? "?") + "\u{FE0E}"
    }

    var body: some View {
        let white = piece.isUppercase
        ZStack {
            Text(glyph).font(.system(size: size * 0.82)).foregroundStyle(.black).scaleEffect(1.07)
            Text(glyph).font(.system(size: size * 0.82)).foregroundStyle(white ? Color.white : Color(white: 0.12))
        }
        .frame(width: size, height: size)
        .allowsHitTesting(false)
    }
}
