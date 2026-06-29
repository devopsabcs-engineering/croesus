---
title: Croesus OBO Demo Guide
description: Provision, deploy, exercise, and read the evidence for the Croesus mock SaaS On-Behalf-Of flow demo without referencing internal research notes
author: Croesus Demo Team
ms.date: 2026-06-29
ms.topic: how-to
keywords:
  - obo
  - msal
  - entra
  - app service
  - app insights
estimated_reading_time: 9
---

## Purpose

This guide takes you from an empty tenant to a working demonstration of a standards-compliant Microsoft Entra On-Behalf-Of (OBO) flow, and shows you how to read the evidence that proves the second token is freshly issued rather than replayed. You can complete every step here on its own. No internal research notes are required.

The demo plays out the vendor relationship directly: we act as the Croesus vendor and provide the setup steps, and you (acting as Desjardins) own the two app registrations in your tenant.

## Architecture in brief

A React single-page application (SPA) signs the user in with the authorization-code flow and PKCE, then requests a token for the middle-tier API scope only. The ASP.NET Core API validates that inbound token, performs the OBO exchange to obtain a separate Microsoft Graph token, and calls `GET /me`. Because the SPA never requests a Graph scope, the OBO boundary is unavoidable.

```mermaid
sequenceDiagram
    participant U as User
    participant S as SPA (public client)
    participant A as croesus-api (confidential client)
    participant E as Entra token endpoint
    participant G as Microsoft Graph
    U->>S: Interactive sign-in (auth code + PKCE)
    S->>E: acquireTokenSilent (scope = api://API/access_as_user)
    E-->>S: token A (aud = API)
    S->>A: GET /api/me with Bearer token A
    A->>A: Validate token A (aud == API, scp == access_as_user)
    A->>E: OBO exchange (on_behalf_of, client cert, assertion = token A)
    E-->>A: token B (aud = Microsoft Graph, new jti and iat)
    A->>G: GET /me with Bearer token B
    G-->>A: user profile
    A-->>S: profile plus decoded-claim evidence
```

## Prerequisites

* An Azure subscription and a single Microsoft Entra tenant where you can create app registrations and grant tenant-wide admin consent.
* The Azure CLI, signed in to that tenant with `az login`.
* A GitHub repository that holds this demo, with the configuration and deploy-identity variables described in [configuration-contract.md](configuration-contract.md).
* Permission to create the App Service, Key Vault, and Application Insights resources that [../infra/main.bicep](../infra/main.bicep) defines.

## Step 1: Provision the app registrations

Run the provisioning script. It creates two single-tenant registrations, the SPA as a public client and the API as a confidential client, exposes the `access_as_user` scope on the API, pre-authorizes the SPA, and grants the Microsoft Graph `User.Read` delegated permission.

```bash
./scripts/provision-app-registrations.sh
```

The script prints the identifiers you need next: the tenant ID, the SPA client ID, the API client ID, and the derived API scope (`api://<API_CLIENT_ID>/access_as_user`).

> [!NOTE]
> `az ad app create` is not idempotent. Re-running the script creates duplicate registrations. For repeatable runs, look up each registration by display name first, or store the returned identifiers and reuse them.

Record the outputs as GitHub Actions repository variables. The full mapping, including which component reads each value, lives in [configuration-contract.md](configuration-contract.md).

> [!IMPORTANT]
> The API confidential-client credential is a certificate stored in Key Vault. It never appears in repository variables, in a `.bicepparam` file, or in the workflow. The API reads it through an App Service Key Vault reference resolved by a managed identity.

## Step 2: Deploy the SPA and the API

The deploy workflow at [../.github/workflows/deploy-croesus.yml](../.github/workflows/deploy-croesus.yml) does the rest. It authenticates to Azure with an OpenID Connect federated credential, so there is no long-lived deploy secret. It then provisions the infrastructure from [../infra/main.bicep](../infra/main.bicep) and deploys the [../spa/](../spa/) and [../api/](../api/) projects to the `croesus-spa` and `croesus-api` Web Apps.

