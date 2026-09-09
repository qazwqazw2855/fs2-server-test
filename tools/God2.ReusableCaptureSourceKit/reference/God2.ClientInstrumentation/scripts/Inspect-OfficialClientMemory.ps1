[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ClientExe
)

$ErrorActionPreference = "Stop"
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class God2ReadMemory {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
        byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);
}
'@

$clientPath = (Resolve-Path -LiteralPath $ClientExe).Path
$process = Start-Process -FilePath $clientPath `
    -WorkingDirectory (Split-Path -Parent $clientPath) -PassThru
$handle = [IntPtr]::Zero
try {
    $handle = [God2ReadMemory]::OpenProcess(0x410, $false, [uint32]$process.Id)
    if ($handle -eq [IntPtr]::Zero) { throw "OpenProcess failed." }
    $elapsed = 0
    foreach ($delay in @(1, 2, 2, 5)) {
        Start-Sleep -Seconds $delay
        $elapsed += $delay
        $process.Refresh()
        if ($process.HasExited) {
            [pscustomobject]@{ Seconds = $elapsed; Exited = $true }
            break
        }
        $base = $process.MainModule.BaseAddress.ToInt64()
        foreach ($rva in @(0x78A48, 0x78D70, 0x7FC10, 0x147096, 0x1470AE, 0x7F940)) {
            $buffer = New-Object byte[] 16
            $read = [UIntPtr]::Zero
            $ok = [God2ReadMemory]::ReadProcessMemory(
                $handle, [IntPtr]($base + $rva), $buffer,
                [UIntPtr]::new([uint64]16), [ref]$read)
            [pscustomobject]@{
                Seconds = $elapsed
                RVA = "0x{0:X8}" -f $rva
                ReadSucceeded = $ok
                Bytes = ($buffer | ForEach-Object { $_.ToString("X2") }) -join " "
            }
        }
    }
} finally {
    if ($handle -ne [IntPtr]::Zero) {
        [God2ReadMemory]::CloseHandle($handle) | Out-Null
    }
    if ($null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}
