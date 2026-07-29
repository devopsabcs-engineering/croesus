<!-- markdownlint-disable-file -->

# Croesus `/token` response alternatives analysis

## Research scope

Primary evidence inputs:

* `.copilot-tracking/research/subagents/2026-07-28/repository-customer-evidence-research.md`
* `.copilot-tracking/research/subagents/2026-07-28/authoritative-token-semantics-research.md`

Questions under investigation:

* Which response position best fits the demonstrated protocol and app-registration evidence?
* What assumptions, security properties, customer effects, limitations, and falsifiers apply to alternatives A through E?
* Can an authorization-code `/token` transaction trigger Conditional Access behavior similar to OBO without implying identical evaluation?
* What should be sent separately to Mathieu and Olivier?
* Which repository tests and documents require correction before further customer use?

## Decision summary

Select **alternative A**: accept Olivier's authorization-code-with-PKCE
explanation as the leading, protocol-plausible hypothesis, but not yet as a
proved account of the implementation. Request one correlated transaction
before changing Conditional Access policy, trusting Croesus IP addresses,
moving redemption behind a Desjardins proxy, or requiring an OBO redesign.

This position best fits all currently demonstrated facts:

* Microsoft Entra's `/oauth2/v2.0/token` endpoint is used for multiple grants.
	A backend call to that URL does not identify OBO or replay.
* Olivier's stated request is compatible with ordinary authorization-code
	redemption: `grant_type=authorization_code`, a one-time `code`, and the PKCE
	`code_verifier`.
* The three supplied Croesus registrations are single-tenant SPA registrations
	with delegated Microsoft Graph `User.Read`, no exposed Croesus API scope, no
	app roles, and no secret or certificate. They fit direct delegated Graph
	acquisition materially better than the repository's confidential OBO middle
	tier.
* Those registrations do not prove which component holds the verifier or
	redeems the code. A server-side redeemer using a registration whose redirect
	URI is typed as SPA is an implementation detail that must be demonstrated,
	not inferred from the registration export.
* `TokenProtectionStatusDetails=unbound` and status code `1008` describe the
	sign-in session's binding status. They do not prove that the same access-token
	value was replayed, and they do not identify the OAuth grant.
* Conditional Access can affect authorization-code token issuance and can
	produce non-interactive behavior that resembles an OBO token request in the
	logs. The two transactions must not be described as having identical policy
	evaluation.

The selected approach is not passive acceptance. It is a controlled evidence
gate. Croesus must demonstrate the grant and token continuity, while
Desjardins must provide the correlated policy evaluation that produced the
block. Until then, the status is **unresolved, with authorization-code plus PKCE
as the leading explanation**.

## Endpoint evidence

![Microsoft identity platform OIDC endpoint overview](assets/latest-info/image.png)

The supplied Microsoft Learn image says the Token endpoint at
`/oauth2/v2.0/token` redeems an authorization code, refresh token, or client
credential. It directly answers why `/token` can be mandatory before refresh
and without OBO. The image does not identify Croesus's grant, prove its
server-side topology, or explain the Conditional Access result.

The request body, not the endpoint name, distinguishes the relevant grants:

```text
Authorization code + PKCE:
	grant_type=authorization_code
	code=<one-time authorization code>
	code_verifier=<PKCE verifier>

Microsoft Entra OBO:
	grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer
	assertion=<access token for the middle tier>
	requested_token_use=on_behalf_of
	scope=<downstream resource scopes>
	plus confidential-client authentication
```

## Conditional Access answer

**Yes. An authorization-code redemption at `/token` can trigger or surface
Conditional Access behavior similar to an OBO token request, but the evaluation
must not be claimed to be identical.**

Both operations ask Entra to issue tokens and can appear as non-interactive
activity after an interactive user step. Either can be blocked, require user
interaction, or carry policy and session context into a resource decision.
Source network, client type, application, resource, tenant, device state,
session state, and the policies in scope can influence the observed result.

The operations differ in security context:

* Authorization-code redemption presents a short-lived, one-time authorization
	code and, when PKCE applies, a matching verifier for the OAuth client and
	redirect URI.
* OBO presents an inbound user access token as `assertion`; the middle tier
	authenticates as a confidential client and requests a token for a downstream
	resource.
* The application and resource in the sign-in record, the client type, the
	source IP used for token issuance, available device/session claims, and the
	downstream claims-challenge path can therefore differ.
