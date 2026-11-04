import CoreGraphics

enum BoardChecksum {
    static func compute(_ image: CGImage, board: BoardRect) -> UInt64? {
        guard image.bitsPerPixel == 32, let data = image.dataProvider?.data, let ptr = CFDataGetBytePtr(data) else { return nil }
        let bpr = image.bytesPerRow, w = image.width, h = image.height
        let s = board.square
        var hash: UInt64 = 0xcbf29ce484222325
        for r in 0..<8 { for c in 0..<8 {
            for fy in [0.25, 0.5, 0.75] { for fx in [0.25, 0.5, 0.75] {
                let x = min(max(Int(board.x + (Double(c) + fx) * s), 0), w - 1)
                let y = min(max(Int(board.y + (Double(r) + fy) * s), 0), h - 1)
                let p = ptr + y * bpr + x * 4
                for k in 0..<3 {
                    hash ^= UInt64(p[k] >> 2)
                    hash = hash &* 0x100000001b3
                }
            } }
        } }
        return hash
    }
}
