import { getApiToken } from "./getApiToken";
import type { AccountInfo, IPublicClientApplication } from "@azure/msal-browser";

/**
 * Decoded-claim summary for one leg of the OBO flow, as returned by the API.
 * The API decodes each token server-side and surfaces only these non-sensitive
 * header/payload claims as evidence.
 */
export interface ClaimSummary {
  /** Audience: the resource the token was minted for. */
  aud: string;
  /** Delegated scope present on the token (leg 1 only carries access_as_user). */
  scp?: string;
  /** JWT ID: unique per token. Distinct jti across legs proves a real exchange. */
  jti: string;
  /** Issued-at (epoch seconds). */
  iat: number;
}

/**
 * Evidence the API returns from GET /api/me. It captures both legs of the
 * On-Behalf-Of exchange so the SPA can prove the tokens are bound, not replayed.
 */
export interface MeResponse {
  displayName?: string;
  userPrincipalName?: string;
  evidence: {
    /** Token A: SPA -> API. aud == API_CLIENT_ID, scp == access_as_user. */
    leg1: ClaimSummary;
    /** Token B: API -> Microsoft Graph (via OBO). Distinct aud and jti from token A. */
    leg2: ClaimSummary;
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
