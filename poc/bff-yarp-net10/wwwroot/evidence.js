const statusElement = document.getElementById('status');
const evidenceElement = document.getElementById('evidence');
const logoutForm = document.getElementById('logout');

function describe(label, value) {
  const term = document.createElement('dt');
  term.textContent = label;
  const detail = document.createElement('dd');
  detail.textContent = value;
  evidenceElement.append(term, detail);
}

// A 401 carrying an interaction-required result becomes a deliberate top-level navigation. It is never
// resolved by fetching an identity-provider page into this document.
function handleInteractionRequired(payload) {
  statusElement.textContent = 'Sign-in is required. Continuing to the identity provider.';
  window.location.assign(payload.loginPath);
}

async function loadEvidence() {
  evidenceElement.replaceChildren();
  statusElement.textContent = 'Loading.';

  const response = await fetch('/bff/evidence', { credentials: 'same-origin' });
  if (response.status === 401) {
    handleInteractionRequired(await response.json());
    return;
  }

  if (!response.ok) {
    statusElement.textContent = `Evidence unavailable (status ${response.status}).`;
    return;
  }

  const evidence = await response.json();
  describe('Application run', evidence.applicationRunId);
  describe('Token custody', evidence.tokenCustody);
  describe('Custody detail', evidence.tokenCustodyDetail);
  describe('Session cookie', evidence.sessionCookie.name);
  describe('__Host- prefix', String(evidence.sessionCookie.hasHostPrefix));
  describe('HttpOnly', String(evidence.sessionCookie.httpOnly));
  describe('Secure', String(evidence.sessionCookie.secure));
  describe('SameSite', evidence.sessionCookie.sameSite);
  describe('Owned API audience validation', evidence.ownedApiAudienceValidation);
  describe('Authentication method', evidence.authenticationMethod ?? 'not reported');
  describe('Authentication time', evidence.authenticationTime ?? 'not reported');
  describe('Granted delegated scopes', evidence.grantedDelegatedScopes.join(' ') || 'none recorded');
  describe('Acquisition operations', String(evidence.acquisitions.length));

  statusElement.textContent = 'Loaded.';
  logoutForm.hidden = false;
}

async function signOut(event) {
  event.preventDefault();
  const tokenResponse = await fetch('/bff/antiforgery', { credentials: 'same-origin' });
  if (!tokenResponse.ok) {
    statusElement.textContent = 'Sign-out could not start.';
    return;
  }

  const token = await tokenResponse.json();
  const response = await fetch('/bff/logout', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { [token.headerName]: token.requestToken }
  });

  statusElement.textContent = response.ok ? 'Signed out.' : 'Sign-out failed.';
  evidenceElement.replaceChildren();
  logoutForm.hidden = true;
}

document.getElementById('load').addEventListener('click', loadEvidence);
logoutForm.addEventListener('submit', signOut);
