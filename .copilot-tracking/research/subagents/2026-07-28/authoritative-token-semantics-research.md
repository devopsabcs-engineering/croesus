<!-- markdownlint-disable-file -->
# Authoritative Token Semantics Research

## Research scope

* What makes Microsoft identity platform on-behalf-of (OBO) specifically OBO, including the Entra `/oauth2/v2.0/token` parameters and token chain?
* Is a vendor API endpoint named `/token` that accepts an Entra access token and returns another token necessarily OBO?
* How can OBO be distinguished from bearer-token relay or replay, a proprietary session/token broker, OAuth token exchange, client credentials, or exchange at a separate authorization server?
* What token claims and HTTP evidence are minimally sufficient for classification?
* What are the security implications of accepting a token minted for one audience at another endpoint?
* Which exact tests and evidence requests are suitable for an enterprise customer and vendor discussion?

## Local Croesus evidence

The available correspondence establishes a disagreement, not a protocol
classification:

* Croesus states that Central uses OAuth with PKCE, does not use OBO, and must
	make a server-side call to the Microsoft Entra `/token` endpoint from Croesus
	public IP addresses.
* Desjardins states that the Croesus server reuses a token under the user's
	identity and associates the failure with Conditional Access and an unbound
	sign-in session.
* The customer asks why `/token` is mandatory without OBO and whether token
	refresh would have the same Conditional Access behavior.

These statements are compatible with several different implementations. A
confidential web application normally redeems an authorization code at the
authorization server's token endpoint. PKCE adds proof that the redeemer
possesses the verifier associated with the authorization request. Neither the
server-side location nor the `/token` path makes that operation OBO.

The local evidence does not include the redacted HTTP form body, client
authentication method, input artifact type, requested resource, output token
audience, or downstream use. It therefore cannot establish authorization-code
redemption, OBO, refresh, token exchange, relay, or a proprietary broker.

The repository's replay endpoint is explicitly a negative control. It accepts a
Graph token and re-presents that same bearer token to Graph. It demonstrates
what replay looks like, but it is not evidence of Croesus's implementation.
Relevant local sources are:

* `assets/latest-info/email-thread-with-croesus.md`
* `assets/latest-info/mathieu-santerre.md`
* `api/Controllers/ReplayController.cs`
* `api/Tests/ReplayEndpointTests.cs`
* `docs/evidence-narrative.md`

## Authoritative protocol findings

### Token endpoint semantics

RFC 6749 defines the token endpoint as the authorization-server endpoint used
to obtain an access token by presenting an authorization grant or refresh
token. The endpoint is shared by multiple grant types. Microsoft likewise
describes `/oauth2/v2.0/token` as accepting an authorization code, refresh
token, or client credential. A request URL alone cannot identify the grant.

The standards language is direct: RFC 6749 says the token endpoint is used "to
obtain an access token by presenting its authorization grant or refresh token."
Microsoft's protocol overview describes the endpoint as redeeming "an
authorization code, refresh token, or client credential for tokens." These
short excerpts establish that `/token` is a shared endpoint role, not an OBO
label.

The HTTP request body and client authentication provide the primary
classification evidence.

### Authorization code with PKCE

An authorization-code flow has two linked requests:

1. The authorization request sends `response_type=code`, `client_id`,
	 `redirect_uri`, and normally `scope`. PKCE adds `code_challenge` and
	 `code_challenge_method`.
2. The token request sends `grant_type=authorization_code`, the one-time
	 `code`, the same `redirect_uri` when required, and the PKCE
	 `code_verifier`. A confidential client also authenticates according to its
	 registered method.

The authorization code, not an access token, is the input artifact. A
server-based web application can perform this redemption from its backend. The
result does not become OBO because the token request is server-side.

### Microsoft Entra OBO

OBO applies when a protected middle-tier API receives a user-delegated access
token intended for that API and needs a different access token for a downstream
API. Microsoft specifies this token request shape:

```http
grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer
client_id=<middle-tier-client-id>
assertion=<access-token-audienced-to-middle-tier>
requested_token_use=on_behalf_of
scope=<downstream-resource-scopes>
```

The middle tier is a confidential client and must authenticate, for example
with a secret, certificate-backed `client_assertion`, workload identity, or an
SDK-supported equivalent. The decisive Microsoft-specific fields are
`assertion` and `requested_token_use=on_behalf_of`. An ordinary authorization
code or refresh token is not an OBO assertion.

