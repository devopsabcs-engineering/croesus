import { useState } from "react";
import {
  AuthenticatedTemplate,
  UnauthenticatedTemplate,
  useMsal,
} from "@azure/msal-react";
import { loginRequest } from "./authConfig";
import { callApiMe, type MeResponse } from "./api";
import { EvidencePanel } from "./components/EvidencePanel";

function ContrastPanel() {
  return (
    <section style={{ marginTop: 32 }}>
      <h3>Wrong vs right: replay vs On-Behalf-Of</h3>
      <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
        <div
          style={{
            flex: "1 1 320px",
            border: "1px solid #f0c0c0",
            borderRadius: 8,
            padding: 16,
            background: "#fff5f5",
          }}
        >
          <h4 style={{ marginTop: 0, color: "#b00020" }}>✗ Broken — token replay</h4>
          <p style={{ fontSize: 14, lineHeight: 1.6 }}>
            The SPA acquires a token and <strong>replays the same token to Microsoft Graph</strong>.
            That token&apos;s audience is the API, so Graph rejects it (wrong <code>aud</code>). If the
            SPA instead requested a Graph scope directly, the browser would hold a Graph token — a
            bearer credential that can be exfiltrated and replayed from anywhere.
          </p>
        </div>
        <div
          style={{
            flex: "1 1 320px",
            border: "1px solid #c0e0c0",
            borderRadius: 8,
            padding: 16,
            background: "#f3fbf3",
          }}
        >
          <h4 style={{ marginTop: 0, color: "#0a7d28" }}>✓ Correct — On-Behalf-Of</h4>
          <p style={{ fontSize: 14, lineHeight: 1.6 }}>
            The SPA holds <strong>only</strong> the API-scoped token (token A). The API exchanges it
            for a <strong>distinct</strong> Graph token (token B) by authenticating its
            confidential-client credential. The <strong>SPA never holds a Graph token</strong>; the
            two tokens have different audiences and different <code>jti</code> values — proof the
            tokens are bound to their resources, not relayed.
          </p>
        </div>
      </div>
    </section>
  );
}

function SignInButton() {
  const { instance } = useMsal();
  return (
    <button
      onClick={() => instance.loginPopup(loginRequest).catch(console.error)}
      style={buttonStyle}
    >
      Sign in
    </button>
  );
}

function SignOutButton() {
  const { instance } = useMsal();
  return (
    <button onClick={() => instance.logoutPopup().catch(console.error)} style={secondaryButtonStyle}>
      Sign out
    </button>
  );
}

const buttonStyle: React.CSSProperties = {
  padding: "8px 16px",
  fontSize: 14,
  borderRadius: 6,
  border: "none",
  background: "#0067c0",
  color: "#fff",
  cursor: "pointer",
};

const secondaryButtonStyle: React.CSSProperties = {
  ...buttonStyle,
  background: "#5a5a5a",
};

export default function App() {
  const { instance, accounts } = useMsal();
  const [data, setData] = useState<MeResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleCallApi() {
    const account = accounts[0];
    if (!account) return;
    setLoading(true);
    setError(null);
    try {
      const result = await callApiMe(instance, account);
      setData(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setData(null);
    } finally {
      setLoading(false);
    }
  }

  return (
    <main style={{ maxWidth: 880, margin: "40px auto", padding: "0 16px", fontFamily: "Segoe UI, system-ui, sans-serif" }}>
      <h1>Croesus — On-Behalf-Of flow demo</h1>
      <p style={{ color: "#555" }}>
        The SPA requests only the API scope and never a Microsoft Graph scope. The API performs the
        OBO exchange and returns decoded-claim evidence proving the tokens are bound, not replayed.
      </p>

      <div style={{ display: "flex", gap: 12, alignItems: "center", margin: "16px 0" }}>
        <UnauthenticatedTemplate>
          <SignInButton />
        </UnauthenticatedTemplate>
        <AuthenticatedTemplate>
          <button onClick={handleCallApi} style={buttonStyle} disabled={loading}>
            {loading ? "Calling…" : "Call API"}
          </button>
          <SignOutButton />
        </AuthenticatedTemplate>
      </div>

      {error && (
        <p style={{ color: "#b00020", fontFamily: "monospace", whiteSpace: "pre-wrap" }}>{error}</p>
      )}

      <AuthenticatedTemplate>
        {data && <EvidencePanel data={data} />}
      </AuthenticatedTemplate>

      <ContrastPanel />
    </main>
  );
}
