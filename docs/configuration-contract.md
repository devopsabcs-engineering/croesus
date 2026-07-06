---
title: Croesus Demo Configuration Contract
description: Single source-of-truth catalog of every public (non-secret) configuration value for the Croesus mock SaaS OBO-flow demo and the consumer that reads it
author: Croesus Demo Team
ms.date: 2026-06-29
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
| API delegated scope | `API_SCOPE` (`api://<API_CLIENT_ID>/access_as_user`) | Derived from `API_CLIENT_ID` by the `provision` script | `VITE_API_SCOPE` | `AzureAd:Scopes` | not used | `vars.API_SCOPE` |
| API base URL the SPA calls | `API_BASE_URL` | Bicep output (App Service hostname) | `VITE_API_BASE_URL` | not used | derived output | `vars.API_BASE_URL` |
| SPA App Service / Web App name | `croesus-spa` | Fixed convention in this contract | not used | not used | `spaAppName` | `vars.SPA_APP_NAME` |
| API App Service / Web App name | `croesus-api` | Fixed convention in this contract | not used | not used | `apiAppName` | `vars.API_APP_NAME` |
| Resource group | `RESOURCE_GROUP` | Bicep deployment target, stored as repo variable | not used | not used | deployment scope | `vars.RESOURCE_GROUP` |
| Key Vault name | `KEY_VAULT_NAME` | Bicep output | not used | referenced in Key Vault URI | `keyVaultName` | `vars.KEY_VAULT_NAME` |
| Log Analytics workspace ID | `LOG_ANALYTICS_WORKSPACE_ID` | Bicep output (workspace customer ID) | not used | not used | derived output | `vars.LOG_ANALYTICS_WORKSPACE_ID` |
| Application Insights connection string | `APPLICATIONINSIGHTS_CONNECTION_STRING` | Bicep output (App Insights resource), stored in Key Vault | not used | `APPLICATIONINSIGHTS_CONNECTION_STRING` (App Service Key Vault reference) | `appInsightsConnectionString` | not used (resolved at runtime via Key Vault reference, not the workflow) |
| Replay demo endpoint toggle | `Demo:EnableReplay` | Fixed convention in this contract, default `false` | not used | `Demo__EnableReplay` (set directly in Bicep app settings) | not used | not used |
| Tier 2 replay UI toggle | `VITE_ENABLE_REPLAY_DEMO` | Fixed convention in this contract, default `false` | `VITE_ENABLE_REPLAY_DEMO` | not used | not used | `vars.VITE_ENABLE_REPLAY_DEMO` |
| SPA Graph delegated scope | `VITE_GRAPH_SCOPE` | Fixed convention in this contract, default `User.Read` | `VITE_GRAPH_SCOPE` | not used | not used | `vars.VITE_GRAPH_SCOPE` |
| Microsoft Graph base URL | `VITE_GRAPH_BASE_URL` | Fixed convention in this contract, optional, default `https://graph.microsoft.com/v1.0` | `VITE_GRAPH_BASE_URL` | not used | not used | `vars.VITE_GRAPH_BASE_URL` |

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
| CI test service principal app ID | `TEST_SP_CLIENT_ID` | CI test identity, stored as a GitHub Actions secret | `negative-test.sh` (client-credentials grant for the Graph-audience token) via the `evidence` job | GitHub Actions secret only. Never `vars.*`, never the repository |
| CI test service principal secret | `TEST_SP_CLIENT_SECRET` | CI test identity, stored as a GitHub Actions secret | `negative-test.sh` via the `evidence` job | GitHub Actions secret only |
| CI test user UPN (non-MFA) | `TEST_USERNAME` | Dedicated CI test user, stored as a GitHub Actions secret | `smoke-test.sh` / `negative-test.sh` ROPC token A acquisition | GitHub Actions secret only |
| CI test user password | `TEST_PASSWORD` | Dedicated CI test user, stored as a GitHub Actions secret | `smoke-test.sh` / `negative-test.sh` ROPC token A acquisition | GitHub Actions secret only |

> [!IMPORTANT]
> By default the SPA requests only `API_SCOPE`, and no Microsoft Graph scope appears in SPA configuration. Graph access happens inside the API through the On-Behalf-Of exchange. The Tier 2 replay demo is an intentional, gated exception: when `VITE_ENABLE_REPLAY_DEMO` is `true`, the SPA also requests the `VITE_GRAPH_SCOPE` delegated scope (default `User.Read`) to demonstrate token replay behavior. This exception stays disabled unless the toggle is explicitly set.

## How a new value enters the contract

When a later phase introduces a configuration value, add one row to the matching table above before wiring it into code. Confirm the value is defined in exactly one source of truth, then map it to each consumer. If the value is a credential, it belongs in the secret table and must follow the Key Vault-only exposure rule.
