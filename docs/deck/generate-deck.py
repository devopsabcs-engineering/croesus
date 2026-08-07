"""Generates the Croesus / Desjardins session deck (.pptx), English and French.

Run: python docs/deck/generate-deck.py            # both languages
     python docs/deck/generate-deck.py --lang fr  # one language

Layout lives in code; every user-visible string lives in CONTENT so the two
decks cannot drift apart when the analysis changes.
"""

import argparse
import re
from pathlib import Path

from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.util import Inches, Pt

HERE = Path(__file__).resolve().parent
FORK_PNG = HERE / "fork-panel.png"

REPO = "https://github.com/devopsabcs-engineering/croesus"
PACKET = f"{REPO}/blob/main/assets/croesus-escalation-packet.md"
QUESTIONS = f"{PACKET}#2-questions-for-croesus"
README = f"{REPO}/blob/main/README.md"
ORIGIN_SECTION = f"{README}#who-can-capture-the-token-origin-header"
NARRATIVE = f"{REPO}/blob/main/docs/evidence-narrative.md"
DEMO_GUIDE = f"{REPO}/blob/main/docs/obo-demo-guide.md"
KQL = f"{REPO}/blob/main/scripts/evidence-kql.kusto"
FINDINGS = f"{REPO}/blob/main/assets/croesus-3way-session-findings.md"
ROUTES = f"{FINDINGS}#4-routes-and-workarounds"
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

# French runs longer than English at identical wording, so its type is set down
# a notch to keep the same boxes from overflowing.
SCALE = {"en": 1.0, "fr": 0.92}

CONTENT = {}

