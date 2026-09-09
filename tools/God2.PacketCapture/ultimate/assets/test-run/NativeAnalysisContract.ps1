$script:UltimateMandatoryNativeAnalysisSources = @(
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/ActionGrouping.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/AutomaticSemantic.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/CaptureBackends.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/Core.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/EtwConsumer.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/EvidencePackage.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/Gameplay.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/GpuAcceleration.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/Gui.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/Main.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/Rtx5070Validation.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/SemanticRecovery.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/Storage.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/Transport.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.PacketCapture/UltimateRecovery.cpp'; Architecture = 'x64' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.ClientInstrumentation/src/God2ClientTraceLauncher.cpp'; Architecture = 'x86' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.ClientInstrumentation/src/God2ClientTraceProbe.cpp'; Architecture = 'x86' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.ClientInstrumentation/src/God2PacketCaptureInjector.cpp'; Architecture = 'x86' }
    [pscustomobject][ordered]@{ RelativePath = 'tools/God2.ClientInstrumentation/src/God2TraceSelfTestClient.cpp'; Architecture = 'x86' }
)

function Get-UltimateMandatoryNativeAnalysisSources {
    return @($script:UltimateMandatoryNativeAnalysisSources)
}

function Assert-UltimateRepositoryNativeSourceInventory {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $repository = [System.IO.Path]::GetFullPath($RepositoryRoot)
    $expected = @{}
    foreach ($entry in $script:UltimateMandatoryNativeAnalysisSources) {
        $key = ([string]$entry.RelativePath).ToLowerInvariant()
        if ($expected.ContainsKey($key)) { throw "Duplicate mandatory native source contract: $($entry.RelativePath)" }
        $expected[$key] = $entry
    }

    $discovered = @{}
    foreach ($root in @(
            [pscustomobject]@{ Directory = 'tools/God2.PacketCapture'; Prefix = 'tools/God2.PacketCapture' },
            [pscustomobject]@{ Directory = 'tools/God2.ClientInstrumentation/src'; Prefix = 'tools/God2.ClientInstrumentation/src' })) {
        $directory = [System.IO.Path]::GetFullPath((Join-Path $repository ([string]$root.Directory).Replace('/', '\')))
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            throw "First-party native source directory is missing: $($root.Directory)"
        }
        foreach ($file in @(Get-ChildItem -LiteralPath $directory -File -Filter '*.cpp')) {
            $relative = ([string]$root.Prefix) + '/' + $file.Name
            $key = $relative.ToLowerInvariant()
            if ($discovered.ContainsKey($key)) { throw "Duplicate discovered first-party native source: $relative" }
            $discovered[$key] = $relative
        }
    }

    $missing = @($expected.Keys | Where-Object { -not $discovered.ContainsKey($_) } | Sort-Object)
    $unexpected = @($discovered.Keys | Where-Object { -not $expected.ContainsKey($_) } | ForEach-Object { $discovered[$_] } | Sort-Object)
    if ($missing.Count -ne 0 -or $unexpected.Count -ne 0 -or $discovered.Count -ne $expected.Count) {
        throw ('First-party native source contract changed; update analyzer inventory before release. Missing=[{0}] Unexpected=[{1}]' -f
            (($missing | ForEach-Object { [string]$expected[$_].RelativePath }) -join ', '), ($unexpected -join ', '))
    }
}
