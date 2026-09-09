$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "Invoke-God2GameCatalogBuilder.ps1") -Command refresh-caches
