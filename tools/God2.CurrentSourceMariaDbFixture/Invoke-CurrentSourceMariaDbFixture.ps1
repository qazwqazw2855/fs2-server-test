param(
    [Parameter(Mandatory = $false)]
    [string] $Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string] $BuildIdentity = ""
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$project = Join-Path $PSScriptRoot "God2.CurrentSourceMariaDbFixture.csproj"

& dotnet build $project -c $Configuration | Write-Output
if ($LASTEXITCODE -ne 0) {
    throw "CurrentSourceMariaDbFixture build failed."
}

$output = Join-Path $PSScriptRoot "bin\$Configuration"
$frameworkDirectory = Get-ChildItem -LiteralPath $output -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($null -eq $frameworkDirectory) {
    throw "CurrentSourceMariaDbFixture output was not found under tools\God2.CurrentSourceMariaDbFixture\bin\$Configuration."
}

$assembly = Join-Path $frameworkDirectory.FullName "God2.CurrentSourceMariaDbFixture.dll"
$arguments = @()
if ($BuildIdentity.Length -gt 0) {
    $arguments += "--build-identity"
    $arguments += $BuildIdentity
}

& dotnet $assembly @arguments
if ($LASTEXITCODE -ne 0) {
    throw "CurrentSourceMariaDbFixture execution failed."
}