CONTENT["en"] = {
    "out": "croesus-session-deck.pptx",
    "title_kicker": "DESJARDINS \u2014 CROESUS \u2014 MICROSOFT  |  JOINT WORKING SESSION",
    "title_main": "GPD Central SSO: what the evidence proves,\nand the routes that close it",
    "title_sub": "Entra ID app registrations, Conditional Access, and the "
                 "server-side /token redemption  |  Central only — not Conseiller",
    "title_date": "5 August 2026  —  updated after the three-way session",
    "title_links": [
        ("\u25b6  Session findings and all nine routes", FINDINGS),
        ("\u25b6  Question list (Q1\u2013Q15) on GitHub", QUESTIONS),
        ("\u25b6  Live demo \u2014 croesus-spa.azurewebsites.net", DEMO_APP),
    ],
    "title_notes": "Framing: this is a joint fact-finding session, not a defect "
                   "report. The Conditional Access block is correct-by-design. "
                   "Scope is GPD Central only — Conseiller is a separate product "
                   "and a separate assessment. Say that out loud early.",

    "s2_kicker": "Why we are here",
    "s2_title": "The question on the table",
    "s2_bullets": [
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
    ],
    "s2_notes": "Keep this cooperative. State plainly that the CA block is not a "
                "Desjardins misconfiguration and not an accusation against Croesus.",

    "s3_kicker": "Where we stand today",
    "s3_title": "Bottom line",
    "s3_bullets": [
                [("Central’s UI topology is ", False), ("unresolved", True),
                 (": SPA, multi-page Web Forms, and hybrid all remain possible. One URL, "
                    ".aspx paths, and PKCE do not classify it.", False)],
                [("SPA and BFF are compatible. ", True),
                 ("Treat the BFF label as provisional until Q15 confirms token custody, "
                    "cookie properties, and backend mediation.", False)],
                [("Registration type follows the ", False), ("code redeemer", True),
                 (". If the same backend redeems and retains tokens, it is a confidential "
                    "‘web’ client. A separate browser public client may legitimately "
                    "remain ‘spa’.", False)],
        [("The prod/non-prod split is ", False), ("not yet explained", True),
         (" — the blocked leg carries no device context in either tenant. "
          "Desjardins can settle this alone, this week.", False)],
        [("The Conditional Access block is ", False), ("correct-by-design", True),
         (". The “Unbound (1008)” line is a binding status, not proof of "
          "replay and not a grant classifier.", False)],
    ],
    "s3_notes": "This slide is the executive summary. If the session ends after "
                "five minutes, these five lines are what must land. Scope: "
                "Central only.",

    "s4_kicker": "Discipline",
    "s4_title": "What the evidence does \u2014 and does not \u2014 prove",
    "s4_headers": ["Observation", "What it proves", "What it does NOT prove"],
    "s4_rows": [
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
    ],
    "s4_notes": "This is the credibility slide. Showing what we refuse to "
                "over-claim earns the right to the ask on slide 9.",

    "s5_kicker": "Step 1 \u2014 done",
    "s5_title": "New evidence: the browser trace",
    "s5_lede": "A Desjardins user captured a HAR of a real Central sign-in.",
    "s5_present_head": "Present in the trace",
    "s5_present": [
        ("/oauth2/v2.0/authorize", True),
        ("The front-channel leg runs in the user's browser, as expected.", False),
    ],
    "s5_absent_head": "Absent from the trace",
    "s5_absent": [
        ("/oauth2/v2.0/token \u2014 no occurrence at all", True),
        ("The code leaves the browser but is redeemed elsewhere.", False),
    ],
    "s5_conclusion": "Conclusion: /token did not occur in this sampled browser "
                     "transaction. That is consistent with Olivier's account, "
                     "but does not independently identify the redeemer.",
    "s5_consequence": "Consequence: this HAR cannot classify the UI or prove the "
                      "BFF boundary. Q7, Q8, and Q15 request the missing evidence. "
                      "Caveat: one sample, one environment.",
    "s5_notes": "Credit Mathieu for capturing this. Emphasise that this confirms "
                "what Croesus said \u2014 we are validating the vendor, not "
                "contradicting them.",

    "s6_kicker": "If backend redemption is confirmed",
    "s6_title": "The two explanations left",
    "s6_intro": "Entra rejects a plain server-side redemption of a ‘spa’ "
                "authorization code with AADSTS9002327. Central works in Prod, so "
                "the same backend redeems and retains tokens, Prod success leaves "
                "two explanations.",
    "s6_a_head": "1 — A synthesised Origin header",
    "s6_a": [
        ("A server-to-server POST sets no Origin of its own.", True),
        ("If one is present, application code is adding it — and a spa-typed "
         "code is surviving a rule Entra wrote to reject it.", False),
        ("Fix: register Central as a ‘web’ confidential client. Prove it with "
         "a client secret — no library, no framework uplift — then harden "
         "with a certificate.", True),
    ],
    "s6_b_head": "2 — An undisclosed ‘web’ registration",
    "s6_b": [
        ("The backend authenticates as something outside our three exports.", True),
        ("Then the shape is already correct and we have simply never been shown "
         "it.", False),
        ("Fix: Croesus discloses the complete inventory (Q8); any accommodation "
         "is scoped to it.", True),
    ],
    "s6_caveat": "Separately, and still open: the blocked leg carries no device "
                 "context in EITHER tenant, yet Prod passes. That is Desjardins’ "
                 "to settle from its own policy inventory and sign-in logs.",
    "s6_notes": "This is the core slide. Neither explanation is a crisis and both "
                "have small remediations. Q8 separates them in one answer.",

    "s7_kicker": "Background",
    "s7_title": "Why the registration shape matters",
    "s7_headers": ["", "spa (public client)", "web (confidential client)"],
    "s7_rows": [
        ["Describes", "A browser public client", "A confidential server client"],
        ["Credential", "None \u2014 no secret, no certificate",
         "Secret or certificate required"],
        ["Redeems a code", "Only from a cross-origin browser request (Origin header)",
         "From a server, no Origin required"],
        ["Server-side redemption", "Rejected: AADSTS9002327", "Supported"],
        ["Refresh token lifetime", "Capped at 24 hours, non-sliding",
         "Not capped that way"],
        ["Decision evidence", "Browser redeems and holds tokens",
         "Backend redeems and holds tokens"],
    ],
    "s7_quote": "\u201cTokens issued for the 'Single-Page Application' client-type "
                "may only be redeemed via cross-origin requests.\u201d  "
                "\u2014 AADSTS9002327",
    "s7_notes": "Make the point technically, not accusingly: this is a platform "
                "rule, not an opinion. It is also cheap to fix on either branch.",

    "s7b_kicker": "Screenshot",
    "s7b_title": "The same architecture fork, live in the demo",
    "s7b_link": "\u25b6  Open it live \u2014 croesus-spa.azurewebsites.net",
    "s7b_notes": "The panel renders without signing in, so it can be walked "
                 "through on screen share. Q7, Q8, Q15, and the full question list are "
                 "clickable straight from the page.",

    "s8_kicker": "Ownership of the evidence",
    "s8_title": "Who can answer what",
    "s8_headers": ["Question", "Who can answer", "Status"],
    "s8_rows": [
        ["Complete registration / service-principal inventory",
         "Croesus", "OPEN \u2014 Q8, the primary ask"],
        ["Origin header on the server-side /token call",
         "Croesus only \u2014 their outbound request", "Open \u2014 Q7"],
        ["Which auth library, and any .NET uplift on the roadmap",
         "Croesus", "Open \u2014 Q13"],
        ["How Prod and non-prod scope CA for Central",
         "Desjardins \u2014 own policy inventory", "In progress \u2014 highest value"],
        ["Origin header on a browser-side /token call",
         "Desjardins \u2014 browser DevTools", "Ruled out \u2014 no /token in the HAR"],
    ],
    "s8_link": "Details: README \u2014 Who can capture the /token Origin header \u2192",
    "s8_notes": "The raw Origin header is never a field in the tenant sign-in "
                "logs. It exists only on the HTTP request the caller sends to Entra.",

    "s9_kicker": "What we need from Croesus",
    "s9_title": "The asks: Q8 first, then Q7",
    "s9_q7_head": "Q8 \u2014 complete registration inventory",
    "s9_q7": [
        ("Every app registration and service principal GPD Central uses, across "
         "environments.", True),
        ("Including any confidential web/API registration that is not among the "
         "three spa exports we hold.", False),
        ("One answer separates the two surviving explanations. This is the "
         "fastest route to a supported configuration.", True),
    ],
    "s9_q8_head": "Q7 \u2014 one redacted /token capture",
    "s9_q8": [
        ("For one successful and one failing transaction:", True),
        ("Is an Origin header present?  \u2022  grant_type  \u2022  client_id  "
         "\u2022  redirect URI  \u2022  client-authentication method", False),
        ("Presence indicators or SHA-256 hashes only. Never raw codes, tokens, "
         "secrets, or assertions.", True),
    ],
    "s9_also": "Also open: Q15 UI and BFF evidence  \u2022  Q13 auth library and .NET roadmap  \u2022  Q10 sizing "
               "of the change  \u2022  Q12 route Entra calls over the existing "
               "site-to-site VPN  \u2022  Q4 AWS egress ranges  \u2022  Q9 does "
               "Conseiller share anything with Central?",
    "s9_link": "\u25b6  Full question list Q1\u2013Q15 on GitHub \u2192",
    "s9_notes": "Stress the redaction rule up front \u2014 it is what makes Q7 "
                "answerable by a security-conscious vendor. Q8 leads because it "
                "can end the ambiguity in a single reply.",

    "s10_kicker": "Nine routes, ranked",
    "s10_title": "Remediation ladder",
    "s10_bullets": [
        [("Check tenant parity first \u2014 Desjardins alone.", True),
         (" Compare how Prod and non-prod scope Conditional Access for Central. "
          "No vendor dependency, and it may be the whole fix.", False)],
        [("Route Entra calls over the existing site-to-site VPN (Q12).", True),
         (" The /token request then egresses from a Desjardins-owned address. "
          "No Croesus code change at all \u2014 the strongest short-term lever.", False)],
        [("Correct the registration shape \u2014 spa \u2192 web.", True),
         (" Staged: a client secret proves it with no library and no framework "
          "uplift; a certificate hardens it later. Per-tenant, so Desjardins can "
          "move first.", False)],
        [("Rule out device trust as the lever.", True),
         (" Cross-tenant \u2018Trust compliant devices\u2019 only fires on a B2B "
          "guest sign-in. Workstations join one tenant \u2014 Prod \u2014 so in "
          "non-prod \u2018require compliant device\u2019 is unsatisfiable, not "
          "merely strict.", False)],
        [("A named location is a condition, not a grant.", True),
         (" Grant controls combine with AND, so a trusted location does not "
          "satisfy a compliant-device requirement. On-Behalf-Of remains an "
          "option, not a requirement.", False)],
    ],
    "s10_notes": "The point: every route is small and reversible. Nobody needs a "
                 "re-architecture and Desjardins does not need to weaken CA. On "
                 "device trust \u2014 credit Mathieu Santerre for the correction: "
                 "the inbound trust settings honour a device claim carried in a "
                 "guest's token from their home tenant, so they cannot bridge "
                 "Prod device state into a native non-prod sign-in. Do not use "
                 "the .NET 4.5.2 support status as leverage \u2014 raise it "
                 "separately through vendor risk review.",

    "s11_kicker": "Reference implementation",
    "s11_title": "The working demo",
    "s11_bullets": [
        [("A live SPA + API in a Microsoft demo tenant that makes the flow shapes "
          "observable \u2014 nothing runs against Desjardins or Croesus tenants.",
          False)],
        [("Wrong vs right:", True),
         (" a token replay to Microsoft Graph is rejected on audience, while a "
          "real On-Behalf-Of exchange issues a distinct, separately audienced "
          "token.", False)],
                [("The registration fork:", True),
                 (" a panel separates UI topology from the redeemer decision, with "
                    "direct links to Q8, Q7, and Q15.", False)],
        [("Token inspector and evidence panel:", True),
         (" decoded claims show audience binding rather than a reused bearer. The "
          "raw-token inspector is gated off by default \u2014 we ask the vendor "
          "for hashes, so we do not demo copying live tokens.", False)],
        [("Tier 2 exhibits (gated):", True),
         (" a server-side replay control, and a report-only Token Protection "
          "policy that reproduces the real 1008 signal on a supported resource.",
          False)],
    ],
    "s11_links": [
        ("\u25b6  Open the demo \u2014 croesus-spa.azurewebsites.net", DEMO_APP),
        ("\u25b6  Demo walkthrough guide", DEMO_GUIDE),
        ("\u25b6  Evidence narrative (Q\u2011by\u2011Q mapping)", NARRATIVE),
        ("\u25b6  Evidence KQL queries", KQL),
    ],
    "s11_notes": "Demo order: sign in, Call API, show the evidence panel, then the "
                 "replay button, then scroll to the registration-fork panel and "
                 "click through to Q7/Q8/Q15.",

    "s12_kicker": "Leaving this session",
    "s12_title": "Next steps and owners",
    "s12_headers": ["#", "Action", "Owner", "Closes"],
    "s12_rows": [
        ["1", "Compare Conditional Access scoping for Central across Prod and "
              "non-prod", "Desjardins", "May be the whole fix \u2014 no vendor wait"],
        ["2", "Provide the complete registration and service-principal inventory",
         "Croesus", "Q8 \u2014 primary"],
        ["3", "Provide one redacted /token capture (hashes / presence only)",
         "Croesus", "Q7"],
        ["4", "Provide UI-topology and BFF-boundary evidence", "Croesus", "Q15"],
        ["5", "Confirm the auth library and any .NET Framework uplift plan",
         "Croesus", "Q13"],
        ["6", "Assess routing Entra calls over the existing site-to-site VPN",
         "Joint", "Q12 \u2014 fastest unblock"],
    ],
    "s12_guardrail": "Guardrail until then: do not change Conditional Access, do "
                     "not allowlist the AWS egress IPs, do not mandate "
                     "On-Behalf-Of, and do not generalise any of this to "
                     "Conseiller.",
    "s12_notes": "Close by restating the guardrail. It protects both parties: no "
                 "premature policy loosening, no premature vendor "
                 "re-architecture. Item 1 is ours and needs nobody's permission.",

    "s13_kicker": "Everything referenced in this deck",
    "s13_title": "Links",
    "s13_links": [
        ("Session findings and all nine routes \u2014 START HERE", FINDINGS),
        ("Routes and workarounds (R1\u2013R9)", ROUTES),
        ("Question list Q1\u2013Q15 (escalation packet)", QUESTIONS),
        ("Escalation packet \u2014 full document", PACKET),
        ("Full analysis \u2014 README", README),
        ("Who can capture the /token Origin header", ORIGIN_SECTION),
        ("Evidence narrative \u2014 question-by-question mapping", NARRATIVE),
        ("On-Behalf-Of demo guide", DEMO_GUIDE),
        ("Evidence KQL queries", KQL),
        ("Live demo application", DEMO_APP),
    ],
    "s13_notes": "Share the deck itself \u2014 every link is clickable in "
                 "slideshow mode.",
}

