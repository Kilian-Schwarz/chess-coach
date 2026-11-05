import AppKit
import SwiftUI

@MainActor
extension AppModel {
    static let lostGrace: TimeInterval = 2.5
    static let reviewStableFrames = 4
    static let maxUnclassified = 6
    static let stuckSeconds = 3.0
    static let regionMargin = 0.2
    static let windowRetrySeconds = 3.0
    static var mouseDown: Bool { NSEvent.pressedMouseButtons & 1 != 0 }

    func moveConfirmed(_ moves: [PlayedMove], marks: [(file: Int, rank: Int)]) -> Bool {
        guard !Self.mouseDown else { return false }
        let marked = Set(marks.map { UCIMove.squareName($0.file, $0.rank) })
        return moves.allSatisfy { marked.isSuperset(of: [String($0.uci.prefix(2)), String($0.uci.dropFirst(2).prefix(2))]) }
    }

    func nextDelay() -> Duration {
        .milliseconds(PollingSchedule.delayMs(paused: activity.isPaused, confirmSoon: confirmSoon, tracked: tracked != nil,
                                              lostFor: Date().timeIntervalSince(lostSince ?? Date()),
                                              calmFor: Date().timeIntervalSince(lastChangeAt),
                                              lowPower: ProcessInfo.processInfo.isLowPowerModeEnabled))
    }

    func locateBoard() async throws -> Located? {
        let currentTemplates = templates
        if let tr = tracked {
            if let loc = try await locateInRegion(tr, templates: currentTemplates) { return loc }
            tracked = nil
        }

        let started = Date()
        var scanMs = 0
        let found: Located? = try await ScreenCapture.firstDisplay(preferred: tracked?.displayID ?? lastDisplay) { d in
            let t0 = Date()
            let located: Located? = await Task.detached(priority: .userInitiated) {
                guard let img = RGBImage(cgImage: d.image),
                      let res = BoardScanner.scan(img, templates: currentTemplates) else { return nil }
                return Located(display: d, scan: res, scale: d.frame.width / Double(img.width), unchanged: false)
            }.value
            scanMs += Int(Date().timeIntervalSince(t0) * 1000)
            return located
        }
        let totalMs = Int(Date().timeIntervalSince(started) * 1000)
        regionMisses = 0
        if let f = found {
            let b = f.scan.board
            boardStabilizer.reset()
            relearnGuard.reset()
            let stable = boardStabilizer.accept(BoardRect(x: b.x * f.scale, y: b.y * f.scale, size: b.size * f.scale)).rect
            tracked = (f.display.displayID, f.display.frame, CGRect(x: stable.x, y: stable.y, width: stable.size, height: stable.size))
            lastChecksum = f.scale == 1 ? BoardChecksum.compute(f.display.image, board: b) : nil
            cachedLocated = f
            verifyFailedSince = nil
            lostLogged = false
            trackedWindow = nil
            windowCapture = false
            windowAttemptAt = .distantPast
            Log.write("Board found on the full screen: \(Int(stable.size)) px at \(Int(stable.x)),\(Int(stable.y)) in \(totalMs) ms (detection \(scanMs) ms)")
        } else {
            tracked = nil
            cachedLocated = nil
            lastChecksum = nil
            if totalMs > 300 { Log.write("Full-screen search without a board took \(totalMs) ms (detection \(scanMs) ms)") }
        }
        return found
    }

