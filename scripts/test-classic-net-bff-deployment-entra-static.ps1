[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$provisionPath = Join-Path $PSScriptRoot 'provision-classic-net-bff-deployment.ps1'
$cleanupPath = Join-Path $PSScriptRoot 'cleanup-classic-net-bff-deployment.ps1'
$workflowPath = Join-Path (Split-Path -Parent $PSScriptRoot) '.github\workflows\classic-net-bff-poc.yml'
$provisionSource = Get-Content -LiteralPath $provisionPath -Raw
$cleanupSource = Get-Content -LiteralPath $cleanupPath -Raw
$workflowSource = Get-Content -LiteralPath $workflowPath -Raw

function Get-ScriptAst {
    param([Parameter(Mandatory)][string]$Path)

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $Path,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "$Path has $($parseErrors.Count) PowerShell parser error(s)."
    }
    return $ast
}

function Assert-SourceOrder {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string[]]$Fragments,
        [Parameter(Mandatory)][string]$Message
    )

    $previousIndex = -1
    foreach ($fragment in $Fragments) {
        $index = $Source.IndexOf($fragment, $previousIndex + 1, [System.StringComparison]::Ordinal)
        if ($index -lt 0 -or $index -le $previousIndex) {
            throw $Message
        }
        $previousIndex = $index
    }
}

function Get-FunctionSource {
    param(
        [Parameter(Mandatory)][System.Management.Automation.Language.ScriptBlockAst]$Ast,
        [Parameter(Mandatory)][string]$Name
    )

    $functionAst = $Ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq $Name
        }, $true)
    if ($null -eq $functionAst) {
        throw "Expected function was not found: $Name"
    }
    return $functionAst.Extent.Text
}

$provisionAst = Get-ScriptAst -Path $provisionPath
$cleanupAst = Get-ScriptAst -Path $cleanupPath

$invokeGraphSource = Get-FunctionSource -Ast $provisionAst -Name 'Invoke-Graph'
$callbackFunctionSource = Get-FunctionSource -Ast $provisionAst -Name 'Assert-DeploymentCallbackUri'
. ([scriptblock]::Create($callbackFunctionSource))

foreach ($validUri in @(
        'https://legacy.example.com/signin-oidc',
        'https://modern.example.com:443/signin-oidc')) {
    Assert-DeploymentCallbackUri -Value $validUri -ParameterName 'TestUri' | Out-Null
}

foreach ($invalidUri in @(
        '/signin-oidc',
        'http://legacy.example.com/signin-oidc',
        'https://user:password@legacy.example.com/signin-oidc',
        'https://legacy.example.com/signin-oidc?source=test',
        'https://legacy.example.com/signin-oidc#fragment',
        'https://legacy.example.com/',
        'https://legacy.example.com/signin-oidc/')) {
    try {
        Assert-DeploymentCallbackUri -Value $invalidUri -ParameterName 'TestUri' | Out-Null
        throw "Unsafe callback URI was accepted: $invalidUri"
    }
    catch {
        if ($_.Exception.Message -eq "Unsafe callback URI was accepted: $invalidUri") {
            throw
        }
    }
}

foreach ($requiredFragment in @(
        '[ValidateRange(1, 7)]',
    "'Croesus.ClassicNetBffDeployment.v1'",
    'not tagged as workflow-managed. Refusing to claim it.',
        "'AzureADMyOrg'",
        "'AzureADMultipleOrgs'",
        'web = @{ redirectUris = $redirectUris }',
        'spa = @{ redirectUris = @() }',
        'isFallbackPublicClient = $false',
        '$MyInvocation.InvocationName -ne',
        '[EnvironmentVariableTarget]::Process')) {
    if (-not $provisionSource.Contains($requiredFragment)) {
        throw "Provisioning contract is missing: $requiredFragment"
    }
}

