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

    private static let attackVerbs = ["attack", "threat", "angriff", "angreif", "greift", "bedroh", "attaqu", "menac", "ataca", "amenaz",
                                      "attacc", "minacc", "aanval", "bedreig", "atak", "zagra", "atac", "ameaç", "напад", "угрож",
                                      "атак", "загрожу", "saldır", "tehdit", "攻撃", "狙", "攻击", "威胁", "공격", "위협"]
    private static let blockers = [" by ", " von ", "wird", " ist ", " sind ", " is ", " are ", " not ", "n't", "nicht", "kein", " no ", " pas ",
                                   " par ", " por ", " door ", "przez", " не ", "x-ray", "röntgen", "xray", "once ", "if ", "wenn", "falls", "sobald", "would"]
    private static let prepositions: Set<String> = ["on", "auf", "en", "sur", "su", "op", "na", "em", "на", "de", "à", "a", "in", "an", "bei", "sulla", "sul", "в"]

    private struct Mention {
        let kind: Character
        let square: String?
        let start: Int
        let end: Int
    }

    private static func squareToken(_ w: String) -> Bool {
        let c = Array(w)
        return c.count == 2 && "abcdefgh".contains(c[0]) && ("1"..."8").contains(c[1])
    }

    private static func mentions(_ sentence: String) -> (words: [(text: String, range: Range<String.Index>)], found: [Mention]) {
        var words: [(String, Range<String.Index>)] = []
        var start: String.Index?
        let lower = sentence.lowercased()
        for i in lower.indices {
            let c = lower[i]
            if c.isLetter || c.isNumber {
                if start == nil { start = i }
            } else if let s = start {
                words.append((String(lower[s..<i]), s..<i)); start = nil
            }
        }
        if let s = start { words.append((String(lower[s...]), s..<lower.endIndex)) }
        var found: [Mention] = []
        for (i, w) in words.enumerated() {
            guard let kind = names[w.0] else { continue }
            var sq: String?
            var end = i
            if i + 1 < words.count, squareToken(words[i + 1].0) { sq = words[i + 1].0; end = i + 1 }
            else if i + 2 < words.count, prepositions.contains(words[i + 1].0), squareToken(words[i + 2].0) { sq = words[i + 2].0; end = i + 2 }
            found.append(Mention(kind: kind, square: sq, start: i, end: end))
        }
        return (words, found)
    }

    static func contradictsAttacks(_ sentence: String, allowed: Set<String>) -> Bool {
        let parsed = mentions(sentence)
        let lower = sentence.lowercased()
        guard parsed.found.count >= 2 else { return false }
        for (a, b) in zip(parsed.found, parsed.found.dropFirst()) {
            guard let from = a.square, a.end + 1 < parsed.words.count, b.start > a.end else { continue }
            let lo = parsed.words[a.end].1.upperBound, hi = parsed.words[b.start].1.lowerBound
            guard lo <= hi else { continue }
            let between = " " + lower[lo..<hi] + " "
            guard attackVerbs.contains(where: { between.contains($0) }), !blockers.contains(where: { between.contains($0) }) else { continue }
            let key = "a:\(a.kind)\(from)>"
            let ok = b.square.map { allowed.contains(key + $0) } ?? allowed.contains(key + "~\(b.kind)")
            if !ok { return true }
        }
        return false
    }

    static func contradictsCheckClaim(_ sentence: String, allowed: Set<String>) -> Bool {
        for raw in sentence.split(whereSeparator: { $0.isWhitespace }) {
            var word = String(raw)
            while let l = word.last, ".,;:!?)\"”»".contains(l) { word.removeLast() }
            while let f = word.first, "(\"“«".contains(f) { word.removeFirst() }
            guard let last = word.last, last == "+" || last == "#" else { continue }
            let san = SAN.delocalize(word)
            guard san.range(of: "^[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](=[QRBN])?[+#]$", options: .regularExpression) != nil else { continue }
            if !allowed.contains("san:" + san) { return true }
        }
        return false
    }

    private static func addAttacks(_ board: Placement, to set: inout Set<String>) {
        for r in 0..<8 { for f in 0..<8 {
            guard let p = board.squares[r][f] else { continue }
            let kind = Character(p.lowercased()), from = UCIMove.squareName(f, r)
            for (tf, tr) in board.attacks(file: f, rank: r) {
                set.insert("a:\(kind)\(from)>\(UCIMove.squareName(tf, tr))")
                if let t = board.squares[tr][tf], Placement.color(of: t) != Placement.color(of: p) {
                    set.insert("a:\(kind)\(from)>~\(Character(t.lowercased()))")
                }
            }
        } }
    }

    static func allowed(positions: [ChessPosition], lines: [[String]]) -> Set<String> {
        var set = Set<String>()
        func add(_ pos: ChessPosition, children: Bool = false) {
            addAttacks(pos.board, to: &set)
            if children { for side in [pos, pos.nullMove] { for m in side.legalMoves() {
                addAttacks(side.board.applying(m), to: &set)
                set.insert("san:" + SAN.san(m, in: side))
            } } }
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
            add(pos, children: true)
            for pv in lines {
                var cur = pos
                for u in pv.prefix(6) {
                    guard let m = UCIMove(u), cur.isLegal(m) else { break }
                    set.insert("san:" + SAN.san(m, in: cur))
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
            if mentionsInvented(s, allowed: allowed) || contradictsAttacks(s, allowed: allowed) || contradictsCheckClaim(s, allowed: allowed) { removed += 1 } else { kept.append(s) }
        }
        let out = kept.joined().trimmingCharacters(in: .whitespacesAndNewlines)
        return (out.isEmpty ? nil : out, removed)
    }
}
