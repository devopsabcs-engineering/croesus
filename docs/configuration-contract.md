---
title: Croesus Demo Configuration Contract
description: Single source-of-truth catalog of every public (non-secret) configuration value for the Croesus mock SaaS OBO-flow demo and the consumer that reads it
author: Croesus Demo Team
ms.date: 2026-09-22
ms.topic: reference
keywords:
  - configuration
  - msal
  - obo
  - github actions
  - bicep
estimated_reading_time: 6
---

## Purpose

This document is the authoritative catalog of configuration for the Croesus mock SaaS demo. Every value the SPA, the API, the Bicep templates, the provisioning scripts, and the deploy workflow depend on appears here exactly once, alongside its source of truth and the components that consume it.

Two rules govern this contract:

* Public, non-secret values (tenant ID, client IDs, the API scope, resource names) live as GitHub Actions repository or environment variables (`vars.*`). They are not credentials, so they may be baked into the SPA build and the API app settings.
* The API confidential-client credential is a certificate stored in Key Vault. It never appears in `vars.*`, the repository, a `.bicepparam` file, or the workflow. The API reads it through an App Service Key Vault reference resolved by a managed identity.

## Public configuration values

Each row below names one value, where it is defined, and every component that reads it. A value defined as a GitHub Actions variable flows outward to the SPA build, the API app settings, and the Bicep parameters through the workflow.

| Config value | Canonical identifier | Source of truth | SPA build var | API app setting | Bicep param | GitHub Actions reference |
| --- | --- | --- | --- | --- | --- | --- |
| Microsoft Entra tenant ID | `AZURE_TENANT_ID` | Provisioning output, stored as repo variable | `VITE_TENANT_ID` | `AzureAd:TenantId` | `tenantId` | `vars.AZURE_TENANT_ID` |
| SPA (public client) app registration ID | `SPA_CLIENT_ID` | `provision` script output, stored as repo variable | `VITE_SPA_CLIENT_ID` | not used | `spaClientId` | `vars.SPA_CLIENT_ID` |
| API (confidential client) app registration ID | `API_CLIENT_ID` | `provision` script output, stored as repo variable | not used | `AzureAd:ClientId` | `apiClientId` | `vars.API_CLIENT_ID` |
| API delegated scope | `API_SCOPE` (`api://<API_CLIENT_ID>/access_as_user`) | Derived from `API_CLIENT_ID` by the `provision` script | `VITE_API_SCOPE` | `AzureAd:Scopes` (not the enforcement path; see note) | not used | `vars.API_SCOPE` |
| API base URL the SPA calls | `API_BASE_URL` | Bicep output (App Service hostname) | `VITE_API_BASE_URL` | not used | derived output | `vars.API_BASE_URL` |
| SPA App Service / Web App name | `croesus-spa` | Fixed convention in this contract | not used | not used | `spaAppName` | `vars.SPA_APP_NAME` |
| API App Service / Web App name | `croesus-api` | Fixed convention in this contract | not used | not used | `apiAppName` | `vars.API_APP_NAME` |
| Resource group | `RESOURCE_GROUP` | Bicep deployment target, stored as repo variable | not used | not used | deployment scope | `vars.RESOURCE_GROUP` |
| Key Vault name | `KEY_VAULT_NAME` | Bicep output | not used | referenced in Key Vault URI | `keyVaultName` | `vars.KEY_VAULT_NAME` |
| Log Analytics workspace ID | `LOG_ANALYTICS_WORKSPACE_ID` | Bicep output (workspace customer ID) | not used | not used | derived output | `vars.LOG_ANALYTICS_WORKSPACE_ID` |
| Application Insights connection string | `APPLICATIONINSIGHTS_CONNECTION_STRING` | Bicep output (App Insights resource), stored in Key Vault | not used | `APPLICATIONINSIGHTS_CONNECTION_STRING` (App Service Key Vault reference) | `appInsightsConnectionString` | not used (resolved at runtime via Key Vault reference, not the workflow) |
| Replay demo endpoint toggle | `Demo:EnableReplay` | Fixed convention in this contract, default `false` | not used | `Demo__EnableReplay` (set directly in Bicep app settings) | not used | not used |
| Tier 2 replay UI toggle | `VITE_ENABLE_REPLAY_DEMO` | Fixed convention in this contract, default `false` | `VITE_ENABLE_REPLAY_DEMO` | not used | not used | `vars.VITE_ENABLE_REPLAY_DEMO` |
| Raw-token inspector toggle | `VITE_ENABLE_TOKEN_INSPECTOR` | Fixed convention in this contract, default `false` | `VITE_ENABLE_TOKEN_INSPECTOR` | not used | not used | `vars.ENABLE_TOKEN_INSPECTOR` |
| SPA Graph delegated scope | `VITE_GRAPH_SCOPE` | Fixed convention in this contract, default `User.Read` | `VITE_GRAPH_SCOPE` | not used | not used | `vars.VITE_GRAPH_SCOPE` |
| Microsoft Graph base URL | `VITE_GRAPH_BASE_URL` | Fixed convention in this contract, optional, default `https://graph.microsoft.com/v1.0` | `VITE_GRAPH_BASE_URL` | not used | not used | `vars.VITE_GRAPH_BASE_URL` |
| Reference BFF (confidential client) app registration ID | `BFF_CLIENT_ID` | `provision` script output, stored as repo variable | not used | not used | not used | `vars.BFF_CLIENT_ID` |
| Comparison PoC ingress posture | `ingressMode` | [../infra/poc/main.bicepparam](../infra/poc/main.bicepparam), `Public` or `Private`, default `Public` | not used | not used | `ingressMode` | not used |
| Comparison PoC App Service plan SKU | `appServicePlanSkuName` | [../infra/poc/main.bicepparam](../infra/poc/main.bicepparam), default `B1` | not used | not used | `appServicePlanSkuName` | not used |

