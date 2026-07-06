import {
  Configuration,
  PublicClientApplication,
  RedirectRequest,
} from "@azure/msal-browser";

// All values are public, non-secret build-time configuration injected by Vite.
// See docs/configuration-contract.md for the source of truth of each value.
const tenantId = import.meta.env.VITE_TENANT_ID;
const spaClientId = import.meta.env.VITE_SPA_CLIENT_ID;
const apiScope = import.meta.env.VITE_API_SCOPE;
// GATED Tier 2a demo only: the Graph delegated scope the SPA acquires so it can
// forward a Graph token to the API's server-side replay endpoint. Defaults to
// "User.Read". This is the ONE intentional exception to the "SPA requests only
// the API scope" invariant and is used exclusively by the gated replay flow.
const graphScope = import.meta.env.VITE_GRAPH_SCOPE ?? "User.Read";

export const msalConfig: Configuration = {
  auth: {
    clientId: spaClientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
  },
  cache: { cacheLocation: "sessionStorage", storeAuthStateInCookie: false },
};

// CRITICAL: the SPA requests ONLY the middle-tier API scope.
// No Microsoft Graph scope appears here. Graph access happens exclusively
// inside the API via the On-Behalf-Of exchange.
export const apiRequest: RedirectRequest = {
  scopes: [apiScope],
};

// Login request used for interactive sign-in. Same single API scope —
// never openid/profile-plus-Graph and never a Graph resource scope.
export const loginRequest: RedirectRequest = {
  scopes: [apiScope],
};

// GATED Tier 2a demo only. Requests ONLY the Graph delegated scope so the SPA can
// forward a Graph token to the API's server-side replay endpoint. This is NOT used
// on the API path — apiRequest/loginRequest above remain API-scope-only, and no
// Graph scope is ever added to them.
export const graphRequest: RedirectRequest = {
  scopes: [graphScope],
};

export const pca = new PublicClientApplication(msalConfig);