* A similar block or `unbound` result does not show that the two grants were
	evaluated through the same path or for the same reason.

The exact answer for Croesus requires the failed policy name, grant and session
controls, result, failure code, application, resource, and correlation ID from
the actual sign-in record. Conditional Access is part of the outcome, but its
presence does not classify the protocol.

## Alternative A: provisional authorization-code explanation

### Protocol assumptions

* Central initiates an authorization request with `response_type=code` and a
	PKCE challenge.
* A Croesus component that possesses the matching verifier redeems the one-time
	code at Entra with `grant_type=authorization_code`.
* The returned delegated token is intended for Microsoft Graph `User.Read` and
	is the token later presented to Graph.
* No inbound access token is submitted as an OBO `assertion`, and no previously
	issued access token is reused as the authorization grant.

### Security properties

* PKCE binds code redemption to possession of the verifier and reduces
	authorization-code interception risk.
* Entra still enforces client, redirect URI, consent, tenant, scope, and
	Conditional Access requirements.
* Graph must validate the token's signature, issuer, lifetime, audience, and
	delegated scope.
* Holding architecture and policy steady preserves the evidence needed to
	classify the failure and avoids creating an exception around a potentially
	invalid flow.

This option does not make server-side token storage or forwarding safe by
assertion. Croesus must still show where the verifier and resulting access token
exist, how they are protected, and that the exact token is used only at its
intended resource.

### Evidence support

* Olivier explicitly reports OAuth with PKCE, no OBO, and mandatory server-side
	Entra `/token` redemption.
* The Microsoft endpoint overview and authorization-code documentation confirm
	that this is a normal reason to call `/token`.
* All three supplied Croesus registrations have SPA redirect URIs and delegated
	Graph `User.Read`, with no exposed API scope or confidential credential.
* AWS source IP is compatible with server-side code redemption and does not
	prove bearer replay.

Support is provisional because the redacted token request and resource call are
absent.

### Customer impact

* No immediate production redesign or Conditional Access exception.
* A bounded joint trace can resolve the terminology and causal dispute in one
	controlled non-production transaction.
* The response acknowledges Croesus's plausible explanation without asking
	Desjardins to weaken policy based on an unverified claim.
* Meeting time can focus on evidence instead of debating the overloaded
	`/token` endpoint name.

### Limitations

* The current exports do not identify the actual AWS redeemer.
* A SPA registration with no client credential is not the repository mock's
	confidential web API. Croesus must explain how its backend legitimately owns
	the public-client transaction and PKCE verifier, and which registered redirect
	URI applies.
* This approach does not explain why production succeeds while non-production
	fails, why `1008` appears, or whether Central and Conseiller share a flow.
* It does not rule out a separate undisclosed backend registration, later token
	relay, or refresh behavior.

### Falsifiers

Alternative A is falsified if the correlated trace shows any of these facts:

* No prior authorization request with the matching PKCE challenge.
* No `grant_type=authorization_code`, `code`, or matching `code_verifier`.
* An access token, rather than the one-time code, is the input grant.
* `assertion` plus `requested_token_use=on_behalf_of`, which would establish OBO.
* The Graph request uses the same access-token fingerprint received on an
	earlier, differently audienced leg rather than the token returned by the
	demonstrated code redemption.
* The `client_id`, redirect URI, or client authentication method does not match
	an authorized registration and application type.

### Fit with Croesus registrations

This is the best fit among the five alternatives. The exports support a public
SPA client requesting delegated Graph `User.Read` directly. They do not support
the OBO mock's required shape: a client obtains a token for an exposed middle
tier, and a confidential middle tier with a credential exchanges that token for
Graph. The remaining fit question is topology, not grant availability: which
component receives the authorization response and holds the PKCE verifier?

## Alternative B: maintain the categorical replay and OBO-required position

### Protocol assumptions

* The browser obtains or receives an access token that Croesus later presents
	from AWS without a new, valid authorization-server issuance.
* Croesus is acting as a middle-tier API that receives a token for itself and
	then calls Graph for the user.
* Because the backend calls Graph for a user, OBO is assumed to be the only
	acceptable architecture.
* `1008` and the second non-interactive sign-in are treated as proof of token
	replay.

### Security properties

If those assumptions are true, requiring OBO creates explicit audience
separation: token A is intended for the Croesus API, and token B is intended for
Graph. The confidential middle tier authenticates to Entra, and Graph never
receives token A. This reduces cross-resource bearer relay and gives each API a
clear validation boundary.

