param([switch]$Publish, [switch]$VerifyUi)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskTools = Join-Path $taskRoot '.tools'
$taskSdk = Join-Path $taskTools 'dotnet'
$taskDotnet = Join-Path $taskSdk 'dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
New-Item -ItemType Directory -Path $taskTools -Force | Out-Null
if (-not (Test-Path -LiteralPath $taskDotnet)) {
    $taskInstaller = Join-Path $taskTools 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $taskInstaller -UseBasicParsing
    & $taskInstaller -Version '10.0.402' -InstallDir $taskSdk -NoPath
}
$env:DOTNET_ROOT = $taskSdk
$taskReader = Join-Path $taskTools 'WzComparerR2'
$taskPin = '621251801cb14a423ad6737d3cdeabb616b1635c'
if (-not (Test-Path -LiteralPath $taskReader)) {
    & git clone --filter=blob:none --no-checkout https://github.com/Kagamia/WzComparerR2.git $taskReader
    if ($LASTEXITCODE -ne 0) { throw 'Could not download the archive reader.' }
    & git -C $taskReader sparse-checkout set WzComparerR2.WzLib Build
    if ($LASTEXITCODE -ne 0) { throw 'Could not select the archive reader sources.' }
    & git -C $taskReader checkout $taskPin
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out the pinned reader.' }
}
if ((& git -C $taskReader rev-parse HEAD) -ne $taskPin) { throw 'The archive reader checkout does not match the pinned revision.' }
Push-Location $taskRoot
try {
    & $taskDotnet run --project tests/Verify.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core verification failed.' }
    & $taskDotnet build src/App/MapleHelper.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    if ($VerifyUi) {
        & $taskDotnet run --project tests/Ui/Ui.csproj -c Release -- --boss-timers
        if ($LASTEXITCODE -ne 0) { throw 'Boss timer UI verification failed.' }
        & $taskDotnet run --project tests/Ui/Ui.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'UI verification failed. Quit any already running helper and retry.' }
    }
    if ($Publish) {
        & $taskDotnet publish src/App/MapleHelper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
        if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
        Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $taskRoot 'dist')
        Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSES') -Destination (Join-Path $taskRoot 'dist') -Recurse -Force
        Write-Output ('Ready: ' + (Join-Path $taskRoot 'dist\MapleStoryClassicUIHelper.exe'))
    }
} finally { Pop-Location }
