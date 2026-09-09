[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
$schemaRoot = Join-Path $PSScriptRoot 'assets\schemas'
$fixtureRoot = Join-Path $PSScriptRoot 'tests\schema-fixtures'

function Has-JsonProperty {
    param($Object, [Parameter(Mandatory=$true)][string]$Name)
    if ($null -eq $Object) { return $false }
    if ($Object -is [Collections.IDictionary]) {
        return $Object.Contains($Name)
    }
    return $null -ne $Object.PSObject.Properties[$Name]
}

function Get-JsonProperty {
    param($Object, [Parameter(Mandatory=$true)][string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [Collections.IDictionary]) {
        Write-Output -NoEnumerate $Object[$Name]
        return
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    Write-Output -NoEnumerate $property.Value
}

function Get-JsonObjectNames {
    param($Object)
    if ($Object -is [Collections.IDictionary]) {
        return @($Object.Keys | ForEach-Object { [string]$_ })
    }
    return @($Object.PSObject.Properties | ForEach-Object { $_.Name })
}

function Test-JsonObjectValue {
    param($Value)
    return $null -ne $Value -and
        -not ($Value -is [string]) -and
        -not ($Value -is [bool]) -and
        -not ($Value -is [System.Array]) -and
        -not ($Value -is [Collections.IList]) -and
        ($Value -is [Collections.IDictionary] -or
         $Value.PSObject.TypeNames -contains 'System.Management.Automation.PSCustomObject')
}

function Test-JsonArrayValue {
    param($Value)
    return $null -ne $Value -and -not ($Value -is [string]) -and
        ($Value -is [System.Array] -or $Value -is [Collections.IList])
}

function Test-JsonIntegerValue {
    param($Value)
    return $Value -is [sbyte] -or $Value -is [byte] -or
        $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]
}

function Test-JsonNumberValue {
    param($Value)
    return (Test-JsonIntegerValue $Value) -or $Value -is [single] -or
        $Value -is [double] -or $Value -is [decimal]
}

function Test-JsonDeepEqual {
    param($Left, $Right)
    if ($null -eq $Left -or $null -eq $Right) {
        return $null -eq $Left -and $null -eq $Right
    }
    if ((Test-JsonNumberValue $Left) -and (Test-JsonNumberValue $Right)) {
        return [decimal]$Left -eq [decimal]$Right
    }
    if ((Test-JsonArrayValue $Left) -or (Test-JsonArrayValue $Right)) {
        if (-not (Test-JsonArrayValue $Left) -or
            -not (Test-JsonArrayValue $Right)) { return $false }
        $leftItems = @($Left)
        $rightItems = @($Right)
        if ($leftItems.Count -ne $rightItems.Count) { return $false }
        for ($index = 0; $index -lt $leftItems.Count; ++$index) {
            if (-not (Test-JsonDeepEqual $leftItems[$index] $rightItems[$index])) {
                return $false
            }
        }
        return $true
    }
    if ((Test-JsonObjectValue $Left) -or (Test-JsonObjectValue $Right)) {
        if (-not (Test-JsonObjectValue $Left) -or
            -not (Test-JsonObjectValue $Right)) { return $false }
        $leftNames = @(Get-JsonObjectNames $Left | Sort-Object)
        $rightNames = @(Get-JsonObjectNames $Right | Sort-Object)
        if ($leftNames.Count -ne $rightNames.Count) { return $false }
        for ($index = 0; $index -lt $leftNames.Count; ++$index) {
            $leftValue = Get-JsonProperty $Left $leftNames[$index]
            $rightValue = Get-JsonProperty $Right $rightNames[$index]
            if ($leftNames[$index] -cne $rightNames[$index] -or
                -not (Test-JsonDeepEqual $leftValue $rightValue)) {
                return $false
            }
        }
        return $true
    }
    if ($Left -is [string] -or $Right -is [string]) {
        return $Left -is [string] -and $Right -is [string] -and
            [string]$Left -ceq [string]$Right
    }
    if ($Left -is [bool] -or $Right -is [bool]) {
        return $Left -is [bool] -and $Right -is [bool] -and
            [bool]$Left -eq [bool]$Right
    }
    return $Left -eq $Right
}

function Test-JsonType {
    param($Value, [Parameter(Mandatory=$true)][string]$Type)
    switch ($Type) {
        'null' { return $null -eq $Value }
        'object' { return Test-JsonObjectValue $Value }
        'array' { return Test-JsonArrayValue $Value }
        'string' { return $Value -is [string] }
        'boolean' { return $Value -is [bool] }
        'integer' { return Test-JsonIntegerValue $Value }
        'number' { return Test-JsonNumberValue $Value }
        default { throw "Unsupported JSON Schema type in self-test: $Type" }
    }
}

function Resolve-LocalSchemaReference {
    param($RootSchema, [Parameter(Mandatory=$true)][string]$Reference)
    if (-not $Reference.StartsWith('#/')) {
        throw "Only local schema references are supported by the direct self-test: $Reference"
    }
    $current = $RootSchema
    foreach ($rawSegment in $Reference.Substring(2).Split('/')) {
        $segment = $rawSegment.Replace('~1', '/').Replace('~0', '~')
        if (-not (Has-JsonProperty $current $segment)) {
            throw "Unresolved local schema reference: $Reference"
        }
        $current = Get-JsonProperty $current $segment
    }
    return $current
}

function Test-JsonSchemaNode {
    param($Schema, $Instance, $RootSchema)
    if ($Schema -is [bool]) { return [bool]$Schema }
    if (-not (Test-JsonObjectValue $Schema)) {
        $observedType = if ($null -eq $Schema) { 'null' } else { $Schema.GetType().FullName }
        throw "JSON Schema node must be an object or boolean; observed=$observedType"
    }

    if (Has-JsonProperty $Schema '$ref') {
        $resolved = Resolve-LocalSchemaReference $RootSchema ([string](Get-JsonProperty $Schema '$ref'))
        if (-not (Test-JsonSchemaNode $resolved $Instance $RootSchema)) { return $false }
    }
    if (Has-JsonProperty $Schema 'allOf') {
        $allOf = Get-JsonProperty $Schema 'allOf'
        foreach ($candidate in @($allOf)) {
            if (-not (Test-JsonSchemaNode $candidate $Instance $RootSchema)) { return $false }
        }
    }
    if (Has-JsonProperty $Schema 'anyOf') {
        $matched = $false
        $anyOf = Get-JsonProperty $Schema 'anyOf'
        foreach ($candidate in @($anyOf)) {
            if (Test-JsonSchemaNode $candidate $Instance $RootSchema) { $matched = $true; break }
        }
        if (-not $matched) { return $false }
    }
    if (Has-JsonProperty $Schema 'oneOf') {
        $matches = 0
        $oneOf = Get-JsonProperty $Schema 'oneOf'
        foreach ($candidate in @($oneOf)) {
            if (Test-JsonSchemaNode $candidate $Instance $RootSchema) { ++$matches }
        }
        if ($matches -ne 1) { return $false }
    }
    if (Has-JsonProperty $Schema 'not') {
        $notSchema = Get-JsonProperty $Schema 'not'
        if (Test-JsonSchemaNode $notSchema $Instance $RootSchema) { return $false }
    }
    if (Has-JsonProperty $Schema 'if') {
        $ifSchema = Get-JsonProperty $Schema 'if'
        $condition = Test-JsonSchemaNode $ifSchema $Instance $RootSchema
        if ($condition -and (Has-JsonProperty $Schema 'then')) {
            $thenSchema = Get-JsonProperty $Schema 'then'
            if (-not (Test-JsonSchemaNode $thenSchema $Instance $RootSchema)) {
                return $false
            }
        }
        if (-not $condition -and (Has-JsonProperty $Schema 'else')) {
            $elseSchema = Get-JsonProperty $Schema 'else'
            if (-not (Test-JsonSchemaNode $elseSchema $Instance $RootSchema)) {
                return $false
            }
        }
    }
    if (Has-JsonProperty $Schema 'const') {
        $constant = Get-JsonProperty $Schema 'const'
        if (-not (Test-JsonDeepEqual $constant $Instance)) { return $false }
    }
    if (Has-JsonProperty $Schema 'enum') {
        $enumMatch = $false
        $enumValues = Get-JsonProperty $Schema 'enum'
        foreach ($candidate in @($enumValues)) {
            if (Test-JsonDeepEqual $candidate $Instance) { $enumMatch = $true; break }
        }
        if (-not $enumMatch) { return $false }
    }
    if (Has-JsonProperty $Schema 'type') {
        $typeMatch = $false
        $schemaType = Get-JsonProperty $Schema 'type'
        foreach ($candidateType in @($schemaType)) {
            if (Test-JsonType $Instance ([string]$candidateType)) { $typeMatch = $true; break }
        }
        if (-not $typeMatch) { return $false }
    }

    if (Test-JsonObjectValue $Instance) {
        if (Has-JsonProperty $Schema 'required') {
            $requiredNames = Get-JsonProperty $Schema 'required'
            foreach ($requiredName in @($requiredNames)) {
                if (-not (Has-JsonProperty $Instance ([string]$requiredName))) { return $false }
            }
        }
        $properties = Get-JsonProperty $Schema 'properties'
        if ($null -ne $properties) {
            foreach ($propertyName in Get-JsonObjectNames $properties) {
                if (Has-JsonProperty $Instance $propertyName) {
                    $propertySchema = Get-JsonProperty $properties $propertyName
                    $propertyValue = Get-JsonProperty $Instance $propertyName
                    $propertyValid = Test-JsonSchemaNode `
                        $propertySchema $propertyValue $RootSchema
                    if (-not $propertyValid) {
                        return $false
                    }
                }
            }
        }
        $patternProperties = Get-JsonProperty $Schema 'patternProperties'
        if ($null -ne $patternProperties) {
            foreach ($propertyName in Get-JsonObjectNames $Instance) {
                foreach ($pattern in Get-JsonObjectNames $patternProperties) {
                    if ($propertyName -cmatch $pattern) {
                        $patternSchema = Get-JsonProperty $patternProperties $pattern
                        $propertyValue = Get-JsonProperty $Instance $propertyName
                        if (-not (Test-JsonSchemaNode `
                                $patternSchema $propertyValue $RootSchema)) {
                            return $false
                        }
                    }
                }
            }
        }
        $hasAdditional = Has-JsonProperty $Schema 'additionalProperties'
        $additional = if ($hasAdditional) {
            Get-JsonProperty $Schema 'additionalProperties'
        } else { $null }
        if ($hasAdditional -and $additional -is [bool] -and -not [bool]$additional) {
            $allowed = @{}
            if ($null -ne $properties) {
                foreach ($propertyName in Get-JsonObjectNames $properties) {
                    $allowed[$propertyName] = $true
                }
            }
            foreach ($propertyName in Get-JsonObjectNames $Instance) {
                $patternMatched = $false
                if ($null -ne $patternProperties) {
                    foreach ($pattern in Get-JsonObjectNames $patternProperties) {
                        if ($propertyName -cmatch $pattern) {
                            $patternMatched = $true
                            break
                        }
                    }
                }
                if (-not $allowed.ContainsKey($propertyName) -and
                    -not $patternMatched) { return $false }
            }
        }
    }

    if (Test-JsonArrayValue $Instance) {
        $items = @($Instance)
        if (Has-JsonProperty $Schema 'minItems') {
            $minimumItems = [int](Get-JsonProperty $Schema 'minItems')
            if ($items.Count -lt $minimumItems) { return $false }
        }
        if (Has-JsonProperty $Schema 'maxItems') {
            $maximumItems = [int](Get-JsonProperty $Schema 'maxItems')
            if ($items.Count -gt $maximumItems) { return $false }
        }
        $prefixItems = @()
        if (Has-JsonProperty $Schema 'prefixItems') {
            $prefixValue = Get-JsonProperty $Schema 'prefixItems'
            $prefixItems = @($prefixValue)
            $prefixCount = [Math]::Min($items.Count, $prefixItems.Count)
            for ($index = 0; $index -lt $prefixCount; ++$index) {
                if (-not (Test-JsonSchemaNode $prefixItems[$index] $items[$index] $RootSchema)) {
                    return $false
                }
            }
        }
        if (Has-JsonProperty $Schema 'items') {
            $itemSchema = Get-JsonProperty $Schema 'items'
            $start = if ($prefixItems.Count -gt 0) { $prefixItems.Count } else { 0 }
            for ($index = $start; $index -lt $items.Count; ++$index) {
                if (-not (Test-JsonSchemaNode $itemSchema $items[$index] $RootSchema)) {
                    return $false
                }
            }
        }
    }

    if ($Instance -is [string]) {
        if (Has-JsonProperty $Schema 'minLength') {
            $minimumLength = [int](Get-JsonProperty $Schema 'minLength')
            if ($Instance.Length -lt $minimumLength) { return $false }
        }
        if (Has-JsonProperty $Schema 'maxLength') {
            $maximumLength = [int](Get-JsonProperty $Schema 'maxLength')
            if ($Instance.Length -gt $maximumLength) { return $false }
        }
        if (Has-JsonProperty $Schema 'pattern') {
            $pattern = [string](Get-JsonProperty $Schema 'pattern')
            if ($Instance -cnotmatch $pattern) { return $false }
        }
    }
    if (Test-JsonNumberValue $Instance) {
        if (Has-JsonProperty $Schema 'minimum') {
            $minimum = [decimal](Get-JsonProperty $Schema 'minimum')
            if ([decimal]$Instance -lt $minimum) { return $false }
        }
        if (Has-JsonProperty $Schema 'maximum') {
            $maximum = [decimal](Get-JsonProperty $Schema 'maximum')
            if ([decimal]$Instance -gt $maximum) { return $false }
        }
    }
    return $true
}

