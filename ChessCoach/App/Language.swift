import Foundation

enum Language {
    static let codes = ["en", "de", "fr", "es", "it", "pt-BR", "nl", "pl", "tr", "ru", "uk", "zh-Hans", "ja", "ko"]

    static var current: String {
        let preferred = Bundle.main.preferredLocalizations.first ?? "en"
        return codes.contains(preferred) ? preferred : "en"
    }

    static var englishName: String {
        Locale(identifier: "en").localizedString(forIdentifier: current) ?? "English"
    }

    static var stored: String {
        let list = UserDefaults.standard.persistentDomain(forName: Bundle.main.bundleIdentifier ?? "")?["AppleLanguages"] as? [String]
        return list?.first.flatMap { codes.contains($0) ? $0 : nil } ?? ""
    }

    static let atLaunch = stored

    static func store(_ code: String) {
        if code.isEmpty { UserDefaults.standard.removeObject(forKey: "AppleLanguages") }
        else { UserDefaults.standard.set([code], forKey: "AppleLanguages") }
    }

    static func nativeName(_ code: String) -> String {
        Locale(identifier: code).localizedString(forIdentifier: code)?.localizedCapitalized ?? code
    }
}
