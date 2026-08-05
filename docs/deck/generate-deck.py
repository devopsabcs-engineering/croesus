"""Generates the Croesus / Desjardins session deck (.pptx).

Run: python docs/deck/generate-deck.py
"""

from pathlib import Path

from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.util import Inches, Pt

OUT = Path(__file__).resolve().parent / "croesus-session-deck.pptx"
FORK_PNG = Path(__file__).resolve().parent / "fork-panel.png"

REPO = "https://github.com/devopsabcs-engineering/croesus"
PACKET = f"{REPO}/blob/main/assets/croesus-escalation-packet.md"
QUESTIONS = f"{PACKET}#2-questions-for-croesus"
README = f"{REPO}/blob/main/README.md"
ORIGIN_SECTION = f"{README}#who-can-capture-the-token-origin-header"
NARRATIVE = f"{REPO}/blob/main/docs/evidence-narrative.md"
DEMO_GUIDE = f"{REPO}/blob/main/docs/obo-demo-guide.md"
KQL = f"{REPO}/blob/main/scripts/evidence-kql.kusto"
DEMO_APP = "https://croesus-spa.azurewebsites.net"

BLUE_DEEP = RGBColor(0x00, 0x1B, 0x3D)
BLUE = RGBColor(0x00, 0x67, 0xC0)
TEXT = RGBColor(0x1A, 0x1A, 0x1A)
MUTED = RGBColor(0x56, 0x56, 0x56)
RED = RGBColor(0xB0, 0x00, 0x20)
GREEN = RGBColor(0x0A, 0x7D, 0x28)
AMBER = RGBColor(0x8A, 0x6D, 0x00)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
BG = RGBColor(0xFA, 0xFB, 0xFC)
BORDER = RGBColor(0xE1, 0xE5, 0xE8)

prs = Presentation()
prs.slide_width = Inches(13.333)
prs.slide_height = Inches(7.5)


def new_slide(fill=BG):
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    slide.background.fill.solid()
    slide.background.fill.fore_color.rgb = fill
    return slide


def add_text(slide, text, x, y, w, h, size=18, bold=False, color=TEXT,
             align=PP_ALIGN.LEFT, italic=False):
    box = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = box.text_frame
    tf.word_wrap = True
    p = tf.paragraphs[0]
    p.alignment = align
    run = p.add_run()
    run.text = text
    run.font.size = Pt(size)
    run.font.bold = bold
    run.font.italic = italic
    run.font.color.rgb = color
    run.font.name = "Segoe UI"
    return box


def add_link(slide, label, url, x, y, w, h, size=14, bold=False):
    box = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = box.text_frame
    tf.word_wrap = True
    run = tf.paragraphs[0].add_run()
    run.text = label
    run.font.size = Pt(size)
    run.font.bold = bold
    run.font.name = "Segoe UI"
    run.hyperlink.address = url
    return box


def title(slide, heading, kicker=None):
    if kicker:
        add_text(slide, kicker.upper(), 0.7, 0.42, 11.9, 0.35, size=12,
                 bold=True, color=BLUE)
        add_text(slide, heading, 0.7, 0.75, 11.9, 0.85, size=32, bold=True,
                 color=BLUE_DEEP)
    else:
        add_text(slide, heading, 0.7, 0.55, 11.9, 0.9, size=32, bold=True,
                 color=BLUE_DEEP)
    line = slide.shapes.add_shape(1, Inches(0.7), Inches(1.62), Inches(11.9),
                                  Pt(2.5))
    line.fill.solid()
    line.fill.fore_color.rgb = BLUE
    line.line.fill.background()
    line.shadow.inherit = False


def bullets(slide, items, x=0.75, y=2.0, w=11.8, h=4.6, size=17, gap=10):
    box = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = box.text_frame
    tf.word_wrap = True
    for i, item in enumerate(items):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.space_after = Pt(gap)
        parts = item if isinstance(item, list) else [(item, False)]
        marker = p.add_run()
        marker.text = "\u25aa  "
        marker.font.size = Pt(size)
        marker.font.color.rgb = BLUE
        marker.font.name = "Segoe UI"
        for text, bold in parts:
            run = p.add_run()
            run.text = text
            run.font.size = Pt(size)
            run.font.bold = bold
            run.font.color.rgb = TEXT
            run.font.name = "Segoe UI"
    return box


