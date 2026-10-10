param(
    [Parameter(Mandatory = $true)][string]$IsccPath,
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('LaunchHere-InstallerChecks-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$testRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$testId = [Guid]::NewGuid().ToString('N')
$testName = 'LaunchHere Installer Test ' + $testId
$testAppId = '{' + [Guid]::NewGuid().ToString() + '}'
$testMutexName = 'Local\LaunchHere.InstallerTest.' + $testId
$startupName = 'LaunchHere.InstallerTest.' + $testId
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $testAppId + '_is1'
$installFolder = Join-Path $testRoot 'InstalledApp'
$startShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ($testName + '.lnk')
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) ($testName + '.lnk')
$testInstaller = Join-Path $testRoot 'InstallerTest.exe'
$uninstaller = Join-Path $installFolder 'unins000.exe'
$testCount = 0
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:testCount++
    Write-Output "PASS $Message"
}
function RunInstaller([string]$Name, [string]$Tasks = '', [bool]$ExpectSuccess = $true) {
    $logPath = Join-Path $testRoot ($Name + '.log')
    $process = Start-Process -FilePath $testInstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/DIR="' + $installFolder + '"'), ('/TASKS="' + $Tasks + '"'), ('/LOG="' + $logPath + '"')) -WindowStyle Hidden -Wait -PassThru
    Check (($process.ExitCode -eq 0) -eq $ExpectSuccess) "$Name returns the expected exit code ($($process.ExitCode))"
}
function RunUninstaller([string]$Name, [bool]$ExpectSuccess = $true) {
    $logPath = Join-Path $testRoot ($Name + '.log')
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + $logPath + '"')) -WindowStyle Hidden -Wait -PassThru
    Check (($process.ExitCode -eq 0) -eq $ExpectSuccess) "$Name returns the expected exit code ($($process.ExitCode))"
}
function GetStartup {
    (Get-ItemProperty -LiteralPath $startupKey -Name $startupName -ErrorAction SilentlyContinue).$startupName
}

# All registry entries and shortcuts use a random test identity, so testing
# never installs over a real LaunchHere copy or changes its startup preference.
& $IsccPath "/DOutputFolder=$testRoot" "/DAppName=$testName" "/DInstallerAppId=$testAppId" "/DAppMutexName=$testMutexName" "/DStartupValueName=$startupName" /FInstallerTest (Join-Path $sourceRoot 'Installer\LaunchHere.iss')
if ($LASTEXITCODE -ne 0) { throw 'Test installer compilation failed.' }
$mutex = $null
try {
    $mutex = New-Object System.Threading.Mutex($false, $testMutexName)
    RunInstaller 'running-app-blocks-install' '' $false
    Check (-not (Test-Path -LiteralPath $uninstaller)) 'running-app protection prevents file installation'
    $mutex.Dispose(); $mutex = $null

    RunInstaller 'fresh-install'
    foreach ($name in @('LaunchHere.exe', 'LaunchHere.exe.config', 'README.md')) {
        $expectedHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot $name)).Hash
        $actualHash = (Get-FileHash -LiteralPath (Join-Path $installFolder $name)).Hash
        Check ($expectedHash -eq $actualHash) "$name installed intact"
    }
    Check (Test-Path -LiteralPath $uninstallKey) 'registered in current-user Windows Apps list'
    $builtVersion = (Get-Item -LiteralPath (Join-Path $sourceRoot 'LaunchHere.exe')).VersionInfo.FileVersion
    $expectedVersion = $builtVersion.Substring(0, $builtVersion.LastIndexOf('.'))
    Check ((Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -eq $expectedVersion) 'correct version registered'
    Check (Test-Path -LiteralPath $startShortcut) 'Start menu shortcut created'
    Check (-not (Test-Path -LiteralPath $desktopShortcut)) 'desktop shortcut is optional'
    $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startShortcut)
    Check ($shortcut.TargetPath -eq (Join-Path $installFolder 'LaunchHere.exe') -and $shortcut.Arguments -eq '--settings') 'shortcut opens the settings GUI'
    Check ($null -eq (GetStartup)) 'fresh install does not enable startup'
    $selfTest = Start-Process -FilePath (Join-Path $installFolder 'LaunchHere.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    Check ($selfTest.ExitCode -eq 0) 'installed app passes geometry self-test'

    $portableCommand = '"' + (Join-Path $testRoot 'OldPortable\LaunchHere.exe') + '"'
    New-ItemProperty -LiteralPath $startupKey -Name $startupName -Value $portableCommand -PropertyType String -Force | Out-Null
    RunInstaller 'upgrade-with-desktop-shortcut' 'desktopicon'
    Check ((GetStartup) -eq ('"' + (Join-Path $installFolder 'LaunchHere.exe') + '"')) 'existing portable startup entry follows installed copy'
    Check (Test-Path -LiteralPath $desktopShortcut) 'requested desktop shortcut created'
    Check (@(Get-ChildItem -LiteralPath $installFolder -Filter 'unins*.exe').Count -eq 1) 'upgrade reuses a single uninstaller'

    $mutex = New-Object System.Threading.Mutex($false, $testMutexName)
    RunUninstaller 'running-app-blocks-uninstall' $false
    Check (Test-Path -LiteralPath (Join-Path $installFolder 'LaunchHere.exe')) 'blocked uninstall leaves app intact'
    $mutex.Dispose(); $mutex = $null
    RunUninstaller 'uninstall'
    Check (-not (Test-Path -LiteralPath (Join-Path $installFolder 'LaunchHere.exe'))) 'uninstall removes installed app'
    Check (-not (Test-Path -LiteralPath $uninstallKey)) 'uninstall removes Windows Apps registration'
    Check (-not (Test-Path -LiteralPath $startShortcut) -and -not (Test-Path -LiteralPath $desktopShortcut)) 'uninstall removes its shortcuts'
    Check ($null -eq (GetStartup)) 'uninstall clears startup entry for its installed copy'

    $otherCommand = '"' + (Join-Path $testRoot 'OtherApp.exe') + '"'
    New-ItemProperty -LiteralPath $startupKey -Name $startupName -Value $otherCommand -PropertyType String -Force | Out-Null
    RunInstaller 'reinstall'
    Check ((GetStartup) -eq $otherCommand) 'install preserves unrelated startup command'
    RunUninstaller 'second-uninstall'
    Check ((GetStartup) -eq $otherCommand) 'uninstall preserves unrelated startup command'
    Write-Output "$testCount installer checks passed. Logs: $testRoot"
}
finally {
    if ($mutex) { $mutex.Dispose() }
    if (Test-Path -LiteralPath $uninstaller) {
        Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait | Out-Null
    }
    Remove-ItemProperty -LiteralPath $startupKey -Name $startupName -ErrorAction SilentlyContinue
}
