import Foundation

struct ScanResult {
    var board: BoardRect
    var features: [[SquareFeature]]
    var screen: [[Character?]]
    var looksLikeStart: Bool
}

enum BoardScanner {
    static func scan(_ img: RGBImage, templates: PieceTemplates) -> ScanResult? {
        guard let board = BoardDetector.detect(img) else { return nil }
        return analyze(img, board: board, templates: templates)
    }

    static func analyze(_ img: RGBImage, board: BoardRect, templates: PieceTemplates) -> ScanResult {
        var features: [[SquareFeature]] = []
        for r in 0..<8 {
            features.append((0..<8).map { c in SquareFeature.extract(img, board: board, col: c, row: r) })
        }
        var screen = features.map { row in row.map { templates.classify($0) } }
        for king in ["K", "k"] as [Character] where !screen.joined().contains(king) {
            var best: (r: Int, c: Int, d: Double)?
            for r in 0..<8 { for c in 0..<8 where screen[r][c] == nil && features[r][c].fill > PieceTemplates.occupiedFill {
                let d = templates.distance(features[r][c], to: king)
                if d < (best?.d ?? 0.9) { best = (r, c, d) }
            } }
            if let b = best { screen[b.r][b.c] = king }
        }
        return ScanResult(board: board, features: features, screen: screen,
                          looksLikeStart: PieceTemplates.looksLikeStart(features))
    }

    static func guessWhiteAtBottom(_ screen: [[Character?]]) -> Bool? {
        var wSum = 0.0, wN = 0.0, bSum = 0.0, bN = 0.0
        for r in 0..<8 { for c in 0..<8 {
            guard let p = screen[r][c] else { continue }
            let weight = (p == "K" || p == "k") ? 3.0 : 1.0
            if p.isUppercase { wSum += Double(r) * weight; wN += weight } else { bSum += Double(r) * weight; bN += weight }
        } }
        guard wN > 0, bN > 0 else { return nil }
        let diff = wSum / wN - bSum / bN
        if abs(diff) < 0.5 { return nil }
        return diff > 0
    }
}
