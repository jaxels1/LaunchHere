param(
    [string]$IsccPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..')
)
$ErrorActionPreference = 'Stop'
if (-not $IsccPath) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand) { $IsccPath = $isccCommand.Source }
    else {
        $compilerCandidates = @(
            (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
        )
        $IsccPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 6.7 or 7 from https://jrsoftware.org/isdl.php, or pass -IsccPath with the full path to ISCC.exe.'
}
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$installerOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
& $IsccPath "/DOutputFolder=$installerOutput" (Join-Path $PSScriptRoot 'Installer\LaunchHere.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer build failed: $LASTEXITCODE" }
$fileVersion = (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'LaunchHere.exe')).VersionInfo.FileVersion
$releaseVersion = $fileVersion.Substring(0, $fileVersion.LastIndexOf('.'))
Write-Output (Join-Path $installerOutput "LaunchHere-$releaseVersion-Setup-x64.exe")
