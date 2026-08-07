const REPO = "https://github.com/devopsabcs-engineering/croesus";
const PACKET = `${REPO}/blob/main/assets/croesus-escalation-packet.md`;
const FINDINGS = `${REPO}/blob/main/assets/croesus-3way-session-findings.md`;

const cardBase: React.CSSProperties = {
  flex: "1 1 320px",
  minWidth: 0,
  borderRadius: 8,
  padding: 16,
};

const codeStyle: React.CSSProperties = {
  fontFamily: "Consolas, monospace",
  fontSize: 13,
};

/**
 * Session exhibit: UI topology is unresolved, while registration type follows
 * the code redeemer and token custodian. Rendered unauthenticated so it can be
 * walked through with Croesus and Desjardins without signing in.
 */
export function RegistrationShapePanel() {
  return (
    <section style={{ marginTop: 32 }}>
      <h3>
        Separate UI topology from token architecture
      </h3>

      <p style={{ fontSize: 13, lineHeight: 1.6, color: "#666", marginTop: -8 }}>
        Scope: <strong>GPD Central only.</strong> Conseiller is a separate product and a separate
        assessment.
      </p>

      <p style={{ fontSize: 14, lineHeight: 1.6, color: "#444" }}>
        Croesus reports .NET Framework 4.5.2, roughly 130 <code style={codeStyle}>.aspx</code>{" "}
        pages, one URL for functionality, a BFF, and server-side code redemption. Croesus has also
        alternated between SPA and multi-page descriptions. The UI may be a JavaScript SPA served by
        Web Forms, a multi-page Web Forms application, or a hybrid. One URL,{" "}
        <code style={codeStyle}>.aspx</code> paths, and PKCE do not classify it.
      </p>

      <p style={{ fontSize: 14, lineHeight: 1.6, color: "#444" }}>
        A Desjardins browser trace contains <code style={codeStyle}>/oauth2/v2.0/authorize</code> and{" "}
        <strong>
          <code style={codeStyle}>/oauth2/v2.0/token</code> is not
        </strong>
        . That proves <code style={codeStyle}>/token</code> did not occur in the sampled browser
        transaction. It does not classify the UI or independently identify the redeemer. SPA and BFF
        are compatible; the BFF label remains provisional until token custody, the session cookie,
        and backend mediation are demonstrated.
      </p>

      <p style={{ fontSize: 14, lineHeight: 1.6, color: "#444" }}>
        Registration type follows the code redeemer and token custodian. If the same backend redeems
        and retains tokens, it is a confidential <code style={codeStyle}>web</code> client regardless
        of UI topology. If a separate browser public client exists, its{" "}
        <code style={codeStyle}>spa</code> registration may be legitimate. If Croesus&rsquo;s reported
        backend redemption is confirmed, Prod success leaves two explanations.
      </p>

      <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
        <div style={{ ...cardBase, border: "1px solid #f0c0c0", background: "#fff5f5" }}>
          <h4 style={{ marginTop: 0, color: "#b00020" }}>
            1 — The backend synthesises an <code style={codeStyle}>Origin</code> header
          </h4>
          <p style={{ fontSize: 14, lineHeight: 1.6 }}>
            A server-to-server POST sets no <code style={codeStyle}>Origin</code> of its own. If one
            is present, application code is adding it, and a <code style={codeStyle}>spa</code>-typed
            code is surviving redemption on a rule Entra wrote to reject it.
            <br />
            <strong>Fix:</strong> register Central as a <code style={codeStyle}>web</code>{" "}
            confidential client. Prove it with a client secret first — no library and no framework
            uplift — then harden with a certificate.
          </p>
        </div>
        <div style={{ ...cardBase, border: "1px solid #e6d3a3", background: "#fffaf0" }}>
          <h4 style={{ marginTop: 0, color: "#8a6d00" }}>
            2 — An undisclosed <code style={codeStyle}>web</code> registration exists
          </h4>
          <p style={{ fontSize: 14, lineHeight: 1.6 }}>
            The backend authenticates as a registration <strong>outside</strong> the three{" "}
            <code style={codeStyle}>spa</code> exports we were given, and the shape is already
            correct.
            <br />
            <strong>Fix:</strong> Croesus discloses the complete registration and service-principal
            inventory, and any accommodation is scoped to it.
          </p>
        </div>
      </div>

      <p style={{ fontSize: 14, lineHeight: 1.6, marginTop: 16 }}>
        <strong>What settles it:</strong>{" "}
        <a href={`${PACKET}#2-questions-for-croesus`} target="_blank" rel="noreferrer">
          Q8
        </a>{" "}
        (complete registration inventory),{" "}
        <a href={`${PACKET}#2-questions-for-croesus`} target="_blank" rel="noreferrer">
          Q7
        </a>{" "}
        (redacted <code style={codeStyle}>/token</code> capture — is an{" "}
        <code style={codeStyle}>Origin</code> header present?), and{" "}
        <a href={`${PACKET}#2-questions-for-croesus`} target="_blank" rel="noreferrer">
          Q15
        </a>{" "}
        (UI routing, PKCE-verifier owner, browser token visibility, session-cookie properties, and
        backend mediation). Presence indicators and SHA-256
        hashes only — never raw tokens or secrets.{" "}
        <a href={PACKET} target="_blank" rel="noreferrer">
          Full question list →
        </a>
      </p>

      <p style={{ fontSize: 14, lineHeight: 1.6 }}>
        <strong>Still unexplained, and it is Desjardins&rsquo; to settle:</strong> the blocked leg
        carries no device context in <em>either</em> tenant, yet Prod passes and Dev does not. Device
        posture alone cannot be the whole story — so either the blocked leg is not the one we believe,
        or the two tenants scope Conditional Access differently for Central.{" "}
        <a href={FINDINGS} target="_blank" rel="noreferrer">
          All findings and the nine remediation routes →
        </a>
      </p>

      <p style={{ fontSize: 13, lineHeight: 1.6, color: "#666" }}>
        Guardrail: until the registration shape and the grant are settled, do not change Conditional
        Access, allowlist the AWS egress IPs, or mandate On-Behalf-Of. The Conditional Access block is
        correct-by-design.
      </p>
    </section>
  );
}