    private func locateInRegion(_ tr: (displayID: CGDirectDisplayID, frame: CGRect, rect: CGRect), templates currentTemplates: PieceTemplates) async throws -> Located? {
        if trackedWindow == nil, boardColors != nil, Date().timeIntervalSince(windowAttemptAt) > Self.windowRetrySeconds { await resolveWindow(tr) }
        let m = max(tr.rect.width * Self.regionMargin, 40)
        let wanted = tr.rect.insetBy(dx: -m, dy: -m)
        var captured: CapturedDisplay?
        if let w = trackedWindow {
            captured = try? await ScreenCapture.captureWindowRegion(w, displayID: tr.displayID, displayFrame: tr.frame, rect: wanted)
            if captured == nil { trackedWindow = nil; windowAttemptAt = .distantPast }
        }
        windowCapture = captured != nil
        if captured == nil { captured = try await ScreenCapture.captureRegion(displayID: tr.displayID, frame: tr.frame, rect: wanted) }
        guard let d = captured else {
            return regionFailed("region capture unavailable", image: nil)
        }
        let expected = BoardRect(x: tr.rect.minX - d.origin.x, y: tr.rect.minY - d.origin.y, size: tr.rect.width)
        if let sum = BoardChecksum.compute(d.image, board: expected), sum == lastChecksum, var cached = cachedLocated {
            cached.unchanged = true
            regionMisses = 0
            verifyFailedSince = nil
            return cached
        }
        let img = d.image, colors = boardColors, origin = (x: Double(d.origin.x), y: Double(d.origin.y))
        var stabilizer = boardStabilizer
        let result: RegionObserver.Result? = await Task.detached(priority: .userInitiated) {
            guard let rgb = RGBImage(cgImage: img) else { return nil }
            return RegionObserver.observe(rgb, origin: origin, expected: expected, templates: currentTemplates, colors: colors, stabilizer: &stabilizer)
        }.value
        boardStabilizer = stabilizer
        func accept(_ scan: ScanResult, rect r: BoardRect) -> Located {
            tracked = (tr.displayID, tr.frame, CGRect(x: r.x, y: r.y, width: r.size, height: r.size))
            lastChecksum = BoardChecksum.compute(d.image, board: BoardRect(x: r.x - d.origin.x, y: r.y - d.origin.y, size: r.size))
            let loc = Located(display: d, scan: scan, scale: 1, unchanged: false)
            cachedLocated = loc
            regionMisses = 0
            verifyFailedSince = nil
            return loc
        }
        switch result {
        case .scanned(let scan, let r, let changed)?:
            if changed {
                rectUnsettled = true
                let line = "Board moved or resized: \(Int(r.size)) px at \(Int(r.x)),\(Int(r.y))"
                Log.write(line)
                FrameDebug.save(d.image, reason: line)
            }
            return accept(scan, rect: BoardRect(x: r.x, y: r.y, size: r.size))
        case .verified(let scan)?:
            return accept(scan, rect: BoardRect(x: tr.rect.minX, y: tr.rect.minY, size: tr.rect.width))
        case .covered?:
            missView = .covered
            regionMisses = max(regionMisses, 1)
            verifyFailedSince = nil
            if !windowCapture { return regionFailed("board covered, not captured from its own window", image: d.image) }
            if var cached = cachedLocated { cached.unchanged = true; return cached }
            return regionFailed("covered without a cached board", image: d.image)
        case .failed(let reason)?:
            if windowCapture { trackedWindow = nil; windowAttemptAt = .distantPast }
            return regionFailed(reason, image: d.image)
        case nil:
            return regionFailed("crop unreadable", image: d.image)
        }
    }

    private func resolveWindow(_ tr: (displayID: CGDirectDisplayID, frame: CGRect, rect: CGRect)) async {
        windowAttemptAt = Date()
        guard let colors = boardColors, let candidates = try? await ScreenCapture.windowCandidates(displayFrame: tr.frame, rect: tr.rect) else { return }
        let expectedLocal = tr.rect
        for w in candidates {
            guard let d = try? await ScreenCapture.captureWindowRegion(w, displayID: tr.displayID, displayFrame: tr.frame, rect: expectedLocal),
                  abs(Double(d.origin.x) - expectedLocal.minX) < 2, abs(Double(d.origin.y) - expectedLocal.minY) < 2, Double(d.image.width) >= expectedLocal.width - 2 else { continue }
            let img = d.image, size = tr.rect.width
            let verdict: BoardReadability.Verification = await Task.detached(priority: .userInitiated) {
                guard let rgb = RGBImage(cgImage: img) else { return .failed }
                let scan = BoardScanner.analyze(rgb, board: BoardRect(x: 0, y: 0, size: size), templates: PieceTemplates())
                return BoardReadability.verify(scan.features, colors: colors)
            }.value
            if verdict == .matched {
                trackedWindow = w
                Log.write("Board window resolved: \(w.owningApplication?.applicationName ?? "?"), \(Int(w.frame.width))x\(Int(w.frame.height))")
                return
            }
        }
        trackedWindow = nil
    }

