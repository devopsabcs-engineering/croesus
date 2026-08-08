---
title: GitHub Actions Run 31219189056 Observation
description: Read-only observation evidence for the classic-net-bff-poc workflow run
ms.date: 2026-08-07
ms.topic: troubleshooting
---

## Research Scope

* Follow GitHub Actions run `31219189056` in `devopsabcs-engineering/croesus` to completion using authenticated `gh` CLI/API calls
* Observe only, without editing application files or triggering workflows
* On failure, identify the exact failed job, step, command, error or status code, failure category, and smallest remediation
* On success, identify deployed URLs, resource names, any surfaced app registration client ID, and exact HTTP/authentication redirect smoke results

## Baseline Evidence

* Workflow: `classic-net-bff-poc`
* Workflow file: `.github/workflows/classic-net-bff-poc.yml`
* Run number: `4`
* Attempt: `1`
* Event: `workflow_dispatch`
* Branch: `main`
* Commit: `0d99b879845bd8b44e18051648a3adb5ee1b1344`
* Created: `2026-08-07T21:13:44Z`
* First observed status: `in_progress`
* Run URL: <https://github.com/devopsabcs-engineering/croesus/actions/runs/31219189056>

## Final Status

* Status: `completed`
* Conclusion: `failure`
* Updated: `2026-08-07T21:17:52Z`
* Successful job: `Build, test, package, and validate` (`92999672126`)
* Failed job: `Deploy protected PoC environment` (`93000324766`)

## Job and Step Evidence

The failed job was `Deploy protected PoC environment`. Step 7,
`Provision Entra and deploy infrastructure in one process`, failed between
`2026-08-07T21:17:42Z` and `2026-08-07T21:17:45Z`.

The workflow dot-sourced:

```powershell
. .\scripts\provision-classic-net-bff-deployment.ps1 @provisionParameters
```

The provisioning script's first directory request was equivalent to:

```powershell
az rest --method GET `
  --uri "https://graph.microsoft.com/v1.0/applications?`$filter=displayName eq 'croesus-bff-a3v24wppuvd34-web'&`$select=id,appId,displayName,passwordCredentials,tags" `
  --output json `
  --only-show-errors
```

Microsoft Graph returned `403 Forbidden` with code
`Authorization_RequestDenied` and message
`Insufficient privileges to complete the operation.` The script threw at
`scripts/provision-classic-net-bff-deployment.ps1:49`, and the step ended with
`Process completed with exit code 1.` The Graph request ID and client request
ID were both `1b6e2c16-b054-4497-a7ef-2b1075500216`.

## Deployment and Smoke Evidence

* Azure OIDC succeeded at `2026-08-07T21:17:10Z`
* Deterministic resource group creation succeeded: `croesus-bff-poc-rg`
* Bicep compilation and deployment what-if succeeded
* The actual `az deployment group create` command was not reached because
  Graph provisioning failed first in the same step
* Legacy package build and artifact transfer succeeded, but the App Service
  deployment step was skipped
* Modern package build and artifact transfer succeeded, but the App Service
  deployment step was skipped
* Authentication redirect verification was skipped, so there are no HTTP
  status or redirect results for this run
* The deterministic app names were
  `croesus-bff-a3v24wppuvd34-legacy` and
  `croesus-bff-a3v24wppuvd34-modern`
* Their intended callback URLs were
  `https://croesus-bff-a3v24wppuvd34-legacy.azurewebsites.net/signin-oidc`
  and
  `https://croesus-bff-a3v24wppuvd34-modern.azurewebsites.net/signin-oidc`
* No deployed base URLs or app registration client ID were surfaced. The
  summary environment showed all three output values as empty

## Classification

* Azure OIDC: succeeded
* Graph provisioning: failed with HTTP `403 Authorization_RequestDenied`
* Bicep deployment: compile and what-if succeeded; actual deployment was not
  attempted
* App package deployment: packages were prepared, but both deployment steps
  were skipped
* HTTP smoke checks: skipped

The smallest remediation is to grant the GitHub OIDC bootstrap app registration
the Microsoft Graph application permission `Application.ReadWrite.All`, then
have a tenant administrator grant admin consent. Repository guidance states
that `Application.ReadWrite.OwnedBy` is insufficient for this convergence path
and that `Directory.ReadWrite.All` is not required.

## Clarifying Questions

None.
