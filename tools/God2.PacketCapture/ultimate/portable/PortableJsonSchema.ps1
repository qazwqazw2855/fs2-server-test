Set-StrictMode -Version 2.0

function Get-PortableJsonProperty([object]$Object,[string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-PortableExternalPathReferences {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][object[]]$Files
    )
    $hits=[Collections.Generic.List[object]]::new()
    foreach($file in @($Files|Where-Object{$_.Path -match '\.(ps1|cmd)$'})){
        $relative=[string]$file.Path
        $path=Join-Path $Root $relative.Replace('/','\')
        if($relative -match '\.ps1$'){
            $tokens=$null;$errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile(
                $path,[ref]$tokens,[ref]$errors)
            foreach($error in @($errors)){
                [void]$hits.Add([pscustomobject]@{
                    Path=$relative;Detail=('parse-error:'+$error.Extent.StartLineNumber)
                })
            }
            $strings=$ast.FindAll({param($node)
                $node -is [Management.Automation.Language.StringConstantExpressionAst] -or
                $node -is [Management.Automation.Language.ExpandableStringExpressionAst]
            },$true)
            foreach($string in $strings){
                $value=[string]$string.Value
                if($value -cmatch '^[A-Za-z]:[\\/]' -or $value -cmatch '^\\\\[^\\]'){
                    [void]$hits.Add([pscustomobject]@{Path=$relative;Detail='rooted-string-literal'})
                }
            }
            $variables=$ast.FindAll({param($node)
                $node -is [Management.Automation.Language.VariableExpressionAst]
            },$true)
            foreach($variable in $variables){
                if([string]$variable.VariablePath.UserPath -cmatch
                    '^env:(USERPROFILE|HOMEDRIVE|HOMEPATH|OneDrive)$'){
                    [void]$hits.Add([pscustomobject]@{Path=$relative;Detail='user-profile-environment'})
                }
            }
        }else{
            $raw=Get-Content -LiteralPath $path -Raw
            if($raw -cmatch '(?im)(?:^|[\s"''])(?:[A-Za-z]:[\\/]|\\\\[^\\])' -or
                $raw -cmatch '(?i)%(?:USERPROFILE|HOMEDRIVE|HOMEPATH|OneDrive)%'){
                [void]$hits.Add([pscustomobject]@{Path=$relative;Detail='external-cmd-path'})
            }
        }
    }
    return @($hits|Sort-Object Path,Detail -Unique)
}

function Test-PortableJsonType([object]$Value,[string]$Type) {
    switch ($Type) {
        'object' { return $Value -is [Management.Automation.PSCustomObject] -or
            $Value -is [Collections.IDictionary] }
        'array' { return $Value -is [Collections.IList] -or $Value -is [Array] }
        'string' { return $Value -is [string] }
        'boolean' { return $Value -is [bool] }
        'integer' { return $Value -is [sbyte] -or $Value -is [byte] -or
            $Value -is [int16] -or $Value -is [uint16] -or
            $Value -is [int32] -or $Value -is [uint32] -or
            $Value -is [int64] -or $Value -is [uint64] }
        'number' { return $Value -is [ValueType] -and $Value -isnot [bool] }
        'null' { return $null -eq $Value }
        default { return $false }
    }
}

function Test-PortableJsonSchemaNode {
    param(
        [object]$Value,
        [Parameter(Mandatory)][object]$Schema,
        [Parameter(Mandatory)][string]$JsonPath,
        [Collections.Generic.List[string]]$Errors
    )

    $type = [string](Get-PortableJsonProperty $Schema 'type')
    if (-not [string]::IsNullOrWhiteSpace($type) -and
        -not (Test-PortableJsonType $Value $type)) {
        [void]$Errors.Add("$JsonPath expected type $type")
        return
    }

    $constProperty = $Schema.PSObject.Properties['const']
    if ($null -ne $constProperty) {
        $actual = $Value | ConvertTo-Json -Compress -Depth 20
        $expected = $constProperty.Value | ConvertTo-Json -Compress -Depth 20
        if ($actual -cne $expected) { [void]$Errors.Add("$JsonPath does not match const") }
    }

    $enum = Get-PortableJsonProperty $Schema 'enum'
    if ($null -ne $enum) {
        $actual = $Value | ConvertTo-Json -Compress -Depth 20
        $matched = @($enum | Where-Object {
            ($_ | ConvertTo-Json -Compress -Depth 20) -ceq $actual
        }).Count -gt 0
        if (-not $matched) { [void]$Errors.Add("$JsonPath is not in enum") }
    }

    if ($type -ceq 'string') {
        $minimumLength = Get-PortableJsonProperty $Schema 'minLength'
        if ($null -ne $minimumLength -and $Value.Length -lt [int]$minimumLength) {
            [void]$Errors.Add("$JsonPath is shorter than minLength")
        }
        $pattern = [string](Get-PortableJsonProperty $Schema 'pattern')
        if (-not [string]::IsNullOrWhiteSpace($pattern) -and $Value -cnotmatch $pattern) {
            [void]$Errors.Add("$JsonPath does not match pattern")
        }
    }

    if ($type -in @('integer','number')) {
        $minimum = Get-PortableJsonProperty $Schema 'minimum'
        $maximum = Get-PortableJsonProperty $Schema 'maximum'
        if ($null -ne $minimum -and [decimal]$Value -lt [decimal]$minimum) {
            [void]$Errors.Add("$JsonPath is below minimum")
        }
        if ($null -ne $maximum -and [decimal]$Value -gt [decimal]$maximum) {
            [void]$Errors.Add("$JsonPath is above maximum")
        }
    }

    if ($type -ceq 'array') {
        $rows = @($Value)
        $minItems = Get-PortableJsonProperty $Schema 'minItems'
        $maxItems = Get-PortableJsonProperty $Schema 'maxItems'
        if ($null -ne $minItems -and $rows.Count -lt [int]$minItems) {
            [void]$Errors.Add("$JsonPath has fewer than minItems")
        }
        if ($null -ne $maxItems -and $rows.Count -gt [int]$maxItems) {
            [void]$Errors.Add("$JsonPath has more than maxItems")
        }
        $itemSchema = Get-PortableJsonProperty $Schema 'items'
        if ($null -ne $itemSchema) {
            for ($index = 0; $index -lt $rows.Count; ++$index) {
                Test-PortableJsonSchemaNode -Value $rows[$index] -Schema $itemSchema `
                    -JsonPath "$JsonPath[$index]" -Errors $Errors
            }
        }
    }

    if ($type -ceq 'object') {
        $actualNames = @($Value.PSObject.Properties.Name)
        $requiredProperties = Get-PortableJsonProperty $Schema 'required'
        if ($null -ne $requiredProperties) {
            foreach ($requiredName in @($requiredProperties)) {
                if ($actualNames -cnotcontains [string]$requiredName) {
                    [void]$Errors.Add("$JsonPath missing required property $requiredName")
                }
            }
        }
        $properties = Get-PortableJsonProperty $Schema 'properties'
        if ($null -ne $properties) {
            $declaredNames = @($properties.PSObject.Properties.Name)
            if ((Get-PortableJsonProperty $Schema 'additionalProperties') -eq $false) {
                foreach ($actualName in $actualNames) {
                    if ($declaredNames -cnotcontains $actualName) {
                        [void]$Errors.Add("$JsonPath has undeclared property $actualName")
                    }
                }
            }
            foreach ($property in $properties.PSObject.Properties) {
                if ($actualNames -ccontains $property.Name) {
                    Test-PortableJsonSchemaNode -Value (Get-PortableJsonProperty $Value $property.Name) `
                        -Schema $property.Value -JsonPath "$JsonPath.$($property.Name)" -Errors $Errors
                }
            }
        }
    }
}

function Test-PortableJsonSchemaFile {
    param(
        [Parameter(Mandatory)][string]$JsonPath,
        [Parameter(Mandatory)][string]$SchemaPath
    )
    $document = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $schema = Get-Content -LiteralPath $SchemaPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $errors = [Collections.Generic.List[string]]::new()
    Test-PortableJsonSchemaNode -Value $document -Schema $schema -JsonPath '$' -Errors $errors
    [pscustomobject][ordered]@{ Passed=($errors.Count -eq 0); Errors=@($errors) }
}
