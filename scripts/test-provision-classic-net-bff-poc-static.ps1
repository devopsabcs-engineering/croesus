[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'provision-classic-net-bff-poc.ps1'
$source = Get-Content -LiteralPath $scriptPath -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath,
    [ref]$tokens,
    [ref]$parseErrors)

if ($parseErrors.Count -gt 0) {
    throw "Provisioning script has $($parseErrors.Count) PowerShell parser error(s)."
}

function Assert-SourceOrder {
    param(
        [Parameter(Mandatory)][string]$Earlier,
        [Parameter(Mandatory)][string]$Later,
        [Parameter(Mandatory)][string]$Message
    )

    $earlierIndex = $source.IndexOf($Earlier, [System.StringComparison]::Ordinal)
    $laterIndex = $source.IndexOf($Later, [System.StringComparison]::Ordinal)
    if ($earlierIndex -lt 0 -or $laterIndex -lt 0 -or $earlierIndex -ge $laterIndex) {
        throw $Message
    }
}

function Get-FunctionSource {
    param([Parameter(Mandatory)][string]$Name)

    $functionAst = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $Name
        }, $true)
    if ($null -eq $functionAst) {
        throw "Expected function was not found: $Name"
    }
    return $functionAst.Extent.Text
}

$initializeSource = Get-FunctionSource -Name 'Initialize-ProtectedSecretFile'
$platformCheckIndex = $initializeSource.IndexOf('if (-not $IsWindows)', [System.StringComparison]::Ordinal)
$createNewIndex = $initializeSource.IndexOf('[System.IO.FileMode]::CreateNew', [System.StringComparison]::Ordinal)
if ($platformCheckIndex -lt 0 -or $createNewIndex -lt 0 -or $platformCheckIndex -ge $createNewIndex) {
    throw 'The Windows guard must run before the protected placeholder is created.'
}
foreach ($requiredPreflightFragment in @(
        'Set-Acl -LiteralPath $Path -AclObject $acl',
        'Assert-SecretFileProtection -Path $Path',
        '(Get-Item -LiteralPath $Path).Length -eq 0',
        'Remove-Item -LiteralPath $Path -Force')) {
    if (-not $initializeSource.Contains($requiredPreflightFragment)) {
        throw "Protected-file preflight is missing: $requiredPreflightFragment"
    }
}

Assert-SourceOrder `
    -Earlier 'Initialize-ProtectedSecretFile -Path $fullSecretPath' `
    -Later 'applications/$applicationObjectId/addPassword' `
    -Message 'Protected-file preflight must complete before Graph addPassword.'

$ownershipIndex = $source.IndexOf('$state.passwordCredential = $credentialState', [System.StringComparison]::Ordinal)
$stateWriteIndex = $source.IndexOf(
    '$state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fullStatePath',
    $ownershipIndex,
    [System.StringComparison]::Ordinal)
$secretWriteIndex = $source.IndexOf('Write-ProtectedSecretFile -Path $fullSecretPath', [System.StringComparison]::Ordinal)
if ($ownershipIndex -lt 0 -or
    $stateWriteIndex -lt 0 -or
    $secretWriteIndex -lt 0 -or
    $ownershipIndex -ge $stateWriteIndex -or
    $stateWriteIndex -ge $secretWriteIndex) {
    throw 'Credential ownership must be assigned and persisted before the protected secret write.'
}

$writeSource = Get-FunctionSource -Name 'Write-ProtectedSecretFile'
if (-not $writeSource.Contains('[System.IO.FileMode]::Truncate')) {
    throw 'Secret material must be written by truncating the existing protected file.'
}
if ([regex]::Matches($writeSource, 'Assert-SecretFileProtection -Path \$Path').Count -lt 2) {
    throw 'The protected ACL must be verified before and after writing secret material.'
}

$outputCommands = @('Write-Output', 'Write-Host', 'Write-Information', 'Write-Warning', 'Write-Verbose', 'Write-Debug')
$unsafeOutput = $ast.Find({
        param($node)
        if ($node -isnot [System.Management.Automation.Language.CommandAst]) {
            return $false
        }
        $commandName = $node.GetCommandName()
        return $commandName -in $outputCommands -and
            $node.Extent.Text -match '(?i)secretText|clientSecret'
    }, $true)
if ($null -ne $unsafeOutput) {
    throw "Secret material is referenced by an output command: $($unsafeOutput.Extent.Text)"
}

if ($source -notmatch 'Run cleanup-classic-net-bff-poc\.ps1 with this StatePath before retrying') {
    throw 'The post-creation write failure must direct the operator to cleanup.'
}

Write-Output 'Provisioning credential-safety static checks OK'
