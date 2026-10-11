param([string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('LaunchHere-Benchmark-' + [Guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$program = Join-Path $output 'AnimationBenchmark.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $sourceRoot
try {
    & $compiler /nologo /optimize+ /target:exe /platform:x64 /main:LaunchHere.AnimationBenchmark "/out:$program" /win32manifest:app.manifest /win32icon:LaunchHere.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Net.Http.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll LaunchHere.cs Settings.cs SettingsWindow.cs GlassControls.cs StudioControls.cs Updates.cs UpdateFiles.cs UpdateRunner.cs AssemblyInfo.cs Tests\AnimationBenchmark.cs
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark compilation failed.' }
    Copy-Item LaunchHere.exe.config ($program + '.config')
    $process = Start-Process -FilePath $program -ArgumentList ('"' + $output + '"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'benchmark.txt') -RedirectStandardError (Join-Path $output 'errors.txt')
    $process.WaitForExit()
    Get-Content (Join-Path $output 'benchmark.txt')
    if ($process.ExitCode -ne 0) { Get-Content (Join-Path $output 'errors.txt'); throw 'Benchmark failed.' }
} finally { Pop-Location }
