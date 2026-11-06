# Build edifier-mp85-battery.exe with the .NET Framework C# compiler.
# Needs the Windows 10/11 SDK (for the combined Windows.winmd).

$ErrorActionPreference = 'Stop'

$csc     = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$facades = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Facades"
$winmd   = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\UnionMetadata\10.*\Windows.winmd" |
           Sort-Object { [version]$_.Directory.Name } -Descending |
           Select-Object -First 1 -ExpandProperty FullName
if (-not $winmd) { throw 'Windows.winmd not found. Install the Windows SDK.' }

$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

& $csc /nologo /nowarn:1701 /optimize+ `
    /out:"$out\edifier-mp85-battery.exe" `
    /r:"$winmd" `
    /r:"$facades\System.Runtime.dll" `
    /r:"$facades\System.Threading.Tasks.dll" `
    /r:"$facades\System.Runtime.InteropServices.WindowsRuntime.dll" `
    /r:"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll" `
    "$PSScriptRoot\src\EdifierMp85Battery.cs"

if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
"Built $out\edifier-mp85-battery.exe"