function Read-StrictJsonFile {
    param([Parameter(Mandatory=$true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "UTF-8 BOM is forbidden: $Path"
    }
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    return $text | ConvertFrom-Json
}

function Read-StrictUtf8Text {
    param([Parameter(Mandatory=$true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "UTF-8 BOM is forbidden: $Path"
    }
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
}

function Copy-JsonValue {
    param($Value)
    return ($Value | ConvertTo-Json -Depth 100 -Compress) | ConvertFrom-Json
}

function Get-OpenObjectSchemaPaths {
    param($Node, [string]$Path = '$')
    $issues = New-Object Collections.Generic.List[string]
    if ($null -eq $Node -or $Node -is [string] -or $Node -is [bool] -or
        (Test-JsonNumberValue $Node)) { return @() }
    if (Test-JsonArrayValue $Node) {
        $index = 0
        foreach ($item in @($Node)) {
            foreach ($issue in @(Get-OpenObjectSchemaPaths $item "$Path[$index]")) {
                $issues.Add($issue)
            }
            ++$index
        }
        return @($issues)
    }
    if (-not (Test-JsonObjectValue $Node)) { return @() }
    $schemaTypes = if (Has-JsonProperty $Node 'type') {
        @((Get-JsonProperty $Node 'type'))
    } else { @() }
    if ($schemaTypes -contains 'object') {
        if (-not (Has-JsonProperty $Node 'additionalProperties') -or
            (Get-JsonProperty $Node 'additionalProperties') -isnot [bool] -or
            [bool](Get-JsonProperty $Node 'additionalProperties')) {
            $issues.Add($Path)
        }
    }
    foreach ($propertyName in Get-JsonObjectNames $Node) {
        $child = Get-JsonProperty $Node $propertyName
        foreach ($issue in @(Get-OpenObjectSchemaPaths $child "$Path.$propertyName")) {
            $issues.Add($issue)
        }
    }
    return @($issues)
}

$results = New-Object Collections.Generic.List[object]
function Add-TestResult {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $results.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Detail=$Detail })
}

$contracts = @(
    [pscustomobject]@{
        Name='injector-result-v2'
        Schema='injector-result-v2.schema.json'
        Fixture='injector-result-v2.positive.json'
        Origin='real product attach evidence: product-evidence-20260809-143402/analysis/attach-result-v2.json'
        TypeMutation={ param($value) $value.code='0' }
        EnumMutation={ param($value) $value.status='ATTACHED_SPOOF' }
    },
    [pscustomobject]@{
        Name='native-semantic-wire-verification-v1'
        Schema='native-semantic-wire-verification-v1.schema.json'
        Fixture='native-semantic-wire-verification-v1.positive.json'
        Origin='real final-reader report: product-drain-20260809-141954/analysis/native-semantic-wire-verification.json'
        TypeMutation={ param($value) $value.inputLineCount='25' }
        EnumMutation={ param($value) $value.rows[0].eventType='CorruptType' }
    },
    [pscustomobject]@{
        Name='deep-probe-candidate-map-v2'
        Schema='deep-probe-candidate-map-v2.schema.json'
        Fixture='deep-probe-candidate-map-v2.positive.json'
        Origin='real identity-blocked probe output: product-drain-20260809-141954/raw/trace/deep-probe-candidate-map.json'
        TypeMutation={ param($value) $value.DomainCount='25' }
        EnumMutation={ param($value) $value.Architecture='x64' }
    },
    [pscustomobject]@{
        Name='semantic-shared-ring-health-v3'
        Schema='semantic-shared-ring-health-v3.schema.json'
        Fixture='semantic-shared-ring-health-v3.positive.json'
        Origin='producer-shaped initial snapshot from Gui.cpp WriteSharedTransportHealth'
        TypeMutation={ param($value) $value.TransportReady='true' }
        EnumMutation={ param($value) $value.Lifecycle='Ready' }
    },
    [pscustomobject]@{
        Name='semantic-shared-ring-health-v4'
        Schema='semantic-shared-ring-health-v4.schema.json'
        Fixture='semantic-shared-ring-health-v4.positive.json'
        Origin='producer-shaped four-lane snapshot from Gui.cpp WriteSharedTransportHealth'
        TypeMutation={ param($value) $value.Sampled='0' }
        EnumMutation={ param($value) $value.Lane00LastDropReason=12 }
    },
    [pscustomobject]@{
        Name='god2-semantic-event-v1'
        Schema='god2-semantic-event-v1.schema.json'
        Fixture='god2-semantic-event-v1.positive.json'
        Origin='fresh Ultimate 02b canonical-v1-semantic-wire.jsonl line 1'
        TypeMutation={ param($value) $value.SchemaVersion='1' }
        EnumMutation={ param($value) $value.EventType='ObjectResolution' }
    },
    [pscustomobject]@{
        Name='god2-semantic-event-v2'
        Schema='god2-semantic-event-v2.schema.json'
        Fixture='god2-semantic-event-v2.positive.json'
        Origin='real ProbeDiagnostic producer line from private-fault-ringnull-runtime2'
        TypeMutation={ param($value) $value.Payload.RuntimeDiagnosticObserved='true' }
        EnumMutation={ param($value) $value.Payload.ContractStatus='CandidateOnlyBlockedContract' }
    }
)

foreach ($contract in $contracts) {
    $schemaPath = Join-Path $schemaRoot $contract.Schema
    $fixturePath = Join-Path $fixtureRoot $contract.Fixture
    $schema = Read-StrictJsonFile $schemaPath
    $fixture = Read-StrictJsonFile $fixturePath
    $expectedId = 'https://god2.local/schemas/' + $contract.Schema
    $identityPassed = [string](Get-JsonProperty $schema '$schema') -ceq
            'https://json-schema.org/draft/2020-12/schema' -and
        [string](Get-JsonProperty $schema '$id') -ceq $expectedId -and
        (Get-JsonProperty $schema 'additionalProperties') -is [bool] -and
        -not [bool](Get-JsonProperty $schema 'additionalProperties') -and
        @(Get-OpenObjectSchemaPaths $schema).Count -eq 0
    Add-TestResult "positive.$($contract.Name).schema-identity-and-closure" `
        $identityPassed $expectedId
    Add-TestResult "positive.$($contract.Name).real-producer-instance" `
        (Test-JsonSchemaNode $schema $fixture $schema) $contract.Origin

    $unknown = Copy-JsonValue $fixture
    $unknown | Add-Member -NotePropertyName 'UntrustedUnknownField' -NotePropertyValue 1
    Add-TestResult "negative.$($contract.Name).unknown-field" `
        (-not (Test-JsonSchemaNode $schema $unknown $schema)) 'closed root rejects unknown property'

    $wrongType = Copy-JsonValue $fixture
    & $contract.TypeMutation $wrongType
    Add-TestResult "negative.$($contract.Name).wrong-type" `
        (-not (Test-JsonSchemaNode $schema $wrongType $schema)) 'typed property mutation rejected'

    $wrongEnum = Copy-JsonValue $fixture
    & $contract.EnumMutation $wrongEnum
    Add-TestResult "negative.$($contract.Name).wrong-enum" `
        (-not (Test-JsonSchemaNode $schema $wrongEnum $schema)) 'enum/const mutation rejected'
}

$v2SchemaName = 'god2-semantic-event-v2.schema.json'
$v2SchemaPath = Join-Path $schemaRoot $v2SchemaName
$v2FixturePath = Join-Path $fixtureRoot 'god2-semantic-event-v2.positive.json'
$v2Schema = Read-StrictJsonFile $v2SchemaPath
$v2Fixture = Read-StrictJsonFile $v2FixturePath
$v2UnknownPayload = Copy-JsonValue $v2Fixture
$v2UnknownPayload.Payload | Add-Member -NotePropertyName 'UntrustedNestedPayload' `
    -NotePropertyValue 'must-not-pass'
Add-TestResult 'negative.god2-semantic-event-v2.unknown-nested-payload-field' `
    (-not (Test-JsonSchemaNode $v2Schema $v2UnknownPayload $v2Schema)) `
    'recursive closure rejects an unknown nested payload property'
$v2MissingPayload = Copy-JsonValue $v2Fixture
$v2MissingPayload.Payload.PSObject.Properties.Remove('RuntimeDiagnosticObserved')
Add-TestResult 'negative.god2-semantic-event-v2.missing-profile-field' `
    (-not (Test-JsonSchemaNode $v2Schema $v2MissingPayload $v2Schema)) `
    'closed ProbeDomainStatus profile rejects a missing required field'
$v2QuotedBoolean = Copy-JsonValue $v2Fixture
$v2QuotedBoolean.Payload.RuntimeActivationObserved = 'false'
Add-TestResult 'negative.god2-semantic-event-v2.quoted-boolean' `
    (-not (Test-JsonSchemaNode $v2Schema $v2QuotedBoolean $v2Schema)) `
    'quoted false cannot satisfy the typed runtime-activation contract'
$v2SourceSwap = Copy-JsonValue $v2Fixture
$v2SourceSwap.SourceToken = 'ObjectProbe'
Add-TestResult 'negative.god2-semantic-event-v2.source-domain-mapping-swap' `
    (-not (Test-JsonSchemaNode $v2Schema $v2SourceSwap $v2Schema)) `
    'SourceToken, Domain and Probe are one closed mapping'
$v2ActivationSpoof = Copy-JsonValue $v2Fixture
$v2ActivationSpoof.Payload.RuntimeActivationObserved = $true
Add-TestResult 'negative.god2-semantic-event-v2.identity-blocked-activation-spoof' `
    (-not (Test-JsonSchemaNode $v2Schema $v2ActivationSpoof $v2Schema)) `
    'identity-blocked confirmed contract cannot claim runtime activation'
$v2SensitiveRawShape = Copy-JsonValue $v2Fixture
$v2SensitiveRawShape.EventType = 'ObjectResolved'
$v2SensitiveRawShape.SourceToken = ''
$v2SensitiveRawShape.Payload = [pscustomobject]@{
    Property='Account'; RawValue='raw-secret'; NormalizedValue='normalized'; Value='secret'
}
Add-TestResult 'negative.god2-semantic-event-v2.raw-sensitive-pass-through-shape' `
    (-not (Test-JsonSchemaNode $v2Schema $v2SensitiveRawShape $v2Schema)) `
    'unclassified sensitive payload cannot enter the canonical v2 profile'

$compatibilitySchemaName = 'god2-semantic-event-v1-compatibility-input.schema.json'
$compatibilitySchemaPath = Join-Path $schemaRoot $compatibilitySchemaName
$compatibilityFixturePath = Join-Path $fixtureRoot `
    'god2-semantic-event-v1-compatibility-input.positive.json'
$compatibilitySchema = Read-StrictJsonFile $compatibilitySchemaPath
$compatibilityFixture = Read-StrictJsonFile $compatibilityFixturePath
$compatibilityExpectedId = 'https://god2.local/schemas/' + $compatibilitySchemaName
$compatibilityOpenPaths = @(Get-OpenObjectSchemaPaths $compatibilitySchema)
Add-TestResult 'positive.god2-semantic-event-v1-compatibility-input.schema-identity-and-explicit-root-open-profile' `
    ([string](Get-JsonProperty $compatibilitySchema '$schema') -ceq
            'https://json-schema.org/draft/2020-12/schema' -and
        [string](Get-JsonProperty $compatibilitySchema '$id') -ceq $compatibilityExpectedId -and
        (Get-JsonProperty $compatibilitySchema 'additionalProperties') -is [bool] -and
        [bool](Get-JsonProperty $compatibilitySchema 'additionalProperties') -and
        $compatibilityOpenPaths.Count -eq 1 -and $compatibilityOpenPaths[0] -ceq '$') `
    'only the explicitly named noncanonical input root is open'
Add-TestResult 'positive.god2-semantic-event-v1-compatibility-input.real-reader-instance' `
    (Test-JsonSchemaNode $compatibilitySchema $compatibilityFixture $compatibilitySchema) `
    'explicit fixture-only/noncanonical/nonpromotable ObjectResolution/EventId input'

$compatibilityUnknown = Copy-JsonValue $compatibilityFixture
$compatibilityUnknown | Add-Member -NotePropertyName 'SecondLegacyExtension' `
    -NotePropertyValue 'accepted-only-by-noncanonical-input-profile'
Add-TestResult 'positive.god2-semantic-event-v1-compatibility-input.unknown-extension-explicitly-accepted' `
    (Test-JsonSchemaNode $compatibilitySchema $compatibilityUnknown $compatibilitySchema) `
    'compatibility input accepts legacy extensions but is never a promotion authority'
$compatibilityWrongType = Copy-JsonValue $compatibilityFixture
$compatibilityWrongType.SchemaVersion = '1'
Add-TestResult 'negative.god2-semantic-event-v1-compatibility-input.wrong-type' `
    (-not (Test-JsonSchemaNode $compatibilitySchema $compatibilityWrongType $compatibilitySchema)) `
    'quoted SchemaVersion is rejected'
$compatibilityWrongEnum = Copy-JsonValue $compatibilityFixture
$compatibilityWrongEnum.EventType = 'ObjectResolutionSpoof'
Add-TestResult 'negative.god2-semantic-event-v1-compatibility-input.wrong-enum' `
    (-not (Test-JsonSchemaNode $compatibilitySchema $compatibilityWrongEnum $compatibilitySchema)) `
    'unknown compatibility EventType is rejected'
$compatibilityMissingIdentity = Copy-JsonValue $compatibilityFixture
$compatibilityMissingIdentity.PSObject.Properties.Remove('EventId')
Add-TestResult 'negative.god2-semantic-event-v1-compatibility-input.missing-event-identity' `
    (-not (Test-JsonSchemaNode $compatibilitySchema $compatibilityMissingIdentity $compatibilitySchema)) `
    'SemanticEventId or EventId remains mandatory'

$evidenceSourcePath = Join-Path $RepositoryRoot 'tools\God2.PacketCapture\EvidencePackage.cpp'
$evidenceSource = Read-StrictUtf8Text $evidenceSourcePath
$canonicalMatch = [regex]::Match($evidenceSource,
    'std::string\s+SemanticEventV1Schema\(\)\s*\{\s*return R"\((?<schema>.*?)\)";\s*\}',
    [Text.RegularExpressions.RegexOptions]::Singleline)
$compatibilityMatch = [regex]::Match($evidenceSource,
    'std::string\s+SemanticEventV1CompatibilityInputSchema\(\)\s*\{\s*return R"\((?<schema>.*?)\)";\s*\}',
    [Text.RegularExpressions.RegexOptions]::Singleline)
$canonicalAssetText = Read-StrictUtf8Text `
    (Join-Path $schemaRoot 'god2-semantic-event-v1.schema.json')
$compatibilityAssetText = Read-StrictUtf8Text $compatibilitySchemaPath
Add-TestResult 'positive.semantic-v1.assets-byte-identical-to-evidence-builtins' `
    ($canonicalMatch.Success -and $compatibilityMatch.Success -and
        $canonicalAssetText -ceq ($canonicalMatch.Groups['schema'].Value + "`n") -and
        $compatibilityAssetText -ceq ($compatibilityMatch.Groups['schema'].Value + "`n")) `
    'canonical and compatibility schemas have exact UTF-8 LF built-in/asset parity'

$generatedV2HeaderPath = Join-Path $RepositoryRoot `
    'tools\God2.PacketCapture\SemanticEventV2Schema.generated.h'
$generatedV2Header = Read-StrictUtf8Text $generatedV2HeaderPath
$generatedV2Matches = [regex]::Matches($generatedV2Header,
    'R"(?<delimiter>G2V2C\d+)\((?<chunk>.*?)\)\k<delimiter>"',
    [Text.RegularExpressions.RegexOptions]::Singleline)
$generatedV2Schema = [string]::Concat(@($generatedV2Matches | ForEach-Object {
    $_.Groups['chunk'].Value
}))
$v2AssetText = Read-StrictUtf8Text $v2SchemaPath
Add-TestResult 'positive.semantic-v2.asset-byte-identical-to-evidence-generated-builtin' `
    ($generatedV2Matches.Count -gt 1 -and $v2AssetText -ceq $generatedV2Schema) `
    'generated closed schema has exact UTF-8 LF built-in/asset parity'
$v2OpenPaths = @(Get-OpenObjectSchemaPaths $v2Schema)
$v2DefinitionValue = Get-JsonProperty $v2Schema '$defs'
Add-TestResult 'positive.semantic-v2.recursive-closure-and-profile-count' `
    ($v2OpenPaths.Count -eq 0 -and
        @(Get-JsonObjectNames $v2DefinitionValue).Count -eq 41) `
    'all payload definitions are recursively closed and the profile family count is 41'

$registryPath = Join-Path $schemaRoot 'schema-registry.json'
$registry = Read-StrictJsonFile $registryPath
$entryValue = Get-JsonProperty $registry 'Entries'
$entries = @($entryValue)
$schemaFiles = @(Get-ChildItem -LiteralPath $schemaRoot -File -Filter '*.schema.json')
$registeredNames = @($entries | ForEach-Object { [string]$_.SchemaFile })
$registeredIds = @($entries | ForEach-Object { [string]$_.SchemaId })
$registeredPatterns = @($entries | ForEach-Object { [string]$_.ArtifactPattern })
$expectedRegistryCount = $schemaFiles.Count
$registryPassed = [string](Get-JsonProperty $registry 'SchemaVersion') -ceq
        'god2-ultimate-schema-registry-v1' -and
    $expectedRegistryCount -gt 0 -and $entries.Count -eq $expectedRegistryCount -and
    @($registeredNames | Sort-Object -Unique).Count -eq $expectedRegistryCount -and
    @($registeredIds | Sort-Object -Unique).Count -eq $expectedRegistryCount -and
    @($registeredPatterns | Sort-Object -Unique).Count -eq $expectedRegistryCount -and
    @($schemaFiles | Where-Object { $registeredNames -notcontains $_.Name }).Count -eq 0
foreach ($contract in $contracts) {
    $expectedId = 'https://god2.local/schemas/' + $contract.Schema
    $registryPassed = $registryPassed -and $registeredNames -contains $contract.Schema -and
        $registeredIds -contains $expectedId
}
Add-TestResult 'positive.schema-registry.exact-file-id-mapping' $registryPassed `
    "entries=$($entries.Count); schemaFiles=$($schemaFiles.Count)"

$statusV3RegistryPassed = @($entries | Where-Object {
        [string]$_.SchemaFile -ceq 'enhanced-capture-status-v3.schema.json' -and
        [string]$_.SchemaId -ceq 'https://god2.local/schemas/enhanced-capture-status-v3.schema.json' -and
        [string]$_.ArtifactPattern -ceq 'reports/evidence/**/enhanced-capture-status-v3.json'
    }).Count -eq 1
Add-TestResult 'positive.schema-registry.bridge-aware-status-v3-registered-once' `
    $statusV3RegistryPassed 'closed bridge-aware strict-unload authority has one exact file/id/pattern registration'

$acceptanceSchemaAssetPath = Join-Path $schemaRoot 'enhanced-capture-acceptance.schema.json'
$acceptanceSchemaSourcePath = Join-Path $RepositoryRoot `
    'tools\God2.PacketCapture\validation\enhanced-capture-acceptance.schema.json'
$acceptanceSchemaAssetBytes = [IO.File]::ReadAllBytes($acceptanceSchemaAssetPath)
$acceptanceSchemaSourceBytes = [IO.File]::ReadAllBytes($acceptanceSchemaSourcePath)
$acceptanceSchemasEqual = $acceptanceSchemaAssetBytes.Length -eq $acceptanceSchemaSourceBytes.Length
if ($acceptanceSchemasEqual) {
    for ($index = 0; $index -lt $acceptanceSchemaAssetBytes.Length; ++$index) {
        if ($acceptanceSchemaAssetBytes[$index] -ne $acceptanceSchemaSourceBytes[$index]) {
            $acceptanceSchemasEqual = $false
            break
        }
    }
}
Add-TestResult 'positive.acceptance-schema.validation-and-bundled-assets-byte-identical' `
    $acceptanceSchemasEqual 'formal validator and packaged schema are the same UTF-8 bytes'

$compatibilityRegistryPassed = $registeredNames -contains $compatibilitySchemaName -and
    $registeredIds -contains $compatibilityExpectedId -and
    @($entries | Where-Object {
        [string]$_.SchemaFile -ceq $compatibilitySchemaName -and
        [string]$_.ArtifactPattern -ceq
            'semantic-event-input:SchemaVersion=1;Profile=NonCanonicalV1CompatibilityInput'
    }).Count -eq 1
Add-TestResult 'positive.schema-registry.compatibility-input-explicitly-noncanonical' `
    $compatibilityRegistryPassed 'the sole open input schema is named and patterned as noncanonical'

$v2RegistryPassed = @($entries | Where-Object {
        [string]$_.SchemaFile -ceq $v2SchemaName -and
        [string]$_.SchemaId -ceq
            'https://god2.local/schemas/god2-semantic-event-v2.schema.json' -and
        [string]$_.ArtifactPattern -ceq
            'semantic-event-line:SchemaVersion=2;Profile=ClosedCanonicalPayload'
    }).Count -eq 1
Add-TestResult 'positive.schema-registry.closed-v2-profile-registered-once' `
    $v2RegistryPassed 'closed canonical v2 payload schema has one exact registry mapping'

$availabilityPreserved = $registeredNames -contains
        'semantic-shared-ring-health-availability.schema.json' -and
    $registeredNames -contains 'deep-probe-candidate-map-availability.schema.json'
Add-TestResult 'positive.schema-registry.availability-schemas-preserved' `
    $availabilityPreserved 'availability schemas remain separately registered'

foreach ($result in $results) {
    $prefix = if ($result.Passed) { 'PASS' } else { 'FAIL' }
    Write-Output "$prefix $($result.Name) - $($result.Detail)"
}
$passed = @($results | Where-Object { $_.Passed }).Count
$failed = $results.Count - $passed
Write-Output "closedMachineSchemaSelfTest=$($results.Count) passed=$passed failed=$failed"
if ($failed -ne 0) { exit 1 }