The token chain is:

1. The client obtains token A for the middle-tier API.
2. The client presents token A to that API as a bearer token.
3. The middle tier validates token A for itself.
4. The middle tier submits token A as the OBO `assertion` and authenticates
	 itself to Entra.
5. Entra issues token B for the downstream API, subject to consent and policy.
6. The middle tier uses token B, not token A, at the downstream API.

### Refresh token grant

Refresh uses `grant_type=refresh_token`, a `refresh_token`, and, when requested,
an allowed `scope`. Confidential clients authenticate as required. A refresh
token is a distinct credential issued for renewal; it is not an inbound API
access token and refresh is not OBO.

Conditional Access can affect initial issuance, refresh, or downstream resource
access differently. A refresh can fail with an interaction-required condition
when policy, session, sign-in frequency, risk, or claims requirements demand a
new interactive authorization. That does not make refresh behavior equivalent
to OBO.

### Client credentials grant

Client credentials uses `grant_type=client_credentials` and confidential-client
authentication. On the Microsoft identity platform, `scope` normally names one
resource followed by `/.default`. The resulting token represents the
application, not a user-delegated chain. Typical authorization is conveyed in
`roles`, while delegated tokens normally use `scp`.

### RFC 8693 token exchange

The standards-based token exchange extension has a different wire signature:

```http
grant_type=urn:ietf:params:oauth:grant-type:token-exchange
subject_token=<input-security-token>
subject_token_type=<URI-identifying-input-token-type>
```

Optional parameters include `actor_token`, `actor_token_type`, `resource`,
`audience`, `scope`, and `requested_token_type`. A successful response identifies
the returned token using `issued_token_type`. RFC 8693 supports impersonation
and delegation models and can cross issuer or trust-domain boundaries when the
authorization server's policy permits it.

Microsoft Entra OBO has similar business intent in some architectures, but its
Microsoft request contract is not the RFC 8693 request contract. The two labels
must not be treated as interchangeable without observing their parameters.

### Bearer relay, replay, and proprietary brokerage

RFC 6750 defines a bearer token by possession: any party holding it can use it
unless another mechanism constrains it. Relay or replay means presenting the
same access-token value to a resource, commonly in `Authorization: Bearer`.
There is no new authorization-server issuance in that step.

RFC 6750 defines a bearer token as one where "any party in possession of the
token" can use it. This is why token value continuity across process and
resource boundaries is material evidence for relay or replay.

A vendor endpoint can also accept a valid Entra token for the vendor API, use it
as proof of application sign-in, and return a vendor session cookie or
proprietary token. That can be a legitimate application session bootstrap. It
is not necessarily an OAuth authorization server, OBO, or token exchange. The
endpoint name `/token` has no classification authority.

## Classification framework

| Candidate operation | Required or distinguishing input | Expected output and use | What would disprove it |
|---------------------|-----------------------------------|-------------------------|------------------------|
| Authorization code + PKCE | `grant_type=authorization_code`, one-time `code`, `code_verifier`, matching authorization request with `code_challenge` | Tokens from the same authorization server for the registered client and requested resource | Input is an access token, refresh token, client credentials only, or no prior authorization code |
| Microsoft Entra OBO | JWT bearer grant, inbound API access token in `assertion`, `requested_token_use=on_behalf_of`, downstream `scope`, confidential-client authentication | Fresh Entra access token for a downstream API; middle tier uses it instead of the inbound token | No `assertion`, no `requested_token_use`, or assertion is not audienced to the middle tier |
| Refresh token grant | `grant_type=refresh_token`, `refresh_token`, optional `scope`, client authentication when required | Renewed access token and possibly a replacement refresh token | Input is an authorization code or API access token |
| Client credentials | `grant_type=client_credentials`, application authentication, Microsoft `resource/.default` scope | App-only token, normally with `roles` and no user delegation | User access token, authorization code, or refresh token is the grant input |
| RFC 8693 token exchange | Token-exchange grant URI, `subject_token`, `subject_token_type`; optional actor and target parameters | Security token plus `issued_token_type` for an authorized target | Microsoft OBO fields alone, ordinary bearer use, or proprietary request fields |
| Bearer relay or replay | Same access-token bytes presented again to a resource, often in `Authorization: Bearer` | No newly issued OAuth token; resource accepts or rejects the presented token | A verified authorization-server transaction issues a distinct token before the resource call |
| Proprietary session or token broker | Vendor-defined endpoint and parameters; Entra token may be proof of sign-in | Vendor cookie or token governed by a documented vendor trust and validation contract | Request conforms to a standard grant and response is issued by the corresponding authorization server |
| Exchange at a separate authorization server | Entra token presented to another issuer under an explicit federation or exchange contract | Token from the second issuer, with its own audience and trust domain | No second issuer or no documented trust/exchange mechanism |