foreach ($requestBodyFragment in @(
        '[System.IO.Path]::GetTempPath()',
        '[guid]::NewGuid()',
        '[System.IO.File]::WriteAllText(',
        '[System.Text.UTF8Encoding]::new($false)',
        "'--body', `"@`$bodyPath`"",
        'finally {',
        'Remove-Item -LiteralPath $bodyPath -Force')) {
    if (-not $invokeGraphSource.Contains($requestBodyFragment)) {
        throw "Invoke-Graph request-body safety contract is missing: $requestBodyFragment"
    }
}

Assert-SourceOrder `
    -Source $invokeGraphSource `
    -Fragments @(
        '$bodyPath = $null',
        'try {',
        '[System.IO.File]::WriteAllText(',
        "'--body', `"@`$bodyPath`"",
        '$result = & az @arguments',
        'return ($result -join [Environment]::NewLine) | ConvertFrom-Json',
        'finally {',
        'Remove-Item -LiteralPath $bodyPath -Force') `
    -Message 'Invoke-Graph must use a file-backed request body and remove it in finally after execution and parsing.'

if ($invokeGraphSource -match '''--body'',\s*\$json') {
    throw 'Invoke-Graph must not pass serialized JSON inline to az rest.'
}

Assert-SourceOrder `
    -Source $provisionSource `
    -Fragments @(
        'applications/$applicationObjectId/addPassword',
        '[Console]::WriteLine("::add-mask::$secretText")',
        '$state.passwordCredential = [ordered]@{',
        'Save-State',
        'applications/$applicationObjectId/removePassword') `
    -Message 'Credential rotation must create, mask, persist the new key ID, and only then remove prior credentials.'

Assert-SourceOrder `
    -Source $workflowSource `
    -Fragments @(
        '$existsRaw = az group exists --name $env:RESOURCE_GROUP',
        '$groupExistsExitCode = $LASTEXITCODE',
        'if ($groupExistsExitCode -ne 0)',
        '$exists = $existsRaw | ConvertFrom-Json') `
    -Message 'The validation workflow must reject az group exists failures before parsing the response.'

if ($workflowSource -notmatch '\$exists\s+-isnot\s+\[bool\]') {
    throw 'The validation workflow must reject malformed or non-boolean az group exists output.'
}

if ($provisionSource -notmatch '\$priorKeyId -eq \$newCredentialKeyId') {
    throw 'Credential rotation must explicitly preserve the newly created key.'
}

$outputCommands = @('Write-Output', 'Write-Host', 'Write-Information', 'Write-Warning', 'Write-Verbose', 'Write-Debug')
$unsafeOutput = $provisionAst.Find({
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

foreach ($unsafePersistenceFragment in @(
        'SecretOutputPath',
        'clientSecret =',
        'GITHUB_OUTPUT',
        'GITHUB_STATE')) {
    if ($provisionSource.Contains($unsafePersistenceFragment)) {
        throw "Provisioning contains an unsafe secret persistence or output path: $unsafePersistenceFragment"
    }
}

foreach ($ownershipGuard in @(
    "'Croesus.ClassicNetBffDeployment.v1'",
    'Find-WorkflowManagedApplication -ExpectedDisplayName $DisplayName',
    'not tagged as workflow-managed. Refusing deletion.',
        '[bool]$state.passwordCredential.created',
        '[bool]$state.servicePrincipal.created',
        '[bool]$state.application.created',
        'Assert-ApplicationIdentity -Application $application',
        '[string]$servicePrincipal.appId -ne [string]$state.application.clientId',
        'The app registration was reused; leaving it intact')) {
    if (-not $cleanupSource.Contains($ownershipGuard)) {
        throw "Cleanup ownership guard is missing: $ownershipGuard"
    }
}

$rbacPatterns = @(
    ('role' + '\s+assignment'),
    ('create' + '-for-rbac'))
foreach ($source in @($provisionSource, $cleanupSource)) {
    foreach ($rbacPattern in $rbacPatterns) {
        if ($source -match $rbacPattern) {
            throw "Deployment Entra automation must not contain subscription RBAC operations: $rbacPattern"
        }
    }
}

if ($null -eq $cleanupAst) {
    throw 'Cleanup AST was not produced.'
}

Write-Output 'Deployment Entra static security checks OK'