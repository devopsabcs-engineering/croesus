import type { ReplayAttemptResult, ServerReplayResult } from "../api";

function ClaimRow({ label, value, ok }: { label: string; value?: string; ok?: boolean }) {
  const color = ok === undefined ? "#333" : ok ? "#0a7d28" : "#b00020";
  return (
    <tr>
      <td style={{ padding: "4px 12px 4px 0", fontWeight: 600, whiteSpace: "nowrap", width: 136 }}>
        {label}
      </td>
      <td
        style={{
          padding: "4px 0",
          fontFamily: "monospace",
          color,
          overflowWrap: "anywhere",
          wordBreak: "break-word",
        }}
      >
        {value ?? "(n/a)"}
      </td>
    </tr>
  );
}

/**
 * Renders the bad-path negative control: the SPA replayed the API-audienced
 * token A directly to Microsoft Graph. A 401 here is the EXPECTED, successful
 * outcome — Graph rejects the token because its audience is the API, not Graph.
 *
 * This panel deliberately frames 401 as a green-check success and states that
 * this is the audience-bound negative control, NOT the literal Token Protection
 * 1008 signal.
 */
export function ReplayAttemptPanel({ result }: { result: ReplayAttemptResult }) {
  const graphRejected = result.status === 401;
  const audienceIsNotGraph =
    result.tokenAudience !== undefined &&
    !result.tokenAudience.toLowerCase().includes("graph.microsoft.com");

  return (
    <section style={{ marginTop: 32 }}>
      <h3 style={{ marginBottom: 4 }}>Bad path evidence: replay API token to Graph</h3>
      <p style={{ marginTop: 0, color: "#555" }}>
        The SPA deliberately replays token A (audienced to the API) directly to Microsoft Graph. A{" "}
        <strong>401 is the expected, successful negative-control outcome</strong>.
      </p>

      <div
        style={{
          border: graphRejected ? "1px solid #c0e0c0" : "1px solid #f0c0c0",
          borderRadius: 8,
          padding: 16,
          minWidth: 0,
          background: graphRejected ? "#f3fbf3" : "#fff5f5",
        }}
      >
        <h4 style={{ margin: "0 0 12px", color: graphRejected ? "#0a7d28" : "#b00020" }}>
          {graphRejected ? "✓" : "✗"} Graph status {result.status}
          {graphRejected ? " (expected negative control)" : " (unexpected)"}
        </h4>
        <table style={{ borderCollapse: "collapse", tableLayout: "fixed", width: "100%", fontSize: 14 }}>
          <tbody>
            <ClaimRow label="attempted target" value={result.attemptedTarget} />
            <ClaimRow label="token audience" value={result.tokenAudience} ok={audienceIsNotGraph} />
            <ClaimRow label="expected audience" value={result.expectedAudience} />
            <ClaimRow label="Graph status" value={String(result.status)} ok={graphRejected} />
            <ClaimRow label="hasCnf" value={String(result.hasCnf)} />
            <ClaimRow label="jti" value={result.tokenJti} />
            <ClaimRow label="iat" value={result.tokenIssuedAt} />
          </tbody>
        </table>
      </div>

      <ul style={{ marginTop: 16, fontSize: 14, lineHeight: 1.6 }}>
        <li style={{ color: audienceIsNotGraph ? "#0a7d28" : "#b00020" }}>
          {audienceIsNotGraph ? "✓" : "✗"} Token <code>aud</code> is the API, not Graph
        </li>
        <li style={{ color: graphRejected ? "#0a7d28" : "#b00020" }}>
          {graphRejected ? "✓" : "✗"} Graph rejected the replay with 401 (expected)
        </li>
        <li style={{ color: "#0a7d28" }}>
          ✓ This is the audience-bound negative control, <strong>not</strong> the literal Token
          Protection 1008 signal
        </li>
      </ul>

      <p style={{ marginTop: 12, color: "#555", fontSize: 13, lineHeight: 1.6 }}>
        {result.interpretation}
      </p>
    </section>
  );
}

/** One wrong→right row of the OBO gap checklist. */
function GapRow({ label, wrong, right }: { label: string; wrong: string; right: string }) {
  return (
    <li style={{ marginBottom: 10, lineHeight: 1.6 }}>
      <strong>{label}</strong>
      <div style={{ color: "#b00020" }}>
        <span aria-hidden>✗ </span>
        {wrong}
      </div>
      <div style={{ color: "#0a7d28" }}>
        <span aria-hidden>✓ </span>
        {right}
      </div>
    </li>
  );
}