Classification must use the full transaction. A new token value, changed token
identifier, changed audience, server-side egress IP, or successful `/token`
response is supporting evidence only. None identifies the grant by itself.

The current Croesus claim is **unclassified**. Its description is compatible
with ordinary server-side authorization-code redemption, but the available
evidence does not prove that implementation. Nothing presently proves OBO, RFC
8693 exchange, client credentials, token relay, or proprietary brokerage.

## Minimum evidence

The minimum safe evidence package is a redacted, correlated trace of the
authorization and token transactions plus the eventual resource call. Retain
parameter names and non-secret identifiers while replacing credential values
with stable fingerprints.

### Authorization request

Capture:

* Full authorization-server host and path, tenant segment, and protocol version
* `client_id`, `response_type`, `redirect_uri`, `scope`, and `response_mode`
* PKCE `code_challenge_method` and a fingerprint of `code_challenge`
* Correlation identifiers and timestamp
* Whether the user agent or backend initiated each redirect

### Token request

Capture:

* Destination host and exact path
* HTTP method, media type, timestamp, source component, and correlation ID
* Exact parameter names and non-secret values, especially `grant_type`,
	`client_id`, `scope`, `resource`, `requested_token_use`,
	`subject_token_type`, and `requested_token_type`
* Presence and stable hash only for `code`, `code_verifier`, `refresh_token`,
	`assertion`, `subject_token`, `actor_token`, `client_secret`, and
	`client_assertion`
* Client authentication method, such as secret, certificate assertion, managed
	identity, workload federation, or public-client designation
* HTTP status, OAuth error fields, response token types, and response issuer

Do not collect raw authorization codes, access tokens, refresh tokens, secrets,
assertions, session cookies, or private keys in email, tickets, screenshots, or
general-purpose logs.

### Token and resource evidence

For JWTs that the organization is entitled to inspect, capture only the header
and bounded claims needed for classification:

* `typ`, `alg`, and signing key identifier
* `iss`, `aud`, `tid`, `sub`, `oid`, `azp` or `appid`, and optional `idtyp`
* `scp` and `roles`
* `iat`, `nbf`, `exp`, `uti` or `jti`, and presence of `cnf`

Also capture the final resource URL or resource identifier, which token
fingerprint was presented, and the response status. The resource must validate
the token; decoding a JWT in a client or middle tier is not validation.

Some Microsoft access tokens are opaque or encrypted to the client. Claims
must not be assumed available, stable, or intended for client-side inspection.
In that case, use authorization-server sign-in logs, resource-side validation
telemetry, app registration configuration, SDK logs, and safe token
fingerprints. Wire parameters remain sufficient to distinguish the grant.

## Security implications

An access token is issued for a resource identified by its audience. A different
endpoint must not accept that token as authorization merely because its
signature is valid or its issuer is trusted. It must validate that it is the
intended audience, along with signature, trusted issuer and tenant, lifetime,
client or actor, and required scopes or roles.

Accepting a foreign-audience token creates a confused-deputy boundary failure.
It can let a token legitimately acquired for a lower-value resource authorize
operations at a higher-value resource. It also broadens the effect of token
disclosure and makes relay possible across services that were meant to have
separate authorization boundaries.

Bearer-token threats described by RFC 6750 include disclosure, redirect to an
unintended resource, and replay. Controls include TLS, audience restriction,
short token lifetimes, secure storage, least-privilege scopes or roles, and
proof-of-possession or token binding where the platform and resource support
it. IP allowlisting can constrain network access, but it does not repair an
invalid audience model or transform replay into a standard grant.