def add_table(slide, headers, rows, x, y, w, col_widths=None, size=13,
              header_size=13, row_h=0.5):
    shape = slide.shapes.add_table(len(rows) + 1, len(headers), Inches(x),
                                   Inches(y), Inches(w),
                                   Inches(0.45 + row_h * len(rows)))
    table = shape.table
    if col_widths:
        for i, cw in enumerate(col_widths):
            table.columns[i].width = Inches(cw)
    for i, head in enumerate(headers):
        cell = table.cell(0, i)
        cell.text = head or " "
        cell.fill.solid()
        cell.fill.fore_color.rgb = BLUE_DEEP
        cell.vertical_anchor = MSO_ANCHOR.MIDDLE
        p = cell.text_frame.paragraphs[0]
        p.runs[0].font.size = Pt(header_size)
        p.runs[0].font.bold = True
        p.runs[0].font.color.rgb = WHITE
        p.runs[0].font.name = "Segoe UI"
    for r, row in enumerate(rows, start=1):
        for c, value in enumerate(row):
            cell = table.cell(r, c)
            cell.text = value or " "
            cell.fill.solid()
            cell.fill.fore_color.rgb = WHITE if r % 2 else RGBColor(0xF2, 0xF6, 0xFA)
            cell.vertical_anchor = MSO_ANCHOR.MIDDLE
            p = cell.text_frame.paragraphs[0]
            p.runs[0].font.size = Pt(size)
            p.runs[0].font.color.rgb = TEXT
            p.runs[0].font.name = "Segoe UI"
    return table


def card(slide, x, y, w, h, heading, body, accent):
    box = slide.shapes.add_shape(5, Inches(x), Inches(y), Inches(w), Inches(h))
    box.fill.solid()
    box.fill.fore_color.rgb = WHITE
    box.line.color.rgb = BORDER
    box.shadow.inherit = False
    box.text_frame.text = ""
    bar = slide.shapes.add_shape(1, Inches(x), Inches(y), Inches(w), Pt(5))
    bar.fill.solid()
    bar.fill.fore_color.rgb = accent
    bar.line.fill.background()
    bar.shadow.inherit = False
    add_text(slide, heading, x + 0.25, y + 0.22, w - 0.5, 0.5, size=17,
             bold=True, color=accent)
    tb = slide.shapes.add_textbox(Inches(x + 0.25), Inches(y + 0.85),
                                  Inches(w - 0.5), Inches(h - 1.05))
    tf = tb.text_frame
    tf.word_wrap = True
    for i, (text, bold) in enumerate(body):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.space_after = Pt(8)
        run = p.add_run()
        run.text = text
        run.font.size = Pt(14)
        run.font.bold = bold
        run.font.color.rgb = TEXT
        run.font.name = "Segoe UI"


def notes(slide, text):
    slide.notes_slide.notes_text_frame.text = text


# ---------------------------------------------------------------- SLIDE 1
s = new_slide(WHITE)
band = s.shapes.add_shape(1, Inches(0), Inches(0), Inches(13.333), Inches(2.9))
band.fill.solid()
band.fill.fore_color.rgb = BLUE_DEEP
band.line.fill.background()
band.shadow.inherit = False
add_text(s, "DESJARDINS \u2014 CROESUS \u2014 MICROSOFT  |  JOINT WORKING SESSION",
         0.8, 0.85, 11.7, 0.4, size=13, bold=True, color=RGBColor(0x7F, 0xC4, 0xFF))
add_text(s, "GPD Central SSO: what the evidence proves,\nand the two questions that close it",
         0.8, 1.25, 11.7, 1.4, size=34, bold=True, color=WHITE)
add_text(s, "Entra ID app registrations, Conditional Access, and the server-side "
            "/token redemption", 0.8, 3.25, 11.7, 0.5, size=18, color=MUTED)
add_text(s, "3 August 2026", 0.8, 3.85, 5, 0.4, size=15, color=MUTED)
add_link(s, "\u25b6  Live demo \u2014 croesus-spa.azurewebsites.net", DEMO_APP,
         0.8, 4.6, 6, 0.4, size=16, bold=True)
add_link(s, "\u25b6  Question list (Q1\u2013Q9) on GitHub", QUESTIONS,
         0.8, 5.1, 6, 0.4, size=16, bold=True)
add_link(s, "\u25b6  Full analysis \u2014 README", README, 0.8, 5.6, 6, 0.4,
         size=16, bold=True)