The security conclusion is sound only for an actual middle-tier delegation
problem. Declaring replay without token continuity evidence weakens the
credibility of the control recommendation.

### Evidence support

* Mathieu's and Microsoft's messages describe a second non-interactive sign-in
	from AWS and characterize it as reuse under the user's identity.
* The sign-in session is reported as `Unbound (statusCode 1008)`.
* The local replay endpoint demonstrates what same-bearer forwarding would look
	like as a negative control.

The evidence package contains no token fingerprint continuity, redacted grant
body, raw sign-in rows, or resource request proving that Croesus performed that
negative-control operation.

### Customer impact

* Preserves the strongest current Desjardins security position.
* Risks accusing Croesus of replay when its described operation is ordinary code
	redemption.
* Makes the joint meeting adversarial and may force a costly redesign before the
	actual failure is classified.
* Can delay non-production access while leaving the true policy or registration
	mismatch unresolved.

### Limitations

* `/token`, AWS egress, a non-interactive sign-in, and `1008` are each
	non-discriminating.
* Bearer access tokens are used under the user's delegated identity by design;
	that fact alone does not show improper reuse.
* OBO is not required merely because a server redeems an authorization code or
	uses a resulting Graph token.
* Token Protection currently has client and resource support constraints; an
	unbound status cannot be generalized into a protocol trace.

### Falsifiers

* A correlated `grant_type=authorization_code` request with a one-time code,
	matching verifier, and newly returned Graph token used at Graph.
* Different stable fingerprints for the code-redemption output and every prior
	access token crossing another application boundary.
* A topology with no protected Croesus middle-tier API and no inbound API token
	requiring downstream delegation.

### Fit with Croesus registrations

Poor. The three registrations expose no Croesus API scope, contain no app roles,
and have no secret or certificate. They request Graph `User.Read` directly.
They cannot be the confidential OBO middle tier represented by the repository
mock. A separate undisclosed backend registration could change this assessment,
which is why the full client inventory remains part of the evidence request.

## Alternative C: allowlist Croesus IPs or proxy `/token`

### Protocol assumptions

* The OAuth flow is valid and the failure is caused primarily by the network
	location from which token redemption occurs.
* Production and non-production differ because their AWS egress addresses or
	trusted-location treatment differ.
* A Desjardins proxy would cause Entra to evaluate a Desjardins-controlled source
	location and would not break redirect, PKCE, client, or session semantics.

### Security properties

IP allowlisting can restrict where a request originates. A controlled proxy can
centralize egress, logging, rate controls, and network governance. Neither
control establishes token audience, grant type, client legitimacy, possession
of the PKCE verifier, or absence of bearer relay. An attacker or compromised
workload operating through an allowed source still benefits from the exception.

Moving `/token` behind a proxy also introduces a new high-value path that may
observe authorization codes, verifiers, client assertions, or token responses.
It requires explicit secret handling, TLS, redaction, availability, and
ownership controls.

### Evidence support

* Croesus supplied distinct production and non-production AWS egress IPs.
* Olivier initially attributed the difference to non-production IPs not being
	authorized and asked whether a proxy or trusted IP is expected.
* Mathieu states that none of the Croesus IPs are configured in Desjardins
	Conditional Access, including the production addresses. Production success
	therefore weakens an IP-only explanation.

### Customer impact

* Allowlisting is operationally quick but expands trusted network surface and
	can hide the discriminating failure.
* A proxy creates implementation, support, privacy, latency, and availability
	obligations across organizational boundaries.
* Either change can make the symptom disappear without proving whether the
	original flow was secure.
* IP changes and multi-environment lifecycle management become recurring
	dependencies.

### Limitations

* Network location is one Conditional Access input, not an OAuth grant
	classifier or token-binding control.
* It does not repair wrong-audience acceptance, access-token relay, invalid
	public/confidential-client behavior, or an incorrect redirect topology.
* The exact failed policy and named-location result have not been supplied.

### Falsifiers

* The failed policy does not contain a location condition relevant to the
	transaction.
* The same failure occurs from a location that satisfies the policy.
* Production and non-production traces differ in device, tenant, app, resource,
	consent, or grant rather than source location.
* The Croesus trace reveals token relay, a client mismatch, or invalid audience,
	none of which an allowlist or proxy corrects.

