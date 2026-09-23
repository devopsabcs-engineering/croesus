[CmdletBinding()]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidOverwritingBuiltInCmdlets',
    '',
    Scope = 'Function',
    Target = 'Start-Sleep',
    Justification = 'The static test replaces Start-Sleep so retry delays can be asserted without waiting.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSUseShouldProcessForStateChangingFunctions',
    '',
    Scope = 'Function',
    Target = 'Start-Sleep',
    Justification = 'The Start-Sleep test double only records requested delays in script scope.')]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$provisionPath = Join-Path $PSScriptRoot 'provision-classic-net-bff-deployment.ps1'
$cleanupPath = Join-Path $PSScriptRoot 'cleanup-classic-net-bff-deployment.ps1'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $repositoryRoot '.github\workflows\classic-net-bff-poc.yml'
$bicepPath = Join-Path $repositoryRoot 'infra\poc\main.bicep'
$legacyStartupPath = Join-Path $repositoryRoot 'poc\legacy-net452\Startup.cs'
$legacyWebConfigPath = Join-Path $repositoryRoot 'poc\legacy-net452\web.config'
$modernWebConfigPath = Join-Path $repositoryRoot 'poc\modern-net10\web.config'
$provisionSource = Get-Content -LiteralPath $provisionPath -Raw
$cleanupSource = Get-Content -LiteralPath $cleanupPath -Raw
$workflowSource = Get-Content -LiteralPath $workflowPath -Raw
$bicepSource = Get-Content -LiteralPath $bicepPath -Raw
$legacyStartupSource = Get-Content -LiteralPath $legacyStartupPath -Raw
[xml]$legacyWebConfig = Get-Content -LiteralPath $legacyWebConfigPath -Raw
[xml]$modernWebConfig = Get-Content -LiteralPath $modernWebConfigPath -Raw

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

