import AppKit
import SwiftUI

struct OverlayState: Equatable {
    struct Arrow: Equatable { var from: (col: Int, row: Int); var to: (col: Int, row: Int); var partner = false
        static func == (a: Arrow, b: Arrow) -> Bool { a.from == b.from && a.to == b.to && a.partner == b.partner } }
    struct Square: Equatable, Hashable { var col: Int; var row: Int }
    var arrow: Arrow?
    var threatArrow: Arrow?
    var highlight: Square?
    var unsafe: [Square] = []
    var partnerHighlight = false
    var label: String?
    var hint: String?
}

@MainActor
final class OverlayController {
    private var panel: NSPanel?
    private let model = OverlayModel()

    func show(boardInScreen rect: CGRect, state: OverlayState) {
        let panel = self.panel ?? makePanel()
        self.panel = panel
        let frame = rect.insetBy(dx: -4, dy: -4)
        if panel.frame != frame { panel.setFrame(frame, display: true) }
        if model.state != state { model.state = state }
        if !panel.isVisible { panel.orderFrontRegardless() }
    }

    func hide() {
        panel?.orderOut(nil)
    }

    private func makePanel() -> NSPanel {
        let p = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.ignoresMouseEvents = true
        p.level = .statusBar
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        p.contentView = NSHostingView(rootView: OverlayView(model: model))
        return p
    }
}

@MainActor
final class OverlayModel: ObservableObject {
    @Published var state = OverlayState()
}

struct OverlayView: View {
    @ObservedObject var model: OverlayModel

    var body: some View {
        GeometryReader { geo in
            let inset: CGFloat = 4
            let size = min(geo.size.width, geo.size.height) - 2 * inset
            let sq = size / 8
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 3)
                    .strokeBorder(Color.green.opacity(0.55), lineWidth: 2)

                ForEach(model.state.unsafe, id: \.self) { s in
                    SquareMark(color: .red, fill: 0.22, stroke: 0.5, line: max(1.5, sq * 0.03), size: sq)
                        .offset(x: inset + CGFloat(s.col) * sq, y: inset + CGFloat(s.row) * sq)
                }

                if let h = model.state.highlight {
                    let c = model.state.partnerHighlight ? Color.blue : Color.yellow
                    SquareMark(color: c, fill: 0.28, stroke: 0.95, line: max(2, sq * 0.06), size: sq)
                        .offset(x: inset + CGFloat(h.col) * sq, y: inset + CGFloat(h.row) * sq)
                }

                if let a = model.state.arrow {
                    let from = CGPoint(x: inset + (CGFloat(a.from.col) + 0.5) * sq, y: inset + (CGFloat(a.from.row) + 0.5) * sq)
                    let to = CGPoint(x: inset + (CGFloat(a.to.col) + 0.5) * sq, y: inset + (CGFloat(a.to.row) + 0.5) * sq)
                    ArrowShape(from: from, to: to, width: sq * 0.2)
                        .fill((a.partner ? Color.blue : Color.green).opacity(0.75))
                        .shadow(color: .black.opacity(0.35), radius: 2)
                    Circle()
                        .strokeBorder((a.partner ? Color.blue : Color.green).opacity(0.9), lineWidth: max(2, sq * 0.06))
                        .frame(width: sq * 0.9, height: sq * 0.9)
                        .position(from)
                }

                if let t = model.state.threatArrow {
                    let from = CGPoint(x: inset + (CGFloat(t.from.col) + 0.5) * sq, y: inset + (CGFloat(t.from.row) + 0.5) * sq)
                    let to = CGPoint(x: inset + (CGFloat(t.to.col) + 0.5) * sq, y: inset + (CGFloat(t.to.row) + 0.5) * sq)
                    ArrowShape(from: from, to: to, width: sq * 0.16)
                        .fill(Color.red.opacity(0.7))
                        .shadow(color: .black.opacity(0.35), radius: 2)
                }

                if let text = model.state.label ?? model.state.hint {
                    Text(text)
                        .font(.headline)
                        .monospacedDigit()
                        .foregroundStyle(.white)
                        .padding(.horizontal, 8)
                        .padding(.vertical, 4)
                        .background(Color.black.opacity(0.7), in: Capsule())
                        .padding(inset + 6)
                }
            }
        }
        .allowsHitTesting(false)
    }
}

struct ArrowShape: Shape {
    var from: CGPoint
    var to: CGPoint
    var width: CGFloat

    func path(in rect: CGRect) -> Path {
        let dx = to.x - from.x, dy = to.y - from.y
        let len = max(sqrt(dx * dx + dy * dy), 1)
        let ux = dx / len, uy = dy / len
        let nx = -uy, ny = ux
        let head = min(width * 2.4, len * 0.6)
        let start = CGPoint(x: from.x + ux * width * 1.2, y: from.y + uy * width * 1.2)
        let neck = CGPoint(x: to.x - ux * head, y: to.y - uy * head)
        let w = width / 2
        var p = Path()
        p.move(to: CGPoint(x: start.x + nx * w, y: start.y + ny * w))
        p.addLine(to: CGPoint(x: neck.x + nx * w, y: neck.y + ny * w))
        p.addLine(to: CGPoint(x: neck.x + nx * w * 2.6, y: neck.y + ny * w * 2.6))
        p.addLine(to: to)
        p.addLine(to: CGPoint(x: neck.x - nx * w * 2.6, y: neck.y - ny * w * 2.6))
        p.addLine(to: CGPoint(x: neck.x - nx * w, y: neck.y - ny * w))
        p.addLine(to: CGPoint(x: start.x - nx * w, y: start.y - ny * w))
        p.closeSubpath()
        return p
    }
}

private struct SquareMark: View {
    let color: Color
    let fill: Double
    let stroke: Double
    let line: CGFloat
    let size: CGFloat

    var body: some View {
        Rectangle()
            .fill(color.opacity(fill))
            .overlay(Rectangle().strokeBorder(color.opacity(stroke), lineWidth: line))
            .frame(width: size, height: size)
    }
}