notes(s, "Framing: this is a joint fact-finding session, not a defect report. "
         "The Conditional Access block is correct-by-design. We have one open "
         "question and two asks that close it.")

# ---------------------------------------------------------------- SLIDE 2
s = new_slide()
title(s, "The question on the table", "Why we are here")
bullets(s, [
    [("Desjardins users sign in to GPD Central. A ", False),
     ("second, non-interactive sign-in", True),
     (" then arrives from the Croesus AWS backend and is ", False),
     ("blocked by Conditional Access", True), (" in non-prod.", False)],
    [("One question: is that expected OAuth behaviour, or a misconfiguration?", True)],
    [("The answer decides the remedy \u2014 an ", False),
     ("internal Desjardins change", True), (" or a ", False),
     ("vendor-side change at Croesus", True), (".", False)],
    [("Nobody is at fault yet. The block is a Zero Trust control working as "
      "designed; the open item is what the blocked call actually is.", False)],
], y=2.15, size=19, gap=16)
notes(s, "Keep this cooperative. State plainly that the CA block is not a "
         "Desjardins misconfiguration and not an accusation against Croesus.")

# ---------------------------------------------------------------- SLIDE 3
s = new_slide()
title(s, "Bottom line", "Where we stand today")
bullets(s, [
    [("The Conditional Access block is ", False), ("correct-by-design", True),
     (". CA reacts to the origin and context of the request, not to the "
      "grant type.", False)],
    [("The /token redemption is now ", False), ("confirmed server-side", True),
     (" \u2014 a Desjardins browser trace shows /authorize but no /token. This "
      "corroborates Croesus.", False)],
    [("The grant is ", False), ("still not classified", True),
     (" from a captured request. /token is the normal endpoint for code "
      "redemption and refresh \u2014 it is not evidence of On-Behalf-Of.", False)],
    [("The \u201cUnbound (1008)\u201d line is a ", False),
     ("device- and session-binding status", True),
     (", not proof of token replay and not a grant classifier.", False)],
    [("Next: resolve the fork with ", False), ("Q7 and Q8", True),
     (". Change nothing in Conditional Access until then.", False)],
], y=2.1, size=17, gap=14)
notes(s, "This slide is the executive summary. If the session ends after five "
         "minutes, these five lines are what must land.")

# ---------------------------------------------------------------- SLIDE 4
s = new_slide()
title(s, "What the evidence does \u2014 and does not \u2014 prove", "Discipline")
add_table(s, ["Observation", "What it proves", "What it does NOT prove"], [
    ["Calls to /oauth2/v2.0/token",
     "A token is being requested at the standard endpoint",
     "Not OBO. Code redemption and refresh use the same endpoint"],
    ["Sign-in status \u201cUnbound (1008)\u201d",
     "The client is not integrated with the platform broker",
     "Not token replay. Not a grant type. Out of scope for browser\u2192Graph"],
    ["Conditional Access blocked the call",
     "The request lacked compliant-device context from an untrusted IP",
     "Nothing about which grant was used \u2014 CA is grant-agnostic"],
    ["Three exported registrations are \u2018spa\u2019",
     "No secret, no certificate, no exposed API scope",
     "Not the full inventory \u2014 other registrations may exist (Q8)"],
], x=0.75, y=2.1, w=11.85, col_widths=[3.0, 4.2, 4.65], size=13, row_h=0.95)
notes(s, "This is the credibility slide. Showing what we refuse to over-claim "
         "earns the right to the ask on slide 9.")

# ---------------------------------------------------------------- SLIDE 5
s = new_slide()
title(s, "New evidence: the browser trace", "Step 1 \u2014 done")
add_text(s, "A Desjardins user captured a HAR of a real Central sign-in.",
         0.75, 2.0, 11.8, 0.4, size=18, color=MUTED)
card(s, 0.75, 2.55, 5.75, 2.3, "Present in the trace", [
    ("/oauth2/v2.0/authorize", True),
    ("The front-channel leg runs in the user's browser, as expected.", False),
], GREEN)
card(s, 6.85, 2.55, 5.75, 2.3, "Absent from the trace", [
    ("/oauth2/v2.0/token \u2014 no occurrence at all", True),
    ("The code leaves the browser but is redeemed elsewhere.", False),
], RED)
add_text(s, "Conclusion: the redemption is server-side. This corroborates "
            "Olivier's account and rules out browser redemption for this capture.",
         0.75, 5.1, 11.8, 0.6, size=19, bold=True, color=BLUE_DEEP)
