[CmdletBinding()]
param(
    [string]$DisplayName = 'Croesus Classic BFF Comparison PoC',
    [string]$StatePath = '.classic-net-bff-poc-state.json',
    [switch]$MultiTenant,
    [switch]$IncludeUserRead,
    [switch]$CreateDevSecret,
    [string]$SecretOutputPath,
    [ValidateRange(1, 30)]
    [int]$SecretLifetimeDays = 7
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$graphBaseUri = 'https://graph.microsoft.com/v1.0'
$graphAppId = '00000003-0000-0000-c000-000000000000'
$userReadPermissionId = 'e1fe6dd8-ba31-4d61-89e7-88639da4683d'
$redirectUris = @(
    'https://localhost:44352/signin-oidc'
    'https://localhost:7100/signin-oidc'
)

function Write-Status {
    param([Parameter(Mandatory)][string]$Message)

    Write-Information ">>> $Message" -InformationAction Continue
}

function Invoke-Graph {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PATCH', 'DELETE')][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [object]$Body
    )

    $arguments = @('rest', '--method', $Method, '--uri', $Uri, '--output', 'json', '--only-show-errors')
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 20 -Compress
        $arguments += @('--headers', 'Content-Type=application/json', '--body', $json)
    }

    $result = & az @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Microsoft Graph request failed: $Method $Uri"
    }

    if ([string]::IsNullOrWhiteSpace(($result -join [Environment]::NewLine))) {
        return $null
    }

    return ($result -join [Environment]::NewLine) | ConvertFrom-Json
}

function Assert-SecretFileProtection {
    param([Parameter(Mandatory)][string]$Path)

    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = Get-Acl -LiteralPath $Path
    $explicitRules = @($acl.GetAccessRules(
            $true,
            $false,
            [System.Security.Principal.SecurityIdentifier]))
    $fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
    $validRule = $explicitRules.Count -eq 1 -and
        $explicitRules[0].IdentityReference -eq $identity -and
        $explicitRules[0].AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow -and
        ($explicitRules[0].FileSystemRights -band $fullControl) -eq $fullControl

    if (-not $acl.AreAccessRulesProtected -or -not $validRule) {
        throw 'The secret output ACL was not restricted to the current Windows identity.'
    }
}

function Initialize-ProtectedSecretFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not $IsWindows) {
        throw 'Dev secret creation is supported only on Windows because this script enforces a Windows ACL.'
    }

    $placeholderCreated = $false
    try {
        $placeholder = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $placeholder.Dispose()
        $placeholderCreated = $true

        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl = New-Object System.Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
            $identity,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow)
        $acl.AddAccessRule($rule)
        Set-Acl -LiteralPath $Path -AclObject $acl
        Assert-SecretFileProtection -Path $Path
    }
    catch {
        if ($placeholderCreated -and
            (Test-Path -LiteralPath $Path -PathType Leaf) -and
            (Get-Item -LiteralPath $Path).Length -eq 0) {
            Remove-Item -LiteralPath $Path -Force
        }
        throw
    }
}

function Write-ProtectedSecretFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    Assert-SecretFileProtection -Path $Path
    $stream = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Truncate,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $writer = New-Object System.IO.StreamWriter(
            $stream,
            (New-Object System.Text.UTF8Encoding($false)))
        try {
            $writer.Write($Content)
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
    Assert-SecretFileProtection -Path $Path
}

if ($CreateDevSecret -and [string]::IsNullOrWhiteSpace($SecretOutputPath)) {
    throw '-SecretOutputPath is required when -CreateDevSecret is specified.'
}

if (-not $CreateDevSecret -and -not [string]::IsNullOrWhiteSpace($SecretOutputPath)) {
    throw '-SecretOutputPath can be used only with -CreateDevSecret.'
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Sign in with az login before running this script.'
}

