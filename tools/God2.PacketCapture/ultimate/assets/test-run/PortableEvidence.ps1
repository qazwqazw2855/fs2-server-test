Set-StrictMode -Version 2.0

$script:UltimatePortableTextExtensions = @(
    '.json', '.jsonl', '.xml', '.trx', '.txt', '.md', '.csv', '.log', '.sarif', '.props', '.targets',
    '.cmd', '.bat', '.ps1'
)
$script:UltimatePortableTrxName = 'PORTABLE_TEST_RUN'
$script:UltimatePortableTrxUser = 'PORTABLE_USER'
$script:UltimatePortableTrxComputer = 'PORTABLE_COMPUTER'
$script:UltimatePortableTrxDeploymentRoot = 'PORTABLE_DEPLOYMENT_ROOT'
$script:UltimatePortableForbiddenEnvironmentTokens = @(
    [string]$env:USERNAME, [string]$env:COMPUTERNAME, [string]$env:USERDOMAIN
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.Length -ge 4 } | Select-Object -Unique

function Test-UltimateAbsoluteLocalPathText {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $patterns = @(
        '(?i)(?<![A-Za-z0-9+.-])[A-Z]:(?:\\+|/)',
        '(?i)file:/{2,3}(?:[A-Z]:|home/|Users/|private/|var/|tmp/)',
        '\\{2,}[A-Za-z0-9_.-]+\\+[A-Za-z0-9$_. -]+',
        '(?i)(?:^|[\s=:"''])/(?:home|Users|private|var/folders|tmp)/'
    )
    foreach ($pattern in $patterns) {
        if ([regex]::IsMatch($Text, $pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)) {
            return $true
        }
    }
    return $false
}

function Assert-UltimatePortableText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory = $true)][string]$Context
    )
    foreach ($codePoint in @(0xFFFD, 0x9225, 0x9286, 0x951B, 0x9359, 0x6D60)) {
        if ($Text.IndexOf([char]$codePoint) -ge 0) {
            throw "Portable evidence contains replacement or known mojibake characters: $Context"
        }
    }
    foreach ($character in $Text.ToCharArray()) {
        $codePoint = [int]$character
        $category = [char]::GetUnicodeCategory($character)
        if (($category -eq [Globalization.UnicodeCategory]::Control -and $codePoint -notin @(9, 10, 13)) -or
            $category -eq [Globalization.UnicodeCategory]::Format) {
            throw "Portable evidence contains prohibited control or format characters: $Context"
        }
    }
    foreach ($token in $script:UltimatePortableForbiddenEnvironmentTokens) {
        if ($Text.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Portable evidence contains a local user or machine identity token: $Context"
        }
    }
    if (Test-UltimateAbsoluteLocalPathText $Text) {
        throw "Portable evidence contains an absolute local path: $Context"
    }
}

function Assert-UltimatePortableObjectStrings {
    param(
        [object]$Value,
        [Parameter(Mandatory = $true)][string]$Context,
        [int]$Depth = 0
    )
    if ($Depth -gt 128) { throw "Portable evidence object exceeds bounded nesting: $Context" }
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        Assert-UltimatePortableText ([string]$Value) $Context
        return
    }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            Assert-UltimatePortableObjectStrings $Value[$key] $Context ($Depth + 1)
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) { Assert-UltimatePortableObjectStrings $item $Context ($Depth + 1) }
        return
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.MemberType -in @('NoteProperty', 'Property')) {
                Assert-UltimatePortableObjectStrings $property.Value $Context ($Depth + 1)
            }
        }
    }
}