### Fit with Croesus registrations

Neutral on registration shape and therefore insufficient as a protocol remedy.
The registrations neither require trusted Croesus IPs nor require a Desjardins
proxy. The option can be reconsidered only after a valid grant is demonstrated
and the actual Conditional Access export identifies network location as the
unsatisfied control.

## Alternative D: require immediate OBO redesign

### Protocol assumptions

* Croesus should be modeled as a protected middle-tier API.
* The frontend should acquire a token audienced to that API, not Graph.
* The middle tier should validate token A and use Microsoft Entra OBO to obtain
	token B for Graph.
* A confidential-client credential and downstream consent can be provisioned
	and operated by Croesus.

### Security properties

OBO provides strong audience and responsibility separation for a true
frontend-to-API-to-Graph architecture. The frontend cannot send its
Croesus-API token to Graph successfully; the middle tier authenticates itself;
Graph receives a Graph-audienced delegated token; and each boundary can enforce
least privilege. OBO remains subject to Conditional Access and downstream
claims challenges. It does not guarantee that the current block disappears.

### Evidence support

* The repository mock demonstrates the intended OBO architecture and contains a
	certificate-backed middle-tier design.
* OBO is an appropriate pattern when a protected API receives a user token for
	itself and must call a downstream API.
* No supplied evidence establishes that this is Croesus's actual topology or
	requirement.

### Customer impact

* Requires at least one exposed Croesus API scope, frontend permission changes,
	a confidential backend registration or credential, consent, token-cache
	design, claims-challenge handling, deployment, and regression testing.
* Changes the trust boundary and support model for Central and potentially
	Conseiller.
* Can improve architecture if a middle tier truly exists, but is disproportionate
	before the current flow is classified.

### Limitations

* OBO solves downstream delegation, not every server-side authorization-code
	flow.
* It may be unnecessary if the application is legitimately a direct delegated
	Graph client.
* It does not by itself solve an incompatible Conditional Access policy,
	unsupported token-binding scenario, or missing cross-tenant device context.
* An immediate mandate would turn the repository's mock into a presumed factual
	model of Croesus, which the evidence explicitly does not support.

### Falsifiers

* A demonstrated direct-client architecture with no Croesus protected API and
	no inbound API token requiring a downstream exchange.
* A valid authorization-code-with-PKCE trace where the newly issued Graph token
	is used only at Graph.
* A product requirement that Croesus cannot or does not act as a resource API
	between the browser and Graph.

### Fit with Croesus registrations

Very poor without new registrations or substantial modification. The supplied
objects are SPAs with direct Graph delegated permission, no exposed API scopes,
no preauthorization for a Croesus API, and no confidential credentials. The
repository's two-registration OBO design is a proposed replacement, not a
description of these objects.

## Alternative E: frame the issue as Conditional Access configuration only

### Protocol assumptions

* Croesus's grant and token handling are valid.
* The observed failure is fully explained by tenant, device-compliance,
	location, Token Protection, or another Conditional Access difference.
* No protocol, registration, audience, or token-continuity defect contributes
	to the failure.

### Security properties

This approach keeps identity enforcement at the tenant policy layer and can
produce a precise remedy when the flow is known-good. It preserves existing
application architecture. It is unsafe to call the policy a misconfiguration
before confirming that the application transaction is valid; a correct policy
may be exposing an unsupported or insecure token path.

### Evidence support

* The failing event includes a Conditional Access outcome and reported unbound
	session status.
* The correspondence identifies device-compliance, tenant, controlled-network,
	and location differences between working and failing scenarios.
* The exact policy export, grant controls, session controls, and AADSTS failure
	are absent. The evidence therefore proves that policy participated, not that
	configuration alone is the root cause.

### Customer impact

* Directs the investigation to Desjardins policy owners and could identify a
	narrow environment mismatch quickly.
* Risks shifting all accountability to Desjardins before Croesus demonstrates
	its request.
* A policy exception could normalize an invalid flow or reduce protection for
	other applications.
* Calling this configuration-only can prematurely close the architecture and
	token-handling investigation.

### Limitations

* Conditional Access outcome and protocol classification are separate
	questions.
* The same policy can produce different results for authorization code, refresh,
	OBO, and resource access because their app, resource, client, and session
	context differ.
* `1008` is not proof that Conditional Access is incorrectly configured.

### Falsifiers

