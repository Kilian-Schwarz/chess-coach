import Foundation

struct Loc {
    let table: String?

    nonisolated(unsafe) static var bundle = Bundle.main
    static let display = Loc(table: nil)
    static let facts = Loc(table: "EnglishOnly")

    var locale: Locale {
        Locale(identifier: table == nil ? Loc.bundle.preferredLocalizations.first ?? "en" : "en")
    }

    func callAsFunction(_ key: StaticString, _ value: String.LocalizationValue) -> String {
        String(localized: key, defaultValue: value, table: table, bundle: Loc.bundle)
    }
}

extension String {
    var lowercasedFirst: String { prefix(1).lowercased() + dropFirst() }
    var capitalizedFirst: String { prefix(1).uppercased() + dropFirst() }
}