> [!NOTE]
> `BFF_CLIENT_ID` belongs to the reference back-end-for-frontend in [../poc/bff-yarp-net10](../poc/bff-yarp-net10), which is a separate registration from the API and the SPA. Its redirect URIs are derived from `BFF_BASE_URI` in [../scripts/provision-app-registrations.sh](../scripts/provision-app-registrations.sh), whose default names the dedicated BFF site rather than the existing comparison apps. `ingressMode` and `appServicePlanSkuName` govern the comparison PoC in [../infra/poc/main.bicep](../infra/poc/main.bicep) only; neither reaches the SPA or the API. Private ingress requires `B1` or better, and the template refuses the combination at deploy time on `F1` or `D1`.

## Reference BFF app settings

The keys below are read only by the reference back-end-for-frontend and the owned downstream API in [../poc/bff-yarp-net10](../poc/bff-yarp-net10) and [../poc/owned-api-net10](../poc/owned-api-net10). They do not reach the SPA or the demonstration API, so they sit outside the table above. Every one is set as an App Service application setting using the double-underscore form that maps onto a configuration section.

| App setting | Configuration key | Purpose | Required outside Development and PoC |
| --- | --- | --- | --- |
| `DownstreamApi__Scopes__0` | `DownstreamApi:Scopes:0` | The delegated scope the BFF requests for the owned API, in the form `api://<API app ID>/access_as_user`. The resource portion also determines what the evidence surface reports as the acquisition target. | Yes |
| `ProxyPolicy__AllowedDestinationOrigins__0` | `ProxyPolicy:AllowedDestinationOrigins:0` | Origin allowlist enforced before a request is forwarded. A destination absent from this list is refused rather than proxied. | Yes |
| `ReverseProxy__Clusters__owned-api__Destinations__primary__Address` | `ReverseProxy:Clusters:owned-api:Destinations:primary:Address` | The YARP destination address for the owned API cluster. It must resolve to an origin present in the allowlist above. | Yes |
| `DataProtection__KeyRingPath` | `DataProtection:KeyRingPath` | Directory holding the Data Protection key ring. On Windows App Service, use a path under `D:\home\data` so keys survive restart and span instances. An unset value leaves the key ring ephemeral, and the configuration validator refuses to start. | Yes |
| `DataProtection__ProtectKeysWithDpapi` | `DataProtection:ProtectKeysWithDpapi` | Opt-in DPAPI encryption of the key ring, default `false`. Leave it unset on App Service: the worker runs without a loaded user profile, so user-scoped DPAPI fails at startup, and machine-scoped keys cannot be read by a second instance. | No |
| `DistributedCache__Redis__ConnectionString` | `DistributedCache:Redis:ConnectionString` | Backing store for the server-side session ticket store and the MSAL token cache. Unset, both fall back to an in-process cache that cannot span instances or survive a restart. | Yes |
| `Authentication__Mode` | `Authentication:Mode` | Selects the authentication posture the BFF applies at startup. | Yes |
| `Authentication__AllowedTenantIds__0` | `Authentication:AllowedTenantIds:0` | Single-tenant issuer allowlist enforced by the tenant policy during token validation, independent of the authority configured for the authority endpoint. | Yes |

Telemetry uses three further settings on each PoC site: `APPLICATIONINSIGHTS_CONNECTION_STRING`, `ApplicationInsightsAgent_EXTENSION_VERSION` set to `~3`, and `XDT_MicrosoftApplicationInsights_Mode` set to `recommended`. The component `croesus-bff-poc-ai` is workspace-based and writes into `croesus-bff-poc-law`, which is why telemetry must be read through the Log Analytics workspace rather than through `az monitor app-insights query`.

