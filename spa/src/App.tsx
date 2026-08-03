import { useState } from "react";
import {
  AuthenticatedTemplate,
  UnauthenticatedTemplate,
  useMsal,
} from "@azure/msal-react";
import { loginRequest } from "./authConfig";
import {
  callApiMe,
  callApiReplay,
  callGraphWithApiTokenWrongWay,
  type MeResponse,
  type ReplayAttemptResult,
  type ServerReplayResult,
} from "./api";
import { EvidencePanel } from "./components/EvidencePanel";
import { RegistrationShapePanel } from "./components/RegistrationShapePanel";
import { ReplayAttemptPanel, ServerReplayPanel } from "./components/ReplayAttemptPanel";
import { TokenInspectorPanel } from "./components/TokenInspectorPanel";

// GATED Tier 2a demo. The server-side replay section renders only when this build
// flag is exactly the string "true"; with the gate off, nothing Tier 2 is shown.
const replayDemoEnabled = import.meta.env.VITE_ENABLE_REPLAY_DEMO === "true";

function ContrastPanel() {
  return (
    <section style={{ marginTop: 32 }}>
      <h3>Wrong vs right: replay vs On-Behalf-Of</h3>
      <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
        <div
          style={{
            flex: "1 1 320px",
            minWidth: 0,
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
            minWidth: 0,
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

const replayButtonStyle: React.CSSProperties = {
  ...buttonStyle,
  background: "#b00020",
};

export default function App() {
  const { instance, accounts } = useMsal();
  const [data, setData] = useState<MeResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [replay, setReplay] = useState<ReplayAttemptResult | null>(null);
  const [replayError, setReplayError] = useState<string | null>(null);
  const [replayLoading, setReplayLoading] = useState(false);
  const [serverReplay, setServerReplay] = useState<ServerReplayResult | null>(null);
  const [serverReplayError, setServerReplayError] = useState<string | null>(null);
  const [serverReplayLoading, setServerReplayLoading] = useState(false);

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

  async function handleReplayToGraph() {
    const account = accounts[0];
    if (!account) return;
    setReplayLoading(true);
    setReplayError(null);
    try {
      const result = await callGraphWithApiTokenWrongWay(instance, account);
      setReplay(result);
    } catch (e) {
      setReplayError(e instanceof Error ? e.message : String(e));
      setReplay(null);
    } finally {
      setReplayLoading(false);
    }
  }

  async function handleServerReplay() {
    const account = accounts[0];
    if (!account) return;
    setServerReplayLoading(true);
    setServerReplayError(null);
    try {
      const result = await callApiReplay(instance, account);
      setServerReplay(result);
    } catch (e) {
      setServerReplayError(e instanceof Error ? e.message : String(e));
      setServerReplay(null);
    } finally {
      setServerReplayLoading(false);
    }
  }

  return (
    <main
      style={{
        boxSizing: "border-box",
        maxWidth: 880,
        width: "100%",
        margin: "40px auto",
        padding: "0 16px",
        overflowX: "hidden",
        fontFamily: "Segoe UI, system-ui, sans-serif",
      }}
    >
      <h1>Croesus — On-Behalf-Of flow demo</h1>
      <p style={{ color: "#555" }}>
        The SPA requests only the API scope and never a Microsoft Graph scope. The API performs the
        OBO exchange and returns decoded-claim evidence proving the tokens are bound, not replayed.
      </p>

      <div
        style={{ display: "flex", gap: 12, alignItems: "center", flexWrap: "wrap", margin: "16px 0" }}
      >
        <UnauthenticatedTemplate>
          <SignInButton />
        </UnauthenticatedTemplate>
        <AuthenticatedTemplate>
          <button onClick={handleCallApi} style={buttonStyle} disabled={loading}>
            {loading ? "Calling…" : "Call API"}
          </button>
          <button onClick={handleReplayToGraph} style={replayButtonStyle} disabled={replayLoading}>
            {replayLoading ? "Replaying…" : "Replay API token to Graph (wrong)"}
          </button>
          {replayDemoEnabled && (
            <button
              onClick={handleServerReplay}
              style={replayButtonStyle}
              disabled={serverReplayLoading}
            >
              {serverReplayLoading ? "Replaying…" : "Server-side replay (Tier 2a, wrong)"}
            </button>
          )}
          <SignOutButton />
        </AuthenticatedTemplate>
      </div>

      {error && (
        <p style={{ color: "#b00020", fontFamily: "monospace", whiteSpace: "pre-wrap" }}>{error}</p>
      )}

      {replayError && (
        <p style={{ color: "#b00020", fontFamily: "monospace", whiteSpace: "pre-wrap" }}>
          {replayError}
        </p>
      )}

      {serverReplayError && (
        <p style={{ color: "#b00020", fontFamily: "monospace", whiteSpace: "pre-wrap" }}>
          {serverReplayError}
        </p>
      )}

      <AuthenticatedTemplate>
        {data && <EvidencePanel data={data} />}
      </AuthenticatedTemplate>

      <AuthenticatedTemplate>
        <TokenInspectorPanel />
      </AuthenticatedTemplate>

      <ContrastPanel />

      <RegistrationShapePanel />

      <AuthenticatedTemplate>
        {replay && <ReplayAttemptPanel result={replay} />}
      </AuthenticatedTemplate>

      {replayDemoEnabled && (
        <AuthenticatedTemplate>
          {serverReplay && <ServerReplayPanel result={serverReplay} />}
        </AuthenticatedTemplate>
      )}
    </main>
  );
}
