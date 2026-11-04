import CryptoKit
import Foundation

var failures = 0
func check(_ ok: Bool, _ name: String) {
    print(ok ? "ok   \(name)" : "FAIL \(name)")
    if !ok { failures += 1 }
}

check(UpdateCheck.isNewer("0.2.0", than: "0.1.9"), "newer minor")
check(UpdateCheck.isNewer("1.0", than: "0.9.9"), "newer major")
check(UpdateCheck.isNewer("0.10.0", than: "0.9.0"), "numeric compare")
check(!UpdateCheck.isNewer("0.1.0", than: "0.1"), "equal with missing part")
check(!UpdateCheck.isNewer("0.1.0", than: "0.2.0"), "older")

check(!UpdateCheck.isNewer("0.1.0-beta", than: "0.1.0"), "suffix ignored")
check(UpdateCheck.isNewer("0.2.0-beta", than: "0.1.0"), "suffix newer")

let id = UpdateCheck.bundleID
check(UpdateCheck.acceptable(shortVersion: "0.2.0", bundleID: id, feedVersion: "0.2.0", current: "0.1.0"), "staged app accepted")
check(!UpdateCheck.acceptable(shortVersion: "0.1.0", bundleID: id, feedVersion: "0.2.0", current: "0.1.0"), "staged version differs from feed")
check(!UpdateCheck.acceptable(shortVersion: "0.1.0", bundleID: id, feedVersion: "0.1.0", current: "0.1.0"), "replay of running version")
check(!UpdateCheck.acceptable(shortVersion: "0.0.9", bundleID: id, feedVersion: "0.0.9", current: "0.1.0"), "downgrade")
check(!UpdateCheck.acceptable(shortVersion: "0.2.0", bundleID: "com.example.other", feedVersion: "0.2.0", current: "0.1.0"), "foreign bundle id")
check(!UpdateCheck.acceptable(shortVersion: nil, bundleID: id, feedVersion: "0.2.0", current: "0.1.0"), "missing version")

func feedWith(version: String, file: String) -> UpdateFeed {
    UpdateFeed(version: version, notes: nil, macos: .init(file: file, sha256: "", signature: ""))
}
check(UpdateCheck.isValid(feedWith(version: "0.2.0", file: "ChessCoach-0.2.0-macos.zip")), "valid feed")
check(!UpdateCheck.isValid(feedWith(version: "0.2", file: "ChessCoach-0.2.0-macos.zip")), "short version rejected")
check(!UpdateCheck.isValid(feedWith(version: "0.2.0", file: "../evil.zip")), "path in file rejected")
check(!UpdateCheck.isValid(feedWith(version: "0.2.0", file: "ChessCoach 0.2.0.zip")), "space in file rejected")
check(UpdateCheck.downloadURL(version: "0.2.0", file: "ChessCoach-0.2.0-macos.zip") != nil, "download url")

let json = #"{"version":"0.2.0","notes":"x","macos":{"file":"a.zip","sha256":"ab","signature":"cd"},"windows":{"file":"b.zip","sha256":"ef","signature":"gh"}}"#
let feed = try? JSONDecoder().decode(UpdateFeed.self, from: Data(json.utf8))
check(feed?.version == "0.2.0" && feed?.macos?.file == "a.zip", "feed parse")

let args = CommandLine.arguments
guard args.count == 4 else { print("usage: <zip> <public-key-base64> <signature-base64>"); exit(2) }
let data = try Data(contentsOf: URL(fileURLWithPath: args[1]))
let sha = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
check(UpdateCheck.verify(data, sha256: sha, signature: args[3], publicKey: args[2]), "valid signature")
check(!UpdateCheck.verify(data + Data([0]), sha256: sha, signature: args[3], publicKey: args[2]), "tampered data")
check(!UpdateCheck.verify(data, sha256: String(repeating: "0", count: 64), signature: args[3], publicKey: args[2]), "wrong hash")
check(!UpdateCheck.verify(data, sha256: sha, signature: args[3], publicKey: UpdateCheck.publicKey), "foreign key")
exit(failures == 0 ? 0 : 1)