/**
 * GATED Tier 2a evidence: the SPA acquired a real Microsoft Graph token and
 * FORWARDED it to the API, which replayed it to a fixed Graph target server-side.
 * This reproduces the token-replay SHAPE (a token acquired in one place, presented
 * from another) — it is NOT the literal Conditional Access "Token Protection 1008"
 * signal.
 *
 * Renders the wrong path (server-side replay) beside the right path (On-Behalf-Of),
 * then the five-item wrong→right OBO gap checklist. Only bounded, non-sensitive
 * claims are shown; the raw Graph token is never displayed.
 */
export function ServerReplayPanel({ result }: { result: ServerReplayResult }) {
  const { server, forwardedGraphToken } = result;
  const forwardedIat =
    forwardedGraphToken.iat !== undefined
      ? new Date(forwardedGraphToken.iat * 1000).toISOString()
      : undefined;

  return (
    <section style={{ marginTop: 32 }}>
      <h3 style={{ marginBottom: 4 }}>Tier 2a — server-side token replay</h3>
      <p style={{ marginTop: 0, color: "#555" }}>
        The SPA acquired a real Microsoft Graph token and <strong>forwarded it to the API</strong>,
        which replayed it to a fixed Graph target server-side. This{" "}
        <strong>reproduces the replay SHAPE, not the literal 1008 signal</strong> — a Conditional
        Access Token Protection denial is a distinct, environment-specific control.
      </p>

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
          <h4 style={{ margin: "0 0 12px", color: "#b00020" }}>
            ✗ Broken — forwarded (replayed) Graph token
          </h4>
          <table
            style={{ borderCollapse: "collapse", tableLayout: "fixed", width: "100%", fontSize: 14 }}
          >
            <tbody>
              <ClaimRow label="attempted target" value={server.attemptedTarget} />
              <ClaimRow label="token audience" value={server.tokenAudience ?? forwardedGraphToken.aud} />
              <ClaimRow label="token scope" value={server.tokenScope ?? forwardedGraphToken.scp} />
              <ClaimRow label="Graph status" value={String(server.status)} />
              <ClaimRow label="hasCnf" value={String(server.hasCnf ?? forwardedGraphToken.hasCnf)} />
              <ClaimRow label="jti (forwarded)" value={server.tokenJti ?? forwardedGraphToken.jti} />
              <ClaimRow label="iat (forwarded)" value={server.tokenIssuedAt ?? forwardedIat} />
            </tbody>
          </table>
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
          <h4 style={{ margin: "0 0 12px", color: "#0a7d28" }}>✓ Correct — On-Behalf-Of</h4>
          <p style={{ fontSize: 14, lineHeight: 1.6, margin: 0 }}>
            The SPA holds <strong>only</strong> the API-scoped token. The API never receives a
            forwarded Graph token; it authenticates its own confidential-client credential to mint a{" "}
            <strong>distinct</strong> Graph token (token B) with a fresh <code>jti</code>/<code>iat</code>{" "}
            and a Graph audience. See the audience-binding evidence above for the live OBO legs.
          </p>
        </div>
      </div>

      <h4 style={{ marginBottom: 8 }}>Wrong → right: closing the OBO gap</h4>
      <ol style={{ marginTop: 0, fontSize: 14, paddingLeft: 20 }}>
        <GapRow
          label="Credential"
          wrong="The presenter forwards/replays a token it did not mint."
          right="The API authenticates its own confidential-client credential (certificate) to mint token B."
        />
        <GapRow
          label="Exposed scope"
          wrong="The SPA holds a Graph-scoped token — an exfiltratable bearer credential in the browser."
          right="The SPA holds only the API scope; the Graph scope is never exposed to the browser."
        />
        <GapRow
          label="Distinct leg-2 audience"
          wrong="The same token is reused across a resource boundary (audience mismatch)."
          right="Leg 2 carries a distinct Microsoft Graph audience — a real exchange, not a replay."
        />
        <GapRow
          label="Fresh jti / iat"
          wrong="The replayed token keeps its original jti/iat from when it was first issued."
          right="Token B is freshly minted by Entra with its own jti/iat (and correlation id)."
        />
        <GapRow
          label="Pre-authorization + reject-the-token"
          wrong="No audience/pre-authorization check — a foreign-audience token is accepted anywhere."
          right="A pre-authorized OBO exchange runs and the API rejects foreign-audience tokens (the reject-the-token rule)."
        />
      </ol>

      <p style={{ marginTop: 12, color: "#555", fontSize: 13, lineHeight: 1.6 }}>
        {server.interpretation}
      </p>
    </section>
  );
}

export default ReplayAttemptPanel;