Assert-SourceOrder `
    -Source $legacyStartupSource `
    -Fragments @(
        'using Microsoft.Owin.Extensions;',
        'app.UseCookieAuthentication(CookieOptionsFactory.Create());',
        'app.UseOpenIdConnectAuthentication(oidcOptions);',
        'app.UseStageMarker(PipelineStage.Authenticate);') `
    -Message 'Legacy authentication must keep cookie before OIDC and stage both at AuthenticateRequest.'

$legacyResourceIndex = $bicepSource.IndexOf(
    "resource legacyApp 'Microsoft.Web/sites@2025-03-01'",
    [System.StringComparison]::Ordinal)
$modernResourceIndex = $bicepSource.IndexOf(
    "resource modernApp 'Microsoft.Web/sites@2025-03-01'",
    [System.StringComparison]::Ordinal)
$bffResourceIndex = $bicepSource.IndexOf(
    "resource bffApp 'Microsoft.Web/sites@2025-03-01'",
    [System.StringComparison]::Ordinal)
$outputIndex = $bicepSource.IndexOf(
    "@description('Name of the shared Windows App Service plan.')",
    [System.StringComparison]::Ordinal)
if ($legacyResourceIndex -lt 0 -or $modernResourceIndex -le $legacyResourceIndex -or
    $bffResourceIndex -le $modernResourceIndex -or $outputIndex -le $bffResourceIndex) {
    throw 'The Bicep App Service resource boundaries could not be identified.'
}
$legacyResourceSource = $bicepSource.Substring(
    $legacyResourceIndex,
    $modernResourceIndex - $legacyResourceIndex)
$modernResourceSource = $bicepSource.Substring(
    $modernResourceIndex,
    $bffResourceIndex - $modernResourceIndex)
if ($legacyResourceSource.Contains('use32BitWorkerProcess')) {
    throw 'The legacy App Service worker bitness must remain unchanged.'
}
if (-not $modernResourceSource.Contains('use32BitWorkerProcess: false')) {
    throw 'The self-contained win-x64 modern App Service must use a 64-bit worker process.'
}

$assemblyNamespace = [System.Xml.XmlNamespaceManager]::new($legacyWebConfig.NameTable)
$assemblyNamespace.AddNamespace('asm', 'urn:schemas-microsoft-com:asm.v1')
$newtonsoftAssembly = $legacyWebConfig.SelectSingleNode(
    '/configuration/runtime/asm:assemblyBinding/asm:dependentAssembly' +
    '[asm:assemblyIdentity[@name="Newtonsoft.Json" and ' +
    '@publicKeyToken="30ad4fe6b2a6aeed"]]',
    $assemblyNamespace)
if ($null -eq $newtonsoftAssembly) {
    throw 'The legacy web.config must identify the Newtonsoft.Json strong-named assembly.'
}
$bindingRedirect = $newtonsoftAssembly.SelectSingleNode(
    'asm:bindingRedirect[@oldVersion="0.0.0.0-13.0.0.0" and @newVersion="13.0.0.0"]',
    $assemblyNamespace)
if ($null -eq $bindingRedirect) {
    throw 'The legacy web.config must redirect Newtonsoft.Json versions through 13.0.0.0.'
}
if ($legacyWebConfig.configuration.'system.web'.compilation.targetFramework -cne '4.8' -or
    $legacyWebConfig.configuration.'system.web'.customErrors.mode -cne 'On') {
    throw 'The legacy binding redirect must preserve net48 and customErrors mode On.'
}
$legacyHttpRuntime = $legacyWebConfig.SelectSingleNode('/configuration/system.web/httpRuntime')
if ($null -eq $legacyHttpRuntime -or
    $legacyHttpRuntime.targetFramework -cne '4.8' -or
    $legacyHttpRuntime.maxQueryStringLength -cne '8192' -or
    $legacyHttpRuntime.Attributes.Count -ne 2) {
    throw 'The legacy httpRuntime must bound maxQueryStringLength at 8192 and preserve net48.'
}

$legacyRequestLimits = $legacyWebConfig.SelectSingleNode(
    '/configuration/system.webServer/security/requestFiltering/requestLimits')
if ($null -eq $legacyRequestLimits -or
    $legacyRequestLimits.maxQueryString -cne '8192' -or
    $legacyRequestLimits.Attributes.Count -ne 1) {
    throw 'The legacy web.config must raise only maxQueryString to the bounded value 8192.'
}

$legacyOwinModule = $legacyWebConfig.SelectSingleNode(
    '//modules/add[contains(@type, "Microsoft.Owin.Host.SystemWeb.OwinHttpModule")]')
if ($null -ne $legacyOwinModule) {
    throw 'The legacy web.config must rely on Katana pre-application startup instead of explicitly registering OwinHttpModule.'
}

$modernSystemWebServer = $modernWebConfig.SelectSingleNode(
    '/configuration/location[@path="." and @inheritInChildApplications="false"]/system.webServer')
$modernRequestLimits = $modernSystemWebServer.SelectSingleNode(
    'security/requestFiltering/requestLimits')
$modernHandler = $modernSystemWebServer.SelectSingleNode(
    'handlers/add[@name="aspNetCore" and @path="*" and @verb="*" and ' +
    '@modules="AspNetCoreModuleV2" and @resourceType="Unspecified"]')
$modernAspNetCore = $modernSystemWebServer.SelectSingleNode(
    'aspNetCore[@processPath=".\Croesus.ModernBff.exe" and ' +
    '@stdoutLogEnabled="false" and @stdoutLogFile=".\logs\stdout" and ' +
    '@hostingModel="inprocess"]')
if ($null -eq $modernRequestLimits -or
    $modernRequestLimits.maxQueryString -cne '8192' -or
    $modernRequestLimits.Attributes.Count -ne 1) {
    throw 'The modern web.config must raise only maxQueryString to the bounded value 8192.'
}
if ($null -eq $modernHandler -or $null -eq $modernAspNetCore) {
    throw 'The modern web.config must preserve ANCM V2 in-process routing to Croesus.ModernBff.exe.'
}

$invokeGraphSource = Get-FunctionSource -Ast $provisionAst -Name 'Invoke-Graph'
$callbackFunctionSource = Get-FunctionSource -Ast $provisionAst -Name 'Assert-DeploymentCallbackUri'
. ([scriptblock]::Create($invokeGraphSource))
. ([scriptblock]::Create($callbackFunctionSource))

function az {
    $resultIndex = $script:mockAzAttemptCount
    $script:mockAzAttemptCount++
    $global:LASTEXITCODE = $script:mockAzExitCodes[$resultIndex]
    Write-Output ($script:mockAzResults[$resultIndex])
}

function Start-Sleep {
    param([Parameter(Mandatory)][double]$Seconds)

    $script:mockSleepSeconds += $Seconds
}

function Initialize-GraphMockState {
    param(
        [Parameter(Mandatory)][string[]]$Results,
        [Parameter(Mandatory)][int[]]$ExitCodes
    )

    $script:mockAzResults = $Results
    $script:mockAzExitCodes = $ExitCodes
    $script:mockAzAttemptCount = 0
    $script:mockSleepSeconds = @()
}

$transientNotFound = 'ERROR: Not Found({"error":{"code":"Request_ResourceNotFound","message":"not yet visible"}})'
Initialize-GraphMockState `
    -Results @($transientNotFound, $transientNotFound, $transientNotFound, '{"value":[]}') `
    -ExitCodes @(1, 1, 1, 0)
$retryResult = Invoke-Graph -Method GET -Uri 'https://graph.microsoft.com/v1.0/applications/test-object-id'
if ($script:mockAzAttemptCount -ne 4 -or @($retryResult.value).Count -ne 0) {
    throw 'Invoke-Graph must make at most four attempts and return the successful final response.'
}
if (($script:mockSleepSeconds -join ',') -cne '2,4,8') {
    throw "Invoke-Graph retry delays must be exactly 2,4,8 seconds; observed $($script:mockSleepSeconds -join ',')."
}

$transientConcurrencyViolation = 'ERROR: Graph failure({"error":{"code":"Directory_ConcurrencyViolation","message":"DO_NOT_LOG_RAW_CONCURRENCY_MESSAGE"}})'
Initialize-GraphMockState `
    -Results @($transientConcurrencyViolation, $transientConcurrencyViolation, '{"value":[]}') `
    -ExitCodes @(1, 1, 0)
$retryResult = Invoke-Graph -Method POST -Uri 'https://graph.microsoft.com/v1.0/applications/test-object-id/removePassword'
if ($script:mockAzAttemptCount -ne 3 -or @($retryResult.value).Count -ne 0) {
    throw 'Invoke-Graph must retry the exact Directory_ConcurrencyViolation Graph code without an HTTP status.'
}
if (($script:mockSleepSeconds -join ',') -cne '2,4') {
    throw "Directory_ConcurrencyViolation retry delays must follow the bounded schedule; observed $($script:mockSleepSeconds -join ',')."
}

foreach ($nonTransientResult in @(
        'ERROR: Not Found({"error":{"code":"Request_ResourceNotFoundExtra","message":"wrong code"}})',
        'ERROR: Graph failure({"error":{"code":"Directory_ConcurrencyViolationExtra","message":"wrong code"}})',
        'ERROR: Bad Request({"error":{"code":"Request_ResourceNotFound","message":"wrong status"}})',
        'ERROR: Forbidden({"error":{"code":"Authorization_RequestDenied","message":"DO_NOT_LOG_RAW_PAYLOAD"}})')) {
    Initialize-GraphMockState -Results @($nonTransientResult) -ExitCodes @(1)
    try {
        Invoke-Graph -Method GET -Uri 'https://graph.microsoft.com/v1.0/applications/test-object-id' | Out-Null
        throw 'Invoke-Graph accepted a non-transient Graph failure.'
    }
    catch {
        if ($_.Exception.Message -eq 'Invoke-Graph accepted a non-transient Graph failure.') {
            throw
        }
        if ($script:mockAzAttemptCount -ne 1 -or $script:mockSleepSeconds.Count -ne 0) {
            throw 'Invoke-Graph must fail non-transient Graph responses immediately without sleeping.'
        }
        if ($_.Exception.Message.Contains('DO_NOT_LOG_RAW_PAYLOAD')) {
            throw 'Invoke-Graph final failure context must not expose raw Azure CLI payloads.'
        }
    }
}

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

foreach ($retryFragment in @(
        '$maxAttempts = 4',
        '$result = & az @arguments 2>&1',
        '$exitCode = $LASTEXITCODE',
        '$hasSemanticNotFound = [regex]::IsMatch(',
        '$httpStatus -ceq ''404''',
        '$graphCode -ceq ''Request_ResourceNotFound''',
        '$graphCode -ceq ''Directory_ConcurrencyViolation''',
        '$isTransientResourceNotFound -or',
        '$isTransientDirectoryConcurrencyViolation',
        '-not $isTransientGraphFailure -or $attempt -eq $maxAttempts',
        '$delaySeconds = [math]::Pow(2, $attempt)',
        'Start-Sleep -Seconds $delaySeconds')) {
    if (-not $invokeGraphSource.Contains($retryFragment)) {
        throw "Invoke-Graph bounded retry contract is missing: $retryFragment"
    }
}

Assert-SourceOrder `
    -Source $invokeGraphSource `
    -Fragments @(
        '$bodyPath = $null',
        'try {',
        '$maxAttempts = 4',
        '[System.IO.File]::WriteAllText(',
        "'--body', `"@`$bodyPath`"",
        'for ($attempt = 1; $attempt -le $maxAttempts; $attempt++)',
        '$result = & az @arguments 2>&1',
        '$exitCode = $LASTEXITCODE',
        'if ($exitCode -eq 0)',
        'return $resultText | ConvertFrom-Json',
        '$isTransientResourceNotFound =',
        '$isTransientDirectoryConcurrencyViolation =',
        '$isTransientGraphFailure =',
        'if (-not $isTransientGraphFailure -or $attempt -eq $maxAttempts)',
        'throw "Microsoft Graph request failed:',
        '$delaySeconds = [math]::Pow(2, $attempt)',
        'Start-Sleep -Seconds $delaySeconds',
        'finally {',
        'Remove-Item -LiteralPath $bodyPath -Force') `
    -Message 'Invoke-Graph must capture stderr, fail non-transient requests immediately, bound retry delay, and remove its file-backed body in finally.'

if ($invokeGraphSource -match '(?s)Write-(?:Output|Host|Information|Warning|Verbose|Debug).*\$(?:result|resultText|bodyPath|json)') {
    throw 'Invoke-Graph must not log raw Azure CLI output, request payloads, or temporary request-body paths.'
}

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

foreach ($smokeFragment in @(
        'REGISTRATION_CLIENT_ID: ${{ steps.infrastructure.outputs.registration_client_id }}',
        '$handler.AllowAutoRedirect = $false',
        '$Client.GetAsync("$BaseUrl/api/session")',
        "-ChallengePath '/signin'",
        "-ChallengePath '/'",
        '$redirectStatuses = @(301, 302, 303, 307, 308)',
        '$location.Host -cne ''login.microsoftonline.com''',
        '$query[''client_id''] -cne $ExpectedClientId',
        '$query[''redirect_uri''] -cne $ExpectedRedirectUri',
        '$expectedRedirectUri = "$BaseUrl/signin-oidc"',
        '$maxAttempts = 8',
        '$retryDelaySeconds = 10',
        'for ($attempt = 1; $attempt -le $maxAttempts; $attempt++)',
        'if ($attempt -eq $maxAttempts)',
        'Start-Sleep -Seconds $retryDelaySeconds')) {
    if (-not $workflowSource.Contains($smokeFragment)) {
        throw "The deployment readiness smoke contract is missing: $smokeFragment"
    }
}

if ($workflowSource.Contains('foreach ($url in @($env:LEGACY_BASE_URL, $env:MODERN_BASE_URL))')) {
    throw 'The workflow must not regress to root-only smoke checks.'
}
if ($workflowSource -match '(?s)Verify authentication redirects.*Invoke-WebRequest') {
    throw 'The deployment readiness check must use a client with redirects disabled.'
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

$global:LASTEXITCODE = 0
Write-Output 'Deployment Entra static security checks OK'