add_text(s, "Consequence: Desjardins can no longer observe the Origin header \u2014 "
            "it now exists only on Croesus's outbound request. Caveat: one sample, "
            "one environment.", 0.75, 5.75, 11.8, 0.8, size=15, color=MUTED)
notes(s, "Credit Mathieu for capturing this. Emphasise that this confirms what "
         "Croesus said \u2014 we are validating the vendor, not contradicting them.")

# ---------------------------------------------------------------- SLIDE 6
s = new_slide()
title(s, "The fork this creates", "Hypothesis to verify")
add_text(s, "Microsoft Entra rejects a plain server-side redemption of a "
            "\u2018spa\u2019 authorization code with AADSTS9002327. A server-side "
            "redemption and a spa-only registration cannot both hold.",
         0.75, 2.0, 11.8, 0.7, size=16, color=MUTED)
card(s, 0.75, 2.9, 5.75, 3.1, "Branch A \u2014 the redemption FAILS", [
    ("Entra returns AADSTS9002327.", True),
    ("A spa public client cannot service a no-Origin redemption. The "
     "supported registration shape is missing.", False),
    ("Fix: Croesus registers a \u2018web\u2019 confidential client with a "
     "certificate credential.", True),
], RED)
card(s, 6.85, 2.9, 5.75, 3.1, "Branch B \u2014 the redemption SUCCEEDS", [
    ("No AADSTS9002327 in the logs.", True),
    ("Then the backend authenticates as a registration outside the three spa "
     "exports we were given.", False),
    ("Fix: Croesus discloses the complete inventory (Q8); any accommodation "
     "is scoped to it.", True),
], AMBER)
add_text(s, "Caveat: if Conditional Access blocks the leg first, the logs show a "
            "CA failure code instead \u2014 that does not settle the question.",
         0.75, 6.2, 11.8, 0.5, size=14, italic=True, color=MUTED)
notes(s, "This is the core hypothesis slide. Exactly one branch is true and each "
         "has a different, small remediation. Neither is a crisis.")

# ---------------------------------------------------------------- SLIDE 7
s = new_slide()
title(s, "Why the registration shape matters", "Background")
add_table(s, ["", "spa (public client)", "web (confidential client)"], [
    ["Credential", "None \u2014 no secret, no certificate",
     "Secret or certificate required"],
    ["Redeems a code", "Only from a cross-origin browser request (Origin header)",
     "From a server, no Origin required"],
    ["Server-side redemption",
     "Rejected: AADSTS9002327", "Supported"],
    ["Exposed API scope", "None in the three exports", "Required for On-Behalf-Of"],
    ["What we hold today", "All three: dev-dev, dev-prod, prod-prod", "None disclosed"],
], x=0.75, y=2.1, w=11.85, col_widths=[2.7, 4.6, 4.55], size=14, row_h=0.72)
add_text(s, "\u201cTokens issued for the 'Single-Page Application' client-type may "
            "only be redeemed via cross-origin requests.\u201d  \u2014 AADSTS9002327",
         0.75, 6.15, 11.8, 0.6, size=15, italic=True, color=BLUE_DEEP)
notes(s, "Make the point technically, not accusingly: this is a platform rule, "
         "not an opinion. It is also cheap to fix on either branch.")

# ------------------------------------------------------------ SLIDE 7-BIS
if FORK_PNG.exists():
    s = new_slide(WHITE)
    title(s, "The same fork, live in the demo", "Screenshot")
    pic = s.shapes.add_picture(str(FORK_PNG), Inches(3.55), Inches(1.95),
                               height=Inches(5.1))
    pic.left = Inches((13.333 - pic.width.inches) / 2)
    add_link(s, "\u25b6  Open it live \u2014 croesus-spa.azurewebsites.net",
             DEMO_APP, 0.75, 7.02, 6.5, 0.35, size=13, bold=True)
    notes(s, "The panel renders without signing in, so it can be walked through "
             "on screen share. Q7, Q8, and the full question list are clickable "
             "straight from the page.")

