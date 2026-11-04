import Foundation

enum InstallLocation {
    static func isInstalled(_ bundlePath: String, home: String = NSHomeDirectory()) -> Bool {
        let path = (bundlePath as NSString).standardizingPath
        let roots = ["/Applications/", (home as NSString).appendingPathComponent("Applications") + "/"]
        return roots.contains { path.hasPrefix($0) }
    }

    static func isTranslocated(_ bundlePath: String) -> Bool {
        bundlePath.contains("/AppTranslocation/")
    }

    static func isOnDiskImage(_ bundlePath: String) -> Bool {
        bundlePath.hasPrefix("/Volumes/")
    }

    static func targetDirectory(isWritable: (String) -> Bool, home: String = NSHomeDirectory()) -> String {
        isWritable("/Applications") ? "/Applications" : (home as NSString).appendingPathComponent("Applications")
    }
}
