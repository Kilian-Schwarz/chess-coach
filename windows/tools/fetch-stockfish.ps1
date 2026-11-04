param([string]$Target = (Join-Path $PSScriptRoot "..\..\dist\windows\publish"))
$ErrorActionPreference = "Stop"
$tag = "sf_19"
$asset = "stockfish-windows-x86-64-universal.zip"
$sha256 = "3c8bf1f9ea66a09350a40df4f632288285ac206d99f33ab5842c408fc30b48a7"
Write-Host "Downloading $asset ($tag)"
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("stockfish-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $zip = Join-Path $tmp "stockfish.zip"
    Invoke-WebRequest -Uri "https://github.com/official-stockfish/Stockfish/releases/download/$tag/$asset" -OutFile $zip -Headers @{ "User-Agent" = "Chess Coach" }
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($actual -ne $sha256) { throw "Checksum mismatch for ${asset}: $actual" }
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $exe = Get-ChildItem -Path $tmp -Recurse -Filter "stockfish*.exe" | Select-Object -First 1
    if (-not $exe) { throw "No stockfish executable in the archive." }
    $dest = Join-Path $Target "stockfish"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item $exe.FullName (Join-Path $dest "stockfish.exe") -Force
    Get-ChildItem -Path $tmp -Recurse -Include "Copying.txt", "AUTHORS" | Copy-Item -Destination $dest -Force
    Write-Host "Done: $dest\stockfish.exe"
}
finally {
    Remove-Item $tmp -Recurse -Force
}
