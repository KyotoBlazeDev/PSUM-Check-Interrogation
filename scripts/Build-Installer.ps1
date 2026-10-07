[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoPath 'PSUM Check Interrogation WinUI 3.csproj'
if (-not $Version) {
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $Version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
}
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
    @($Version.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count) {
    throw 'Version must be X.Y.Z, with each component between 0 and 65534.'
}

if (-not $IsccPath) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand) { $IsccPath = $isccCommand.Source }
    else { $IsccPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup 6 is required. Install it or pass -IsccPath to ISCC.exe.'
}

$publishPath = Join-Path $repoPath 'artifacts/publish/win-x64'
$installerPath = Join-Path $repoPath 'artifacts/installer'
# Only remove this script's fixed output directories, after checking their location.
foreach ($outputPath in @($publishPath, $installerPath)) {
    $resolvedPath = [IO.Path]::GetFullPath($outputPath)
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoPath 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output path is outside artifacts: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) { Remove-Item -LiteralPath $resolvedPath -Recurse -Force }
    New-Item -ItemType Directory -Path $resolvedPath -Force | Out-Null
}

& dotnet publish $projectPath -c Release -p:Platform=x64 -r win-x64 --self-contained true `
    "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" `
    -p:DebugType=None -p:DebugSymbols=false -o $publishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$appName = 'PSUM Check Interrogation WinUI 3'
foreach ($requiredFile in @("$appName.exe", "$appName.dll", "$appName.pri", 'App.xbf', 'MainWindow.xbf',
    'coreclr.dll', 'hostfxr.dll', 'Microsoft.UI.Xaml.dll', 'Assets/battery_paper_app_icon.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $requiredFile) -PathType Leaf)) {
        throw "The self-contained publish folder is missing $requiredFile"
    }
}
Copy-Item -LiteralPath (Join-Path $repoPath 'LICENSE'), (Join-Path $repoPath 'README.md') -Destination $publishPath

& $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publishPath" "/O$installerPath" (Join-Path $repoPath 'installer/PSUM.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

$setupPath = Join-Path $installerPath "PSUM-Check-Interrogation-$Version-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw "Installer not found: $setupPath" }
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $setupPath -Leaf)" | Set-Content -LiteralPath "$setupPath.sha256" -Encoding ascii
Write-Host "Installer: $setupPath"