There is one important distinction for a vendor sign-in endpoint. It may
legitimately accept an Entra token if that token's audience is the vendor API
and the endpoint validates it as the intended resource. Minting a vendor-local
session afterward is an application authentication decision. Accepting a token
audienced to Microsoft Graph, another Desjardins API, or another tenant/resource
as if it were issued to the vendor API would require a separate, explicit trust
or exchange design and cannot be justified by signature validation alone.

## Enterprise tests and evidence requests

### Vendor evidence request

Request one successful and one failing trace from the same non-production
scenario. Ask Croesus to provide:

1. A sequence diagram naming the browser, Croesus frontend, Croesus backend,
	 Microsoft Entra authorization server, and every protected resource.
2. The redacted `/authorize` request and redirect response.
3. The redacted token request using the evidence rules above.
4. The app registration type, redirect URI, client ID, client authentication
	 method, exposed scopes, requested API permissions, and tenant model.
5. Bounded claims or trusted platform telemetry for every input and output
	 token, including issuer, audience, calling client, delegated scopes or app
	 roles, times, and a stable fingerprint.
6. The exact destination where each output token is presented.
7. Correlated Entra interactive and non-interactive sign-in records, including
	 application, resource, source IP, Conditional Access result, failure code,
	 and correlation ID.
8. The library and method that constructs the token request, with token cache
	 behavior and refresh behavior identified separately.

### Controlled classification tests

Run these tests in a non-production tenant with synthetic accounts and no raw
tokens in shared logs:

1. Capture the initial sign-in. Verify that an observed authorization code is
	 redeemed once with `grant_type=authorization_code` and, when PKCE is claimed,
	 a `code_verifier` tied to the authorization request's challenge.
2. Repeat after the access token expires. Determine whether the application
	 uses `grant_type=refresh_token`, a new authorization code, silent browser
	 authorization, or another mechanism. Record any interaction-required or
	 Conditional Access result separately.
3. Search the token request for `assertion` and
	 `requested_token_use=on_behalf_of`. If present, verify that the assertion's
	 audience is the middle tier and that the output token targets the named
	 downstream API.
4. Search for the RFC 8693 token-exchange grant, `subject_token`, and
	 `subject_token_type`. If absent, do not label the operation RFC 8693 token
	 exchange.
5. Fingerprint token values at process boundaries. Verify whether the same
	 access-token bytes are presented twice or whether a trusted authorization
	 server issues a distinct token between calls.
6. Send a token audienced to the wrong test resource. Each resource must reject
	 it. Do not weaken production validation to conduct this test.
7. If a vendor-local token is returned, identify its issuer, audience, format,
	 lifetime, revocation/session semantics, key ownership, and the resource that
	 accepts it. Confirm whether the endpoint claims OAuth authorization-server
	 status or is an application session endpoint.
8. Compare successful production-tenant and failing non-production-tenant
	 traces using the same fields. Isolate differences in tenant, authority,
	 consent, app registration, device state, network location, and Conditional
	 Access policy rather than assuming source IP is the cause.

### Decision rule for the customer discussion

Use the observed request body to settle terminology first:

* `code` plus `code_verifier` means authorization-code redemption with PKCE.
* `refresh_token` means refresh.
* `assertion` plus `requested_token_use=on_behalf_of` means Microsoft Entra OBO.
* `subject_token` plus the RFC 8693 grant URI means standards-based token
	exchange.
* `client_credentials` means app-only acquisition.
* The same access-token fingerprint at both resource calls means relay or
	replay, subject to verification that no intermediary reissued it.
* None of these signatures means the vendor must document its proprietary
	contract before the operation can be classified.

## Microsoft-specific behavior and standards caveats

* Microsoft access tokens are owned by the resource API. Clients should treat
	them as opaque and should not build logic that depends on their format or
	undocumented claims.
* Microsoft commonly uses `uti` as a token identifier. RFC 9068 specifies `jti`
	for tokens conforming to that profile. A report should therefore request
	`uti` or `jti` when observable, not assume every Entra token has `jti`.
* RFC 9068 defines an interoperable JWT access-token profile, including
	`typ=at+jwt` and validation requirements. OAuth 2.0 itself does not require
	JWT access tokens, and an arbitrary Entra token must not be presumed to
	conform to RFC 9068.
* `aud` can be represented differently across Microsoft token versions and
	resource configurations. Compare it to the resource's configured accepted
	audience rather than relying on visual string heuristics alone.
