import AppKit
import SwiftUI

@MainActor
extension AppModel {
    func nextDelay() -> Duration {
        if activity.isPaused { return .milliseconds(1500) }
        if tracked == nil {
            let lost = Date().timeIntervalSince(lostSince ?? Date())
            return .milliseconds(lost > 120 ? 4000 : lost > 30 ? 2000 : 1000)
        }
        let calm = Date().timeIntervalSince(lastChangeAt)
        if calm < 3 { return .milliseconds(400) }
        return .milliseconds(Int(min(1500, 400 + (calm - 3) / 12 * 1100)))
    }

    func locateBoard() async throws -> Located? {
        let currentTemplates = templates
        if let tr = tracked {
            let m = tr.rect.width * 0.1
            if let d = try await ScreenCapture.captureRegion(displayID: tr.displayID, frame: tr.frame,
                                                             rect: tr.rect.insetBy(dx: -m, dy: -m)) {
                let expected = BoardRect(x: tr.rect.minX - d.origin.x, y: tr.rect.minY - d.origin.y, size: tr.rect.width)
                let sum = BoardChecksum.compute(d.image, board: expected)
                if let sum, sum == lastChecksum, var cached = cachedLocated {
                    cached.unchanged = true
                    return cached
                }
                let img = d.image
                let scan: ScanResult? = await Task.detached(priority: .userInitiated) {
                    guard let rgb = RGBImage(cgImage: img) else { return nil }
                    return BoardScanner.scan(rgb, templates: currentTemplates)
                }.value
                if let scan {
                    let b = scan.board
                    tracked = (tr.displayID, tr.frame, CGRect(x: d.origin.x + b.x, y: d.origin.y + b.y, width: b.size, height: b.size))
                    lastChecksum = BoardChecksum.compute(d.image, board: b)
                    let loc = Located(display: d, scan: scan, scale: 1, unchanged: false)
                    cachedLocated = loc
                    return loc
                }
                Log.write("Board area lost, searching the full screen")
            }
            tracked = nil
        }

        let displays = try await ScreenCapture.captureDisplays(preferred: tracked?.displayID ?? lastDisplay)
        let found: Located? = await Task.detached(priority: .userInitiated) {
            for d in displays {
                guard let img = RGBImage(cgImage: d.image),
                      let res = BoardScanner.scan(img, templates: currentTemplates) else { continue }
                return Located(display: d, scan: res, scale: d.frame.width / Double(img.width), unchanged: false)
            }
            return nil
        }.value
        if let f = found {
            let b = f.scan.board
            tracked = (f.display.displayID, f.display.frame,
                       CGRect(x: b.x * f.scale, y: b.y * f.scale, width: b.size * f.scale, height: b.size * f.scale))
            lastChecksum = f.scale == 1 ? BoardChecksum.compute(f.display.image, board: b) : nil
            cachedLocated = f
        } else {
            tracked = nil
            cachedLocated = nil
            lastChecksum = nil
        }
        return found
    }

