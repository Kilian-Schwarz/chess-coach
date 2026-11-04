import Foundation
import Security

struct LLMModel: Identifiable, Hashable {
    let id: String
    let name: String
}

enum LLMError: LocalizedError {
    case badURL, http(Int, String), noModels
    var errorDescription: String? {
        switch self {
        case .badURL: String(localized: "The server URL is invalid.")
        case .http(401, _), .http(403, _): String(localized: "Access denied. Please check the token.")
        case .http(let code, let body): String(localized: "The server reported error \(code). \(body.prefix(160))")
        case .noModels: String(localized: "No model list found at this address. For Open WebUI enter the site address, for vLLM use http://host:8000/v1.")
        }
    }
}

enum LLMClient {
    static func discover(url input: String, token: String) async throws -> (base: String, models: [LLMModel]) {
        var trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)
        while trimmed.hasSuffix("/") { trimmed.removeLast() }
        guard !trimmed.isEmpty else { throw LLMError.badURL }
        if !trimmed.contains("://") { trimmed = "https://" + trimmed }
        var lastError: Error = LLMError.noModels
        for base in [trimmed, trimmed + "/api", trimmed + "/v1"] {
            do {
                let models = try await listModels(base: base, token: token)
                if !models.isEmpty { return (base, models) }
            } catch let e as LLMError {
                if case .http(let code, _) = e, code == 401 || code == 403 { throw e }
                lastError = e
            } catch { lastError = error }
        }
        throw lastError
    }

    static func listModels(base: String, token: String) async throws -> [LLMModel] {
        guard let url = URL(string: base + "/models") else { throw LLMError.badURL }
        var req = URLRequest(url: url, timeoutInterval: 15)
        if !token.isEmpty { req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        let (data, resp) = try await URLSession.shared.data(for: req)
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        guard code == 200 else { throw LLMError.http(code, String(data: data, encoding: .utf8) ?? "") }
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let list = json["data"] as? [[String: Any]] else { throw LLMError.noModels }
        return list.compactMap { m in
            guard let id = m["id"] as? String else { return nil }
            return LLMModel(id: id, name: (m["name"] as? String) ?? id)
        }
    }

    static func explain(base: String, token: String, model: String, system: String, user: String, maxTokens: Int = 700,
                        onText: @escaping @MainActor (String) -> Void) async throws -> String {
        do {
            return try await stream(base: base, token: token, model: model, system: system, user: user,
                             maxTokens: maxTokens, disableThinking: true, onText: onText)
        } catch LLMError.http(400, _) {
            return try await stream(base: base, token: token, model: model, system: system, user: user,
                             maxTokens: maxTokens, disableThinking: false, onText: onText)
        }
    }

    private static func stream(base: String, token: String, model: String, system: String, user: String,
                               maxTokens: Int, disableThinking: Bool, onText: @escaping @MainActor (String) -> Void) async throws -> String {
        guard let url = URL(string: base + "/chat/completions") else { throw LLMError.badURL }
        var req = URLRequest(url: url, timeoutInterval: 120)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if !token.isEmpty { req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        var body: [String: Any] = [
            "model": model,
            "stream": true,
            "temperature": 0.4,
            "max_tokens": maxTokens,
            "messages": [["role": "system", "content": system], ["role": "user", "content": user]],
        ]
        if disableThinking { body["chat_template_kwargs"] = ["enable_thinking": false] }
        req.httpBody = try JSONSerialization.data(withJSONObject: body)

        let (bytes, resp) = try await URLSession.shared.bytes(for: req)
        let code = (resp as? HTTPURLResponse)?.statusCode ?? 0
        guard code == 200 else {
            var text = ""
            for try await line in bytes.lines { text += line; if text.count > 400 { break } }
            throw LLMError.http(code, text)
        }
        var full = ""
        for try await line in bytes.lines {
            try Task.checkCancellation()
            guard line.hasPrefix("data:") else { continue }
            let payload = line.dropFirst(5).trimmingCharacters(in: .whitespaces)
            if payload == "[DONE]" { break }
            guard let d = payload.data(using: .utf8),
                  let json = try? JSONSerialization.jsonObject(with: d) as? [String: Any],
                  let choices = json["choices"] as? [[String: Any]],
                  let delta = choices.first?["delta"] as? [String: Any] ?? choices.first?["message"] as? [String: Any],
                  let piece = delta["content"] as? String, !piece.isEmpty else { continue }
            full += piece
            let shown = Coach.cleanAnswer(full)
            await onText(shown)
        }
        return Coach.cleanAnswer(full)
    }
}

enum TokenStore {
    private static let service = "de.webschwarz.chesscoach"
    private static let account = "llm-token"

    static func load() -> String {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                kSecAttrAccount as String: account, kSecReturnData as String: true]
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess, let d = out as? Data else { return "" }
        return String(data: d, encoding: .utf8) ?? ""
    }

    static func save(_ token: String) {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service,
                                kSecAttrAccount as String: account]
        SecItemDelete(q as CFDictionary)
        guard !token.isEmpty else { return }
        var add = q
        add[kSecValueData as String] = Data(token.utf8)
        SecItemAdd(add as CFDictionary, nil)
    }
}
