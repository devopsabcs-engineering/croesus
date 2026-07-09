import { useState } from "react";
import { useMsal } from "@azure/msal-react";
import { getApiToken } from "../getApiToken";
import { getGraphToken } from "../getGraphToken";

// The Graph-token copy mirrors the existing Tier 2a gate: the SPA only ever
// acquires a Graph token when the replay demo is explicitly enabled.
const replayDemoEnabled = import.meta.env.VITE_ENABLE_REPLAY_DEMO === "true";

const inspectorButtonStyle: React.CSSProperties = {
  padding: "8px 16px",
  fontSize: 14,
  borderRadius: 6,
  border: "none",
  background: "#6b4d00",
  color: "#fff",
  cursor: "pointer",
};

const linkStyle: React.CSSProperties = {
  fontSize: 13,
  color: "#6b4d00",
  fontWeight: 600,
};

type TokenKind = "api" | "graph";

/**
 * DEMO-ONLY token inspector. This panel deliberately surfaces the RAW bearer
 * tokens the SPA can acquire so a presenter can copy them and paste them into
 * jwt.ms or jwt.io for live decoding during a walk-through.
 *
 * This is NOT a production-safe pattern: a raw access token is a bearer
 * credential. It is exposed here only to make the demo's audience-binding claims
 * inspectable by a human. The panel is clearly labeled as demo-only, and the
 * Graph-token copy stays behind the same Tier 2a gate as the rest of the SPA.
 */
export function TokenInspectorPanel() {
  const { instance, accounts } = useMsal();
  const [token, setToken] = useState<string | null>(null);
  const [tokenKind, setTokenKind] = useState<TokenKind | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function acquireAndCopy(kind: TokenKind) {
    const account = accounts[0];
    if (!account) return;
    setBusy(true);
    setError(null);
    setStatus(null);
    try {
      const raw =
        kind === "api"
          ? await getApiToken(instance, account) // token A — aud = API
          : await getGraphToken(instance, account); // aud = Microsoft Graph (Tier 2a gate)
      setToken(raw);
      setTokenKind(kind);
      try {
        await navigator.clipboard.writeText(raw);
        setStatus(
          `Copied ${kind === "api" ? "API token (token A)" : "Graph token"} to the clipboard. Paste it into jwt.ms or jwt.io below.`
        );
      } catch {
        // Clipboard can be blocked (permissions / insecure context). The token is
        // still shown in the read-only box below for manual selection and copy.
        setStatus(
          "Clipboard access was blocked — select the token text below and copy it manually."
        );
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setToken(null);
      setTokenKind(null);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section style={{ marginTop: 32 }}>
      <h3 style={{ marginBottom: 4 }}>Demo-only: copy a raw token for jwt.ms / jwt.io</h3>
      <div
        style={{
          border: "1px solid #e0c56a",
          borderRadius: 8,
          padding: 16,
          background: "#fffbe9",
        }}
      >
        <p style={{ marginTop: 0, color: "#6b4d00", fontSize: 14, lineHeight: 1.6 }}>
          <strong>For demonstration only.</strong> The buttons below expose the{" "}
          <strong>raw bearer token</strong> the SPA acquired so you can paste it into{" "}
          <a href="https://jwt.ms" target="_blank" rel="noopener noreferrer" style={linkStyle}>
            jwt.ms
          </a>{" "}
          or{" "}
          <a href="https://jwt.io" target="_blank" rel="noopener noreferrer" style={linkStyle}>
            jwt.io
          </a>{" "}
          and inspect its claims (<code>aud</code>, <code>scp</code>, <code>jti</code>,{" "}
          <code>iat</code>). A raw access token is a credential — this is deliberately{" "}
          <strong>not</strong> a production-safe pattern and exists only to make the
          audience-binding evidence inspectable by a human.
        </p>

        <div style={{ display: "flex", gap: 12, flexWrap: "wrap", alignItems: "center" }}>
          <button
            onClick={() => acquireAndCopy("api")}
            style={inspectorButtonStyle}
            disabled={busy}
          >
            {busy ? "Working…" : "Copy API token (token A)"}
          </button>
          {replayDemoEnabled && (
            <button
              onClick={() => acquireAndCopy("graph")}
              style={inspectorButtonStyle}
              disabled={busy}
            >
              {busy ? "Working…" : "Copy Graph token (Tier 2a)"}
            </button>
          )}
          <a href="https://jwt.ms" target="_blank" rel="noopener noreferrer" style={linkStyle}>
            Open jwt.ms ↗
          </a>
          <a href="https://jwt.io" target="_blank" rel="noopener noreferrer" style={linkStyle}>
            Open jwt.io ↗
          </a>
        </div>

        {status && (
          <p style={{ marginBottom: 0, color: "#0a7d28", fontSize: 13 }}>{status}</p>
        )}
        {error && (
          <p style={{ marginBottom: 0, color: "#b00020", fontFamily: "monospace", fontSize: 13 }}>
            {error}
          </p>
        )}

        {token && (
          <textarea
            readOnly
            value={token}
            onFocus={(e) => e.currentTarget.select()}
            aria-label={
              tokenKind === "graph" ? "Raw Graph token (demo only)" : "Raw API token (demo only)"
            }
            style={{
              marginTop: 12,
              width: "100%",
              minHeight: 96,
              boxSizing: "border-box",
              fontFamily: "monospace",
              fontSize: 12,
              padding: 8,
              border: "1px solid #e0c56a",
              borderRadius: 6,
              resize: "vertical",
              overflowWrap: "anywhere",
              wordBreak: "break-all",
            }}
          />
        )}
      </div>
    </section>
  );
}

export default TokenInspectorPanel;
