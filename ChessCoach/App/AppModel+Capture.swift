import AppKit
import SwiftUI

@MainActor
extension AppModel {
    func nextDelay() -> Duration {
        if activity.isPaused { return .milliseconds(1500) }
        if confirmSoon, tracked != nil { return .milliseconds(200) }
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
            if let loc = try await locateInRegion(tr, templates: currentTemplates) { return loc }
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
        regionMisses = 0
        if let f = found {
            let b = f.scan.board
            boardStabilizer.reset()
            let stable = boardStabilizer.accept(BoardRect(x: b.x * f.scale, y: b.y * f.scale, size: b.size * f.scale)).rect
            tracked = (f.display.displayID, f.display.frame, CGRect(x: stable.x, y: stable.y, width: stable.size, height: stable.size))
            lastChecksum = f.scale == 1 ? BoardChecksum.compute(f.display.image, board: b) : nil
            cachedLocated = f
            Log.write("Board found on the full screen: \(Int(stable.size)) px at \(Int(stable.x)),\(Int(stable.y))")
        } else {
            tracked = nil
            cachedLocated = nil
            lastChecksum = nil
        }
        return found
    }

    private func locateInRegion(_ tr: (displayID: CGDirectDisplayID, frame: CGRect, rect: CGRect), templates currentTemplates: PieceTemplates) async throws -> Located? {
        var reason = "no capture"
        var lastCrop: (image: CGImage, expected: BoardRect)?
        for factor in [0.12, 0.3] {
            let m = max(tr.rect.width * factor, 40)
            guard let d = try await ScreenCapture.captureRegion(displayID: tr.displayID, frame: tr.frame,
                                                                rect: tr.rect.insetBy(dx: -m, dy: -m)) else {
                reason = "region capture unavailable for \(Int(tr.rect.minX)),\(Int(tr.rect.minY)) \(Int(tr.rect.width)) px"
                break
            }
            let expected = BoardRect(x: tr.rect.minX - d.origin.x, y: tr.rect.minY - d.origin.y, size: tr.rect.width)
            let sum = BoardChecksum.compute(d.image, board: expected)
            if factor == 0.12, let sum, sum == lastChecksum, var cached = cachedLocated {
                cached.unchanged = true
                regionMisses = 0
                return cached
            }
            let img = d.image
            lastCrop = (img, expected)
            let scan: ScanResult? = await Task.detached(priority: .userInitiated) {
                guard let rgb = RGBImage(cgImage: img) else { return nil }
                return BoardScanner.scan(rgb, templates: currentTemplates)
            }.value
            if let scan {
                let b = scan.board
                let stable = boardStabilizer.accept(BoardRect(x: d.origin.x + b.x, y: d.origin.y + b.y, size: b.size))
                if stable.changed { Log.write("Board moved or resized: \(Int(stable.rect.size)) px at \(Int(stable.rect.x)),\(Int(stable.rect.y))") }
                let r = stable.rect
                tracked = (tr.displayID, tr.frame, CGRect(x: r.x, y: r.y, width: r.size, height: r.size))
                lastChecksum = BoardChecksum.compute(d.image, board: BoardRect(x: r.x - d.origin.x, y: r.y - d.origin.y, size: r.size))
                let loc = Located(display: d, scan: scan, scale: 1, unchanged: false)
                cachedLocated = loc
                regionMisses = 0
                return loc
            }
            reason = "no board in a \(d.image.width)x\(d.image.height) crop (margin \(Int(factor * 100)) %)"
        }
        missView = .gone
        if let crop = lastCrop, let colors = boardColors {
            let feats: [[SquareFeature]]? = await Task.detached(priority: .userInitiated) {
                guard let rgb = RGBImage(cgImage: crop.image) else { return nil }
                return BoardScanner.analyze(rgb, board: crop.expected, templates: PieceTemplates()).features
            }.value
            if let feats, BoardReadability.ringMatch(feats, colors) >= BoardReadability.ringShare { missView = .covered }
        }
        if missView == .covered, var cached = cachedLocated {
            regionMisses = max(regionMisses, 1)
            cached.unchanged = true
            return cached
        }
        regionMisses += 1
        if regionMisses < 3, var cached = cachedLocated {
            cached.unchanged = true
            return cached
        }
        Log.write("Board area lost, searching the full screen: \(reason), \(regionMisses) misses in a row")
        return nil
    }

    func tick() async {
        if activity.isPaused {
            if !pausedShown {
                pausedShown = true
                hideAll()
                tracked = nil
                boardStabilizer.reset()
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
            observeEnd(.gone)
            status = String(localized: "Looking for a chessboard …")
            suggestion = nil
            hideAll()
            pendingScreen = nil
            settled = false
            return
        }
        lostSince = nil
        if regionMisses > 0 { observeEnd(missView); return }
        if loc.unchanged && settled && pendingReview == nil {
            if lastView != .readable { observeEnd(lastView) }
            return
        }

        let display = loc.display, scan = loc.scan, scale = loc.scale
        if lastScreen != scan.screen { lastScreen = scan.screen; lastChangeAt = Date() }
        lastDisplay = display.displayID
        lastView = BoardReadability.view(scan.features, colors: boardColors)
        if lastView != .readable { observeEnd(lastView); return }
        boardColors = BoardColors.learn(scan.features)
        let local = boardStabilizer.accept(BoardRect(x: display.origin.x + scan.board.x * scale, y: display.origin.y + scan.board.y * scale,
                                                     size: scan.board.size * scale)).rect
        let boardRect = cocoaRect(for: BoardRect(x: local.x - display.origin.x, y: local.y - display.origin.y, size: local.size),
                                  on: display, scale: 1)
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

        if templates.isEmpty && !scan.looksLikeStart {
            var t = PieceTemplates()
            if let wb = t.learnEarly(scan.features) {
                autoWhiteAtBottom = wb
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
            confirmSoon = tracked != nil
            dropCapture()
            return
        }
        confirmSoon = false

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
        updateLift(placement: placement, me: me, whiteAtBottom: whiteAtBottom)

        if placement != stablePlacement {
            if placement.isStartPosition { observeEnd(.readable, isStart: true) }
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
            let carried = pendingReview?.moves.filter { inRecord($0) } ?? []
            pendingReview = nil
            for line in update.log { Log.write(line) }
            handle(update)
            let coached = update.reviewable.filter { isCoached($0.mover, me: me) }
            let carriedNow = carried.filter { inRecord($0) }
            if !carriedNow.isEmpty { startReview(carriedNow, me: me) }
            if !coached.isEmpty {
                let marked = Set(marking.marked.map { UCIMove.squareName($0.file, $0.rank) })
                if coached.allSatisfy({ marked.isSuperset(of: [String($0.uci.prefix(2)), String($0.uci.dropFirst(2).prefix(2))]) }) {
                    startReview(coached, me: me)
                } else {
                    pendingReview = (coached, marked)
                }
            }
        } else if let pending = pendingReview {
            pendingReview = nil
            let still = pending.moves.filter { inRecord($0) }
            if !still.isEmpty { startReview(still, me: me) }
        }
        guard var pos = record.current, isActive else { return }

        if let o = sideOverride, o != pos.turn { pos = flipped(pos, to: o) }
        else if effectiveAlwaysMyTurn && !duoTraining && pos.turn != me { pos = flipped(pos, to: me) }

        observeEnd(.readable, result: record.current?.result)

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
