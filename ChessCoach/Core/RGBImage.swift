import CoreGraphics

struct RGBImage {
    let width: Int
    let height: Int
    let pixels: [UInt8]

    init?(cgImage: CGImage) {
        let w = cgImage.width, h = cgImage.height
        guard w > 0, h > 0 else { return nil }
        var buf = [UInt8](repeating: 0, count: w * h * 4)
        let ok: Bool = buf.withUnsafeMutableBytes { raw in
            guard let ctx = CGContext(data: raw.baseAddress, width: w, height: h,
                                      bitsPerComponent: 8, bytesPerRow: w * 4,
                                      space: CGColorSpaceCreateDeviceRGB(),
                                      bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return false }
            ctx.interpolationQuality = .none
            ctx.draw(cgImage, in: CGRect(x: 0, y: 0, width: w, height: h))
            return true
        }
        guard ok else { return nil }
        width = w; height = h; pixels = buf
    }

    @inline(__always)
    func rgb(_ x: Int, _ y: Int) -> (Int, Int, Int) {
        let xi = min(max(x, 0), width - 1), yi = min(max(y, 0), height - 1)
        let i = (yi * width + xi) * 4
        return (Int(pixels[i]), Int(pixels[i + 1]), Int(pixels[i + 2]))
    }
}

@inline(__always)
func colorDistance(_ a: (Int, Int, Int), _ b: (Int, Int, Int)) -> Int {
    abs(a.0 - b.0) + abs(a.1 - b.1) + abs(a.2 - b.2)
}
