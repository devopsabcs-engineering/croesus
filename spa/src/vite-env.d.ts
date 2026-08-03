/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_SPA_CLIENT_ID: string;
  readonly VITE_TENANT_ID: string;
  readonly VITE_API_SCOPE: string;
  readonly VITE_API_BASE_URL: string;
  /**
   * Delegated Microsoft Graph scope requested by the SPA for the GATED Tier 2a
   * server-side replay demo only. Defaults to "User.Read" when unset. Never used
   * on the API path (see authConfig apiRequest/loginRequest).
   */
  readonly VITE_GRAPH_SCOPE?: string;
  /**
   * Gate for the Tier 2a server-side replay demo. The Tier 2 section renders only
   * when this equals the string "true"; otherwise nothing Tier 2 is shown.
   */
  readonly VITE_ENABLE_REPLAY_DEMO?: string;
  /**
   * Gate for the demo-only raw-token inspector, which copies a live bearer token
   * to the clipboard for jwt.ms. Renders only when this equals the string "true".
   * Keep it off for customer-facing sessions: the escalation packet asks the
   * vendor for hashes and presence indicators, never raw tokens.
   */
  readonly VITE_ENABLE_TOKEN_INSPECTOR?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
