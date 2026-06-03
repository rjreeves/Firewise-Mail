param(
    [string]$ProjectRoot = $PSScriptRoot,
    [string]$OutputRoot = "",
    [string[]]$CopyTo = @(),
    [switch]$DryRun,
    [switch]$KeepStaging
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Convert-ToRelativePath {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string]$Path
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $rootUri = [System.Uri]::new($rootFull)
    $pathUri = [System.Uri]::new($pathFull)
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Get-RuleLines {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required rule file not found: $Path"
    }

    Get-Content -LiteralPath $Path |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') }
}

function Normalize-RulePath {
    param([Parameter(Mandatory)] [string]$Path)
    return $Path.Replace('\', '/').TrimStart('/')
}

function Test-Excluded {
    param(
        [Parameter(Mandatory)] [string]$RelativePath,
        [Parameter(Mandatory)] [string[]]$ExcludeRules
    )

    $normalized = Normalize-RulePath -Path $RelativePath
    foreach ($rule in $ExcludeRules) {
        $normalizedRule = Normalize-RulePath -Path $rule
        $isDirectoryRule = $normalizedRule.EndsWith('/')
        $normalizedRule = $normalizedRule.TrimEnd('/')

        if ($isDirectoryRule) {
            if ($normalized -eq $normalizedRule -or $normalized.StartsWith("$normalizedRule/")) {
                return $true
            }
            if (-not $normalizedRule.Contains('/') -and $normalized.Contains("/$normalizedRule/")) {
                return $true
            }
            continue
        }

        if ($normalized -like $normalizedRule) {
            return $true
        }
        if (-not $normalizedRule.Contains('/') -and $normalized -like "*/$normalizedRule") {
            return $true
        }
    }
    return $false
}
function Copy-IncludedItem {
    param(
        [Parameter(Mandatory)] [string]$ProjectRoot,
        [Parameter(Mandatory)] [string]$Rule,
        [Parameter(Mandatory)] [string]$StagingRoot,
        [Parameter(Mandatory)] [string[]]$ExcludeRules,
        [System.Collections.Generic.List[string]]$Copied
    )

    $normalizedRule = $Rule.Replace('/', '\').TrimStart('\')
    $sourcePath = Join-Path $ProjectRoot $normalizedRule
    $hasWildcard = $normalizedRule.IndexOfAny([char[]]'*?') -ge 0

    if ($hasWildcard) {
        $matches = Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Force |
            Where-Object {
                $relative = Convert-ToRelativePath -Root $ProjectRoot -Path $_.FullName
                $relative -like $normalizedRule -and -not (Test-Excluded -RelativePath $relative -ExcludeRules $ExcludeRules)
            }
    } elseif (Test-Path -LiteralPath $sourcePath -PathType Container) {
        $matches = Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force |
            Where-Object {
                $relative = Convert-ToRelativePath -Root $ProjectRoot -Path $_.FullName
                -not (Test-Excluded -RelativePath $relative -ExcludeRules $ExcludeRules)
            }
    } elseif (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
        $matches = @(Get-Item -LiteralPath $sourcePath -Force) |
            Where-Object {
                $relative = Convert-ToRelativePath -Root $ProjectRoot -Path $_.FullName
                -not (Test-Excluded -RelativePath $relative -ExcludeRules $ExcludeRules)
            }
    } else {
        Write-Host "Skipping missing include: $Rule"
        return
    }

    foreach ($match in $matches) {
        $relative = Convert-ToRelativePath -Root $ProjectRoot -Path $match.FullName
        $destination = Join-Path $StagingRoot $relative
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        Copy-Item -LiteralPath $match.FullName -Destination $destination -Force
        $Copied.Add($relative) | Out-Null
    }
}

$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
    throw "Project root not found: $ProjectRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $ProjectRoot 'backup-zips'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

$includePath = Join-Path $ProjectRoot 'backup-include.txt'
$excludePath = Join-Path $ProjectRoot 'backup-exclude.txt'
$includeRules = @(Get-RuleLines -Path $includePath)
$excludeRules = @(Get-RuleLines -Path $excludePath)

$timestamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
$backupName = "Firewise-Mail-backup-$timestamp"
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) $backupName
$zipPath = Join-Path $OutputRoot "$backupName.zip"

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

$copied = [System.Collections.Generic.List[string]]::new()
foreach ($rule in $includeRules) {
    Copy-IncludedItem -ProjectRoot $ProjectRoot -Rule $rule -StagingRoot $stagingRoot -ExcludeRules $excludeRules -Copied $copied
}

# Include the rule files themselves so every archive explains how it was made.
foreach ($ruleFile in @('backup-include.txt', 'backup-exclude.txt')) {
    $source = Join-Path $ProjectRoot $ruleFile
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $stagingRoot $ruleFile) -Force
        $copied.Add($ruleFile) | Out-Null
    }
}

$copied = @($copied | Sort-Object -Unique)
if ($copied.Count -eq 0) {
    throw 'No files matched the backup include rules.'
}

Write-Host "Files selected for backup: $($copied.Count)"
$copied | ForEach-Object { Write-Host "  $_" }

if ($DryRun) {
    Write-Host "Dry run only. Staging folder left at: $stagingRoot"
    return
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created: $zipPath"

foreach ($destinationRoot in $CopyTo) {
    if ([string]::IsNullOrWhiteSpace($destinationRoot)) { continue }
    $destinationRoot = [System.IO.Path]::GetFullPath($destinationRoot)
    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
    $destinationZip = Join-Path $destinationRoot (Split-Path -Leaf $zipPath)
    Copy-Item -LiteralPath $zipPath -Destination $destinationZip -Force
    Write-Host "Copied to: $destinationZip"
}

if ($KeepStaging) {
    Write-Host "Staging folder kept at: $stagingRoot"
} else {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}



