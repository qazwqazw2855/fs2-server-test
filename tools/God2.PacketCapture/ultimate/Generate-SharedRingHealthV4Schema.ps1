param(
    [string] $SchemaPath = (Join-Path $PSScriptRoot "assets\schemas\semantic-shared-ring-health-v4.schema.json"),
    [string] $FixturePath = (Join-Path $PSScriptRoot "tests\schema-fixtures\semantic-shared-ring-health-v4.positive.json")
)

$ErrorActionPreference = "Stop"
$integer = [ordered]@{ type = "integer"; minimum = 0; maximum = 2147483647 }
$binary = [ordered]@{ type = "integer"; enum = @(0, 1) }
$dropReason = [ordered]@{ type = "integer"; enum = @(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11) }

$required = [Collections.Generic.List[string]]::new()
$properties = [ordered]@{}
function Add-Field([string] $Name, $Contract) {
    $required.Add($Name)
    $properties[$Name] = $Contract
}

Add-Field "SchemaVersion" ([ordered]@{ const = "god2-semantic-shared-ring-health-v4" })
Add-Field "UpdatedAtUtc" ([ordered]@{
    type = "string"
    pattern = '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z$'
})
Add-Field "Lifecycle" ([ordered]@{
    enum = @("WaitingForProducer", "StoppedAndDrained", "EvidenceBlockedSemanticEvidenceIncomplete")
})
Add-Field "TransportReady" ([ordered]@{ type = "boolean" })
Add-Field "ConsumerIoFailure" ([ordered]@{ type = "boolean" })
Add-Field "AttachedTargetProcessId" ([ordered]@{ type = "integer"; minimum = 0; maximum = 4294967295 })
Add-Field "Consumed" $integer
Add-Field "InvalidPayloads" $integer
foreach ($name in @("ProducerReady", "ProducerClosed", "ConsumerReady", "ConsumerClosed", "ConsumerFailure")) {
    Add-Field $name $binary
}
Add-Field "ProducerProcessId" ([ordered]@{ type = "integer"; minimum = 0; maximum = 4294967295 })
foreach ($name in @("Attempted", "Accepted", "Dropped", "Sampled")) { Add-Field $name $integer }
Add-Field "HighWaterMark" ([ordered]@{ type = "integer"; minimum = 0; maximum = 256 })
Add-Field "ConsumerLag" ([ordered]@{ type = "integer"; minimum = 0; maximum = 256 })

for ($priority = 0; $priority -lt 4; $priority++) {
    $prefix = "Lane0$priority"
    foreach ($name in @("Accepted", "Consumed", "Dropped", "Sampled", "FirstDroppedSequence", "LastDroppedSequence")) {
        Add-Field ($prefix + $name) $integer
    }
    Add-Field ($prefix + "LastDropReason") $dropReason
    Add-Field ($prefix + "HighWaterMark") ([ordered]@{ type = "integer"; minimum = 0; maximum = 64 })
    Add-Field ($prefix + "ConsumerLag") ([ordered]@{ type = "integer"; minimum = 0; maximum = 64 })
}

for ($domain = 0; $domain -lt 25; $domain++) {
    $prefix = "Domain" + $domain.ToString("00")
    foreach ($name in @("Accepted", "Dropped", "FirstDroppedSequence", "LastDroppedSequence")) {
        Add-Field ($prefix + $name) $integer
    }
    Add-Field ($prefix + "LastDropReason") $dropReason
    Add-Field ($prefix + "HighWaterMark") ([ordered]@{ type = "integer"; minimum = 0; maximum = 256 })
    Add-Field ($prefix + "ConsumerLag") ([ordered]@{ type = "integer"; minimum = 0; maximum = 256 })
    Add-Field ($prefix + "WriteFailures") $integer
}
Add-Field "WriteFailures" $integer

$schema = [ordered]@{
    '$schema' = "https://json-schema.org/draft/2020-12/schema"
    '$id' = "https://god2.local/schemas/semantic-shared-ring-health-v4.schema.json"
    title = "God2 semantic shared-ring health v4"
    '$comment' = "Closed 257-field snapshot for the four-lane, 25-domain GSR4 transport. P0/P1 loss invariants are enforced by runtime acceptance in addition to this structural schema."
    type = "object"
    additionalProperties = $false
    required = @($required)
    properties = $properties
}

$fixture = [ordered]@{}
foreach ($name in $required) {
    $fixture[$name] = 0
}
$fixture.SchemaVersion = "god2-semantic-shared-ring-health-v4"
$fixture.UpdatedAtUtc = "2026-08-10T00:00:00.000Z"
$fixture.Lifecycle = "WaitingForProducer"
$fixture.TransportReady = $true
$fixture.ConsumerIoFailure = $false

$encoding = [Text.UTF8Encoding]::new($false, $true)
[IO.File]::WriteAllText($SchemaPath, ($schema | ConvertTo-Json -Depth 12) + "`n", $encoding)
[IO.File]::WriteAllText($FixturePath, ($fixture | ConvertTo-Json -Depth 4 -Compress) + "`n", $encoding)
