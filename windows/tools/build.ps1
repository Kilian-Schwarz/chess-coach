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
$setupProject = Join-Path $PSScriptRoot "..\ChessCoach.Setup\ChessCoach.Setup.csproj"
$setupPublish = Join-Path $dist "setup"
if (Test-Path $setupPublish) { Remove-Item $setupPublish -Recurse -Force }
dotnet publish $setupProject -c Release -r win-x64 --self-contained -p:PublishSingleFile=true "-p:Version=$Version" -o $setupPublish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the setup failed." }
$setup = Join-Path $dist "ChessCoach-$Version-setup.exe"
if (Test-Path $setup) { Remove-Item $setup }
$output = [IO.File]::Create($setup)
foreach ($part in @((Join-Path $setupPublish "ChessCoach-Setup.exe"), $zip)) {
    $input = [IO.File]::OpenRead($part)
    $input.CopyTo($output)
    $input.Dispose()
}
$footer = [Text.Encoding]::ASCII.GetBytes(("{0:x16}" -f (Get-Item $zip).Length) + "CCSETUP1")
$output.Write($footer, 0, $footer.Length)
$output.Dispose()
Write-Host "Built: $zip"
Write-Host "Built: $setup"