* Evidence of same-token relay, wrong-audience acceptance, invalid client type,
	unauthorized redirect, or an undisclosed grant.
* A valid policy result that intentionally blocks the observed app/resource and
	context.
* A failure before Conditional Access becomes decisive, such as OAuth client or
	redirect validation.
* The same request succeeds when policy is report-only but still violates the
	documented resource and audience model.

### Fit with Croesus registrations

Moderate but unproved. Direct delegated Graph clients are subject to Conditional
Access, so policy can explain the symptom. The registrations do not identify
the failed policy, source component, device context, or token-binding support.
They cannot establish that Conditional Access is the only issue.

## Comparative decision

| Alternative | Evidence fit | Security posture before proof | Customer cost | Decision |
|-------------|--------------|-------------------------------|---------------|----------|
| A. Provisional auth code + PKCE, then correlated proof | Strongest | Holds policy and architecture constant while testing the grant | Low and bounded | Select |
| B. Categorical replay and OBO required | Weak | Strong if replay is later proved, but currently overclaims | High dispute and redesign risk | Reject now |
| C. IP allowlist or Desjardins proxy | Weak to conditional | Changes location without proving token semantics | Medium to high, with lasting operations | Defer |
| D. Immediate OBO redesign | Poor for supplied registrations | Strong for a true middle tier, not for every code flow | High | Reject now |
| E. Conditional Access configuration only | Partial | Risks weakening a correct control around an unclassified flow | Variable | Reject as sole framing |

Alternative A is the only option that preserves both security and epistemic
discipline. It treats Croesus's explanation as credible enough to test, treats
Desjardins's policy result as real enough not to bypass, and defines evidence
that can falsify either account.

## Proposed answer to Mathieu

> The `/token` call is not evidence of OBO by itself. Microsoft Entra uses
> `/oauth2/v2.0/token` to redeem authorization codes, refresh tokens, client
> credentials, and OBO assertions. With authorization code plus PKCE, the
> mandatory token request contains `grant_type=authorization_code`, the
> one-time `code`, and the matching `code_verifier`; OBO instead contains an
> access-token `assertion`, `requested_token_use=on_behalf_of`, and
> confidential-client authentication.
>
> The three app-registration exports we have are SPA clients with delegated
> Graph `User.Read`, no exposed Croesus API scope, and no secret or certificate.
> That fits Olivier's claimed grant family better than our OBO mock, but it does
> not yet prove that the AWS component is the legitimate redeemer or that no
> token is relayed later.
>
> Yes, an authorization-code redemption at `/token` can be subject to
> Conditional Access and can create non-interactive behavior similar to an OBO
> token request. We should not say the evaluations are identical: the grant,
> client type, app, resource, source IP, device/session context, and downstream
> claims handling can differ. `Unbound (1008)` describes session binding; it
> does not by itself prove access-token replay.
>
> I recommend that we provisionally accept authorization code plus PKCE as the
> leading explanation and request one correlated non-production trace before
> changing policy or architecture. We should not allowlist Croesus IPs, add a
> proxy, or mandate OBO until that trace shows the grant, the registered client
> and redirect, and which newly issued token is actually sent to Graph.

## Proposed evidence request and response to Olivier

> Thank you. Authorization code plus PKCE is a valid reason for a server-side
> call to the Microsoft Entra `/token` endpoint and is distinct from OBO. To
> reconcile that explanation with the non-interactive sign-in and Conditional
> Access result, could you provide one successful and one failing correlated
> non-production transaction with secrets and raw tokens removed?
>
> For `/authorize`, please retain the timestamp, correlation ID, `client_id`,
> `response_type`, redirect URI, scopes, `code_challenge_method`, and a stable
> hash of the challenge. For `/token`, please retain the endpoint, source
> component, `client_id`, grant type, redirect URI, scopes, client-authentication
> method, and only presence plus stable hashes for `code`, `code_verifier`,
> `refresh_token`, `assertion`, and client credentials. Please also identify the
> app-registration platform type used by the AWS component and the library and
> method that constructs the request.
>
> For the resulting access token and the token actually sent to Graph, please
> provide separate SHA-256 fingerprints and, where safely available, `iss`,
> `aud`, `azp`/`appid`, `scp`/`roles`, `iat`, `exp`, `uti`/`jti`, and whether
> `cnf` is present. Include the Graph URL, HTTP result, `request-id`, and
> `client-request-id`. Do not send authorization codes, verifiers, access or
> refresh tokens, secrets, assertions, cookies, or private keys.
>
> Desjardins will correlate the same transaction with the interactive and
> non-interactive sign-in records, including application, resource, source IP,
> device state, Conditional Access policy and controls, failure code,
> correlation ID, and token-protection status. If the request shows
> `grant_type=authorization_code` with the matching PKCE verifier and Graph uses
> the newly returned token, that supports your explanation. If it shows
> `assertion` plus `requested_token_use=on_behalf_of`, it is OBO. If Graph
> receives the same access-token fingerprint from an earlier leg without a new
> issuance, that supports relay or replay.
>
> We are not asking Croesus to move `/token` behind a Desjardins proxy or to
> redesign to OBO at this stage. We will evaluate the minimum policy or
> architecture change after this transaction is classified. Please provide a
> separate flow for Central and Conseiller if they do not use the same client,
> redirect, backend, and token handling.

