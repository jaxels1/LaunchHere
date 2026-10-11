$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:LaunchHere.exe /win32manifest:app.manifest /win32icon:LaunchHere.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll /reference:System.Net.Http.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll LaunchHere.cs Settings.cs SettingsWindow.cs GlassControls.cs StudioControls.cs Updates.cs UpdateFiles.cs UpdateRunner.cs AssemblyInfo.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $test = Start-Process -FilePath (Join-Path $PSScriptRoot 'LaunchHere.exe') -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Geometry tests failed: $($test.ExitCode)" }
    Write-Output 'Built LaunchHere.exe; all geometry tests passed.'
} finally { Pop-Location }
