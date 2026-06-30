import type { ReplayAttemptResult } from "../api";

function ClaimRow({ label, value, ok }: { label: string; value?: string; ok?: boolean }) {
  const color = ok === undefined ? "#333" : ok ? "#0a7d28" : "#b00020";
  return (
    <tr>
      <td style={{ padding: "4px 12px 4px 0", fontWeight: 600, whiteSpace: "nowrap" }}>{label}</td>
      <td style={{ padding: "4px 0", fontFamily: "monospace", color, wordBreak: "break-all" }}>
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
          background: graphRejected ? "#f3fbf3" : "#fff5f5",
        }}
      >
        <h4 style={{ margin: "0 0 12px", color: graphRejected ? "#0a7d28" : "#b00020" }}>
          {graphRejected ? "✓" : "✗"} Graph status {result.status}
          {graphRejected ? " (expected negative control)" : " (unexpected)"}
        </h4>
        <table style={{ borderCollapse: "collapse", width: "100%", fontSize: 14 }}>
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

export default ReplayAttemptPanel;
