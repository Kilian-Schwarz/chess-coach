import AppKit
let a = CommandLine.arguments
guard a.count == 4, let px = Int(a[2]), let img = NSImage(contentsOfFile: a[1]) else { FileHandle.standardError.write(Data("usage: render.swift svg px out.png\n".utf8)); exit(1) }
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
rep.size = NSSize(width: px, height: px)
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
NSGraphicsContext.current?.imageInterpolation = .high
NSColor.clear.set(); NSRect(x: 0, y: 0, width: px, height: px).fill(using: .copy)
img.draw(in: NSRect(x: 0, y: 0, width: px, height: px), from: .zero, operation: .sourceOver, fraction: 1)
NSGraphicsContext.restoreGraphicsState()
try rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: a[3]))
