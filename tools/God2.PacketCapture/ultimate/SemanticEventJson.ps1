Set-StrictMode -Version 2.0

Add-Type -AssemblyName System.Runtime.Serialization

function Get-God2SemanticJsonChild {
    param(
        [Parameter(Mandatory = $true)] [Xml.XmlNode] $Parent,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    foreach ($child in $Parent.ChildNodes) {
        if ($child.NodeType -eq [Xml.XmlNodeType]::Element -and
            [string]$child.LocalName -ceq $Name) {
            return $child
        }
    }
    return $null
}

function Get-God2SemanticJsonText {
    param(
        [Xml.XmlNode] $Parent,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    if ($null -eq $Parent) { return $null }
    $child = Get-God2SemanticJsonChild -Parent $Parent -Name $Name
    if ($null -eq $child -or [string]$child.GetAttribute('type') -ceq 'null') {
        return $null
    }
    return [string]$child.InnerText
}

function ConvertFrom-God2SemanticEventJson {
    param([Parameter(Mandatory = $true)] [string] $Json)

    $reader = $null
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Json)
        $reader = [Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader(
            $bytes, [Xml.XmlDictionaryReaderQuotas]::Max)
        $document = [Xml.XmlDocument]::new()
        $document.Load($reader)
    }
    catch {
        throw [FormatException]::new('Semantic event JSON is invalid.', $_.Exception)
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
    }

    $root = $document.DocumentElement
    if ($null -eq $root -or [string]$root.GetAttribute('type') -cne 'object') {
        throw [FormatException]::new('Semantic event JSON root must be an object.')
    }

    foreach ($objectNode in @($document.SelectNodes('//*[@type="object"]'))) {
        $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($child in $objectNode.ChildNodes) {
            if ($child.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            if (-not $keys.Add([string]$child.LocalName)) {
                throw [FormatException]::new(
                    "Semantic event JSON contains duplicate key '$($child.LocalName)'.")
            }
        }
    }

    $payloadNode = Get-God2SemanticJsonChild -Parent $root -Name 'Payload'
    $sequenceText = Get-God2SemanticJsonText -Parent $root -Name 'Sequence'
    $fixtureOnlyText = Get-God2SemanticJsonText -Parent $payloadNode -Name 'FixtureOnly'
    return [pscustomobject][ordered]@{
        EventType = Get-God2SemanticJsonText -Parent $root -Name 'EventType'
        EventId = Get-God2SemanticJsonText -Parent $root -Name 'EventId'
        Sequence = if ([string]::IsNullOrWhiteSpace($sequenceText)) {
            $null
        } else { [uint64]$sequenceText }
        SourceToken = Get-God2SemanticJsonText -Parent $root -Name 'SourceToken'
        AuthorityHint = Get-God2SemanticJsonText -Parent $root -Name 'AuthorityHint'
        Payload = [pscustomobject][ordered]@{
            Domain = Get-God2SemanticJsonText -Parent $payloadNode -Name 'Domain'
            FixtureOnly = if ([string]::IsNullOrWhiteSpace($fixtureOnlyText)) {
                $null
            } else { [bool]::Parse($fixtureOnlyText) }
        }
    }
}
