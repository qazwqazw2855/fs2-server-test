param(
    [int64] $LauncherHwnd = 1771934
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
Initialize-God2NativeApi

$hwnd = [IntPtr]$LauncherHwnd
$rect = New-Object God2Automation.NativeApi+RECT
[God2Automation.NativeApi]::GetClientRect($hwnd, [ref]$rect) | Out-Null
$origin = New-Object God2Automation.NativeApi+POINT
$origin.X = 0
$origin.Y = 0
[God2Automation.NativeApi]::ClientToScreen($hwnd, [ref]$origin) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
[pscustomobject]@{
    hwnd = $LauncherHwnd
    clientLeft = $origin.X
    clientTop = $origin.Y
    width = $width
    height = $height
    agreementX = [int]($origin.X + $width * 0.248)
    agreementY = [int]($origin.Y + $height * 0.476)
    startX = [int]($origin.X + $width * 0.434)
    startY = [int]($origin.Y + $height * 0.438)
} | ConvertTo-Json
