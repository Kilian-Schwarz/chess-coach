import Foundation

struct Loc {
    let table: String?

    nonisolated(unsafe) static var bundle = Bundle.main
    static let display = Loc(table: nil)
    static let facts = Loc(table: "EnglishOnly")

    func callAsFunction(_ key: StaticString, _ value: String.LocalizationValue) -> String {
        String(localized: key, defaultValue: value, table: table, bundle: Loc.bundle)
    }
}
