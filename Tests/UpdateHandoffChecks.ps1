param(
    [Parameter(Mandatory = $true)][string]$IsccPath,
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('LaunchHere-Handoff-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$testRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testId = [Guid]::NewGuid().ToString('N')
$testName = 'LaunchHere Handoff Test ' + $testId
$testAppId = '{' + [Guid]::NewGuid().ToString() + '}'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $testAppId + '_is1'
$testCount = 0
function Check([bool]$Value, [string]$Name) { if (-not $Value) { throw $Name }; $script:testCount++; Write-Output "PASS $Name" }
$oldRoot = Join-Path $testRoot 'old'; $newRoot = Join-Path $testRoot 'new'
foreach ($fixture in @(@{ Folder = $oldRoot; Version = '1.2.2.0' }, @{ Folder = $newRoot; Version = '1.3.0.0' })) {
    New-Item -ItemType Directory -Path $fixture.Folder -Force | Out-Null
    $versionSource = Join-Path $fixture.Folder 'Version.cs'
    [System.IO.File]::WriteAllText($versionSource, ('[assembly: System.Reflection.AssemblyVersion("' + $fixture.Version + '")][assembly: System.Reflection.AssemblyFileVersion("' + $fixture.Version + '")]'))
    $fixtureExe = Join-Path $fixture.Folder 'LaunchHere.exe'
    & $compiler /nologo /target:winexe /platform:x64 "/out:$fixtureExe" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'UpdateFixture.cs') $versionSource
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
    foreach ($name in @('LaunchHere.exe.config','README.md')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $fixture.Folder $name) }
}
function BuildFixtureInstaller([string]$FilesRoot, [string]$Name) {
    & $IsccPath "/DOutputFolder=$testRoot" "/DAppFilesFolder=$FilesRoot" "/DAppName=$testName" "/DInstallerAppId=$testAppId" "/DAppMutexName=Local\LaunchHere.Handoff.$testId" "/DStartupValueName=LaunchHere.Handoff.$testId" "/F$Name" (Join-Path $sourceRoot 'Installer\LaunchHere.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Fixture installer compilation failed.' }
}
BuildFixtureInstaller $oldRoot 'OldInstaller'
BuildFixtureInstaller $newRoot 'New-Setup-x64'
$installedRoot = Join-Path $testRoot 'installed-app'
$portableRoot = Join-Path $testRoot 'portable-app'
New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
foreach ($name in @('LaunchHere.exe','LaunchHere.exe.config','README.md')) { Copy-Item -LiteralPath (Join-Path $oldRoot $name) -Destination (Join-Path $portableRoot $name) }
$portablePackage = Join-Path $testRoot 'portable.zip'
$portableFiles = @('LaunchHere.exe','LaunchHere.exe.config','README.md') | ForEach-Object { Join-Path $newRoot $_ }
Compress-Archive -LiteralPath $portableFiles -DestinationPath $portablePackage -Force
$uninstaller = Join-Path $installedRoot 'unins000.exe'
try {
    $install = Start-Process -FilePath (Join-Path $testRoot 'OldInstaller.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $installedRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    Check ($install.ExitCode -eq 0) 'old installer fixture installs'
    foreach ($mode in @(@{ Folder = $portableRoot; Package = $portablePackage; Portable = $true; Name = 'portable' }, @{ Folder = $installedRoot; Package = (Join-Path $testRoot 'New-Setup-x64.exe'); Portable = $false; Name = 'installed' })) {
        $downloadRoot = Join-Path $testRoot ($mode.Name + '-download')
        New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
        $package = Join-Path $downloadRoot ([System.IO.Path]::GetFileName($mode.Package))
        Copy-Item -LiteralPath $mode.Package -Destination $package
        $runner = Join-Path $downloadRoot 'LaunchHere.exe'
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'LaunchHere.exe') -Destination $runner
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'LaunchHere.exe.config') -Destination ($runner + '.config')
        $oldExe = Join-Path $mode.Folder 'LaunchHere.exe'
        $parent = Start-Process -FilePath $oldExe -ArgumentList '--parent' -WindowStyle Hidden -PassThru
        $plan = @{ Package = $package; Sha256 = (Get-FileHash -LiteralPath $package).Hash; AppFolder = $mode.Folder;
            ParentPid = $parent.Id; ParentStartTicks = $parent.StartTime.ToUniversalTime().Ticks; Portable = $mode.Portable; Version = '1.3.0.0' }
        $planPath = Join-Path $downloadRoot 'update.json'
        [System.IO.File]::WriteAllText($planPath, ($plan | ConvertTo-Json))
        $apply = Start-Process -FilePath $runner -ArgumentList ('--apply-update "' + $planPath + '"') -WindowStyle Hidden -PassThru
        Start-Sleep -Milliseconds 400
        Check ((Get-FileHash -LiteralPath $oldExe).Hash -eq (Get-FileHash -LiteralPath (Join-Path $oldRoot 'LaunchHere.exe')).Hash) ($mode.Name + ' helper waits for previous app to exit')
        if (-not $apply.WaitForExit(20000)) { $apply.Kill(); throw 'Update helper timed out' }
        Check ($apply.ExitCode -eq 0) ($mode.Name + ' automatic apply succeeds')
        Check ((Get-FileHash -LiteralPath $oldExe).Hash -eq (Get-FileHash -LiteralPath (Join-Path $newRoot 'LaunchHere.exe')).Hash) ($mode.Name + ' app replaced with new version')
        $marker = Join-Path $mode.Folder 'restart.marker'
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
        Check ((Get-Content -LiteralPath $marker -Raw) -eq '1.3.0.0') ($mode.Name + ' new app automatically restarts with settings')
        Check (-not (Test-Path -LiteralPath $package)) ($mode.Name + ' successful update removes downloaded package')
    }
    Check ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -eq '1.3') 'installed update refreshes Windows Apps version'
    Write-Output "$testCount handoff checks passed. Logs: $testRoot"
}
finally {
    if (Test-Path -LiteralPath $uninstaller) {
        $remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        Check ($remove.ExitCode -eq 0) 'installed fixture uninstalls cleanly'
    }
}