$accountJson = & az account show --output json --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI is not authenticated. Run az login for the target home tenant.'
}
$account = ($accountJson -join [Environment]::NewLine) | ConvertFrom-Json
$tenantId = [string]$account.tenantId
$parsedTenantId = [guid]::Empty
if (-not [guid]::TryParse($tenantId, [ref]$parsedTenantId)) {
    throw 'Azure CLI did not return a valid home tenant ID.'
}

$fullStatePath = [System.IO.Path]::GetFullPath($StatePath)
$stateDirectory = Split-Path -Parent $fullStatePath
if (-not (Test-Path -LiteralPath $stateDirectory -PathType Container)) {
    throw "The state directory does not exist: $stateDirectory"
}

$priorState = $null
if (Test-Path -LiteralPath $fullStatePath) {
    $priorState = Get-Content -LiteralPath $fullStatePath -Raw | ConvertFrom-Json
    if ([string]$priorState.tenantId -ne $tenantId -or [string]$priorState.displayName -ne $DisplayName) {
        throw 'The existing state file belongs to a different tenant or display name. Use another StatePath.'
    }
}

$escapedDisplayName = $DisplayName.Replace("'", "''")
$queryUri = "$graphBaseUri/applications?`$filter=displayName eq '$escapedDisplayName'&`$select=id,appId,displayName"
$applicationResponse = Invoke-Graph -Method GET -Uri $queryUri
$appMatches = @($applicationResponse.value)
if ($appMatches.Count -gt 1) {
    throw "More than one app registration has the exact display name '$DisplayName'. Rename or remove duplicates before continuing."
}

$signInAudience = if ($MultiTenant) { 'AzureADMultipleOrgs' } else { 'AzureADMyOrg' }
$appCreated = $false
if ($appMatches.Count -eq 0) {
    Write-Status "Creating app registration '$DisplayName'"
    $application = Invoke-Graph -Method POST -Uri "$graphBaseUri/applications" -Body @{
        displayName = $DisplayName
        signInAudience = $signInAudience
        web = @{ redirectUris = $redirectUris }
    }
    $appCreated = $true
}
else {
    $application = $appMatches[0]
    if ($null -ne $priorState -and [string]$priorState.application.objectId -eq [string]$application.id) {
        $appCreated = [bool]$priorState.application.created
    }
    Write-Status "Reusing app registration '$DisplayName'"
}

$applicationId = [string]$application.appId
$applicationObjectId = [string]$application.id
$state = [ordered]@{
    schemaVersion = 1
    tenantId = $tenantId
    displayName = $DisplayName
    signInAudience = $signInAudience
    application = [ordered]@{
        objectId = $applicationObjectId
        clientId = $applicationId
        created = $appCreated
    }
    servicePrincipal = $null
    passwordCredential = if ($null -ne $priorState) { $priorState.passwordCredential } else { $null }
}
$state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fullStatePath -Encoding utf8NoBOM

$patchBody = @{
    signInAudience = $signInAudience
    web = @{ redirectUris = $redirectUris }
    spa = @{ redirectUris = @() }
    isFallbackPublicClient = $false
}
$patchBody.requiredResourceAccess = if ($IncludeUserRead) {
    @(
        @{
            resourceAppId = $graphAppId
            resourceAccess = @(
                @{ id = $userReadPermissionId; type = 'Scope' }
            )
        }
    )
}
else {
    @()
}

Write-Status 'Converging sign-in audience and confidential web callbacks'
Invoke-Graph -Method PATCH -Uri "$graphBaseUri/applications/$applicationObjectId" -Body $patchBody | Out-Null

$servicePrincipalQuery = "$graphBaseUri/servicePrincipals?`$filter=appId eq '$applicationId'&`$select=id,appId"
$servicePrincipalResponse = Invoke-Graph -Method GET -Uri $servicePrincipalQuery
$servicePrincipalMatches = @($servicePrincipalResponse.value)
if ($servicePrincipalMatches.Count -gt 1) {
    throw "More than one home-tenant service principal exists for application ID $applicationId."
}

