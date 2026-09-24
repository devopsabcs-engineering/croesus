<#
.SYNOPSIS
    Restores the demo-ready posture of a Croesus App Service plan and its sites.

.DESCRIPTION
    An external governance process returns this subscription's App Service plans to F1 and sets
    publicNetworkAccess to Disabled on roughly a 24 hour cycle. Neither change is authored in this
    repository and no policy assignment enforcing either one is findable at subscription or tenant
    root scope, so the posture is restored on demand rather than prevented.

    Disabled public ingress with no private endpoint on the site leaves it unreachable from every
    network path. That single cause surfaces under two unrelated-looking symptoms: an
    application-layer HTTP 403 to a browser, and "Ip Forbidden (CODE: 403)" to
    azure/webapps-deploy. The site IP rules are Allow all in both cases, so an IP allowlist is
    neither the cause nor the cure. Restoring ingress is a precondition for package deployment,
    not a post-deployment repair, and this script is therefore meant to run first.

    Every write is conditional on a read, so a run against an already-correct environment makes no
    control-plane change and reports no-change. Safe to run before every demo and on every
    pipeline execution.

    This script sets publicNetworkAccess imperatively by design. Where a template redeployment runs
    ahead of the package deployment, prefer that: infra/poc/main.bicep owns the POC posture through
    its ingressMode parameter, and scripts/verify-ingress.ps1 reports drift there without repairing
    it. Use this script for stacks whose pipeline deploys packages without redeploying the template.

.EXAMPLE
    ./scripts/restore-demo-ingress.ps1 -AppName croesus-spa, croesus-api

.EXAMPLE
    ./scripts/restore-demo-ingress.ps1 -AppName croesus-spa, croesus-api -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string[]]$AppName,

    # Both are resolved from the first app when omitted, so a caller that knows only the site names
    # needs no further configuration and cannot drift out of step with the deployment.
    [string]$ResourceGroupName,

    [string]$AppServicePlanName,

    # F1 Free and D1 Shared are the degraded tiers this script exists to reverse, so neither is a
    # valid target. B1 is the lowest tier that supports Always On and a private endpoint.
    [ValidateSet('B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P0v3', 'P1v3', 'P2v3', 'P3v3')]
    [string]$SkuName = 'B1',

    [ValidateSet('Enabled', 'Disabled')]
    [string]$PublicNetworkAccess = 'Enabled'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Az {
    param([Parameter(Mandatory)][string[]]$Arguments)

    # az writes warnings and deprecation notices to stderr on otherwise successful calls, so the
    # streams are separated rather than merged. Merging them puts non-JSON text in front of the
    # payload and ConvertFrom-Json fails on a command that actually succeeded.
    $stdout = [System.Collections.Generic.List[string]]::new()
    $stderr = [System.Collections.Generic.List[string]]::new()

    & az @Arguments -o json 2>&1 | ForEach-Object {
        if ($_ -is [System.Management.Automation.ErrorRecord]) { $stderr.Add($_.ToString()) }
        else { $stdout.Add([string]$_) }
    }

    if ($LASTEXITCODE -ne 0) {
        $detail = (($stderr + $stdout) -join [Environment]::NewLine)
        throw "az $($Arguments -join ' ') failed with exit code $LASTEXITCODE`n$detail"
    }

    $text = ($stdout -join [Environment]::NewLine).Trim()
    if (-not $text) { return $null }
    return $text | ConvertFrom-Json
}

if (-not $ResourceGroupName) {
    $candidates = @(Invoke-Az @('webapp', 'list', '--query', "[?name=='$($AppName[0])'].resourceGroup"))
    if ($candidates.Count -ne 1) {
        throw "Expected exactly one web app named '$($AppName[0])' in the current subscription, found $($candidates.Count). Pass -ResourceGroupName explicitly."
    }
    $ResourceGroupName = $candidates[0]
    Write-Host "Resolved resource group '$ResourceGroupName' from app '$($AppName[0])'."
}

if (-not $AppServicePlanName) {
    $planId = (Invoke-Az @('webapp', 'show', '-g', $ResourceGroupName, '-n', $AppName[0], '--query', 'serverFarmId'))
    $AppServicePlanName = $planId.Split('/')[-1]
    Write-Host "Resolved App Service plan '$AppServicePlanName' from app '$($AppName[0])'."
}