# ---------------------------------------------------------------- SLIDE 8
s = new_slide()
title(s, "Who can answer what", "Ownership of the evidence")
add_table(s, ["Question", "Who can answer", "Status"], [
    ["Origin header on a browser-side /token call",
     "Desjardins \u2014 browser DevTools", "Ruled out \u2014 no /token in the HAR"],
    ["Origin header on the server-side /token call",
     "Croesus only \u2014 their outbound request", "Open \u2014 Q7"],
    ["Did the second leg succeed or fail with AADSTS9002327?",
     "Desjardins \u2014 own Entra sign-in logs", "In progress"],
    ["Complete registration / service-principal inventory",
     "Croesus", "Open \u2014 Q8"],
    ["AWS egress IP ranges, intended flow definition", "Croesus", "Open \u2014 Q4, Q1"],
], x=0.75, y=2.1, w=11.85, col_widths=[5.0, 3.6, 3.25], size=13, row_h=0.72)
add_link(s, "Details: README \u2014 Who can capture the /token Origin header \u2192",
         ORIGIN_SECTION, 0.75, 6.25, 8, 0.4, size=14, bold=True)
notes(s, "The raw Origin header is never a field in the tenant sign-in logs. It "
         "exists only on the HTTP request the caller sends to Entra.")

# ---------------------------------------------------------------- SLIDE 9
s = new_slide()
title(s, "The asks: Q7 and Q8", "What we need from Croesus")
card(s, 0.75, 2.05, 5.75, 3.4, "Q7 \u2014 one redacted /token capture", [
    ("For one successful and one failing transaction:", True),
    ("Is an Origin header present?  \u2022  grant_type  \u2022  client_id  "
     "\u2022  redirect URI  \u2022  client-authentication method", False),
    ("Presence indicators or SHA-256 hashes only. Never raw codes, tokens, "
     "secrets, or assertions.", True),
], BLUE)
card(s, 6.85, 2.05, 5.75, 3.4, "Q8 \u2014 complete registration inventory", [
    ("Every app registration and service principal GPD Central uses, across "
     "environments.", True),
    ("Including any confidential web/API registration that is not among the "
     "three spa exports we hold.", False),
    ("Promoted to equal priority by the server-side finding.", True),
], BLUE)
add_text(s, "Also open: Q1 intended flow  \u2022  Q4 AWS egress ranges  \u2022  "
            "Q5 cross-tenant device compliance  \u2022  Q6 token-binding "
            "compatibility  \u2022  Q9 Central vs Conseiller parity",
         0.75, 5.65, 11.8, 0.5, size=15, color=MUTED)
add_link(s, "\u25b6  Full question list Q1\u2013Q9 on GitHub \u2192", QUESTIONS,
         0.75, 6.25, 8, 0.45, size=17, bold=True)
notes(s, "Stress the redaction rule up front. It is what makes Q7 answerable "
         "by a security-conscious vendor.")

# ---------------------------------------------------------------- SLIDE 10
s = new_slide()
title(s, "Remediation ladder", "Once the fork is resolved")
bullets(s, [
    [("Classify first.", True),
     (" No Conditional Access change, no AWS IP allowlist, and no OBO mandate "
      "until the grant is known.", False)],
    [("Branch A \u2192 register a web confidential client.", True),
     (" A certificate credential on the Croesus side. Smallest possible change; "
      "no Desjardins policy change needed.", False)],
    [("Branch B \u2192 scope to the disclosed registration.", True),
     (" Apply any accommodation to that specific service principal rather than "
      "to a broad IP range.", False)],
    [("Rule out device trust as the lever.", True),
     (" Cross-tenant \u2018Trust compliant devices\u2019 only fires on a B2B guest "
      "sign-in, and the blocked leg is a server-side call with no device "
      "context at all.", False)],
    [("On-Behalf-Of is an option, not a requirement.", True),
     (" It applies only if a genuine confidential middle tier is proven \u2014 "
      "which requires an exposed API scope and a credential.", False)],
], y=2.1, size=17, gap=14)
notes(s, "The point: every branch has a small, reversible fix. Nobody needs a "
         "re-architecture, and Desjardins does not need to weaken CA. On device "
         "trust \u2014 credit Mathieu Santerre for the correction: the inbound "
         "trust settings honour a device claim carried in a guest's token from "
         "their home tenant, so they cannot bridge Prod device state into a "
         "native non-prod sign-in.")