$servicePrincipalCreated = $false
if ($servicePrincipalMatches.Count -eq 0) {
    Write-Status 'Creating the home-tenant service principal'
    $servicePrincipal = Invoke-Graph -Method POST -Uri "$graphBaseUri/servicePrincipals" -Body @{
        appId = $applicationId
    }
    $servicePrincipalCreated = $true
}
else {
    $servicePrincipal = $servicePrincipalMatches[0]
    if ($null -ne $priorState -and [string]$priorState.servicePrincipal.objectId -eq [string]$servicePrincipal.id) {
        $servicePrincipalCreated = [bool]$priorState.servicePrincipal.created
    }
    Write-Status 'Reusing the home-tenant service principal'
}

$state.servicePrincipal = [ordered]@{
    objectId = [string]$servicePrincipal.id
    created = $servicePrincipalCreated
}
$state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fullStatePath -Encoding utf8NoBOM

$credentialState = $state.passwordCredential
if ($CreateDevSecret) {
    if ($null -ne $credentialState -and [bool]$credentialState.created) {
        throw 'State already records a Dev credential created by this script. Run cleanup before creating another.'
    }

    $fullSecretPath = [System.IO.Path]::GetFullPath($SecretOutputPath)
    $fullStatePath = [System.IO.Path]::GetFullPath($StatePath)
    if ($fullSecretPath -eq $fullStatePath) {
        throw 'SecretOutputPath and StatePath must be different files.'
    }
    if (Test-Path -LiteralPath $fullSecretPath) {
        throw "Secret output already exists: $fullSecretPath. Remove it explicitly before requesting a new credential."
    }

    $secretDirectory = Split-Path -Parent $fullSecretPath
    if (-not (Test-Path -LiteralPath $secretDirectory -PathType Container)) {
        throw "The secret output directory does not exist: $secretDirectory"
    }

    Initialize-ProtectedSecretFile -Path $fullSecretPath

    $expiresOn = [DateTimeOffset]::UtcNow.AddDays($SecretLifetimeDays)
    Write-Status "Creating a Dev-only credential that expires in $SecretLifetimeDays day(s)"
    $credential = Invoke-Graph -Method POST -Uri "$graphBaseUri/applications/$applicationObjectId/addPassword" -Body @{
        passwordCredential = @{
            displayName = 'Classic BFF PoC Dev secret'
            endDateTime = $expiresOn.ToString('o')
        }
    }

    $credentialState = [ordered]@{
        keyId = [string]$credential.keyId
        created = $true
    }
    $state.passwordCredential = $credentialState
    $state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fullStatePath -Encoding utf8NoBOM

    $secretDocument = $null
    try {
        $secretDocument = [ordered]@{
            tenantId = $tenantId
            clientId = $applicationId
            clientSecret = [string]$credential.secretText
            expiresOn = [string]$credential.endDateTime
            legacyEnvironmentVariable = 'CROESUS_LEGACY_CLIENT_SECRET'
            modernConfigurationKey = 'AzureAd:ClientSecret'
        }
        Write-ProtectedSecretFile -Path $fullSecretPath -Content ($secretDocument | ConvertTo-Json)
    }
    catch {
        throw "The Dev credential was created and its keyId was recorded in '$fullStatePath', but the protected secret file could not be written. Run cleanup-classic-net-bff-poc.ps1 with this StatePath before retrying. Do not print or inspect the secret output."
    }
    finally {
        $credential.secretText = $null
        if ($null -ne $secretDocument) {
            $secretDocument.clientSecret = $null
        }
    }
    Write-Warning "Credential material was written only to $fullSecretPath with its protected ACL preserved. Delete it after configuring both samples."
}

$state.passwordCredential = $credentialState
$state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fullStatePath -Encoding utf8NoBOM

Write-Status "Provisioning complete. Non-secret state: $fullStatePath"
Write-Output "tenant_id=$tenantId"
Write-Output "client_id=$applicationId"
Write-Output "sign_in_audience=$signInAudience"
