import Foundation

enum RegionObserver {
    enum Result {
        case scanned(ScanResult, rect: BoardRect, changed: Bool)
        case verified(ScanResult)
        case covered
        case failed(String)
    }

    static func observe(_ crop: RGBImage, origin: (x: Double, y: Double), expected: BoardRect, templates: PieceTemplates,
                        colors: BoardColors?, stabilizer: inout RectStabilizer) -> Result {
        var reason = "no board in a \(crop.width)x\(crop.height) crop"
        if let scan = BoardScanner.scan(crop, templates: templates) {
            let b = scan.board
            let colorsMatch = colors.map { BoardReadability.matchShare(scan.features, $0) >= BoardReadability.verifyShare } ?? true
            let found = BoardRect(x: origin.x + b.x, y: origin.y + b.y, size: b.size)
            let stable = stabilizer.propose(found, matchesColors: colorsMatch)
            if stable.outcome != .pending { return .scanned(scan, rect: stable.rect, changed: stable.outcome == .changed) }
            reason = "rect change to \(Int(b.size)) px at \(Int(found.x)),\(Int(found.y)) not confirmed yet (colours match: \(colorsMatch))"
        }
        if expected.x < -1 || expected.y < -1 || expected.x + expected.size > Double(crop.width) + 1 || expected.y + expected.size > Double(crop.height) + 1 {
            return .failed(reason)
        }
        let atExpected = BoardScanner.analyze(crop, board: expected, templates: templates)
        switch BoardReadability.verify(atExpected.features, colors: colors) {
        case .matched: return .verified(atExpected)
        case .covered: return .covered
        case .failed: return .failed(reason)
        }
    }
}