# ---------------------------------------------------------------- SLIDE 11
s = new_slide()
title(s, "The working demo", "Reference implementation")
bullets(s, [
    [("A live SPA + API in a Microsoft demo tenant that makes the flow shapes "
      "observable \u2014 nothing runs against Desjardins or Croesus tenants.", False)],
    [("Wrong vs right:", True),
     (" a token replay to Microsoft Graph is rejected on audience, while a real "
      "On-Behalf-Of exchange issues a distinct, separately audienced token.", False)],
    [("The registration fork:", True),
     (" a panel walks through spa vs web and the AADSTS9002327 behaviour, with "
      "direct links to Q7 and Q8.", False)],
    [("Token inspector and evidence panel:", True),
     (" decoded claims show audience binding rather than a reused bearer. The raw-token "
      "inspector is gated off by default \u2014 we ask the vendor for hashes, so we do not "
      "demo copying live tokens.", False)],
    [("Tier 2 exhibits (gated):", True),
     (" a server-side replay control, and a report-only Token Protection policy "
      "that reproduces the real 1008 signal on a supported resource.", False)],
], y=2.05, size=16, gap=12)
add_link(s, "\u25b6  Open the demo \u2014 croesus-spa.azurewebsites.net", DEMO_APP,
         0.75, 5.95, 6.5, 0.4, size=16, bold=True)
add_link(s, "\u25b6  Demo walkthrough guide", DEMO_GUIDE, 0.75, 6.4, 6.5, 0.4,
         size=16, bold=True)
add_link(s, "\u25b6  Evidence narrative (Q\u2011by\u2011Q mapping)", NARRATIVE,
         7.4, 5.95, 5.2, 0.4, size=16, bold=True)
add_link(s, "\u25b6  Evidence KQL queries", KQL, 7.4, 6.4, 5.2, 0.4, size=16,
         bold=True)
notes(s, "Demo order: sign in, Call API, show the evidence panel, then the "
         "replay button, then scroll to the registration-fork panel and click "
         "through to Q7/Q8.")

# ---------------------------------------------------------------- SLIDE 12
s = new_slide()
title(s, "Next steps and owners", "Leaving this session")
add_table(s, ["#", "Action", "Owner", "Closes"], [
    ["1", "Query Entra sign-in logs for the server-side leg: success vs AADSTS9002327",
     "Desjardins", "Narrows the fork without the vendor"],
    ["2", "Provide one redacted /token capture (hashes / presence only)",
     "Croesus", "Q7"],
    ["3", "Provide the complete registration and service-principal inventory",
     "Croesus", "Q8"],
    ["4", "Provide AWS egress IP ranges and the intended flow definition",
     "Croesus", "Q4, Q1"],
    ["5", "Choose the minimum remediation once the branch is known",
     "Joint", "The escalation"],
], x=0.75, y=2.1, w=11.85, col_widths=[0.6, 5.4, 1.9, 3.95], size=13, row_h=0.72)
add_text(s, "Guardrail until then: do not change Conditional Access, do not "
            "allowlist the AWS egress IPs, and do not mandate On-Behalf-Of.",
         0.75, 6.2, 11.8, 0.6, size=17, bold=True, color=RED)
notes(s, "Close by restating the guardrail. It protects both parties: no "
         "premature policy loosening, no premature vendor re-architecture.")

# ---------------------------------------------------------------- SLIDE 13
s = new_slide(WHITE)
title(s, "Links", "Everything referenced in this deck")
links = [
    ("Question list Q1\u2013Q9 (escalation packet)", QUESTIONS),
    ("Escalation packet \u2014 full document", PACKET),
    ("Full analysis \u2014 README", README),
    ("Who can capture the /token Origin header", ORIGIN_SECTION),
    ("Evidence narrative \u2014 question-by-question mapping", NARRATIVE),
    ("On-Behalf-Of demo guide", DEMO_GUIDE),
    ("Evidence KQL queries", KQL),
    ("Repository \u2014 devopsabcs-engineering/croesus", REPO),
    ("Live demo application", DEMO_APP),
]
for i, (label, url) in enumerate(links):
    add_link(s, f"\u25b6  {label}", url, 0.85, 2.05 + i * 0.5, 11.5, 0.42,
             size=16, bold=True)
notes(s, "Share the deck itself \u2014 every link is clickable in slideshow mode.")

OUT.parent.mkdir(parents=True, exist_ok=True)
prs.save(OUT)
print(f"Wrote {OUT} ({len(prs.slides.__iter__.__self__._sldIdLst)} slides)")