<!-- -->

> [!NOTE]
> The active scope-enforcement path is the `[RequiredScope("access_as_user")]` attribute on the API controllers ([../api/Controllers/MeController.cs](../api/Controllers/MeController.cs) and [../api/Controllers/ReplayController.cs](../api/Controllers/ReplayController.cs)), which rejects a token that lacks the `access_as_user` scope with `403`. The `AzureAd:Scopes` app setting in the table above is not read by the API code and is not the mechanism that enforces the scope; treat it as configuration metadata, not the control.

<!-- -->

> [!NOTE]
> `API_CLIENT_ID`, `SPA_CLIENT_ID`, and `API_SCOPE` are produced by [../scripts/provision-app-registrations.sh](../scripts/provision-app-registrations.sh), which also **persists them as repository variables automatically** via the `gh` CLI when it is installed and authenticated (controlled by `PERSIST_REPO_VARIABLES`, default `auto`). When `gh` is unavailable the script prints the exact `gh variable set` commands to run. The evidence workflow tolerates a missing `API_CLIENT_ID` by filtering the Microsoft Graph leg only.

## Deploy identity values

The deploy workflow authenticates to Azure with an OpenID Connect federated credential. No deploy secret is stored. These values are distinct from the API confidential-client credential and are never used by the running application.

| Config value | Canonical identifier | Source of truth | Consumer | GitHub Actions reference |
| --- | --- | --- | --- | --- |
| Deploy identity app registration ID | `AZURE_CLIENT_ID` | Deploy federated-identity app registration | `azure/login@v2` in the workflow | `vars.AZURE_CLIENT_ID` |
| Azure subscription ID | `AZURE_SUBSCRIPTION_ID` | Target subscription, stored as repo variable | `azure/login@v2` and Bicep deployment | `vars.AZURE_SUBSCRIPTION_ID` |

## Secret configuration values

The following value is a credential. It is managed exclusively in Key Vault and surfaced to the API at runtime through a Key Vault reference resolved by the API App Service managed identity.

| Config value | Canonical identifier | Source of truth | Consumer | Exposure rule |
| --- | --- | --- | --- | --- |
| API confidential-client certificate | `croesus-api-cert` | Key Vault only | API reads it via App Service Key Vault reference and managed identity | Key Vault only. Never `vars.*`, never the repository, never `.bicepparam`, never the workflow |
| Reference BFF confidential-client certificate | `croesus-bff-cert` | Key Vault only, created and attached by [../scripts/provision-app-registrations.sh](../scripts/provision-app-registrations.sh) | Reference BFF confidential-client credential on the `BFF_CLIENT_ID` registration | Key Vault only. The private key never leaves the vault. Never `vars.*`, never the repository, never `.bicepparam`, never the workflow |
| CI test service principal app ID | `TEST_SP_CLIENT_ID` | CI test identity, stored as a GitHub Actions secret | `negative-test.sh` (client-credentials grant for the Graph-audience token) via the `evidence` job | GitHub Actions secret only. Never `vars.*`, never the repository |
| CI test service principal secret | `TEST_SP_CLIENT_SECRET` | CI test identity, stored as a GitHub Actions secret | `negative-test.sh` via the `evidence` job | GitHub Actions secret only |
| CI test user UPN (non-MFA) | `TEST_USERNAME` | Dedicated CI test user, stored as a GitHub Actions secret | `smoke-test.sh` / `negative-test.sh` ROPC token A acquisition | GitHub Actions secret only |
| CI test user password | `TEST_PASSWORD` | Dedicated CI test user, stored as a GitHub Actions secret | `smoke-test.sh` / `negative-test.sh` ROPC token A acquisition | GitHub Actions secret only |

> [!IMPORTANT]
> By default the SPA requests only `API_SCOPE`, and no Microsoft Graph scope appears in SPA configuration. Graph access happens inside the API through the On-Behalf-Of exchange. The Tier 2 replay demo is an intentional, gated exception: when `VITE_ENABLE_REPLAY_DEMO` is `true`, the SPA also requests the `VITE_GRAPH_SCOPE` delegated scope (default `User.Read`) to demonstrate token replay behavior. This exception stays disabled unless the toggle is explicitly set.

## How a new value enters the contract

When a later phase introduces a configuration value, add one row to the matching table above before wiring it into code. Confirm the value is defined in exactly one source of truth, then map it to each consumer. If the value is a credential, it belongs in the secret table and must follow the Key Vault-only exposure rule.
