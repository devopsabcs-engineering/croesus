import {
  AccountInfo,
  IPublicClientApplication,
  InteractionRequiredAuthError,
} from "@azure/msal-browser";
import { graphRequest } from "./authConfig";

/**
 * GATED Tier 2a demo only. Acquire a Microsoft Graph delegated access token
 * (aud = Microsoft Graph, scp = the configured VITE_GRAPH_SCOPE). This is the ONE
 * intentional exception to the "SPA holds only the API token" invariant: the SPA
 * acquires this Graph token solely to FORWARD it to the API's server-side replay
 * endpoint, reproducing the token-replay SHAPE.
 *
 * Tries a silent acquisition first, then falls back to an interactive popup when
 * the user must re-consent or re-authenticate. Mirrors getApiToken's error
 * handling exactly.
 */
export async function getGraphToken(
  instance: IPublicClientApplication,
  account: AccountInfo
): Promise<string> {
  try {
    const res = await instance.acquireTokenSilent({ ...graphRequest, account });
    return res.accessToken; // token B' — aud = Microsoft Graph
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      const res = await instance.acquireTokenPopup(graphRequest);
      return res.accessToken;
    }
    throw e;
  }
}
