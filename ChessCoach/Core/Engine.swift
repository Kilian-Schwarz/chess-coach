import Foundation

enum Score: Equatable {
    case cp(Int), mate(Int), checkmate, stalemate

    func text(_ loc: Loc = .display) -> String {
        switch self {
        case .cp(let v): String(format: "%+.1f", Double(v) / 100)
        case .mate(0), .checkmate: loc("score.checkmate", "Checkmate")
        case .mate(let m) where m > 0: loc("score.mateIn", "Mate in \(m)")
        case .mate(let m): loc("score.matedIn", "mated in \(-m)")
        case .stalemate: loc("score.stalemate", "Stalemate")
        }
    }

    var negated: Score {
        switch self {
        case .cp(let v): .cp(-v)
        case .mate(let m): .mate(-m)
        default: self
        }
    }
}

struct EngineLine {
    let move: String
    let scoreCP: Int?
    let mateIn: Int?
    let pv: [String]

    var value: Int {
        if let m = mateIn { return m > 0 ? 10_000 - m * 10 : -10_000 - m * 10 }
        return scoreCP ?? 0
    }

    var score: Score { mateIn.map(Score.mate) ?? .cp(scoreCP ?? 0) }
    func score(_ loc: Loc = .display) -> String { score.text(loc) }
    var scoreText: String { score() }
}

struct EngineResult {
    let fen: String
    let bestMove: String
    let lines: [EngineLine]

    var scoreCP: Int? { lines.first?.scoreCP }
    var mateIn: Int? { lines.first?.mateIn }
    var value: Int { lines.first?.value ?? 0 }
    var score: Score { lines.first?.score ?? .cp(0) }
    func score(_ loc: Loc = .display) -> String { score.text(loc) }
    var scoreText: String { score() }
}

final class StockfishEngine: @unchecked Sendable {
    static let searchPaths = ["/opt/homebrew/bin/stockfish", "/usr/local/bin/stockfish", "/usr/bin/stockfish"]

    static func findBinary() -> String? {
        if let bundled = Bundle.main.path(forResource: "stockfish", ofType: nil),
           FileManager.default.isExecutableFile(atPath: bundled) { return bundled }
        return searchPaths.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    private let process = Process()
    private let input = Pipe()
    private let output = Pipe()
    private let queue = DispatchQueue(label: "stockfish.io")
    private var lineBuffer = ""
    private var lines: [Int: EngineLine] = [:]
    private var previous: [EngineLine] = []
    private var currentFen = ""
    private var completion: ((EngineResult?) -> Void)?
    private var ignoredBestMoves = 0
    private var searchID = 0

    var isRunning: Bool { process.isRunning }

    init(path: String) throws {
        process.executableURL = URL(fileURLWithPath: path)
        process.standardInput = input
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        process.terminationHandler = { [weak self] p in
            Log.write("Stockfish exited: status \(p.terminationStatus), reason \(p.terminationReason == .uncaughtSignal ? "signal" : "exit"), last position: \(self?.currentFen ?? "-")")
            self?.queue.async { self?.finish(nil) }
        }
        output.fileHandleForReading.readabilityHandler = { [weak self] h in
            let data = h.availableData
            guard let self, !data.isEmpty, let s = String(data: data, encoding: .utf8) else { return }
            self.queue.async { self.consume(s) }
        }
        try process.run()
        Log.write("Stockfish started (PID \(process.processIdentifier))")
        send("uci")
        send("setoption name Threads value \(max(1, min(2, ProcessInfo.processInfo.activeProcessorCount / 2)))")
        send("setoption name Hash value 32")
        send("setoption name MultiPV value 3")
        send("isready")
    }

    deinit { stop() }

    private func kill() {
        guard process.isRunning else { return }
        let pid = process.processIdentifier
        process.terminate()
        queue.asyncAfter(deadline: .now() + 1) {
            if self.process.isRunning { Darwin.kill(pid, SIGKILL) }
        }
    }

    func stop() {
        output.fileHandleForReading.readabilityHandler = nil
        if process.isRunning {
            send("quit")
            kill()
        }
    }

    private func send(_ cmd: String) {
        guard process.isRunning, let d = (cmd + "\n").data(using: .utf8) else { return }
        do { try input.fileHandleForWriting.write(contentsOf: d) }
        catch { Log.write("Sending to Stockfish failed: \(error.localizedDescription)") }
    }

    func analyze(fen: String, movetimeMs: Int) async -> EngineResult? {
        await withCheckedContinuation { cont in
            queue.async {
                if self.completion != nil {
                    self.send("stop")
                    self.ignoredBestMoves += 1
                    self.finish(nil)
                }
                guard self.process.isRunning else {
                    Log.write("Analysis requested but Stockfish is not running")
                    cont.resume(returning: nil)
                    return
                }
                self.currentFen = fen
                self.lines = [:]
                self.previous = []
                self.searchID += 1
                let id = self.searchID
                self.completion = { cont.resume(returning: $0) }
                self.send("position fen \(fen)")
                self.send("go movetime \(movetimeMs)")
                self.queue.asyncAfter(deadline: .now() + .milliseconds(movetimeMs + 3000)) {
                    guard self.searchID == id, self.completion != nil else { return }
                    Log.write("Stockfish did not answer (position \(fen)), restarting")
                    self.finish(nil)
                    self.kill()
                }
            }
        }
    }

    private func finish(_ r: EngineResult?) {
        let c = completion
        completion = nil
        c?(r)
    }

    private func consume(_ chunk: String) {
        lineBuffer += chunk
        while let nl = lineBuffer.firstIndex(of: "\n") {
            let line = String(lineBuffer[..<nl]).trimmingCharacters(in: .whitespaces)
            lineBuffer = String(lineBuffer[lineBuffer.index(after: nl)...])
            handle(line)
        }
    }

    private func handle(_ line: String) {
        let parts = line.split(separator: " ").map(String.init)
        guard let head = parts.first else { return }
        if head == "info", ignoredBestMoves == 0, let i = parts.firstIndex(of: "score"), i + 2 < parts.count {
            if parts.contains("lowerbound") || parts.contains("upperbound") { return }
            var index = 1
            if let mpv = parts.firstIndex(of: "multipv"), mpv + 1 < parts.count { index = Int(parts[mpv + 1]) ?? 1 }
            let value = Int(parts[i + 2])
            let pv = parts.firstIndex(of: "pv").map { Array(parts[($0 + 1)...]) } ?? []
            if index == 1, !lines.isEmpty { previous = merged(); lines = [:] }
            lines[index] = EngineLine(move: pv.first ?? "",
                                      scoreCP: parts[i + 1] == "cp" ? value : nil,
                                      mateIn: parts[i + 1] == "mate" ? value : nil,
                                      pv: pv)
        } else if head == "bestmove", parts.count >= 2 {
            if ignoredBestMoves > 0 { ignoredBestMoves -= 1; return }
            let move = parts[1] == "(none)" ? "" : parts[1]
            finish(EngineResult(fen: currentFen, bestMove: move, lines: merged()))
        }
    }

    private func merged() -> [EngineLine] {
        var seen = Set<String>()
        let all = lines.keys.sorted().compactMap { lines[$0] } + previous
        return Array(all.filter { seen.insert($0.move).inserted }.prefix(max(lines.count, previous.count)))
    }
}
