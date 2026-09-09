param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [uint32]$StartAddress = 0x00470000,
    [uint32]$TargetAddress = 0x004900E3,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HandlerBaseNative {
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
'@

$handle = [HandlerBaseNative]::OpenProcess(0x410, $false, $ProcessId)
if ($handle -eq [IntPtr]::Zero) { throw "OpenProcess failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
try {
    $length = [int]($TargetAddress - $StartAddress + 256)
    $buffer = New-Object byte[] $length
    $read = [IntPtr]::Zero
    if (![HandlerBaseNative]::ReadProcessMemory($handle, [IntPtr]([int64]$StartAddress), $buffer, [IntPtr]$length, [ref]$read)) {
        throw "ReadProcessMemory failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
    $actual = [int]$read.ToInt64()
    $entries = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -le $actual - 96; $i++) {
        if ($buffer[$i] -eq 0x55 -and $buffer[$i+1] -eq 0x8B -and $buffer[$i+2] -eq 0xEC) {
            $hex = ([BitConverter]::ToString($buffer, $i, 96)).Replace('-', '')
            $entries.Add([pscustomobject]@{
                Address = ('0x{0:X8}' -f ([uint64]$StartAddress + [uint64]$i))
                HasMovEbxEcx = $hex.Contains('8BD9')
                Hex96 = $hex
            })
        }
    }
    $ebxLoads = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -le $actual - 6; $i++) {
        if ($buffer[$i] -eq 0x8B -and $buffer[$i+1] -eq 0x1D) {
            $source = [BitConverter]::ToUInt32($buffer, $i + 2)
            $ebxLoads.Add([pscustomobject]@{
                Address = ('0x{0:X8}' -f ([uint64]$StartAddress + [uint64]$i))
                SourceAddress = ('0x{0:X8}' -f $source)
            })
        }
    }
    $result = [ordered]@{
        StartAddress = ('0x{0:X8}' -f $StartAddress)
        TargetAddress = ('0x{0:X8}' -f $TargetAddress)
        BytesRead = $actual
        NearestFunctionEntries = @($entries | Select-Object -Last 24)
        NearestAbsoluteEbxLoads = @($ebxLoads | Select-Object -Last 24)
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    $result | ConvertTo-Json -Depth 6
}
finally { [void][HandlerBaseNative]::CloseHandle($handle) }
