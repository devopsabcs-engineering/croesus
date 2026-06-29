import {
  AccountInfo,
  IPublicClientApplication,
  InteractionRequiredAuthError,
} from "@azure/msal-browser";
import { apiRequest } from "./authConfig";

/**
 * Acquire "token A": an access token whose audience is the middle-tier API
 * (aud = API_CLIENT_ID, scp = access_as_user). This token is NEVER a Graph
 * token — the SPA only ever requests the API scope.
 *
 * Tries a silent acquisition first, then falls back to an interactive popup
 * when the user must re-consent or re-authenticate.
 */
export async function getApiToken(
  instance: IPublicClientApplication,
  account: AccountInfo
): Promise<string> {
  try {
    const res = await instance.acquireTokenSilent({ ...apiRequest, account });
    return res.accessToken; // token A — aud = API_CLIENT_ID
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      const res = await instance.acquireTokenPopup(apiRequest);
      return res.accessToken;
    }
    throw e;
  }
}
