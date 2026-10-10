param(
    [string]$IsccPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..')
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-installer.ps1') -IsccPath $IsccPath -OutputDirectory $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
$releaseOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
if ($releaseOutput.TrimEnd('\') -eq $PSScriptRoot.TrimEnd('\') -or $releaseOutput.StartsWith($PSScriptRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose an output directory outside the source folder.'
}
$fileVersion = (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'LaunchHere.exe')).VersionInfo.FileVersion
$parsedVersion = [Version]$fileVersion
$version = $parsedVersion.ToString($(if ($parsedVersion.Revision -gt 0) { 4 } elseif ($parsedVersion.Build -gt 0) { 3 } else { 2 }))
$portable = Join-Path $releaseOutput "LaunchHere-$version-Windows-x64.zip"
$source = Join-Path $releaseOutput "LaunchHere-$version-source.zip"
$appFiles = @('LaunchHere.exe','LaunchHere.exe.config','README.md') | ForEach-Object { Join-Path $PSScriptRoot $_ }
Compress-Archive -LiteralPath $appFiles -DestinationPath $portable -Force
$sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Force | Where-Object { $_.Name -notin @('LaunchHere.exe','bin','obj','.git') -and $_.Extension -notin @('.zip','.pdb') } | ForEach-Object { $_.FullName }
Compress-Archive -LiteralPath $sourceFiles -DestinationPath $source -Force
$installer = Join-Path $releaseOutput "LaunchHere-$version-Setup-x64.exe"
$hashes = foreach ($file in @($installer, $portable, $source)) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($file) }
[System.IO.File]::WriteAllLines((Join-Path $releaseOutput 'SHA256SUMS.txt'), $hashes, [System.Text.Encoding]::ASCII)
Write-Output 'Release packages ready:'
Write-Output $installer
Write-Output $portable
Write-Output $source
