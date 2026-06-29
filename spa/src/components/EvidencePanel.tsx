import type { ClaimSummary, MeResponse } from "../api";

// The API client ID is derived from the configured API scope
// (api://<API_CLIENT_ID>/access_as_user). This lets us assert leg-1's audience is
// the API WITHOUT referencing any downstream resource identifier in SPA source.
const apiClientId = (import.meta.env.VITE_API_SCOPE.match(/^api:\/\/([^/]+)\//)?.[1] ?? "")
  .toLowerCase();

function audIsApi(aud: string): boolean {
  const a = aud.toLowerCase();
  return a === apiClientId || a === `api://${apiClientId}`;
}

function ClaimRow({ label, value, ok }: { label: string; value: string; ok?: boolean }) {
  const color = ok === undefined ? "#333" : ok ? "#0a7d28" : "#b00020";
  return (
    <tr>
      <td style={{ padding: "4px 12px 4px 0", fontWeight: 600, whiteSpace: "nowrap" }}>{label}</td>
      <td style={{ padding: "4px 0", fontFamily: "monospace", color, wordBreak: "break-all" }}>
        {value}
      </td>
    </tr>
  );
}

function LegCard({
  title,
  subtitle,
  claim,
  audOk,
  scpOk,
}: {
  title: string;
  subtitle: string;
  claim: ClaimSummary;
  audOk: boolean;
  scpOk?: boolean;
}) {
  return (
    <div
      style={{
        border: "1px solid #d0d0d0",
        borderRadius: 8,
        padding: 16,
        flex: "1 1 320px",
        background: "#fafafa",
      }}
    >
      <h4 style={{ margin: "0 0 4px" }}>{title}</h4>
      <p style={{ margin: "0 0 12px", color: "#555", fontSize: 13 }}>{subtitle}</p>
      <table style={{ borderCollapse: "collapse", width: "100%", fontSize: 14 }}>
        <tbody>
          <ClaimRow label="aud" value={claim.aud} ok={audOk} />
          {claim.scp !== undefined && <ClaimRow label="scp" value={claim.scp} ok={scpOk} />}
          <ClaimRow label="jti" value={claim.jti} />
          <ClaimRow label="iat" value={new Date(claim.iat * 1000).toISOString()} />
        </tbody>
      </table>
    </div>
  );
}

/**
 * Renders the two legs of the On-Behalf-Of exchange returned by the API and
 * asserts the binding facts:
 *   - leg 1 (token A): aud == the API (matched against the configured API scope),
 *     scp == access_as_user.
 *   - leg 2 (token B): a DISTINCT downstream resource (the API returns its raw
 *     aud, which is the downstream Microsoft Graph audience) with a different jti.
 *   - leg 1 jti != leg 2 jti  (two distinct tokens => real exchange, not replay).
 *
 * The SPA never hardcodes the downstream resource identifier; leg-2's audience is
 * shown verbatim from the API response, and "bound, not replayed" is proven by the
 * two legs having different audiences and different jti values.
 */
export function EvidencePanel({ data }: { data: MeResponse }) {
  const { leg1, leg2 } = data.evidence;

  const leg1AudIsApi = audIsApi(leg1.aud); // token A is bound to the API
  const leg1ScpOk = leg1.scp === "access_as_user";
  const distinctAud = leg1.aud.toLowerCase() !== leg2.aud.toLowerCase(); // leg 2 is a different resource
  const distinctJti = leg1.jti !== leg2.jti;

  return (
    <section>
      <h3 style={{ marginBottom: 4 }}>Audience-binding evidence</h3>
      <p style={{ marginTop: 0, color: "#555" }}>
        Signed in as <strong>{data.userPrincipalName ?? data.displayName ?? "(unknown)"}</strong>
      </p>

      <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
        <LegCard
          title="Leg 1 — token A (SPA → API)"
          subtitle="The only token the SPA holds. Its audience is the API."
          claim={leg1}
          audOk={leg1AudIsApi}
          scpOk={leg1ScpOk}
        />
        <LegCard
          title="Leg 2 — token B (API → downstream, via OBO)"
          subtitle="Minted by Entra for the API after authenticating its confidential-client credential. The aud below is the downstream resource (Microsoft Graph). The SPA never sees this token."
          claim={leg2}
          audOk={distinctAud}
        />
      </div>

      <ul style={{ marginTop: 16, fontSize: 14, lineHeight: 1.6 }}>
        <li style={{ color: leg1AudIsApi ? "#0a7d28" : "#b00020" }}>
          {leg1AudIsApi ? "✓" : "✗"} Leg 1 <code>aud</code> is the API
        </li>
        <li style={{ color: leg1ScpOk ? "#0a7d28" : "#b00020" }}>
          {leg1ScpOk ? "✓" : "✗"} Leg 1 <code>scp</code> == <code>access_as_user</code>
        </li>
        <li style={{ color: distinctAud ? "#0a7d28" : "#b00020" }}>
          {distinctAud ? "✓" : "✗"} Leg 2 <code>aud</code> is a different resource than leg 1 (shown
          above — the downstream Microsoft Graph audience)
        </li>
        <li style={{ color: distinctJti ? "#0a7d28" : "#b00020" }}>
          {distinctJti ? "✓" : "✗"} Distinct <code>jti</code> across legs (two tokens, real exchange)
        </li>
      </ul>
    </section>
  );
}

export default EvidencePanel;
