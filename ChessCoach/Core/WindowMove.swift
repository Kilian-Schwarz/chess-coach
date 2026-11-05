import CoreGraphics

enum WindowMove {
    enum Change: Equatable {
        case none
        case moved(dx: Double, dy: Double)
        case resized
    }

    static func change(from old: CGRect, to new: CGRect) -> Change {
        if abs(old.width - new.width) >= 1 || abs(old.height - new.height) >= 1 { return .resized }
        let dx = new.minX - old.minX, dy = new.minY - old.minY
        if abs(dx) < 1 && abs(dy) < 1 { return .none }
        return .moved(dx: dx, dy: dy)
    }
}