    private func regionFailed(_ reason: String, image: CGImage?) -> Located? {
        missView = .gone
        if verifyFailedSince == nil { verifyFailedSince = Date() }
        let failedFor = Date().timeIntervalSince(verifyFailedSince ?? Date())
        if let image { FrameDebug.save(image, reason: "Region miss: \(reason), verification failed for \(String(format: "%.1f", failedFor)) s") }
        if failedFor < Self.lostGrace, var cached = cachedLocated {
            regionMisses = max(regionMisses, 1)
            cached.unchanged = true
            return cached
        }
        Log.write("Board area lost, searching the full screen: \(reason), verification failed for \(String(format: "%.1f", failedFor)) s")
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
        do { located = try await locateBoard(); captureFailedSince = nil }
        catch {
            if captureFailedSince == nil { captureFailedSince = Date() }
            Log.write("Screen capture failed: \(error.localizedDescription)")
            guard Date().timeIntervalSince(captureFailedSince ?? Date()) >= Self.lostGrace else { return }
            status = String(localized: "Screen capture failed")
            suggestion = error.localizedDescription
            hideAll()
            tracked = nil
            return
        }
        guard isActive else { return }

        guard let loc = located else {
            if lostSince == nil { lostSince = verifyFailedSince ?? Date() }
            if lastBoardRect != nil, Date().timeIntervalSince(lostSince ?? Date()) < Self.lostGrace { return }
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
        boardPresent = true
        if rectUnsettled { rectUnsettled = false; relearnGuard.reset() }

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

        let badFit = !scan.looksLikeStart && (templates.isEmpty || templates.fitsBadly(scan))
        let stableRect = BoardRect(x: local.x, y: local.y, size: local.size)
        let relearnDue = relearnGuard.observe(bad: badFit && !Self.mouseDown, rect: stableRect, required: templates.isEmpty ? 1 : RelearnGuard.badFrames)
        if relearnDue, relearnTried != scan.screen {
            relearnTried = scan.screen
            let why = "bad fit on \(relearnGuard.count) frames at \(Int(local.size)) px at \(Int(local.x)),\(Int(local.y)), \(record.moves.count) plies tracked"
            var t = PieceTemplates()
            if !templates.isEmpty, let tracked = record.current, let wb = t.learnKnown(scan.features, board: tracked.board, preferWhiteAtBottom: autoWhiteAtBottom) {
                autoWhiteAtBottom = wb
                templates = t
                Self.saveTemplates(t)
                pendingScreen = nil
                stablePlacement = nil
                dropCapture()
                status = String(localized: "Pieces learned")
                let line = "Pieces relearned from the tracked position (\(why)): \(tracked.fen)"
                Log.write(line)
                FrameDebug.save(display.image, reason: line)
                return
            }
            if record.moves.count <= EarlyStart.maxPlies, let found = t.learnEarlyPosition(scan.features) {
                autoWhiteAtBottom = found.whiteAtBottom
                templates = t
                Self.saveTemplates(t)
                record.seed(found.position)
                pendingScreen = nil
                stablePlacement = nil
                dropCapture()
                status = String(localized: "Pieces learned")
                let line = "Pieces relearned from an early position (\(why)): \(found.position.fen)"
                Log.write(line)
                FrameDebug.save(display.image, reason: line)
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

        let unclassified = zip(scan.features.joined(), scan.screen.joined()).filter { $0.0.fill > PieceTemplates.occupiedFill && $0.1 == nil }.count
        if unclassified > Self.maxUnclassified {
            status = Loc.display("problem.kings", "Kings not clearly recognised")
            suggestion = nil
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
            let resolved = orientation.resolve(guess: BoardScanner.guessWhiteAtBottom(scan.screen), screen: scan.screen, isStart: scan.looksLikeStart)
            if let reason = resolved.reason { Log.write("Orientation changed to \(resolved.whiteAtBottom ? "white" : "black") at the bottom: \(reason)") }
            whiteAtBottom = resolved.whiteAtBottom
        }
        let me: PieceColor = whiteAtBottom ? .white : .black
        lastMe = me
        let placement = Placement(screen: scan.screen, whiteAtBottom: whiteAtBottom)
        updateLift(placement: placement, me: me, whiteAtBottom: whiteAtBottom)

        if Self.mouseDown {
            mouseHeld = true
            mismatchSince = nil
            return
        }
        if mouseHeld {
            mouseHeld = false
            pendingScreen = nil
            confirmSoon = true
            return
        }

        if placement == stablePlacement, let cur = record.current, cur.board != placement {
            if mismatchSince == nil || Self.mouseDown { mismatchSince = Date() }
            else if Date().timeIntervalSince(mismatchSince ?? Date()) > Self.stuckSeconds {
                mismatchSince = nil
                forceResync = true
                stablePlacement = nil
                Log.write("Screen and record disagreed for \(Int(Self.stuckSeconds)) s, resyncing")
                return
            }
        } else {
            mismatchSince = nil
        }

        if placement != stablePlacement {
            if placement.isStartPosition { observeEnd(.readable, isStart: true) }
            let marking = SideToMove.detect(scan: scan, whiteAtBottom: whiteAtBottom, templates: templates)
            let update = record.update(placement: placement, whiteAtBottom: whiteAtBottom, hintedMover: marking.mover, forced: forceResync)
            forceResync = false
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
                if moveConfirmed(coached, marks: marking.marked) { startReview(coached, me: me) } else { pendingReview = (coached, 0) }
            }
        } else if var pending = pendingReview {
            pending.moves = pending.moves.filter { inRecord($0) }
            pending.frames = Self.mouseDown ? 0 : pending.frames + 1
            let marks = SideToMove.detect(scan: scan, whiteAtBottom: whiteAtBottom, templates: templates).marked
            if pending.moves.isEmpty {
                pendingReview = nil
            } else if moveConfirmed(pending.moves, marks: marks) || (pending.frames >= Self.reviewStableFrames && !Self.mouseDown) {
                pendingReview = nil
                startReview(pending.moves, me: me)
            } else {
                pendingReview = pending
            }
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
