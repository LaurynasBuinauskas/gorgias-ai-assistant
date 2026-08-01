"""Strip personal data from ticket text before anything is embedded.

This is the only control between customer personal data and the search index. The client
chose to redact and retain rather than set a retention window, which is defensible *provided
the redaction holds* — if it leaks there is nothing behind it. So this module is built to be
tested in isolation, and `residual_identifiers` deliberately re-runs detection over already
redacted text so a batch can be refused rather than trusted.

Two design choices worth knowing:

**Names are matched, not detected.** The ticket object already tells us the customer's and
agent's names. Replacing those exact strings is far more reliable than trying to recognise
arbitrary names in free text, and it degrades gracefully — an unknown name in a signature
block is caught by the signature rules instead.

**Redaction is deliberately narrow.** Exemplars are wanted for their *shape* — how a refund
refusal is phrased — so over-redacting destroys the thing being collected. "30 days" and
"€45.00" stay; a street address and an order number do not.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

# Digits written as words, which pattern matching over numerals misses entirely.
NUMBER_WORDS = (
    "zero|one|two|three|four|five|six|seven|eight|nine|oh|nought|double|triple"
)

# Street types by market. Two word orders exist and both must be covered: "14 Alderney
# Street" puts the type last, "5 Rue Lafayette" puts it in the middle, and German and Dutch
# compound it onto the name ("Hauptstrasse 12", "Keizersgracht 210").
STREET_TYPES = (
    r"street|st\.?|road|rd\.?|avenue|ave\.?|lane|ln\.?|drive|dr\.?|way|close|court|ct\.?|"
    r"boulevard|blvd\.?|place|pl\.?|terrace|square|sq\.?|"
    r"stra(?:ss|ß)e|str\.?|platz|weg|gasse|allee|ring|damm|ufer|"
    r"rue|chemin|impasse|quai|"
    r"via|viale|piazza|corso|largo|"
    r"calle|avenida|plaza|paseo|carrer|"
    r"straat|laan|plein|gracht|kade|singel|dijk|hof|park|"
    r"gatan|vägen|väg|"
    r"ulica|aleja"
)

# Types that attach to the end of a compound street name rather than standing alone.
COMPOUND_STREET_TYPES = (
    r"stra(?:ss|ß)e|str|platz|weg|gasse|allee|ring|damm|ufer|"
    r"straat|laan|plein|gracht|kade|singel|dijk|hof|"
    r"gatan|vägen|väg"
)

PATTERNS: list[tuple[str, str, re.Pattern[str]]] = [
    # Order and tracking identifiers first: they contain digits that later rules would eat.
    ("ORDER", "[ORDER]", re.compile(
        r"#[A-Z]{2,3}#\d{3,7}"                      # the house format: #US#14532
        r"|\border\s*(?:number|no\.?|#|id)?\s*[:#]?\s*[A-Z]{0,3}#?\d{4,10}\b"
        r"|\b(?:bestellnummer|commande|pedido|ordine)\s*[:#]?\s*\d{4,10}\b",
        re.IGNORECASE)),
    ("TRACKING", "[TRACKING]", re.compile(
        r"\b(?:tracking|sendungsnummer|suivi|seguimiento)\s*(?:number|no\.?|code)?\s*[:#]?\s*"
        r"[A-Z0-9]{8,25}\b"
        r"|\b1Z[0-9A-Z]{16}\b"                      # UPS
        r"|\b\d{12,22}\b",                          # DHL/FedEx/GLS style long numerics
        re.IGNORECASE)),
    ("IBAN", "[IBAN]", re.compile(
        r"\b[A-Z]{2}\d{2}[ ]?(?:[A-Z0-9]{4}[ ]?){2,7}[A-Z0-9]{1,4}\b")),
    ("CARD", "[CARD]", re.compile(
        r"\b(?:\d{4}[ -]?){3}\d{4}\b")),
    ("EMAIL", "[EMAIL]", re.compile(
        r"\b[\w.+-]+@[\w-]+\.[\w.-]+\b")),
    ("PHONE", "[PHONE]", re.compile(
        r"(?<![\w#])\+?\d[\d\s().-]{7,17}\d(?![\w])")),
    ("PHONE", "[PHONE]", re.compile(
        # "zero seven nine one double two ..." — four or more number words in a row.
        rf"\b(?:(?:{NUMBER_WORDS})[\s,-]+){{3,}}(?:{NUMBER_WORDS})\b",
        re.IGNORECASE)),
    ("ADDRESS", "[ADDRESS]", re.compile(
        # "14 Alderney Street" — number, name, type.
        rf"\b\d{{1,5}}[a-z]?[,\s]+[\w'’.-]+(?:\s+[\w'’.-]+)?\s+(?:{STREET_TYPES})\b"
        # "5 Rue Lafayette", "12 Via Roma" — number, type, name.
        rf"|\b\d{{1,5}}[a-z]?[,\s]+(?:{STREET_TYPES})\s+[\w'’.-]+(?:\s+[\w'’.-]+)?\b"
        # "Hauptstrasse 12", "Keizersgracht 210" — compound name, then number.
        rf"|\b[\w'’.-]*(?:{COMPOUND_STREET_TYPES})\s+\d{{1,5}}[a-z]?\b",
        re.IGNORECASE)),
    ("POSTCODE", "[POSTCODE]", re.compile(
        r"\b[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}\b"    # UK
        r"|\b\d{5}(?:-\d{4})?\b"                    # US ZIP, DE, ES, IT, FR
        r"|\b\d{4}\s?[A-Z]{2}\b"                    # NL
        r"|\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b",           # CA
        re.IGNORECASE)),
]

PLACEHOLDER = re.compile(r"\[(?:CUSTOMER|AGENT|EMAIL|PHONE|ORDER|TRACKING|ADDRESS|POSTCODE|IBAN|CARD)\]")

# Lines below one of these are a signature block: whatever follows is identity, not content.
SIGNATURE_START = re.compile(
    r"^\s*(?:--+|__+|"
    r"(?:kind|best|warm)\s+regards|regards|sincerely|thanks(?:\s+again)?|thank you|cheers|"
    r"mit freundlichen gr[uü](?:ss|ß)en|viele gr[uü](?:ss|ß)e|"
    r"cordialement|bien à vous|"
    r"saludos|atentamente|"
    r"cordiali saluti|distinti saluti|"
    r"met vriendelijke groet(?:en)?)\s*[,.!]?\s*$",
    re.IGNORECASE)


@dataclass(frozen=True)
class Finding:
    kind: str
    value: str


def redact(text: str, known_names: list[str] | None = None) -> str:
    """Replace personal data with typed placeholders."""
    if not text:
        return text

    redacted = _redact_names(text, known_names or [])
    redacted = _redact_signature_blocks(redacted)

    for _, placeholder, pattern in PATTERNS:
        redacted = pattern.sub(placeholder, redacted)

    # Collapse runs the rules produced, e.g. "[ADDRESS], [POSTCODE] [POSTCODE]".
    return re.sub(r"(\[[A-Z]+\])(?:[\s,]+\1)+", r"\1", redacted)


def residual_identifiers(text: str) -> list[Finding]:
    """Re-detect over redacted text. Any finding means the batch must not be indexed.

    Run as a separate pass rather than folded into `redact` on purpose: this is the check that
    has to fail loudly, and a check that shares state with the thing it verifies is not a check.
    """
    if not text:
        return []

    # Placeholders are the expected output, not a leak.
    masked = PLACEHOLDER.sub("", text)

    findings: list[Finding] = []
    for kind, _, pattern in PATTERNS:
        for match in pattern.finditer(masked):
            findings.append(Finding(kind, match.group(0).strip()))
    return findings


def _redact_names(text: str, known_names: list[str]) -> str:
    """Replace known participant names, longest first so full names beat first names."""
    parts: list[tuple[str, str]] = []
    for entry in known_names:
        name, placeholder = entry if isinstance(entry, tuple) else (entry, "[CUSTOMER]")
        cleaned = (name or "").strip()
        if len(cleaned) < 2:
            continue
        parts.append((cleaned, placeholder))
        # Individual name parts, so "Hi Sammy," is caught as well as "Sammy Nguyen".
        parts.extend((piece, placeholder) for piece in cleaned.split() if len(piece) > 2)

    for value, placeholder in sorted(parts, key=lambda p: len(p[0]), reverse=True):
        text = re.sub(rf"\b{re.escape(value)}\b", placeholder, text, flags=re.IGNORECASE)
    return text


def _redact_signature_blocks(text: str) -> str:
    """Blank the tail of a signature block.

    Signatures are where identifiers hide in shapes no pattern anticipates — a job title, a
    company line, an office address split across three lines. Once a sign-off is seen, what
    follows is identity rather than content, so it is dropped wholesale.
    """
    lines = text.split("\n")
    for index, line in enumerate(lines):
        if not SIGNATURE_START.match(line):
            continue

        tail = lines[index + 1:]
        # A sign-off at the very end is just politeness; only drop a tail that has substance.
        if any(candidate.strip() for candidate in tail):
            return "\n".join([*lines[: index + 1], "[SIGNATURE]"])
    return text
