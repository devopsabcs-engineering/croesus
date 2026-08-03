const REPO = "https://github.com/devopsabcs-engineering/croesus";
const PACKET = `${REPO}/blob/main/assets/croesus-escalation-packet.md`;

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
 * Session exhibit: the registration-shape fork that the confirmed server-side
 * `/token` redemption creates. Rendered unauthenticated so it can be walked
 * through with Croesus and Desjardins without signing in.
 */
export function RegistrationShapePanel() {
  return (
    <section style={{ marginTop: 32 }}>
      <h3>
        The open fork: <code style={codeStyle}>spa</code> public client vs{" "}
        <code style={codeStyle}>web</code> confidential client
      </h3>
      <p style={{ fontSize: 14, lineHeight: 1.6, color: "#444" }}>
        A Desjardins browser trace of a real Central sign-in shows{" "}
        <code style={codeStyle}>/oauth2/v2.0/authorize</code> but{" "}
        <strong>
          no <code style={codeStyle}>/oauth2/v2.0/token</code>
        </strong>
        , so the code is redeemed <strong>server-side</strong> — as the vendor stated. Microsoft
        Entra rejects a plain server-side redemption of a <code style={codeStyle}>spa</code>{" "}
        authorization code with <code style={codeStyle}>AADSTS9002327</code>. The three registrations
        we hold are all <code style={codeStyle}>spa</code> public clients: no secret, no certificate,
        no exposed API scope. Exactly one branch below is true.
      </p>

      <div style={{ display: "flex", gap: 16, flexWrap: "wrap" }}>
        <div style={{ ...cardBase, border: "1px solid #f0c0c0", background: "#fff5f5" }}>
          <h4 style={{ marginTop: 0, color: "#b00020" }}>
            Branch A — the redemption fails (<code style={codeStyle}>AADSTS9002327</code>)
          </h4>
          <p style={{ fontSize: 14, lineHeight: 1.6 }}>
            A <code style={codeStyle}>spa</code> public client cannot service a no-
            <code style={codeStyle}>Origin</code> redemption. The supported shape is missing.
            <br />
            <strong>Fix:</strong> Croesus registers a <code style={codeStyle}>web</code> confidential
            client with a certificate credential.
          </p>
        </div>
        <div style={{ ...cardBase, border: "1px solid #e6d3a3", background: "#fffaf0" }}>
          <h4 style={{ marginTop: 0, color: "#8a6d00" }}>Branch B — the redemption succeeds</h4>
          <p style={{ fontSize: 14, lineHeight: 1.6 }}>
            Then the backend authenticates as a registration <strong>outside</strong> the three{" "}
            <code style={codeStyle}>spa</code> exports we were given.
            <br />
            <strong>Fix:</strong> Croesus discloses the complete registration and service-principal
            inventory, and any accommodation is scoped to it.
          </p>
        </div>
      </div>

      <p style={{ fontSize: 14, lineHeight: 1.6, marginTop: 16 }}>
        <strong>What settles it:</strong>{" "}
        <a href={`${PACKET}#2-questions-for-croesus`} target="_blank" rel="noreferrer">
          Q7
        </a>{" "}
        (redacted <code style={codeStyle}>/token</code> capture — is an{" "}
        <code style={codeStyle}>Origin</code> header present?) and{" "}
        <a href={`${PACKET}#2-questions-for-croesus`} target="_blank" rel="noreferrer">
          Q8
        </a>{" "}
        (complete registration inventory). Presence indicators and SHA-256 hashes only — never raw
        tokens or secrets.{" "}
        <a href={PACKET} target="_blank" rel="noreferrer">
          Full question list →
        </a>
      </p>

      <p style={{ fontSize: 13, lineHeight: 1.6, color: "#666" }}>
        Guardrail: until the grant is classified, do not change Conditional Access, allowlist the AWS
        egress IPs, or mandate On-Behalf-Of. The Conditional Access block is correct-by-design.
      </p>
    </section>
  );
}
