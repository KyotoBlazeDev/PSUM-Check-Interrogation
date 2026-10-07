$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP) {
    throw 'This installation smoke test runs only on the disposable GitHub Actions runner.'
}

$repoPath = Split-Path $PSScriptRoot -Parent
$installers = @(Get-ChildItem -LiteralPath (Join-Path $repoPath 'artifacts/installer') -Filter '*-Setup-x64.exe')
if ($installers.Count -ne 1) { throw 'Expected exactly one setup executable.' }
$testPath = Join-Path $env:RUNNER_TEMP ('PSUM-Setup-Test-' + [guid]::NewGuid().ToString('N'))
$setup = Start-Process -FilePath $installers[0].FullName -ArgumentList @(
    '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$testPath`""
) -Wait -PassThru -WindowStyle Hidden
if ($setup.ExitCode -ne 0) { throw "Silent install failed with exit code $($setup.ExitCode)" }

try {
    $publishPath = Join-Path $repoPath 'artifacts/publish/win-x64'
    foreach ($file in Get-ChildItem -LiteralPath $publishPath -File -Recurse) {
        $relativePath = [IO.Path]::GetRelativePath($publishPath, $file.FullName)
        $installedPath = Join-Path $testPath $relativePath
        if (-not (Test-Path -LiteralPath $installedPath -PathType Leaf) -or
            (Get-FileHash -LiteralPath $installedPath).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
            throw "Setup did not install the expected payload: $relativePath"
        }
    }
    Write-Host 'All installed files match the self-contained publish output.'
} finally {
    $uninstaller = Join-Path $testPath 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
        ) -Wait -PassThru -WindowStyle Hidden
        if ($uninstall.ExitCode -ne 0) { throw "Silent uninstall failed with exit code $($uninstall.ExitCode)" }
        if (Test-Path -LiteralPath (Join-Path $testPath 'PSUM Check Interrogation WinUI 3.exe')) {
            throw 'Uninstall left the application executable behind.'
        }
        Write-Host 'Silent uninstall succeeded.'
    } else { throw 'Setup did not install an uninstaller.' }
}