$changes = [System.Collections.Generic.List[pscustomobject]]::new()
function Add-Change {
    param([string]$Target, [string]$Property, [string]$Before, [string]$After)
    $changes.Add([pscustomobject]@{
            Target   = $Target
            Property = $Property
            Before   = $Before
            After    = $After
            Outcome  = if ($Before -ceq $After) { 'no-change' } elseif ($After -ceq '(skipped)') { 'skipped' } else { 'repaired' }
        })
}

# The plan is reconciled first. Tier degradation can stop the sites it hosts as a side effect, so
# repairing ingress on a site still pinned to F1 would report success and then regress.
$plan = Invoke-Az @('appservice', 'plan', 'show', '-g', $ResourceGroupName, '-n', $AppServicePlanName)
$planBefore = $plan.sku.name

if ($planBefore -cne $SkuName) {
    if ($PSCmdlet.ShouldProcess($AppServicePlanName, "Scale App Service plan from $planBefore to $SkuName")) {
        $null = Invoke-Az @('appservice', 'plan', 'update', '-g', $ResourceGroupName, '-n', $AppServicePlanName, '--sku', $SkuName)
        $planAfter = (Invoke-Az @('appservice', 'plan', 'show', '-g', $ResourceGroupName, '-n', $AppServicePlanName)).sku.name
    }
    else {
        $planAfter = '(skipped)'
    }
}
else {
    $planAfter = $planBefore
}
Add-Change -Target $AppServicePlanName -Property 'sku.name' -Before $planBefore -After $planAfter

foreach ($name in $AppName) {
    $site = Invoke-Az @('webapp', 'show', '-g', $ResourceGroupName, '-n', $name)
    $accessBefore = [string]$site.publicNetworkAccess
    $stateBefore = [string]$site.state

    if ($accessBefore -cne $PublicNetworkAccess) {
        if ($PSCmdlet.ShouldProcess($name, "Set publicNetworkAccess from '$accessBefore' to $PublicNetworkAccess")) {
            # The property is carried on both the site and its siteConfig and the two can disagree.
            # A site reading Enabled while its config reads Disabled still returns 403.
            $null = Invoke-Az @('webapp', 'update', '-g', $ResourceGroupName, '-n', $name,
                '--set', "publicNetworkAccess=$PublicNetworkAccess", "siteConfig.publicNetworkAccess=$PublicNetworkAccess")
            $accessAfter = [string](Invoke-Az @('webapp', 'show', '-g', $ResourceGroupName, '-n', $name)).publicNetworkAccess
        }
        else {
            $accessAfter = '(skipped)'
        }
    }
    else {
        $accessAfter = $accessBefore
    }
    Add-Change -Target $name -Property 'publicNetworkAccess' -Before $accessBefore -After $accessAfter

    if ($stateBefore -cne 'Running') {
        if ($PSCmdlet.ShouldProcess($name, "Start site from state '$stateBefore'")) {
            $null = Invoke-Az @('webapp', 'start', '-g', $ResourceGroupName, '-n', $name)
            $stateAfter = [string](Invoke-Az @('webapp', 'show', '-g', $ResourceGroupName, '-n', $name)).state
        }
        else {
            $stateAfter = '(skipped)'
        }
    }
    else {
        $stateAfter = $stateBefore
    }
    Add-Change -Target $name -Property 'state' -Before $stateBefore -After $stateAfter
}

$changes | Format-Table -AutoSize | Out-String | Write-Host

$expected = @{
    'sku.name'            = $SkuName
    'publicNetworkAccess' = $PublicNetworkAccess
    'state'               = 'Running'
}
$unresolved = $changes | Where-Object { $_.Outcome -ne 'skipped' -and $_.After -cne $expected[$_.Property] }

if ($unresolved) {
    foreach ($item in $unresolved) {
        Write-Host "Unresolved: $($item.Target) $($item.Property) is '$($item.After)', expected '$($expected[$item.Property])'."
    }
    throw 'One or more targets remain outside the demo-ready posture. Package deployment would fail with HTTP 403.'
}

$repaired = @($changes | Where-Object { $_.Outcome -eq 'repaired' }).Count
$skipped = @($changes | Where-Object { $_.Outcome -eq 'skipped' }).Count

if ($skipped -gt 0) {
    Write-Host "$skipped of $($changes.Count) checks are outside the demo-ready posture and were not written because the run was a preview."
}
elseif ($repaired -gt 0) {
    Write-Host "Demo-ready posture restored. $repaired of $($changes.Count) checks required a repair."
}
else {
    Write-Host "Demo-ready posture already correct. No control-plane changes were made."
}
