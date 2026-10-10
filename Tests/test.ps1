param([string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('LaunchHere-Tests-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$testOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
& (Join-Path $sourceRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Push-Location $sourceRoot
try {
    $guiCheck = Join-Path $testOutput 'SettingsChecks.exe'
    & $compiler /nologo /target:exe /platform:x64 /main:LaunchHere.SettingsChecks "/out:$guiCheck" /win32manifest:app.manifest /win32icon:LaunchHere.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll LaunchHere.cs Settings.cs SettingsWindow.cs AssemblyInfo.cs Tests\SettingsChecks.cs
    if ($LASTEXITCODE -ne 0) { throw 'GUI test compilation failed.' }
    Copy-Item -LiteralPath 'LaunchHere.exe.config' -Destination ($guiCheck + '.config')
    $windowCheck = Join-Path $testOutput 'WindowChecks.exe'
    & $compiler /nologo /target:exe /platform:x64 "/out:$windowCheck" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll Tests\WindowChecks.cs
    if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed.' }
    $appPath = Join-Path $sourceRoot 'LaunchHere.exe'
    $checks = @(
        @{ Name = 'hooks'; Executable = $appPath; Arguments = '--verify-hooks' },
        @{ Name = 'settings'; Executable = $guiCheck; Arguments = ('"' + $testOutput + '"') },
        @{ Name = 'windows'; Executable = $windowCheck; Arguments = ('"' + $appPath + '"') }
    )
    foreach ($check in $checks) {
        $stdout = Join-Path $testOutput ($check.Name + '.txt')
        $stderr = Join-Path $testOutput ($check.Name + '-errors.txt')
        $process = Start-Process -FilePath $check.Executable -ArgumentList $check.Arguments -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru
        Get-Content -LiteralPath $stdout
        if ($process.ExitCode -ne 0) {
            Get-Content -LiteralPath $stderr
            throw "$($check.Name) checks failed with exit code $($process.ExitCode). Close the normal app before testing."
        }
    }
    Write-Output "All checks passed. Logs and GUI renders: $testOutput"
} finally { Pop-Location }
