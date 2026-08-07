[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$StatePath = '.classic-net-bff-poc-state.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$graphBaseUri = 'https://graph.microsoft.com/v1.0'
$fullStatePath = [System.IO.Path]::GetFullPath($StatePath)

function Write-Status {
    param([Parameter(Mandatory)][string]$Message)

    Write-Information ">>> $Message" -InformationAction Continue
}

function Invoke-Graph {
    param(
        [Parameter(Mandatory)][ValidateSet('POST', 'DELETE')][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [object]$Body
    )

    $arguments = @('rest', '--method', $Method, '--uri', $Uri, '--output', 'none', '--only-show-errors')
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 10 -Compress
        $arguments += @('--headers', 'Content-Type=application/json', '--body', $json)
    }

    & az @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Microsoft Graph request failed: $Method $Uri"
    }
}

function Test-GraphObject {
    param([Parameter(Mandatory)][string]$Uri)

    $response = & az rest --method GET --uri $Uri --output none --only-show-errors 2>&1
    if ($LASTEXITCODE -eq 0) {
        return $true
    }

    $message = $response -join [Environment]::NewLine
    if ($message -match '404|Request_ResourceNotFound') {
        return $false
    }

    throw "Microsoft Graph lookup failed: GET $Uri`n$message"
}

function Save-State {
    $script:state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fullStatePath -Encoding utf8NoBOM
}

if (-not (Test-Path -LiteralPath $fullStatePath -PathType Leaf)) {
    throw "Provisioning state file not found: $fullStatePath"
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Sign in with az login before running this script.'
}

$state = Get-Content -LiteralPath $fullStatePath -Raw | ConvertFrom-Json
if ([int]$state.schemaVersion -ne 1) {
    throw "Unsupported state schema version: $($state.schemaVersion)"
}

$accountJson = & az account show --output json --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Azure CLI is not authenticated. Run az login for the recorded home tenant.'
}
$account = ($accountJson -join [Environment]::NewLine) | ConvertFrom-Json
if ([string]$account.tenantId -ne [string]$state.tenantId) {
    throw "Azure CLI is signed in to tenant $($account.tenantId), but the state belongs to tenant $($state.tenantId)."
}

$applicationObjectId = [string]$state.application.objectId
$servicePrincipalObjectId = if ($null -ne $state.servicePrincipal) {
    [string]$state.servicePrincipal.objectId
}
else {
    $null
}

if ($null -ne $state.passwordCredential -and [bool]$state.passwordCredential.created) {
    $keyId = [string]$state.passwordCredential.keyId
    if ($PSCmdlet.ShouldProcess("password credential $keyId", 'Remove recorded Dev credential')) {
        if (Test-GraphObject -Uri "$graphBaseUri/applications/$applicationObjectId") {
            Write-Status 'Removing the recorded Dev credential'
            Invoke-Graph -Method POST -Uri "$graphBaseUri/applications/$applicationObjectId/removePassword" -Body @{
                keyId = $keyId
            }
        }
        else {
            Write-Status 'The owning app registration is already absent; the recorded Dev credential is also gone'
        }
        $state.passwordCredential.created = $false
        Save-State
    }
}
else {
    Write-Status 'No script-created Dev credential is recorded; skipping credential removal'
}

if ($null -ne $state.servicePrincipal -and [bool]$state.servicePrincipal.created) {
    if ($PSCmdlet.ShouldProcess("service principal $servicePrincipalObjectId", 'Delete script-created service principal')) {
        if (Test-GraphObject -Uri "$graphBaseUri/servicePrincipals/$servicePrincipalObjectId") {
            Write-Status 'Deleting the script-created home-tenant service principal'
            Invoke-Graph -Method DELETE -Uri "$graphBaseUri/servicePrincipals/$servicePrincipalObjectId"
        }
        else {
            Write-Status 'The script-created home-tenant service principal is already absent'
        }
        $state.servicePrincipal.created = $false
        Save-State
    }
}
else {
    Write-Status 'The home-tenant service principal was reused; leaving it intact'
}

if ([bool]$state.application.created) {
    if ($PSCmdlet.ShouldProcess("app registration $applicationObjectId", 'Delete script-created app registration')) {
        if (Test-GraphObject -Uri "$graphBaseUri/applications/$applicationObjectId") {
            Write-Status 'Deleting the script-created app registration'
            Invoke-Graph -Method DELETE -Uri "$graphBaseUri/applications/$applicationObjectId"
        }
        else {
            Write-Status 'The script-created app registration is already absent'
        }
        $state.application.created = $false
        Save-State
    }
}
else {
    Write-Status 'The app registration was reused; leaving it intact'
}

Write-Status "Cleanup complete. Review and remove the non-secret state file when it is no longer needed: $fullStatePath"
