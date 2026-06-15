# Croesus / GPD Central — Gap-Closure Verification Guide

**Date:** 2026-06-15
**Audience:** Desjardins identity/security operators with the relevant tenant access.
**Status of commands:** All commands below are **operator-run**. They were **not** executed during authoring. Run each against the correct tenant (DEV `MVTDEVDesjardins.onmicrosoft.com` vs PROD `mvtdesjardins.onmicrosoft.com`).

These commands close the gaps that the application-object exports and screenshots cannot answer on their own. Each block notes the gap it closes.

---

## 1. Confirm delegated permission + admin-consent state

Closes: governance gap — admin-consent grants are not present in the application-object export (finding 3.2 #5).

```bash
# Confirm the delegated scope is Microsoft Graph User.Read
az ad app permission list --id 713d6ede-38a2-45f4-8982-89ea4fcf1a7f -o table

# Capture admin-consent (delegated) grants actually present in the tenant
az ad app permission list-grants --id 713d6ede-38a2-45f4-8982-89ea4fcf1a7f -o table
```

Repeat for the other two appIds (`e3e358ea-0aae-4f11-b866-00f76b1cf6c1`, `92dd40a3-f7c2-42ff-9303-fff5928e195a`) against the PROD tenant.

---

## 2. Pull app owners

Closes: governance gap — owners are not present in the application-object export (finding 3.2 #5).

```bash
az ad app owner list --id 713d6ede-38a2-45f4-8982-89ea4fcf1a7f -o table   # DEV tenant
az ad app owner list --id e3e358ea-0aae-4f11-b866-00f76b1cf6c1 -o table   # PROD tenant
az ad app owner list --id 92dd40a3-f7c2-42ff-9303-fff5928e195a -o table   # PROD tenant
```

---

## 3. Resolve the tenant IDs behind the two publisher domains

Closes: makes the cross-tenant device-compliance argument concrete (Section 4 root cause).

```bash
# DEV tenant issuer / tenant ID
az rest --method get \
  --url "https://login.microsoftonline.com/MVTDEVDesjardins.onmicrosoft.com/v2.0/.well-known/openid-configuration" \
  --query issuer

# PROD tenant issuer / tenant ID
az rest --method get \
  --url "https://login.microsoftonline.com/mvtdesjardins.onmicrosoft.com/v2.0/.well-known/openid-configuration" \
  --query issuer
```

The GUID in the returned issuer URL is the tenant ID. (The PROD tenant observed in the sign-in logs is `728d20a5-0b44-47dd-9470-20f37cbf2d9a`; confirm the DEV tenant ID here.)

---

## 4. Capture the verbatim sign-in log evidence (both events)

Closes: the still-open gap — verbatim AADSTS failure code + CA policy name for the **non-prod DEV-tenant** denial (Section 6.1).

In the **DEV tenant**: Entra admin center → **Monitoring → Sign-in logs** (or Log Analytics). Filter to the affected user and the time window, and capture for **both** the interactive and the non-interactive event:

* AADSTS result/failure code
* Conditional Access policy name(s) and result (e.g., the "Restriction Perimetre" perimeter block observed in the DEV tenant)
* Client App / Resource (audience)
* Source IP (expect a Desjardins corp IP for #1 and an AWS IP for #2)
* Device compliance state and trust type
* Token Protection status (**bound** vs **unbound** / code 1008)

Optional Log Analytics query (KQL) if sign-in logs are exported:

```kusto
SigninLogs
| where TimeGenerated > ago(7d)
| where UserPrincipalName == "<affected-user-upn>"
| where AppDisplayName has "CentralGPD" or AppId in ("713d6ede-38a2-45f4-8982-89ea4fcf1a7f","e3e358ea-0aae-4f11-b866-00f76b1cf6c1","92dd40a3-f7c2-42ff-9303-fff5928e195a")
| project TimeGenerated, IsInteractive, IPAddress, ResultType, ResultDescription,
          AppDisplayName, ResourceDisplayName, ConditionalAccessStatus,
          DeviceDetail, Status, TokenProtectionStatusCode = tostring(parse_json(AuthenticationDetails))
| order by TimeGenerated desc
```

Adjust field projection to your schema; `ConditionalAccessPolicies` and Token Protection details may require expanding the relevant dynamic columns.

---

## 5. Optional — inspect the enterprise applications (service principals)

Closes: confirms the SP objects surfaced in the screenshots and their assignment/visibility settings.

```bash
# DEV enterprise app (SP) for app 713d6ede...
az ad sp show --id 713d6ede-38a2-45f4-8982-89ea4fcf1a7f \
  --query "{spObjectId:id, appRoleAssignmentRequired:appRoleAssignmentRequired, tags:tags}"

# PROD enterprise apps (SPs)
az ad sp show --id e3e358ea-0aae-4f11-b866-00f76b1cf6c1 --query "{spObjectId:id, appRoleAssignmentRequired:appRoleAssignmentRequired}"
az ad sp show --id 92dd40a3-f7c2-42ff-9303-fff5928e195a --query "{spObjectId:id, appRoleAssignmentRequired:appRoleAssignmentRequired}"
```

Expected SP object IDs (from screenshots): DEV `2052c434-d7fa-4022-a2f8-a1676c187456`; dev-prod `323e25ff-4afb-47c1-aa3e-0d1142515473`.

---

## 6. After running

Feed the captured AADSTS code, CA policy name, and audience back into Section 6.1 of `assets/app-registration-analysis-findings.md` to close the remaining evidence gaps, and pair with the Croesus answers from `assets/croesus-escalation-packet.md` before applying any Option A accommodation.
