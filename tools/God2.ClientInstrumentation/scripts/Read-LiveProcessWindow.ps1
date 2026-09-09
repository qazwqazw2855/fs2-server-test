param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][uint32]$Address,
    [int]$Length = 1024,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LiveWindowNative {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@

$handle = [LiveWindowNative]::OpenProcess(0x410, $false, $ProcessId)
if ($handle -eq [IntPtr]::Zero) { throw "OpenProcess failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
try {
    $buffer = New-Object byte[] $Length
    $read = [IntPtr]::Zero
    if (![LiveWindowNative]::ReadProcessMemory($handle, [IntPtr]([int64]$Address), $buffer, [IntPtr]$Length, [ref]$read)) {
        throw "ReadProcessMemory failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
    $actual = [int]$read.ToInt64()
    $calls = @()
    $absolute = @()
    for ($i = 0; $i -le $actual - 5; $i++) {
        if ($buffer[$i] -eq 0xE8) {
            $relative = [BitConverter]::ToInt32($buffer, $i + 1)
            $calls += [pscustomobject]@{
                At = ('0x{0:X8}' -f ([uint64]$Address + [uint64]$i))
                Target = ('0x{0:X8}' -f ([int64]$Address + $i + 5 + $relative))
            }
        }
        $value = [BitConverter]::ToUInt32($buffer, $i)
        if ($value -ge 0x00400000 -and $value -lt 0x40000000) {
            $absolute += [pscustomobject]@{
                At = ('0x{0:X8}' -f ([uint64]$Address + [uint64]$i))
                Value = ('0x{0:X8}' -f $value)
            }
        }
    }
    $result = [ordered]@{
        ProcessId = $ProcessId
        Address = ('0x{0:X8}' -f $Address)
        BytesRead = $actual
        Hex = ([BitConverter]::ToString($buffer, 0, $actual)).Replace('-', '')
        RelativeCalls = $calls
        AbsoluteCandidates = $absolute
    }
    $directory = Split-Path -Parent $OutputPath
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 5
}
finally { [void][LiveWindowNative]::CloseHandle($handle) }
