import Foundation

@MainActor
extension AppModel {
    func stopIdleEngine() {
        guard let e = engine, Date().timeIntervalSince(lastAnalysisAt) > 120 else { return }
        e.stop()
        engine = nil
        Log.write("Stockfish stopped after 2 minutes without analysis")
    }

    func acquireEngine(priority: Bool) async {
        if !engineBusy { engineBusy = true; return }
        await withCheckedContinuation { engineWaiters.append((priority, $0)) }
    }

    func releaseEngine() {
        guard !engineWaiters.isEmpty else { engineBusy = false; return }
        let i = engineWaiters.firstIndex { $0.priority } ?? 0
        engineWaiters.remove(at: i).continuation.resume()
    }

    func analysis(_ fen: String, ms: Int? = nil, priority: Bool = true, forced: Bool = false) async -> EngineResult? {
        let wanted = ms ?? effectiveThinkMs
        func cached() -> EngineResult? {
            guard let c = analysisCache[fen], c.ms >= wanted || !priority else { return nil }
            return c.result
        }
        if let c = cached() { return c }
        guard isActive || forced, !failedFens.contains(fen) else { return nil }
        await acquireEngine(priority: priority)
        defer { releaseEngine() }
        if let c = cached() { return c }
        guard isActive || forced, let engine = ensureEngine() else { return nil }
        lastAnalysisAt = Date()
        let result = await engine.analyze(fen: fen, movetimeMs: wanted)
        lastAnalysisAt = Date()
        if let result {
            if analysisCache.count > 300 { analysisCache.removeAll() }
            analysisCache[fen] = CachedAnalysis(result: result, ms: wanted)
        } else if !Task.isCancelled {
            failedFens.insert(fen)
            Log.write("Engine gave no answer for position \(fen), not sending it again")
            engine.stop()
            self.engine = nil
        }
        if !isActive { scheduleIdleStop() }
        return result
    }

    func engineAnalysis(fen: String, ms: Int = 800) async -> EngineResult? {
        await analysis(fen, ms: ms, forced: true)
    }

    func scheduleIdleStop() {
        idleStopTask?.cancel()
        idleStopTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(125))
            guard !Task.isCancelled, let self, !self.isActive else { return }
            self.lastAnalysisAt = .distantPast
            self.stopIdleEngine()
        }
    }

    func ensureEngine() -> StockfishEngine? {
        if let engine, engine.isRunning { return engine }
        guard let path = StockfishEngine.findBinary() else {
            status = String(localized: "Stockfish not found")
            suggestion = String(localized: "Install it with: brew install stockfish")
            return nil
        }
        do { engine = try StockfishEngine(path: path) }
        catch { status = String(localized: "Stockfish does not start"); suggestion = error.localizedDescription; return nil }
        return engine
    }
}
