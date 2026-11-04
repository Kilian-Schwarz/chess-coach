import Foundation

var failures = 0
func check(_ condition: Bool, _ name: String) {
    if !condition { failures += 1; print("FAIL: \(name)") }
}

let home = "/Users/test"
check(InstallLocation.isInstalled("/Applications/Chess Coach.app", home: home), "applications")
check(InstallLocation.isInstalled("/Users/test/Applications/Chess Coach.app", home: home), "user applications")
check(!InstallLocation.isInstalled("/Users/test/Downloads/Chess Coach.app", home: home), "downloads")
check(!InstallLocation.isInstalled("/Volumes/Chess Coach/Chess Coach.app", home: home), "disk image")
check(!InstallLocation.isInstalled("/Applications/../Users/test/Desktop/Chess Coach.app", home: home), "traversal")
check(!InstallLocation.isInstalled("/Applications", home: home), "folder itself")
check(InstallLocation.isTranslocated("/private/var/folders/x/AppTranslocation/ABC/d/Chess Coach.app"), "translocated")
check(!InstallLocation.isTranslocated("/Applications/Chess Coach.app"), "not translocated")
check(InstallLocation.isOnDiskImage("/Volumes/Chess Coach/Chess Coach.app"), "volume")
check(!InstallLocation.isOnDiskImage("/Users/test/Chess Coach.app"), "not volume")
check(InstallLocation.targetDirectory(isWritable: { _ in true }, home: home) == "/Applications", "target admin")
check(InstallLocation.targetDirectory(isWritable: { _ in false }, home: home) == "/Users/test/Applications", "target standard")

if failures > 0 { exit(1) }
print("install ok")