* `scp` indicates delegated permissions. `roles` can indicate application
	permissions and can also appear for user assignments. Use `idtyp`, actor
	claims, and the grant evidence when distinguishing app-only from delegated
	use.
* A backend token call can legitimately originate from Croesus infrastructure
	in a confidential web-app design. Moving redemption to a Desjardins proxy is
	an architectural change, not a protocol requirement, and should be evaluated
	only after the actual flow and trust boundaries are known.
* Conditional Access evaluation and sign-in log shape depend on client type,
	resource, grant, session state, and policy. An interactive authorization,
	authorization-code redemption, refresh, OBO exchange, and downstream resource
	call must not be assumed to produce identical evaluations.
* A redirect URI is fundamental to authorization-code flow registration. OBO
	itself does not require a redirect URI because its input is an API access
	token, not an authorization response delivered through a browser redirect.
* For confidential clients, certificates, managed identities, or workload
	federation are preferable to long-lived shared secrets where supported.

## References

### Standards

* [RFC 6749: The OAuth 2.0 Authorization Framework](https://www.rfc-editor.org/rfc/rfc6749.html), especially sections 3.2, 4.1, 4.4, 6, and 8.3
* [RFC 6750: OAuth 2.0 Bearer Token Usage](https://www.rfc-editor.org/rfc/rfc6750.html), especially sections 2 and 5
* [RFC 7636: Proof Key for Code Exchange by OAuth Public Clients](https://www.rfc-editor.org/rfc/rfc7636.html), especially sections 4.2 and 4.6
* [RFC 8693: OAuth 2.0 Token Exchange](https://www.rfc-editor.org/rfc/rfc8693.html), especially sections 2.1 and 2.2
* [RFC 9068: JWT Profile for OAuth 2.0 Access Tokens](https://www.rfc-editor.org/rfc/rfc9068.html), especially sections 2 and 4
* [OpenID Connect Core 1.0](https://openid.net/specs/openid-connect-core-1_0.html), especially sections 3.1 and 3.1.3

### Microsoft identity platform

* [Microsoft identity platform and OAuth 2.0 On-Behalf-Of flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-on-behalf-of-flow)
* [OAuth 2.0 authorization code flow on the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow)
* [OAuth 2.0 and OpenID Connect protocols on the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-protocols)
* [OpenID Connect on the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-protocols-oidc)
* [Microsoft identity platform access tokens](https://learn.microsoft.com/entra/identity-platform/access-tokens)
* [Access token claims reference](https://learn.microsoft.com/entra/identity-platform/access-token-claims-reference)
* [Claims validation](https://learn.microsoft.com/entra/identity-platform/claims-validation)
* [Application types for the Microsoft identity platform](https://learn.microsoft.com/entra/identity-platform/v2-app-types)
* [Scenario: A web app that calls web APIs](https://learn.microsoft.com/entra/identity-platform/scenario-web-app-call-api-overview)
* [Scenario: A web API that calls web APIs](https://learn.microsoft.com/entra/identity-platform/scenario-web-api-call-api-overview)
* [Security best practices for Microsoft Entra app registration](https://learn.microsoft.com/entra/identity-platform/security-best-practices-for-app-registration)

## Unresolved questions

The following questions require vendor or tenant evidence and cannot be answered
from the correspondence or endpoint name:

* What is the exact redacted body of the Croesus server's token request?
* Is its input an authorization code, refresh token, access token, client
	credential, or vendor-defined artifact?
* Which component owns the OAuth client, and is it registered as public or
	confidential?
* Which redirect URI and PKCE challenge/verifier pair are used?
* What client authentication method does the backend use?
* What are the issuer and accepted audience of the input token?
* What are the issuer and audience of the returned token, and which resource
	receives it?
* Does the same access-token fingerprint cross the browser, Croesus backend,
	and Entra or resource boundary?
* Which exact sign-in record reports status 1008, for which application,
	resource, client type, grant, and correlation ID?
* Is the failing operation initial code redemption, token renewal, downstream
	API acquisition, downstream resource access, or vendor session creation?
* Are Central and Conseiller configured with the same app type, authority,
	redirect URI, permissions, tenant, and token handling?

Until those questions are answered, no Conditional Access exception, trusted-IP
designation, proxy requirement, audience-validation change, or protocol redesign
should be justified by the label `/token` alone.
