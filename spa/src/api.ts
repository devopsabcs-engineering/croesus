import { getApiToken } from "./getApiToken";
import { getGraphToken } from "./getGraphToken";
import type { AccountInfo, IPublicClientApplication } from "@azure/msal-browser";

/**
 * Decoded claims for leg 1 (the inbound SPA->API token A), as returned by the
 * API. This token is audienced to an API we own, so the API cracks it
 * server-side and surfaces only these non-sensitive claims as evidence.
 */
export interface Leg1Claims {
  /** Audience: the resource the token was minted for (this API). */
  aud: string;
  /** Delegated scope present on the token (access_as_user). */
  scp?: string;
  /** Application (client) id of the calling SPA. */
  appid?: string;
  /** Object id of the signed-in user. */
  oid?: string;
  /** JWT ID: unique per token. */
  jti: string;
  /** Issued-at, epoch seconds (serialized as a string by the API). */
  iat: string;
}

/**
 * Evidence for leg 2 (the API->Graph token B produced by the OBO exchange).
 * The Graph JWT is intentionally NOT decoded, so distinct issuance is shown via
 * a different audience plus the OBO result's own correlation id and expiry.
 */
export interface Leg2Claims {
  /** Audience: the downstream resource (Microsoft Graph). */
  aud: string;
  /** Scopes granted on token B. */
  scp?: string;
  /** MSAL correlation id for the OBO request that minted token B. */
  correlationId?: string;
  /** Absolute expiry of token B (ISO-8601). */
  expiresOn?: string;
}

/**
 * Evidence the API returns from GET /api/me. It captures both legs of the
 * On-Behalf-Of exchange so the SPA can prove the tokens are bound, not replayed.
 */
export interface MeResponse {
  user?: {
    displayName?: string;
    userPrincipalName?: string;
    id?: string;
  };
  evidence: {
    /** Token A: SPA -> API. aud == API_CLIENT_ID, scp == access_as_user. */
    leg1: Leg1Claims;
    /** Token B: API -> Microsoft Graph (via OBO). Distinct aud from token A. */
    leg2: Leg2Claims;
  };
}

/**
 * Fetch wrapper that attaches "token A" (the API-audience access token) as a
 * Bearer credential to GET {VITE_API_BASE_URL}/api/me. The SPA never sends a
 * Graph token — only this single API-scoped token.
 */
export async function callApiMe(
  instance: IPublicClientApplication,
  account: AccountInfo
): Promise<MeResponse> {
  const token = await getApiToken(instance, account); // token A
  const baseUrl = import.meta.env.VITE_API_BASE_URL.replace(/\/+$/, "");

  const res = await fetch(`${baseUrl}/api/me`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(`GET /api/me failed: ${res.status} ${res.statusText} ${body}`.trim());
  }

  return (await res.json()) as MeResponse;
}

/**
 * Non-sensitive claims decoded locally from a JWT payload, for evidence display
 * only. This intentionally surfaces a small, bounded set of claims and never the
 * raw token. `hasCnf` indicates a proof-of-possession / token-binding (`cnf`)
 * claim is present without exposing its contents.
 */
export interface DecodedTokenClaims {
  aud?: string;
  scp?: string;
  jti?: string;
  /** Issued-at, epoch seconds (as present in the JWT). */
  iat?: number;
  /** True when a `cnf` (confirmation / proof-of-possession) claim is present. */
  hasCnf: boolean;
}

/**
 * Base64url-decode and JSON-parse the payload segment of a JWT, returning only a
 * bounded set of non-sensitive claims for display. Never throws in a way that
 * breaks the UI: any malformed input yields a safe partial object with
 * `hasCnf: false`. The raw token and full payload are never returned or logged.
 */