## Exact repository corrections

These changes are recommendations only. No production file is modified by this
research task.

### README corrections

Update `README.md` as follows:

1. Replace the categorical statement that raw sign-in logs confirm replay with
	 the evidence-qualified status: a second non-interactive AWS-originated event
	 and unbound session status were observed, but grant and token continuity are
	 unproved.
2. Remove the inference that no credential plus no exposed API scope means the
	 second sign-in can only be replay. Add authorization-code redemption for a
	 directly requested Graph token as a live candidate.
3. Stop describing OBO as the required correction until a middle-tier API and
	 inbound API-audienced token are demonstrated.
4. State that `/token` is shared by authorization code, refresh, client
	 credentials, OBO, and other supported grants; classify by request fields.
5. Replace any claim that `1008` proves access-token replay with the narrower
	 statement that it reports an unbound sign-in session.
6. Replace claims that OBO removes the Conditional Access block with language
	 that OBO can change app/resource/token context but remains subject to
	 downstream Conditional Access and claims challenges.
7. Remove claims that current telemetry proves a fresh leg-2 `jti`/`iat`, a
	 decoded leg-2 audience, or a certificate thumbprint. The current evidence
	 object uses a constant Graph audience label and does not decode token B.

### Evidence narrative and demo guide corrections

Update `docs/evidence-narrative.md` and `docs/obo-demo-guide.md` as follows:

1. Label the repository as an OBO reference architecture and negative-control
	 lab, not a reconstruction of Croesus production.
2. Label Tier 1 as wrong-audience rejection. It does not test same-token replay.
3. Describe Tier 2 as bearer forwarding. Treat Graph success, `401`, and `403`
	 as observations requiring resource telemetry; do not equate `401` with token
	 binding or success with absent audience binding.
4. Delete the documented leg-2 `jti`, `iat`, decoded `aud`, and certificate
	 thumbprint evidence until the implementation emits validated values.
5. Reconcile browser/custom-API Token Protection limitations with any mock
	 `1008` narrative. Keep native-client and supported-resource constraints
	 explicit.
6. Separate protocol classification from Conditional Access outcome throughout
	 the evidence checklist.
7. Correct `docs/configuration-contract.md` so it does not imply that
	 `AzureAd:Scopes` is the active API enforcement path when the source uses
	 `[RequiredScope("access_as_user")]`.
8. Correct deployment wording: the checked-in workflow deploys code and assumes
	 infrastructure is already provisioned; it does not provision Bicep in its
	 normal path.

### Focused test corrections

Update `api/Tests/NegativeControlTests.cs` as follows:

1. Rename
	 `TokenA_PresentedDirectlyToGraph_WouldBeRejected_BecauseAudienceIsNotGraph`
	 to `ApiAudience_DiffersFromGraphAudience` unless the test is changed to call
	 Graph. The current assertion compares two strings and does not observe a
	 Graph rejection.
2. Make test names and comments say that HS256 synthetic tokens with issuer
	 validation disabled prove local middleware configuration only.
3. Add a test that an API-audienced synthetic token is accepted by the API test
	 host and a Graph-audienced token is rejected by that host. Keep this labeled
	 as audience validation, not customer replay evidence.

Update `api/Tests/ReplayEndpointTests.cs` as follows:

1. Parameterize downstream `200`, `401`, `403`, and transport failure responses
	 and assert status-neutral reporting. No test should label one result as
	 binding proof.