    func tick() async {
        if activity.isPaused {
            if !pausedShown {
                pausedShown = true
                hideAll()
                tracked = nil
                dropCapture()
                status = String(localized: "Paused (\(activity.reason))")
                suggestion = nil
                invalidate()
            }
            return
        }
        pausedShown = false

        guard ScreenCapture.hasPermission else {
            needsPermission = true
            status = String(localized: "Screen recording missing")
            suggestion = nil
            hideAll()
            return
        }
        needsPermission = false

        let located: Located?
        do { located = try await locateBoard() }
        catch {
            Log.write("Screen capture failed: \(error.localizedDescription)")
            status = String(localized: "Screen capture failed")
            suggestion = error.localizedDescription
            hideAll()
            tracked = nil
            return
        }
        guard isActive else { return }

        guard let loc = located else {
            if lostSince == nil { lostSince = Date() }
            if let lost = lostSince, Date().timeIntervalSince(lost) > 60 { finishGame() }
            status = String(localized: "Looking for a chessboard …")
            suggestion = nil
            hideAll()
            pendingScreen = nil
            settled = false
            return
        }
        lostSince = nil
        if loc.unchanged && settled { return }

        let display = loc.display, scan = loc.scan, scale = loc.scale
        if lastScreen != scan.screen { lastScreen = scan.screen; lastChangeAt = Date() }
        lastDisplay = display.displayID
        let boardRect = cocoaRect(for: scan.board, on: display, scale: scale)
        lastBoardRect = boardRect

        if scan.looksLikeStart {
            let wb = templates.isEmpty ? nil : BoardScanner.guessWhiteAtBottom(scan.screen)
            let expected = PieceTemplates.startLayout(whiteAtBottom: wb ?? true)
            let recognised = (0..<8).allSatisfy { r in (0..<8).allSatisfy { c in scan.screen[r][c] == (expected[r].isEmpty ? nil : expected[r][c]) } }
            if !recognised {
                var t = PieceTemplates()
                autoWhiteAtBottom = t.learnStart(scan.features)
                templates = t
                Self.saveTemplates(t)
                pendingScreen = nil
                stablePlacement = nil
                dropCapture()
                status = String(localized: "Pieces learned")
                return
            }
        }

        guard !templates.isEmpty else {
            status = String(localized: "Board found, pieces still unknown")
            suggestion = String(localized: "Show a start position once to learn the pieces")
            overlay.show(boardInScreen: boardRect, state: OverlayState(hint: String(localized: "Show the start position to learn the pieces")))
            settled = true
            return
        }

        guard pendingScreen == scan.screen else {
            pendingScreen = scan.screen
            dropCapture()
            return
        }

        let whiteAtBottom: Bool
        switch side {
        case .white: whiteAtBottom = true
        case .black: whiteAtBottom = false
        case .auto:
            if let g = BoardScanner.guessWhiteAtBottom(scan.screen) { autoWhiteAtBottom = g }
            whiteAtBottom = autoWhiteAtBottom ?? true
        }
        let me: PieceColor = whiteAtBottom ? .white : .black
        lastMe = me
        let placement = Placement(screen: scan.screen, whiteAtBottom: whiteAtBottom)

        if placement != stablePlacement {
            if placement.isStartPosition, !record.moves.isEmpty { finishGame() }
            let marking = SideToMove.detect(scan: scan, whiteAtBottom: whiteAtBottom, templates: templates)
            let update = record.update(placement: placement, whiteAtBottom: whiteAtBottom, hintedMover: marking.mover)
            stablePlacement = placement
            sideOverride = nil
            suggestionCard = nil
            answerCard = nil
            threatCard = nil
            tasks[.suggestion]?.cancel()
            questionTask?.cancel()
            ideaTask?.cancel()
            ideaFen = nil
            handle(update)
            for played in update.newMoves where isCoached(played.mover, me: me) {
                await reviewOwnMove(played, me: me)
            }
        }
        guard var pos = record.current, isActive else { return }

        if let o = sideOverride, o != pos.turn { pos = flipped(pos, to: o) }
        else if effectiveAlwaysMyTurn && !duoTraining && pos.turn != me { pos = flipped(pos, to: me) }

        switch pos.result {
        case .checkmate?, .stalemate?: finishGame()
        default: break
        }

        let toMove = pos.turn
        guard isCoached(toMove, me: me) else {
            showNotice(String(localized: "You play \(me.title()) · opponent to move"), overlay: String(localized: "Opponent to move"), board: boardRect)
            return
        }
        let partner = toMove != me
        let who = playerName(toMove, me: me)

        let fen = pos.fen
        if let problem = pos.board.problem(sideToMove: toMove) {
            if fen != lastProblemFen { lastProblemFen = fen; Log.write("Position not checkable (\(problem)): \(fen)") }
            showNotice("\(who) · \(problem)", overlay: problem, board: boardRect)
            return
        }

        guard let result = await analysis(fen) else { return }
        guard isActive, stablePlacement == placement else { return }

        guard UCIMove(result.bestMove) != nil else {
            let text = pos.board.isInCheck(toMove) ? Score.checkmate.text() : Score.stalemate.text()
            showNotice("\(who) · \(text)", overlay: text, board: boardRect)
            return
        }

        var nullResult: EngineResult?
        var threat: Threats.Threat?
        if let nullFen = Threats.nullMoveFEN(pos) {
            nullResult = await analysis(nullFen, ms: 300)
            guard isActive, stablePlacement == placement else { return }
            if let nr = nullResult { threat = Threats.detect(in: pos, current: result, opponent: nr) }
        }
        let profileId = profileId(for: toMove, me: me)
        guard let hints = Hints.build(placement: pos.board, player: toMove, result: result, threatResult: nullResult) else { return }

        status = partner ? String(localized: "You play \(me.title()) · partner to move") : String(localized: "You play \(me.title()) · you to move")
        let ctx = HintContext(position: pos, toMove: toMove, me: me, whiteAtBottom: whiteAtBottom, result: result,
                              hints: hints, threat: threat, fen: fen, boardRect: boardRect, who: who, partner: partner,
                              profileId: profileId, text: pos.board.describeMove(hints.move), score: result.scoreText)
        if hintFen != fen { hintFen = fen; hintLevel = 0 }
        if effectiveShowBest { usedHint[fen] = 4 }
        hintCtx = ctx
        panel.model.canHint = !effectiveShowBest
        renderHints(ctx)
        prefetchIdea(ctx)
        settled = true
    }

    func showNotice(_ text: String, overlay hint: String, board: CGRect) {
        status = text
        suggestion = nil
        clearHints()
        overlay.show(boardInScreen: board, state: OverlayState(hint: hint))
        settled = true
        updatePanelCards()
    }

    func dropCapture() {
        cachedLocated = nil
        lastChecksum = nil
    }

    func flipped(_ pos: ChessPosition, to color: PieceColor) -> ChessPosition {
        var p = pos
        p.turn = color
        p.enPassantFile = nil
        return p
    }

    func profileId(for color: PieceColor, me: PieceColor) -> String? {
        if color == me { return store.activeId }
        return duoTraining ? store.partnerId : nil
    }

    func isCoached(_ color: PieceColor, me: PieceColor) -> Bool { duoTraining || color == me }

    func playerName(_ color: PieceColor, me: PieceColor) -> String {
        guard duoTraining else { return String(localized: "You") }
        return color == me ? String(localized: "\(color.title()) (you)") : String(localized: "\(color.title()) (partner)")
    }

    func cocoaRect(for b: BoardRect, on d: CapturedDisplay, scale: Double) -> CGRect {
        let qx = d.frame.minX + d.origin.x + b.x * scale
        let qy = d.frame.minY + d.origin.y + b.y * scale
        let size = b.size * scale
        let primaryHeight = NSScreen.screens.first?.frame.height ?? d.frame.height
        return CGRect(x: qx, y: primaryHeight - qy - size, width: size, height: size)
    }
}
