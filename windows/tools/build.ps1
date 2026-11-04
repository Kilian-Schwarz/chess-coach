param([string]$Version = "")
$ErrorActionPreference = "Stop"
$dist = Join-Path $PSScriptRoot "..\..\dist\windows"
$project = Join-Path $PSScriptRoot "..\ChessCoach.App\ChessCoach.App.csproj"
if (-not $Version) { $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$publish = Join-Path $dist "publish"
$arguments = @("publish", $project, "-c", "Release", "-r", "win-x64", "--self-contained", "-p:PublishSingleFile=true", "-p:Version=$Version", "-o", $publish)
if ($env:OFFICIAL_BUILD) { $arguments += "-p:OfficialBuild=true" }
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
& (Join-Path $PSScriptRoot "fetch-stockfish.ps1") -Target $publish
$zip = Join-Path $dist "ChessCoach-$Version-windows-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip
Write-Host "Built: $zip"