function Assert-UltimatePortableEvidenceBytes {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][byte[]]$Bytes,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )
    $extension = [System.IO.Path]::GetExtension($RelativePath)
    if ($extension -notin $script:UltimatePortableTextExtensions) { return }
    try {
        $text = (New-Object System.Text.UTF8Encoding($false, $true)).GetString($Bytes)
    } catch { throw "Portable evidence text is not strict UTF-8: $RelativePath" }
    $context = $RelativePath
    Assert-UltimatePortableText $text $context
    if ($extension -in @('.json', '.sarif')) {
        try { $data = $text | ConvertFrom-Json } catch { throw "Portable JSON evidence is invalid: $context" }
        if ($extension -eq '.json' -and $data -isnot [System.Collections.IDictionary] -and
            $data -isnot [System.Management.Automation.PSCustomObject]) {
            throw "Portable JSON evidence root must be an object: $context"
        }
        Assert-UltimatePortableObjectStrings $data $context
    } elseif ($extension -eq '.jsonl') {
        $lineNumber = 0
        foreach ($line in ($text -split "`r?`n")) {
            $lineNumber++
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $data = $line | ConvertFrom-Json } catch { throw "Portable JSONL evidence is invalid: $context/$lineNumber" }
            Assert-UltimatePortableObjectStrings $data $context
        }
    } elseif ($extension -in @('.xml', '.trx', '.props', '.targets')) {
        $settings = New-Object System.Xml.XmlReaderSettings
        $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $reader = [System.Xml.XmlReader]::Create((New-Object System.IO.StringReader($text)), $settings)
        try { $document.Load($reader) } catch { throw "Portable XML evidence is invalid: $context" } finally { $reader.Dispose() }
        foreach ($node in @($document.SelectNodes('//*'))) {
            foreach ($attribute in @($node.Attributes)) {
                Assert-UltimatePortableText ([string]$attribute.Value) $context
            }
        }
        foreach ($node in @($document.SelectNodes('//text()'))) {
            Assert-UltimatePortableText ([string]$node.Value) $context
        }
        if ($extension -eq '.trx') {
            if ($document.DocumentElement.LocalName -ne 'TestRun' -or
                $document.DocumentElement.GetAttribute('name') -ne $script:UltimatePortableTrxName -or
                $document.DocumentElement.GetAttribute('runUser') -ne $script:UltimatePortableTrxUser) {
                throw "Portable TRX retains non-canonical run identity: $context"
            }
            foreach ($node in @($document.SelectNodes('//*[@computerName]'))) {
                if ($node.GetAttribute('computerName') -ne $script:UltimatePortableTrxComputer) {
                    throw "Portable TRX retains a non-canonical computer identity: $context"
                }
            }
            foreach ($attributeName in @('runDeploymentRoot', 'userDeploymentRoot', 'testDeploymentDir')) {
                foreach ($node in @($document.SelectNodes('//*[@' + $attributeName + ']'))) {
                    if ($node.GetAttribute($attributeName) -ne $script:UltimatePortableTrxDeploymentRoot) {
                        throw "Portable TRX retains a non-canonical deployment identity: $context"
                    }
                }
            }
        }
    }
}

function Assert-UltimatePortableEvidenceFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not [System.IO.File]::Exists($full)) { throw "Portable evidence file is missing: $full" }
    Assert-UltimatePortableEvidenceBytes ([System.IO.File]::ReadAllBytes($full)) ([System.IO.Path]::GetFileName($full))
}

function Assert-UltimatePortableEvidenceTree {
    param([Parameter(Mandatory = $true)][string]$Root)
    $full = [System.IO.Path]::GetFullPath($Root)
    if (-not [System.IO.Directory]::Exists($full)) { throw "Portable evidence root is missing: $full" }
    $rootItem = Get-Item -LiteralPath $full -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Portable evidence root may not be a reparse point: $full"
    }
    $files = @(Get-ChildItem -LiteralPath $full -Recurse -File -Force)
    foreach ($file in $files) {
        if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Portable evidence may not contain a reparse point: $($file.Name)"
        }
        Assert-UltimatePortableEvidenceFile $file.FullName
    }
    return [pscustomobject][ordered]@{
        Status = 'ULTIMATE_PORTABLE_EVIDENCE_PATH_SCAN_PASS'
        FileCount = $files.Count
        TextFileCount = @($files | Where-Object { $_.Extension -in $script:UltimatePortableTextExtensions }).Count
    }
}

function Get-UltimateTrxInvariantDigest {
    param([Parameter(Mandatory = $true)][System.Xml.XmlDocument]$Document)
    if ($Document.DocumentElement.LocalName -ne 'TestRun' -or
        $Document.DocumentElement.NamespaceURI -ne 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010') {
        throw 'TRX canonicalization requires the Visual Studio TeamTest 2010 TestRun root'
    }
    $namespace = New-Object System.Xml.XmlNamespaceManager($Document.NameTable)
    $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $records = New-Object System.Collections.Generic.List[string]
    $records.Add('TestRunId=' + $Document.DocumentElement.GetAttribute('id'))
    foreach ($test in @($Document.SelectNodes('//t:TestDefinitions/t:UnitTest', $namespace))) {
        $execution = $test.SelectSingleNode('t:Execution', $namespace)
        $records.Add(('Definition={0}|{1}' -f $test.GetAttribute('id'),
            $(if ($null -eq $execution) { '' } else { $execution.GetAttribute('id') })))
    }
    foreach ($result in @($Document.SelectNodes('//t:Results/t:UnitTestResult', $namespace))) {
        $records.Add(('Result={0}|{1}|{2}|{3}' -f $result.GetAttribute('testId'),
            $result.GetAttribute('executionId'), $result.GetAttribute('outcome'), $result.GetAttribute('duration')))
    }
    $counter = $Document.SelectSingleNode('//t:ResultSummary/t:Counters', $namespace)
    if ($null -eq $counter) { throw 'TRX canonicalization requires ResultSummary/Counters' }
    $counterNames = @(
        'total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
        'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning',
        'completed', 'inProgress', 'pending'
    )
    foreach ($name in $counterNames) { $records.Add(('Counter={0}|{1}' -f $name, $counter.GetAttribute($name))) }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($records -join "`n") + "`n")
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '') } finally { $sha.Dispose() }
}

function Test-UltimateAbsolutePathValue {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    return Test-UltimateAbsoluteLocalPathText $Value
}