CONTENT["fr"] = {
    "out": "croesus-session-deck-fr.pptx",
    "title_kicker": "DESJARDINS \u2014 CROESUS \u2014 MICROSOFT  |  "
                    "S\u00c9ANCE DE TRAVAIL CONJOINTE",
    "title_main": "SSO GPD Central : ce que la preuve d\u00e9montre,\n"
                  "et les voies qui r\u00e8glent le dossier",
    "title_sub": "Inscriptions d'applications Entra ID, acc\u00e8s conditionnel "
                 "et l'\u00e9change du code sur /token c\u00f4t\u00e9 serveur  |  "
                 "Central seulement \u2014 pas Conseiller",
    "title_date": "5 ao\u00fbt 2026  \u2014  mis \u00e0 jour apr\u00e8s la "
                  "s\u00e9ance tripartite",
    "title_links": [
        ("\u25b6  Constats de la s\u00e9ance et les neuf voies", FINDINGS),
        ("\u25b6  Liste des questions (Q1\u2013Q15) sur GitHub", QUESTIONS),
        ("\u25b6  D\u00e9mo en direct \u2014 croesus-spa.azurewebsites.net", DEMO_APP),
    ],
    "title_notes": "Cadrage : il s'agit d'une s\u00e9ance conjointe "
                   "d'\u00e9tablissement des faits, non d'un rapport de d\u00e9faut. "
                   "Le blocage d'acc\u00e8s conditionnel est correct par conception. "
                   "La port\u00e9e se limite \u00e0 GPD Central \u2014 Conseiller "
                   "est un produit distinct et une \u00e9valuation distincte. "
                   "Le dire d'embl\u00e9e.",

    "s2_kicker": "Pourquoi nous sommes ici",
    "s2_title": "La question pos\u00e9e",
    "s2_bullets": [
        [("Les utilisateurs de Desjardins se connectent \u00e0 GPD Central. Une ",
          False), ("deuxi\u00e8me connexion, non interactive", True),
         (", provient ensuite du serveur dorsal Croesus h\u00e9berg\u00e9 sur AWS "
          "et se voit ", False),
         ("bloqu\u00e9e par l'acc\u00e8s conditionnel", True),
         (" en pr\u00e9production.", False)],
        [("Une seule question : est-ce un comportement OAuth attendu, ou une "
          "mauvaise configuration ?", True)],
        [("La r\u00e9ponse d\u00e9termine le correctif \u2014 un ", False),
         ("changement interne chez Desjardins", True), (" ou un ", False),
         ("changement chez le fournisseur, Croesus", True), (".", False)],
        [("Personne n'est en faute \u00e0 ce stade. Le blocage est un contr\u00f4le "
          "Zero Trust qui fonctionne comme pr\u00e9vu ; l'inconnue est la nature "
          "exacte de l'appel bloqu\u00e9.", False)],
    ],
    "s2_notes": "Garder un ton collaboratif. Dire clairement que le blocage n'est "
                "ni une mauvaise configuration de Desjardins, ni une accusation "
                "envers Croesus.",

    "s3_kicker": "O\u00f9 nous en sommes",
    "s3_title": "L'essentiel",
    "s3_bullets": [
                [("La topologie d'interface de Central reste ", False),
                 ("non r\u00e9solue", True),
                 (" : SPA, Web Forms multipage et hybride demeurent possibles. Une URL, "
                    "les chemins .aspx et PKCE ne la classent pas.", False)],
                [("SPA et BFF sont compatibles. ", True),
                 ("Le BFF reste provisoire jusqu'\u00e0 ce que Q15 confirme la garde des "
                    "jetons, le t\u00e9moin de session et la m\u00e9diation du serveur.", False)],
                [("Le type d'inscription suit le ", False),
                 ("composant qui \u00e9change le code", True),
                 (". Si le m\u00eame serveur \u00e9change et conserve les jetons, il est un "
                    "client confidentiel \u00ab web \u00bb. Un client public distinct peut "
                    "l\u00e9gitimement rester \u00ab spa \u00bb.", False)],
        [("L'\u00e9cart production / pr\u00e9production reste ", False),
         ("inexpliqu\u00e9", True),
         (" \u2014 la requ\u00eate bloqu\u00e9e ne porte aucun contexte d'appareil "
          "dans l'un ni l'autre locataire. Desjardins peut trancher seul, cette "
          "semaine.", False)],
        [("Le blocage d'acc\u00e8s conditionnel est ", False),
         ("correct par conception", True),
         (". La mention \u00ab Unbound (1008) \u00bb est un \u00e9tat de liaison, "
          "non une preuve de rejeu ni un indicateur du type d'octroi.", False)],
    ],
    "s3_notes": "Cette diapositive est le r\u00e9sum\u00e9 ex\u00e9cutif. Si la "
                "s\u00e9ance s'arr\u00eate apr\u00e8s cinq minutes, ce sont ces "
                "cinq lignes qui doivent passer. Port\u00e9e : Central seulement.",

    "s4_kicker": "Rigueur",
    "s4_title": "Ce que la preuve d\u00e9montre \u2014 et ce qu'elle ne "
                "d\u00e9montre pas",
    "s4_headers": ["Observation", "Ce que cela d\u00e9montre",
                   "Ce que cela ne d\u00e9montre PAS"],
    "s4_rows": [
        ["Appels \u00e0 /oauth2/v2.0/token",
         "Un jeton est demand\u00e9 au point de terminaison standard",
         "Pas d'OBO. L'\u00e9change du code et le renouvellement utilisent le "
         "m\u00eame point de terminaison"],
        ["\u00c9tat de connexion \u00ab Unbound (1008) \u00bb",
         "Le client n'est pas int\u00e9gr\u00e9 au courtier de la plateforme",
         "Pas un rejeu de jeton. Pas un type d'octroi. Hors port\u00e9e pour "
         "navigateur\u2192Graph"],
        ["L'acc\u00e8s conditionnel a bloqu\u00e9 l'appel",
         "La requ\u00eate n'avait aucun contexte d'appareil conforme, depuis une "
         "IP non approuv\u00e9e",
         "Rien sur le type d'octroi utilis\u00e9 \u2014 l'acc\u00e8s conditionnel "
         "y est indiff\u00e9rent"],
        ["Les trois inscriptions export\u00e9es sont \u00ab spa \u00bb",
         "Aucun secret, aucun certificat, aucune \u00e9tendue d'API expos\u00e9e",
         "Pas l'inventaire complet \u2014 d'autres inscriptions peuvent exister (Q8)"],
    ],
    "s4_notes": "C'est la diapositive de cr\u00e9dibilit\u00e9. Montrer ce que nous "
                "refusons de sur-interpr\u00e9ter donne le droit de formuler la "
                "demande de la diapositive 9.",

    "s5_kicker": "\u00c9tape 1 \u2014 faite",
    "s5_title": "Nouvelle preuve : la trace du navigateur",
    "s5_lede": "Un utilisateur de Desjardins a captur\u00e9 un fichier HAR d'une "
               "v\u00e9ritable connexion \u00e0 Central.",
    "s5_present_head": "Pr\u00e9sent dans la trace",
    "s5_present": [
        ("/oauth2/v2.0/authorize", True),
        ("Le volet frontal s'ex\u00e9cute dans le navigateur de l'utilisateur, "
         "comme pr\u00e9vu.", False),
    ],
    "s5_absent_head": "Absent de la trace",
    "s5_absent": [
        ("/oauth2/v2.0/token \u2014 aucune occurrence", True),
        ("Le code quitte le navigateur, mais il est \u00e9chang\u00e9 ailleurs.",
         False),
    ],
    "s5_conclusion": "Conclusion : /token n'appara\u00eet pas dans cette transaction "
                     "navigateur. Cela concorde avec le t\u00e9moignage d'Olivier, "
                     "sans identifier ind\u00e9pendamment le composant qui l'\u00e9change.",
    "s5_consequence": "Cons\u00e9quence : ce HAR ne classe pas l'interface et ne prouve "
                      "pas la fronti\u00e8re BFF. Q7, Q8 et Q15 demandent la preuve "
                      "manquante. R\u00e9serve : un seul \u00e9chantillon, un environnement.",
    "s5_notes": "Cr\u00e9diter Mathieu pour cette capture. Souligner qu'elle "
                "confirme ce que Croesus a affirm\u00e9 \u2014 nous validons le "
                "fournisseur, nous ne le contredisons pas.",

    "s6_kicker": "Si l'\u00e9change serveur est confirm\u00e9",
    "s6_title": "Les deux explications restantes",
    "s6_intro": "Entra rejette l'\u00e9change c\u00f4t\u00e9 serveur d'un code "
                "d'autorisation \u00ab spa \u00bb avec l'erreur AADSTS9002327. "
                "Si le m\u00eame serveur \u00e9change et conserve les jetons, le "
                "succ\u00e8s en production laisse deux explications.",
    "s6_a_head": "1 \u2014 Un en-t\u00eate Origin synth\u00e9tis\u00e9",
    "s6_a": [
        ("Un POST de serveur \u00e0 serveur n'envoie aucun Origin de lui-m\u00eame.",
         True),
        ("S'il y en a un, c'est le code applicatif qui l'ajoute \u2014 et un code "
         "de type \u00ab spa \u00bb survit \u00e0 une r\u00e8gle qu'Entra a "
         "\u00e9crite pour le refuser.", False),
        ("Correctif : inscrire Central comme client confidentiel \u00ab web \u00bb. "
         "Le prouver avec un secret client \u2014 sans biblioth\u00e8que ni "
         "mont\u00e9e de version \u2014 puis durcir avec un certificat.", True),
    ],
    "s6_b_head": "2 \u2014 Une inscription \u00ab web \u00bb non divulgu\u00e9e",
    "s6_b": [
        ("Le serveur dorsal s'authentifie hors de nos trois exports.", True),
        ("La forme est alors d\u00e9j\u00e0 correcte, et elle ne nous a "
         "simplement jamais \u00e9t\u00e9 montr\u00e9e.", False),
        ("Correctif : Croesus divulgue l'inventaire complet (Q8) ; toute "
         "accommodation y est circonscrite.", True),
    ],
    "s6_caveat": "Par ailleurs, et toujours ouvert : la requ\u00eate "
                 "bloqu\u00e9e ne porte aucun contexte d'appareil dans l'UN NI "
                 "L'AUTRE locataire, et pourtant la production passe. C'est \u00e0 "
                 "Desjardins de trancher, \u00e0 partir de son propre inventaire "
                 "de strat\u00e9gies et de ses journaux de connexion.",
    "s6_notes": "Diapositive centrale. Aucune des deux explications n'est une "
                "crise et chacune appelle une rem\u00e9diation modeste. Q8 les "
                "s\u00e9pare en une seule r\u00e9ponse.",

    "s7_kicker": "Contexte",
    "s7_title": "Pourquoi la forme de l'inscription compte",
    "s7_headers": ["", "spa (client public)", "web (client confidentiel)"],
    "s7_rows": [
        ["D\u00e9crit", "Un client public de navigateur",
         "Un client serveur confidentiel"],
        ["Justificatif", "Aucun \u2014 ni secret, ni certificat",
         "Secret ou certificat requis"],
        ["\u00c9change d'un code",
         "Uniquement depuis une requ\u00eate de navigateur cross-origin "
         "(en-t\u00eate Origin)",
         "Depuis un serveur, sans en-t\u00eate Origin"],
        ["\u00c9change c\u00f4t\u00e9 serveur", "Rejet\u00e9 : AADSTS9002327",
         "Pris en charge"],
        ["Dur\u00e9e des jetons d'actualisation",
         "Plafonn\u00e9e \u00e0 24 heures, non glissante", "Sans ce plafond"],
        ["Preuve d\u00e9cisive", "Le navigateur \u00e9change et garde les jetons",
         "Le serveur \u00e9change et garde les jetons"],
    ],
    "s7_quote": "\u00ab Les jetons \u00e9mis pour le type de client "
                "\u2018Single-Page Application\u2019 ne peuvent \u00eatre "
                "\u00e9chang\u00e9s que par des requ\u00eates cross-origin. \u00bb  "
                "\u2014 AADSTS9002327",
    "s7_notes": "Formuler le point techniquement, sans accusation : c'est une "
                "r\u00e8gle de la plateforme, pas une opinion. Et la premi\u00e8re "
                "\u00e9tape du correctif est peu co\u00fbteuse.",

    "s7b_kicker": "Capture d'\u00e9cran",
    "s7b_title": "La m\u00eame bifurcation d'architecture, dans la d\u00e9mo",
    "s7b_link": "\u25b6  L'ouvrir en direct \u2014 croesus-spa.azurewebsites.net",
    "s7b_notes": "Le panneau s'affiche sans connexion : il peut donc \u00eatre "
                 "pr\u00e9sent\u00e9 en partage d'\u00e9cran. Q8, Q7, Q15 et la liste "
                 "compl\u00e8te des questions sont cliquables directement depuis "
                 "la page. L'interface de la d\u00e9mo est en anglais.",

    "s8_kicker": "\u00c0 qui appartient la preuve",
    "s8_title": "Qui peut r\u00e9pondre \u00e0 quoi",
    "s8_headers": ["Question", "Qui peut r\u00e9pondre", "\u00c9tat"],
    "s8_rows": [
        ["Inventaire complet des inscriptions et principaux de service",
         "Croesus", "OUVERT \u2014 Q8, demande prioritaire"],
        ["En-t\u00eate Origin sur l'appel /token c\u00f4t\u00e9 serveur",
         "Croesus seulement \u2014 sa requ\u00eate sortante", "Ouvert \u2014 Q7"],
        ["Quelle biblioth\u00e8que d'authentification, et mont\u00e9e .NET "
         "pr\u00e9vue ?", "Croesus", "Ouvert \u2014 Q13"],
        ["Port\u00e9e de l'acc\u00e8s conditionnel pour Central : prod vs "
         "pr\u00e9prod", "Desjardins \u2014 son inventaire de strat\u00e9gies",
         "En cours \u2014 valeur la plus \u00e9lev\u00e9e"],
        ["En-t\u00eate Origin sur un appel /token c\u00f4t\u00e9 navigateur",
         "Desjardins \u2014 outils de d\u00e9veloppement du navigateur",
         "\u00c9cart\u00e9 \u2014 aucun /token dans le HAR"],
    ],
    "s8_link": "D\u00e9tails : README \u2014 qui peut capturer l'en-t\u00eate "
               "Origin de /token \u2192",
    "s8_notes": "L'en-t\u00eate Origin brut n'est jamais un champ des journaux de "
                "connexion du locataire. Il n'existe que sur la requ\u00eate HTTP "
                "envoy\u00e9e \u00e0 Entra par l'appelant.",

    "s9_kicker": "Ce que nous demandons \u00e0 Croesus",
    "s9_title": "Les demandes : Q8 d'abord, puis Q7",
    "s9_q7_head": "Q8 \u2014 inventaire complet des inscriptions",
    "s9_q7": [
        ("Chaque inscription d'application et principal de service utilis\u00e9 par "
         "GPD Central, tous environnements confondus.", True),
        ("Y compris toute inscription confidentielle web/API absente des trois "
         "exports \u00ab spa \u00bb que nous d\u00e9tenons.", False),
        ("Une seule r\u00e9ponse s\u00e9pare les deux explications restantes. "
         "C'est la voie la plus rapide vers une configuration prise en charge.",
         True),
    ],
    "s9_q8_head": "Q7 \u2014 une capture /token caviard\u00e9e",
    "s9_q8": [
        ("Pour une transaction r\u00e9ussie et une transaction en \u00e9chec :", True),
        ("Un en-t\u00eate Origin est-il pr\u00e9sent ?  \u2022  grant_type  "
         "\u2022  client_id  \u2022  URI de redirection  \u2022  m\u00e9thode "
         "d'authentification du client", False),
        ("Indicateurs de pr\u00e9sence ou empreintes SHA-256 uniquement. Jamais de "
         "codes, jetons, secrets ou assertions bruts.", True),
    ],
    "s9_also": "\u00c9galement ouvertes : Q15 preuve de l'interface et du BFF  \u2022  Q13 biblioth\u00e8que d'authentification "
               "et feuille de route .NET  \u2022  Q10 ampleur du changement  "
               "\u2022  Q12 acheminer les appels Entra par le VPN site \u00e0 site "
               "existant  \u2022  Q4 plages de sortie AWS  \u2022  Q9 Conseiller "
               "partage-t-il quoi que ce soit avec Central ?",
    "s9_link": "\u25b6  Liste compl\u00e8te des questions Q1\u2013Q15 sur GitHub \u2192",
    "s9_notes": "Insister d'embl\u00e9e sur la r\u00e8gle de caviardage \u2014 "
                "c'est ce qui rend Q7 r\u00e9pondable par un fournisseur soucieux "
                "de s\u00e9curit\u00e9. Q8 passe en t\u00eate car elle peut "
                "lever l'ambigu\u00eft\u00e9 en une seule r\u00e9ponse.",

    "s10_kicker": "Neuf voies, class\u00e9es",
    "s10_title": "\u00c9chelle de rem\u00e9diation",
    "s10_bullets": [
        [("V\u00e9rifier d'abord la parit\u00e9 des locataires \u2014 Desjardins "
          "seul.", True),
         (" Comparer la port\u00e9e de l'acc\u00e8s conditionnel appliqu\u00e9 "
          "\u00e0 Central en production et en pr\u00e9production. Aucune "
          "d\u00e9pendance au fournisseur, et cela pourrait tout r\u00e9gler.", False)],
        [("Acheminer les appels Entra par le VPN site \u00e0 site existant (Q12).",
          True),
         (" La requ\u00eate /token sort alors d'une adresse appartenant \u00e0 "
          "Desjardins. Aucun changement de code chez Croesus \u2014 le levier "
          "\u00e0 court terme le plus fort.", False)],
        [("Corriger la forme de l'inscription \u2014 spa \u2192 web.", True),
         (" Par \u00e9tapes : un secret client le prouve sans biblioth\u00e8que ni "
          "mont\u00e9e de version ; un certificat le durcit ensuite. Par "
          "locataire, donc Desjardins peut avancer en premier.", False)],
        [("\u00c9carter la confiance d'appareil comme levier.", True),
         (" \u00ab Approuver les appareils conformes \u00bb entre locataires ne "
          "s'applique qu'\u00e0 une connexion d'invit\u00e9 B2B. Les postes ne "
          "joignent qu'un seul locataire \u2014 la production \u2014 donc en "
          "pr\u00e9production \u00ab exiger un appareil conforme \u00bb est "
          "insatisfiable, et non simplement s\u00e9v\u00e8re.", False)],
        [("Un emplacement nomm\u00e9 est une condition, pas un octroi.", True),
         (" Les contr\u00f4les d'octroi se combinent avec ET : un emplacement "
          "approuv\u00e9 ne satisfait pas une exigence d'appareil conforme. "
          "On-Behalf-Of reste une option, pas une obligation.", False)],
    ],
    "s10_notes": "Le message : chaque voie est modeste et r\u00e9versible. "
                 "Personne n'a besoin d'une refonte, et Desjardins n'a pas "
                 "\u00e0 affaiblir son acc\u00e8s conditionnel. "
                 "Sur la confiance d'appareil \u2014 cr\u00e9diter Mathieu Santerre "
                 "pour la correction : les param\u00e8tres de confiance entrante "
                 "honorent une revendication d'appareil port\u00e9e par le jeton "
                 "d'un invit\u00e9 provenant de son locataire d'origine ; ils ne "
                 "peuvent donc pas transposer l'\u00e9tat d'appareil de Prod dans "
                 "une connexion native en pr\u00e9production.",

    "s11_kicker": "Impl\u00e9mentation de r\u00e9f\u00e9rence",
    "s11_title": "La d\u00e9mo fonctionnelle",
    "s11_bullets": [
        [("Une application monopage et une API en direct dans un locataire de "
          "d\u00e9monstration Microsoft, qui rendent les formes du flux "
          "observables \u2014 rien ne s'ex\u00e9cute contre les locataires de "
          "Desjardins ou de Croesus.", False)],
        [("Le mauvais et le bon :", True),
         (" un rejeu de jeton vers Microsoft Graph est rejet\u00e9 sur l'audience, "
          "tandis qu'un v\u00e9ritable \u00e9change On-Behalf-Of \u00e9met un "
          "jeton distinct, avec sa propre audience.", False)],
                [("La bifurcation d'inscription :", True),
                 (" un panneau s\u00e9pare la topologie d'interface de la d\u00e9cision du "
                    "composant d'\u00e9change, avec des liens vers Q8, Q7 et Q15.", False)],
        [("Inspecteur de jetons et panneau de preuve :", True),
         (" les revendications d\u00e9cod\u00e9es montrent une liaison d'audience "
          "plut\u00f4t qu'un jeton r\u00e9utilis\u00e9. L'inspecteur de jetons "
          "bruts est d\u00e9sactiv\u00e9 par d\u00e9faut \u2014 nous demandons des "
          "empreintes au fournisseur, nous ne faisons donc pas la "
          "d\u00e9monstration de la copie de jetons vivants.", False)],
        [("Pi\u00e8ces de niveau 2 (sous drapeau) :", True),
         (" un contr\u00f4le de rejeu c\u00f4t\u00e9 serveur, et une politique "
          "Token Protection en mode rapport seul qui reproduit le signal 1008 "
          "r\u00e9el sur une ressource prise en charge.", False)],
    ],
    "s11_links": [
        ("\u25b6  Ouvrir la d\u00e9mo \u2014 croesus-spa.azurewebsites.net", DEMO_APP),
        ("\u25b6  Guide de la d\u00e9monstration", DEMO_GUIDE),
        ("\u25b6  R\u00e9cit de la preuve (correspondance question par question)",
         NARRATIVE),
        ("\u25b6  Requ\u00eates KQL de preuve", KQL),
    ],
    "s11_notes": "Ordre de la d\u00e9mo : se connecter, appeler l'API, montrer le "
                 "panneau de preuve, puis le bouton de rejeu, puis faire "
                 "d\u00e9filer jusqu'au panneau de bifurcation d'inscription et "
                 "cliquer vers Q8/Q7/Q15.",

    "s12_kicker": "En sortant de cette s\u00e9ance",
    "s12_title": "Prochaines \u00e9tapes et responsables",
    "s12_headers": ["#", "Action", "Responsable", "Referme"],
    "s12_rows": [
        ["1", "Comparer la port\u00e9e de l'acc\u00e8s conditionnel pour Central "
              "entre production et pr\u00e9production",
         "Desjardins", "Peut tout r\u00e9gler \u2014 sans attendre le fournisseur"],
        ["2", "Fournir l'inventaire complet des inscriptions et principaux de "
              "service", "Croesus", "Q8 \u2014 prioritaire"],
        ["3", "Fournir une capture /token caviard\u00e9e (empreintes ou "
              "pr\u00e9sence seulement)", "Croesus", "Q7"],
          ["4", "Fournir la preuve de la topologie et de la fronti\u00e8re BFF",
           "Croesus", "Q15"],
          ["5", "Confirmer la biblioth\u00e8que d'authentification et toute "
              "mont\u00e9e de version .NET pr\u00e9vue", "Croesus", "Q13"],
          ["6", "\u00c9valuer l'acheminement des appels Entra par le VPN site "
              "\u00e0 site existant", "Conjoint", "Q12 \u2014 d\u00e9blocage le "
              "plus rapide"],
    ],
    "s12_guardrail": "Garde-fou d'ici l\u00e0 : ne pas modifier l'acc\u00e8s "
                     "conditionnel, ne pas mettre les IP de sortie AWS en liste "
                     "d'autorisation, ne pas imposer On-Behalf-Of, et ne rien "
                     "g\u00e9n\u00e9raliser \u00e0 Conseiller.",
    "s12_notes": "Conclure en redisant le garde-fou. Il prot\u00e8ge les deux "
                 "parties : aucun rel\u00e2chement pr\u00e9matur\u00e9 de "
                 "politique, aucune refonte pr\u00e9matur\u00e9e du c\u00f4t\u00e9 "
                 "du fournisseur. L'\u00e9l\u00e9ment 1 nous appartient et ne "
                 "d\u00e9pend de personne.",

    "s13_kicker": "Tout ce qui est r\u00e9f\u00e9renc\u00e9 dans cette "
                  "pr\u00e9sentation",
    "s13_title": "Liens",
    "s13_links": [
        ("Constats de la s\u00e9ance et les neuf voies \u2014 COMMENCER ICI", FINDINGS),
        ("Voies et contournements (R1\u2013R9)", ROUTES),
        ("Liste des questions Q1\u2013Q15 (dossier d'escalade)", QUESTIONS),
        ("Dossier d'escalade \u2014 document complet", PACKET),
        ("Analyse compl\u00e8te \u2014 README", README),
        ("Qui peut capturer l'en-t\u00eate Origin de /token", ORIGIN_SECTION),
        ("R\u00e9cit de la preuve \u2014 correspondance question par question",
         NARRATIVE),
        ("Guide de d\u00e9monstration On-Behalf-Of", DEMO_GUIDE),
        ("Requ\u00eates KQL de preuve", KQL),
        ("Application de d\u00e9monstration en direct", DEMO_APP),
    ],
    "s13_notes": "Partager la pr\u00e9sentation elle-m\u00eame \u2014 tous les "
                 "liens sont cliquables en mode diaporama.",
}


