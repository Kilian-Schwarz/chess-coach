import Foundation

enum FactCheck {
    private static let kinds = Array("kqrbnp")

    private static let names: [String: Character] = {
        var map: [String: Character] = [:]
        for kind in kinds {
            for loc in [Loc.facts, Loc.display] { map[PieceInfo.name(kind, loc).lowercased()] = kind }
        }
        return map
    }()

    private static let letters: [Character: Character] = {
        var map: [Character: Character] = [:]
        for pieces in ["KQRBN", SAN.displayPieces] {
            for (letter, kind) in zip(pieces, "kqrbn") { map[letter] = kind }
        }
        return map
    }()

    static func allowed(positions: [ChessPosition], lines: [[String]]) -> Set<String> {
        var set = Set<String>()
        func add(_ pos: ChessPosition) {
            for rank in 0..<8 { for file in 0..<8 {
                if let p = pos.board.squares[rank][file] {
                    set.insert("\(Character(p.lowercased())):\(UCIMove.squareName(file, rank))")
                }
            } }
            for side in [pos, pos.nullMove] {
                for m in side.legalMoves() {
                    if let p = side.board.squares[m.fromRank][m.fromFile] {
                        set.insert("\(Character(p.lowercased())):\(UCIMove.squareName(m.toFile, m.toRank))")
                    }
                }
            }
        }
        for pos in positions {
            add(pos)
            for pv in lines {
                var cur = pos
                for u in pv.prefix(6) {
                    guard let m = UCIMove(u), cur.isLegal(m) else { break }
                    cur = cur.applying(m)
                    add(cur)
                }
            }
        }
        return set
    }

    static func mentionsInvented(_ sentence: String, allowed: Set<String>) -> Bool {
        for (name, kind) in names {
            var search = sentence.startIndex..<sentence.endIndex
            while let r = sentence.range(of: name + " ", options: .caseInsensitive, range: search) {
                let rest = sentence[r.upperBound...]
                let chars = Array(rest.prefix(2))
                if chars.count == 2, "abcdefgh".contains(chars[0]), ("1"..."8").contains(chars[1]),
                   !(rest.dropFirst(2).first?.isLetter ?? false) {
                    if !allowed.contains("\(kind):\(chars[0])\(chars[1])") { return true }
                }
                search = r.upperBound..<sentence.endIndex
            }
        }
        let chars = Array(sentence)
        var i = 0
        while i < chars.count {
            if let kind = letters[chars[i]], i == 0 || !chars[i - 1].isLetter {
                var j = i + 1
                if j < chars.count, chars[j] == "x" { j += 1 }
                if j + 1 < chars.count, "abcdefgh".contains(chars[j]), ("1"..."8").contains(chars[j + 1]),
                   !(j + 2 < chars.count && chars[j + 2].isLetter) {
                    if !allowed.contains("\(kind):\(chars[j])\(chars[j + 1])") { return true }
                }
            }
            i += 1
        }
        return false
    }

    static func clean(_ text: String, allowed: Set<String>) -> (text: String?, removed: Int) {
        var sentences: [String] = []
        var current = ""
        let chars = Array(text)
        for (i, c) in chars.enumerated() {
            current.append(c)
            let isEnd = c == "." || c == "!" || c == "?" || c == "\n"
            if isEnd, c == ".", i + 1 < chars.count, !chars[i + 1].isWhitespace { continue }
            if isEnd { sentences.append(current); current = "" }
        }
        if !current.isEmpty { sentences.append(current) }
        var kept: [String] = []
        var removed = 0
        for s in sentences {
            if mentionsInvented(s, allowed: allowed) { removed += 1 } else { kept.append(s) }
        }
        let out = kept.joined().trimmingCharacters(in: .whitespacesAndNewlines)
        return (out.isEmpty ? nil : out, removed)
    }
}