Trigger the workflow from the GitHub Actions tab, or push to the branch the workflow watches. When the run finishes, the post-deploy evidence job has already executed the smoke test and the gated negative tests and written the results to the run summary.

## Step 3: Exercise the flow

Open the SPA at its App Service URL and sign in. The SPA acquires a token for the API scope and calls `GET /api/me` on the middle tier. The API validates the inbound token, runs the OBO exchange, calls Microsoft Graph, and returns your profile along with the decoded-claim evidence for both legs.

You can also run the smoke test directly:

```bash
./scripts/smoke-test.sh
```

A passing smoke test confirms the user can sign in, the API accepts the API-audience token, and the OBO call to Graph succeeds.

## Step 4: Run the negative tests

The negative tests prove the audience boundary is enforced, not merely present. They are gated in CI and you can run them on demand:

```bash
./scripts/negative-test.sh
```

Two checks matter:

* A token whose audience is Microsoft Graph, presented to the API, returns `401`.
* The API-audience token (token A), presented directly to Microsoft Graph, returns `401`.

If both rejections occur, no single token works against both resources, which is the behavioral signature of a real OBO.

## Step 5: Read the Application Insights claim evidence

The API logs decoded claims only. It never logs raw tokens. For each request it records three things:

* Leg 1: the inbound token claims, `aud`, `scp`, `appid`, `oid`, and `jti`, where `aud` equals the API.
* The OBO request shape: the `jwt-bearer` grant, the `on_behalf_of` parameter, and the certificate thumbprint.
* Leg 2: the Graph token claims, where `aud` equals Microsoft Graph and the `jti` and `iat` differ from leg 1.

The distinct `aud` values and distinct `jti` values across the two legs are the proof. Token B is a new issuance, not a relay of token A.

## Step 6: Correlate the Entra sign-in logs

The Application Insights claims are the primary evidence. The Entra non-interactive sign-in logs corroborate them. Run the query in [../scripts/evidence-kql.kusto](../scripts/evidence-kql.kusto) against the workspace that receives the tenant's sign-in logs:

```kusto
AADNonInteractiveUserSignInLogs
| where TimeGenerated > ago(1h)
| where AppDisplayName == "Croesus API" or ResourceDisplayName == "Microsoft Graph"
| project TimeGenerated, CorrelationId, AppDisplayName, ResourceDisplayName, UserPrincipalName, Status
| sort by TimeGenerated asc
```

You see two correlated rows that share a `CorrelationId`: the SPA-to-API leg and the API-to-Graph leg. The two resources differ, which mirrors the two distinct audiences you saw in the claim logs.

## Tenancy: single-tenant demo, multi-tenant in production

This demo registers both applications as single-tenant (`signInAudience = AzureADMyOrg`). Microsoft's guidance recommends "accounts in this directory only" when you build your own app registration for a third party that instructs you to do so, which is exactly the Croesus-to-Desjardins relationship. Both registrations live in one tenant, so the SPA-to-API and API-to-Graph relationships are intra-tenant and OBO works without any cross-tenant provisioning.

> [!NOTE]
> A real vendor-hosted multi-tenant SaaS would register the app once in the vendor tenant (`signInAudience = AzureADMultipleOrgs`), use the `/organizations` authority, validate multiple issuers, and onboard each customer through the admin-consent endpoint. Admin consent provisions the service principal in the customer tenant before any token can be requested for it:
>
> ```text
> https://login.microsoftonline.com/organizations/v2.0/adminconsent
> ```
>
> A multi-tenant Application ID URI must be globally unique (for example `https://<verified-domain>/croesus-api`), whereas single-tenant has no such constraint. That is one more reason single-tenant is the simpler shape for this demo.

## What you have proven

After these steps you have a running OBO flow, two distinct tokens with distinct audiences and `jti` values, enforced audience rejection in both directions, and two independent evidence trails: structured claim logs and correlated sign-in logs. The next document maps this evidence to the specific questions Desjardins asked the vendor.
