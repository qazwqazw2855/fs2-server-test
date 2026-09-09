function Get-LiveStageEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$CampaignPath,
        [Parameter(Mandatory)]
        [string]$EvidencePath,
        [Parameter(Mandatory)]
        [string]$ExporterPath,
        [Parameter(Mandatory)]
        [string]$StatePath,
        [Parameter(Mandatory)]
        [string]$TraceRoot
    )

    if (-not (Test-Path -LiteralPath $CampaignPath -PathType Leaf)) { return $null }

    $exporterSha256 = (Get-FileHash -LiteralPath $ExporterPath -Algorithm SHA256).Hash
    $campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $evidence = $null
    if (Test-Path -LiteralPath $EvidencePath -PathType Leaf) {
        try { $evidence = Get-Content -LiteralPath $EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json }
        catch { $evidence = $null }
    }

    $campaignComplete = [string]$campaign.status -match '^STAGE_\d+_CORRELATION_COMPLETE(?:_WITH_CONTAMINATION)?$'
    $evidenceMatchesCampaign = $null -ne $evidence -and
        [string]$evidence.campaignId -eq [string]$campaign.campaignId
    $evidenceMatchesAnalyzer = $null -ne $evidence -and
        [string]$evidence.analyzerSha256 -eq $exporterSha256
    $evidenceUsable = $evidenceMatchesCampaign -and
        $evidenceMatchesAnalyzer -and
        [string]$evidence.status -notmatch '_EVIDENCE_BLOCKED$'
    if (-not $campaignComplete -or -not $evidenceUsable) {
        & $ExporterPath -StatePath $StatePath -TraceRoot $TraceRoot | Out-Null
        $campaign = Get-Content -LiteralPath $CampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $evidence = Get-Content -LiteralPath $EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }

    if ([string]$evidence.campaignId -ne [string]$campaign.campaignId) {
        throw "Stage evidence campaign mismatch: campaign '$($campaign.campaignId)', evidence '$($evidence.campaignId)'."
    }
    if ([string]$evidence.analyzerSha256 -ne $exporterSha256) {
        $evidence | Add-Member -NotePropertyName analyzerSha256 -NotePropertyValue $exporterSha256 -Force
        $temporary = "$EvidencePath.tmp-$PID"
        [IO.File]::WriteAllText($temporary, ($evidence | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $EvidencePath -Force
    }
    return $evidence
}

function Get-LiveStageUpperSequence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$AnalysisRoot,
        [Parameter(Mandatory)]
        [ValidateRange(1, 1000)]
        [int]$Stage
    )

    $nextCampaignPath = Join-Path $AnalysisRoot ("stage{0}-campaign.json" -f ($Stage + 1))
    if (-not (Test-Path -LiteralPath $nextCampaignPath -PathType Leaf)) { return [int64]::MaxValue }
    $nextCampaign = Get-Content -LiteralPath $nextCampaignPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return [int64]$nextCampaign.baseline.maximumMetadataSequence
}

function Test-LiveEvidenceStatus {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object]$Evidence,
        [Parameter(Mandatory)]
        [string[]]$AcceptedStatus
    )

    return $null -ne $Evidence -and [string]$Evidence.status -in $AcceptedStatus
}