function Convert-UltimatePathMetadataValue {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    if (-not (Test-UltimateAbsolutePathValue $Value)) { return $Value }
    $normalized = $Value.Replace('/', '\').TrimEnd('\')
    $leaf = [System.IO.Path]::GetFileName($normalized)
    if ([string]::IsNullOrWhiteSpace($leaf) -or (Test-UltimateAbsoluteLocalPathText $leaf)) { return '' }
    return $leaf
}

function Convert-UltimatePortableTrxFile {
    param(
        [Parameter(Mandatory = $true)][string]$InputPath,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )
    $inputFull = [System.IO.Path]::GetFullPath($InputPath)
    $outputFull = [System.IO.Path]::GetFullPath($OutputPath)
    if (-not [System.IO.File]::Exists($inputFull)) { throw "TRX input is missing: $inputFull" }
    if ([System.IO.File]::Exists($outputFull)) { throw "Refusing to overwrite canonical TRX: $outputFull" }
    $readerSettings = New-Object System.Xml.XmlReaderSettings
    $readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver = $null
    $document = New-Object System.Xml.XmlDocument
    $document.PreserveWhitespace = $false
    $document.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($inputFull, $readerSettings)
    try { $document.Load($reader) } finally { $reader.Dispose() }
    $beforeDigest = Get-UltimateTrxInvariantDigest $document

    $document.DocumentElement.SetAttribute('name', $script:UltimatePortableTrxName)
    $document.DocumentElement.SetAttribute('runUser', $script:UltimatePortableTrxUser)
    if ($document.DocumentElement.HasAttribute('storage')) {
        $document.DocumentElement.RemoveAttribute('storage')
    }
    $pathAttributeNames = @(
        'storage', 'codeBase', 'path', 'filename', 'fileName', 'deploymentDirectory',
        'runDeploymentRoot', 'userDeploymentRoot', 'testDeploymentDir',
        'relativeTestResultsDirectory', 'resultsDirectory', 'testResultsDirectory'
    )
    foreach ($node in @($document.SelectNodes('//*'))) {
        if ($node.HasAttribute('computerName')) {
            $node.SetAttribute('computerName', $script:UltimatePortableTrxComputer)
        }
        foreach ($attributeName in @('runDeploymentRoot', 'userDeploymentRoot', 'testDeploymentDir')) {
            if ($node.HasAttribute($attributeName)) {
                $node.SetAttribute($attributeName, $script:UltimatePortableTrxDeploymentRoot)
            }
        }
        foreach ($attributeName in $pathAttributeNames) {
            if (-not $node.HasAttribute($attributeName)) { continue }
            $value = $node.GetAttribute($attributeName)
            if (-not (Test-UltimateAbsolutePathValue $value)) { continue }
            $portable = Convert-UltimatePathMetadataValue $value
            if ([string]::IsNullOrWhiteSpace($portable)) { $node.RemoveAttribute($attributeName) }
            else { $node.SetAttribute($attributeName, $portable) }
        }
        if ($node.LocalName -in @('Path', 'FilePath', 'Filename', 'CodeBase', 'DeploymentDirectory', 'TestResultsDirectory') -and
            -not [string]::IsNullOrWhiteSpace($node.InnerText) -and
            (Test-UltimateAbsolutePathValue $node.InnerText)) {
            $node.InnerText = Convert-UltimatePathMetadataValue $node.InnerText
        }
    }
    $afterDigest = Get-UltimateTrxInvariantDigest $document
    if ($afterDigest -ne $beforeDigest) {
        throw 'TRX canonicalization changed test identity, result, duration or counters'
    }
    $directory = [System.IO.Path]::GetDirectoryName($outputFull)
    if (-not [System.IO.Directory]::Exists($directory)) { [void][System.IO.Directory]::CreateDirectory($directory) }
    $writerSettings = New-Object System.Xml.XmlWriterSettings
    $writerSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writerSettings.Indent = $true
    $writerSettings.NewLineChars = "`n"
    $writerSettings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $writerSettings.OmitXmlDeclaration = $false
    $writer = [System.Xml.XmlWriter]::Create($outputFull, $writerSettings)
    try { $document.Save($writer) } finally { $writer.Dispose() }
    $outputDocument = New-Object System.Xml.XmlDocument
    $outputDocument.PreserveWhitespace = $false
    $outputDocument.XmlResolver = $null
    $outputReader = [System.Xml.XmlReader]::Create($outputFull, $readerSettings)
    try { $outputDocument.Load($outputReader) } finally { $outputReader.Dispose() }
    $outputDigest = Get-UltimateTrxInvariantDigest $outputDocument
    if ($outputDigest -ne $beforeDigest) {
        throw 'Canonical TRX bytes do not preserve test identity, result, duration or counters'
    }
    Assert-UltimatePortableEvidenceFile $outputFull
    return [pscustomobject][ordered]@{
        Status = 'ULTIMATE_PORTABLE_TRX_CANONICALIZATION_PASS'
        InvariantSHA256 = $outputDigest
        ResultCount = @($outputDocument.SelectNodes('//*[local-name()="UnitTestResult"]')).Count
    }
}