export function decodeJwtClaims(token: string): DecodedTokenClaims {
  const safe: DecodedTokenClaims = { hasCnf: false };
  try {
    const parts = token.split(".");
    if (parts.length < 2) return safe;

    const segment = parts[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = segment.padEnd(segment.length + ((4 - (segment.length % 4)) % 4), "=");

    const decoded = decodeURIComponent(
      atob(padded)
        .split("")
        .map((c) => "%" + ("00" + c.charCodeAt(0).toString(16)).slice(-2))
        .join("")
    );

    const payload = JSON.parse(decoded) as Record<string, unknown>;
    return {
      aud: typeof payload.aud === "string" ? payload.aud : undefined,
      scp: typeof payload.scp === "string" ? payload.scp : undefined,
      jti: typeof payload.jti === "string" ? payload.jti : undefined,
      iat: typeof payload.iat === "number" ? payload.iat : undefined,
      hasCnf: Object.prototype.hasOwnProperty.call(payload, "cnf"),
    };
  } catch {
    return safe;
  }
}

/**
 * Result of the bad-path negative control: the SPA deliberately replays the
 * API-audienced token A directly to Microsoft Graph. Contains only safe evidence
 * metadata — never the raw token.
 */
export interface ReplayAttemptResult {
  attemptedTarget: string;
  expectedAudience: string;
  tokenAudience?: string;
  tokenScope?: string;
  tokenJti?: string;
  tokenIssuedAt?: string;
  hasCnf: boolean;
  status: number;
  ok: boolean;
  bodyPreview: string;
  interpretation: string;
}

/** Truncate a response body for safe preview display. */
function truncateBody(body: string, max = 500): string {
  if (body.length <= max) return body;
  return body.slice(0, max) + "…";
}

/**
 * Negative control — the WRONG way on purpose. Acquires token A (audienced to
 * the API) and replays it directly to Microsoft Graph's /me endpoint. Graph
 * rejects it because the token's audience is the API, not Graph; a 401 is the
 * EXPECTED, successful outcome of this control.
 *
 * The raw token never leaves this function: it is not returned, not logged, and
 * not placed in React state. Only decoded non-sensitive claims and the Graph
 * response status/body preview are surfaced.
 *
 * This does NOT emit the literal "Token Protection 1008" signal — it is the
 * audience-bound negative control that demonstrates audience binding via Graph's
 * 401, distinct from a Conditional Access Token Protection denial.
 */
export async function callGraphWithApiTokenWrongWay(
  instance: IPublicClientApplication,
  account: AccountInfo
): Promise<ReplayAttemptResult> {
  const token = await getApiToken(instance, account); // token A — aud = API
  const claims = decodeJwtClaims(token); // display only; raw token stays local

  const attemptedTarget = "https://graph.microsoft.com/v1.0/me";
  const expectedAudience = "https://graph.microsoft.com";

  let res: Response;
  try {
    res = await fetch(attemptedTarget, {
      headers: { Authorization: `Bearer ${token}` },
    });
  } catch (e) {
    // Unexpected runtime/network failure — not the expected Graph 401.
    throw new Error(
      `Replay attempt failed before Graph responded: ${e instanceof Error ? e.message : String(e)}`
    );
  }

  const body = await res.text().catch(() => "");
  const bodyPreview = truncateBody(body);

  const interpretation =
    res.status === 401
      ? "Graph rejected the replayed token with 401 because the token's audience is the API, not Graph. This is the expected, successful negative-control result demonstrating audience binding — it is NOT the literal Token Protection 1008 signal."
      : `Unexpected Graph response (${res.status}). The expected negative-control outcome is a 401 from audience binding.`;

  return {
    attemptedTarget,
    expectedAudience,
    tokenAudience: claims.aud,
    tokenScope: claims.scp,
    tokenJti: claims.jti,
    tokenIssuedAt: claims.iat !== undefined ? new Date(claims.iat * 1000).toISOString() : undefined,
    hasCnf: claims.hasCnf,
    status: res.status,
    ok: res.status === 401, // 401 is the expected success for this control
    bodyPreview,
    interpretation,
  };
}

/**
 * Claims-only evidence returned by the API's gated Tier 2a replay endpoint
 * (POST /api/replay). Mirrors the ReplayController response contract: the server
 * forwards the SPA-supplied Graph token to a FIXED Graph target and reports only
 * decoded, non-sensitive claims plus the Graph response status. The raw token is
 * never echoed by the server and never appears here.
 */
export interface ServerReplayEvidence {
  /** Fixed Graph target the server presented the forwarded token to. */
  attemptedTarget: string;
  /** Audience of the forwarded Graph token (decoded server-side). */
  tokenAudience?: string;
  /** Delegated scope on the forwarded Graph token. */
  tokenScope?: string;
  /** JWT ID of the forwarded Graph token. */
  tokenJti?: string;
  /** Issued-at of the forwarded Graph token (ISO-8601, serialized by the API). */
  tokenIssuedAt?: string;
  /** True when the forwarded token carries a `cnf` (proof-of-possession) claim. */
  hasCnf: boolean;
  /** HTTP status Graph returned to the server's replayed request. */
  status: number;
  /** Server's own outcome flag for the replay attempt. */
  ok: boolean;
  /** Server-authored interpretation of the outcome. */
  interpretation: string;
}

/**
 * Result of the GATED Tier 2a server-side replay negative control. Combines the
 * server's ReplayAttempt evidence with a LOCAL decode of the same forwarded Graph
 * token (for display parity with leg 2). The raw Graph token is never returned,
 * logged, or placed in React state — only bounded, non-sensitive claims.
 *
 * This reproduces the replay SHAPE (a token acquired in one place, presented from
 * another), NOT the literal Conditional Access "Token Protection 1008" signal.
 */
export interface ServerReplayResult {
  /** Evidence returned by the API's replay endpoint. */
  server: ServerReplayEvidence;
  /** Non-sensitive claims decoded locally from the forwarded Graph token. */
  forwardedGraphToken: DecodedTokenClaims;
}

/**
 * GATED Tier 2a demo — the WRONG way on purpose, server-side. Acquires token A
 * (audienced to the API, used ONLY as the `Authorization` bearer so the endpoint's
 * `[Authorize]` passes) AND a Microsoft Graph token (forwarded in the request
 * body). POSTs `{ graphToken }` to {VITE_API_BASE_URL}/api/replay; the API replays
 * the forwarded token to a fixed Graph target server-side and returns claims-only
 * evidence.
 *
 * Security invariants:
 *   - The API token is the `Authorization` credential; the Graph token is only in
 *     the body (never a header, never the API's audience).
 *   - The forwarded Graph token is decoded LOCALLY for display; the raw token is
 *     never returned or stored — only bounded, non-sensitive claims.
 *
 * This reproduces the replay SHAPE, not the literal Token Protection 1008 signal.
 */
export async function callApiReplay(
  instance: IPublicClientApplication,
  account: AccountInfo
): Promise<ServerReplayResult> {
  const apiToken = await getApiToken(instance, account); // token A — Authorization only
  const graphToken = await getGraphToken(instance, account); // forwarded in body only
  const forwardedGraphToken = decodeJwtClaims(graphToken); // display only; raw token stays local
  const baseUrl = import.meta.env.VITE_API_BASE_URL.replace(/\/+$/, "");

  const res = await fetch(`${baseUrl}/api/replay`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${apiToken}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ graphToken }),
  });

  if (!res.ok) {
    const body = await res.text().catch(() => "");
    throw new Error(
      `POST /api/replay failed: ${res.status} ${res.statusText} ${body}`.trim()
    );
  }

  const server = (await res.json()) as ServerReplayEvidence;
  return { server, forwardedGraphToken };
}