def french_spacing(node):
    """Applies the French no-break space before : ; ! ? and inside guillemets."""
    if isinstance(node, str):
        node = re.sub(r"\s+([:;!?\u00bb])", "\u00a0\\1", node)
        return re.sub(r"\u00ab\s+", "\u00ab\u00a0", node)
    if isinstance(node, list):
        return [french_spacing(v) for v in node]
    if isinstance(node, tuple):
        return tuple(french_spacing(v) for v in node)
    if isinstance(node, dict):
        return {k: french_spacing(v) for k, v in node.items()}
    return node


def build(lang):
    T = CONTENT[lang]
    if lang == "fr":
        T = {k: (v if k == "out" else french_spacing(v)) for k, v in T.items()}
    scale = SCALE[lang]

    prs = Presentation()
    prs.slide_width = Inches(13.333)
    prs.slide_height = Inches(7.5)

    def pt(size):
        return Pt(size * scale)

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
        run.font.size = pt(size)
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
        run.font.size = pt(size)
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
            p.space_after = Pt(gap * scale)
            parts = item if isinstance(item, list) else [(item, False)]
            marker = p.add_run()
            marker.text = "\u25aa  "
            marker.font.size = pt(size)
            marker.font.color.rgb = BLUE
            marker.font.name = "Segoe UI"
            for text, bold in parts:
                run = p.add_run()
                run.text = text
                run.font.size = pt(size)
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
            p.runs[0].font.size = pt(header_size)
            p.runs[0].font.bold = True
            p.runs[0].font.color.rgb = WHITE
            p.runs[0].font.name = "Segoe UI"
        for r, row in enumerate(rows, start=1):
            for c, value in enumerate(row):
                cell = table.cell(r, c)
                cell.text = value or " "
                cell.fill.solid()
                cell.fill.fore_color.rgb = (WHITE if r % 2
                                            else RGBColor(0xF2, 0xF6, 0xFA))
                cell.vertical_anchor = MSO_ANCHOR.MIDDLE
                p = cell.text_frame.paragraphs[0]
                p.runs[0].font.size = pt(size)
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
            run.font.size = pt(14)
            run.font.bold = bold
            run.font.color.rgb = TEXT
            run.font.name = "Segoe UI"

    def notes(slide, text):
        slide.notes_slide.notes_text_frame.text = text

    # ------------------------------------------------------------ SLIDE 1
    s = new_slide(WHITE)
    band = s.shapes.add_shape(1, Inches(0), Inches(0), Inches(13.333), Inches(2.9))
    band.fill.solid()
    band.fill.fore_color.rgb = BLUE_DEEP
    band.line.fill.background()
    band.shadow.inherit = False
    add_text(s, T["title_kicker"], 0.8, 0.85, 11.7, 0.4, size=13, bold=True,
             color=RGBColor(0x7F, 0xC4, 0xFF))
    add_text(s, T["title_main"], 0.8, 1.25, 11.7, 1.4, size=34, bold=True,
             color=WHITE)
    add_text(s, T["title_sub"], 0.8, 3.25, 11.7, 0.5, size=18, color=MUTED)
    add_text(s, T["title_date"], 0.8, 3.85, 5, 0.4, size=15, color=MUTED)
    for i, (label, url) in enumerate(T["title_links"]):
        add_link(s, label, url, 0.8, 4.6 + i * 0.5, 6.5, 0.4, size=16, bold=True)
    notes(s, T["title_notes"])

    # ------------------------------------------------------------ SLIDE 2
    s = new_slide()
    title(s, T["s2_title"], T["s2_kicker"])
    bullets(s, T["s2_bullets"], y=2.15, size=19, gap=16)
    notes(s, T["s2_notes"])

    # ------------------------------------------------------------ SLIDE 3
    s = new_slide()
    title(s, T["s3_title"], T["s3_kicker"])
    bullets(s, T["s3_bullets"], y=2.1, size=17, gap=14)
    notes(s, T["s3_notes"])

    # ------------------------------------------------------------ SLIDE 4
    s = new_slide()
    title(s, T["s4_title"], T["s4_kicker"])
    add_table(s, T["s4_headers"], T["s4_rows"], x=0.75, y=2.1, w=11.85,
              col_widths=[3.0, 4.2, 4.65], size=13, row_h=0.95)
    notes(s, T["s4_notes"])

    # ------------------------------------------------------------ SLIDE 5
    s = new_slide()
    title(s, T["s5_title"], T["s5_kicker"])
    add_text(s, T["s5_lede"], 0.75, 2.0, 11.8, 0.4, size=18, color=MUTED)
    card(s, 0.75, 2.55, 5.75, 2.3, T["s5_present_head"], T["s5_present"], GREEN)
    card(s, 6.85, 2.55, 5.75, 2.3, T["s5_absent_head"], T["s5_absent"], RED)
    add_text(s, T["s5_conclusion"], 0.75, 5.1, 11.8, 0.6, size=19, bold=True,
             color=BLUE_DEEP)
    add_text(s, T["s5_consequence"], 0.75, 5.75, 11.8, 0.8, size=15, color=MUTED)
    notes(s, T["s5_notes"])

    # ------------------------------------------------------------ SLIDE 6
    s = new_slide()
    title(s, T["s6_title"], T["s6_kicker"])
    add_text(s, T["s6_intro"], 0.75, 2.0, 11.8, 0.7, size=16, color=MUTED)
    card(s, 0.75, 2.9, 5.75, 3.1, T["s6_a_head"], T["s6_a"], RED)
    card(s, 6.85, 2.9, 5.75, 3.1, T["s6_b_head"], T["s6_b"], AMBER)
    add_text(s, T["s6_caveat"], 0.75, 6.2, 11.8, 0.5, size=14, italic=True,
             color=MUTED)
    notes(s, T["s6_notes"])

    # ------------------------------------------------------------ SLIDE 7
    s = new_slide()
    title(s, T["s7_title"], T["s7_kicker"])
    add_table(s, T["s7_headers"], T["s7_rows"], x=0.75, y=2.1, w=11.85,
              col_widths=[2.7, 4.6, 4.55], size=14, row_h=0.72)
    add_text(s, T["s7_quote"], 0.75, 6.15, 11.8, 0.6, size=15, italic=True,
             color=BLUE_DEEP)
    notes(s, T["s7_notes"])

    # -------------------------------------------------------- SLIDE 7-BIS
    if FORK_PNG.exists():
        s = new_slide(WHITE)
        title(s, T["s7b_title"], T["s7b_kicker"])
        pic = s.shapes.add_picture(str(FORK_PNG), Inches(3.55), Inches(1.95),
                                   height=Inches(5.1))
        pic.left = Inches((13.333 - pic.width.inches) / 2)
        add_link(s, T["s7b_link"], DEMO_APP, 0.75, 7.02, 7.0, 0.35, size=13,
                 bold=True)
        notes(s, T["s7b_notes"])

    # ------------------------------------------------------------ SLIDE 8
    s = new_slide()
    title(s, T["s8_title"], T["s8_kicker"])
    add_table(s, T["s8_headers"], T["s8_rows"], x=0.75, y=2.1, w=11.85,
              col_widths=[5.0, 3.6, 3.25], size=13, row_h=0.72)
    add_link(s, T["s8_link"], ORIGIN_SECTION, 0.75, 6.25, 9, 0.4, size=14,
             bold=True)
    notes(s, T["s8_notes"])

    # ------------------------------------------------------------ SLIDE 9
    s = new_slide()
    title(s, T["s9_title"], T["s9_kicker"])
    card(s, 0.75, 2.05, 5.75, 3.4, T["s9_q7_head"], T["s9_q7"], BLUE)
    card(s, 6.85, 2.05, 5.75, 3.4, T["s9_q8_head"], T["s9_q8"], BLUE)
    add_text(s, T["s9_also"], 0.75, 5.65, 11.8, 0.5, size=15, color=MUTED)
    add_link(s, T["s9_link"], QUESTIONS, 0.75, 6.25, 9, 0.45, size=17, bold=True)
    notes(s, T["s9_notes"])

    # ----------------------------------------------------------- SLIDE 10
    s = new_slide()
    title(s, T["s10_title"], T["s10_kicker"])
    bullets(s, T["s10_bullets"], y=2.1, size=17, gap=14)
    notes(s, T["s10_notes"])

    # ----------------------------------------------------------- SLIDE 11
    s = new_slide()
    title(s, T["s11_title"], T["s11_kicker"])
    bullets(s, T["s11_bullets"], y=2.05, size=16, gap=12)
    positions = [(0.75, 5.95, 6.5), (0.75, 6.4, 6.5), (7.4, 5.95, 5.2),
                 (7.4, 6.4, 5.2)]
    for (label, url), (lx, ly, lw) in zip(T["s11_links"], positions):
        add_link(s, label, url, lx, ly, lw, 0.4, size=16, bold=True)
    notes(s, T["s11_notes"])

    # ----------------------------------------------------------- SLIDE 12
    s = new_slide()
    title(s, T["s12_title"], T["s12_kicker"])
    add_table(s, T["s12_headers"], T["s12_rows"], x=0.75, y=2.1, w=11.85,
              col_widths=[0.6, 5.4, 1.9, 3.95], size=13, row_h=0.72)
    add_text(s, T["s12_guardrail"], 0.75, 6.2, 11.8, 0.6, size=17, bold=True,
             color=RED)
    notes(s, T["s12_notes"])

    # ----------------------------------------------------------- SLIDE 13
    s = new_slide(WHITE)
    title(s, T["s13_title"], T["s13_kicker"])
    for i, (label, url) in enumerate(T["s13_links"]):
        add_link(s, f"\u25b6  {label}", url, 0.85, 2.05 + i * 0.5, 11.5, 0.42,
                 size=16, bold=True)
    notes(s, T["s13_notes"])

    out = HERE / CONTENT[lang]["out"]
    out.parent.mkdir(parents=True, exist_ok=True)
    prs.save(out)
    return out, sum(1 for _ in prs.slides)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lang", choices=sorted(CONTENT), action="append",
                        help="Language to generate; repeatable. Default: all.")
    args = parser.parse_args()
    for language in args.lang or sorted(CONTENT):
        path, count = build(language)
        print(f"[{language}] Wrote {path} ({count} slides)")