2. Assert that the forwarded bearer value sent to the stub resource equals the
	 request's supplied test value, which proves the negative-control relay shape
	 locally.
3. Keep and extend redaction assertions to structured logging state, exception
	 paths, and response bodies. Never persist raw bearer values.

Add a focused success-path test for `api/Controllers/MeController.cs` that:

1. Supplies a valid API-audienced synthetic principal.
2. Mocks `ITokenAcquisition` and Graph independently.
3. Verifies token acquisition is requested for Graph `User.Read` and that Graph
	 is invoked only after acquisition succeeds.
4. Verifies telemetry does not claim a decoded leg-2 token identifier, audience,
	 or certificate thumbprint unless those values are actually obtained from a
	 trusted result.
5. Labels the result as mock OBO control-flow coverage, not evidence of Croesus
	 behavior or live Entra issuance.

### Evidence automation corrections

Update `.github/workflows/deploy-croesus.yml` and
`scripts/evidence-kql.kusto` before relying on generated exhibits:

1. Rebuild the SPA with `VITE_ENABLE_REPLAY_DEMO=true` in the gated replay lab;
	 enabling only the API route leaves the UI control absent.
2. Align the evidence query's application display-name filter with the
	 provisioned default `Croesus GPD Central API (mock)`, or filter by immutable
	 application ID.
3. Persist timestamped query output and job metadata as artifacts so a claim can
	 be traced to a specific run.
4. Keep `CorrelationId` joins labeled best-effort and never use them as proof of
	 identical access-token bytes.
5. Add a result rubric that separates grant evidence, token identity, resource
	 response, Conditional Access result, and token-protection status.

## Authoritative references

* [OAuth 2.0 authorization code flow on the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow), including code redemption and PKCE
* [Microsoft identity platform and OAuth 2.0 On-Behalf-Of flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow), including `assertion` and `requested_token_use=on_behalf_of`
* [OpenID Connect on the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-protocols-oidc), matching the supplied endpoint-overview image
* [OAuth 2.0 and OpenID Connect protocols on the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-protocols)
* [Microsoft identity platform access tokens](https://learn.microsoft.com/entra/identity-platform/access-tokens)
* [Microsoft identity platform claims validation](https://learn.microsoft.com/entra/identity-platform/claims-validation)
* [Token Protection overview](https://learn.microsoft.com/entra/identity/conditional-access/concept-token-protection#overview)
* [Token Protection supported resources](https://learn.microsoft.com/entra/identity/conditional-access/concept-token-protection#supported-resources)
* [tokenProtectionStatusDetails resource type](https://learn.microsoft.com/graph/api/resources/tokenprotectionstatusdetails?view=graph-rest-beta)
* [RFC 6749: The OAuth 2.0 Authorization Framework](https://www.rfc-editor.org/rfc/rfc6749.html)
* [RFC 6750: OAuth 2.0 Bearer Token Usage](https://www.rfc-editor.org/rfc/rfc6750.html)
* [RFC 7636: Proof Key for Code Exchange by OAuth Public Clients](https://www.rfc-editor.org/rfc/rfc7636.html)
* [RFC 8693: OAuth 2.0 Token Exchange](https://www.rfc-editor.org/rfc/rfc8693.html)

## Evidence gaps

* The redacted Croesus `/authorize` and `/token` requests are absent.
* The exact `client_id`, redirect URI, client type, and component holding the
	PKCE verifier for the AWS redemption are unknown.
* The three supplied application objects may not be a complete inventory of
	application and service-principal objects involved in Central or Conseiller.
* No stable token fingerprints tie authorization, token issuance, and Graph
	resource use together.
* The raw Desjardins sign-in records, failed Conditional Access policy, controls,
	result, resource, application, failure code, and correlation fields are
	absent.
* Production success versus non-production failure remains unexplained by a
	controlled same-field comparison.
* The evidence does not establish whether Central and Conseiller use the same
	topology.

## Clarifying questions

These questions require Croesus or Desjardins evidence and cannot be answered
from the repository:

* Which component owns the PKCE verifier and performs code redemption?
* Which app-registration object and redirect URI authorize that component?
* Is there an undisclosed confidential backend registration?
* Which exact token is reported as unbound, for which app and resource?
* Which Conditional Access policy and control blocked non-production?
* Does the Graph bearer fingerprint match the token returned by the demonstrated
	code redemption or a token received on an earlier leg?
* Do Central and Conseiller use identical identity flows?