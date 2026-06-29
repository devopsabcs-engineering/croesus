import { getApiToken } from "./getApiToken";
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
