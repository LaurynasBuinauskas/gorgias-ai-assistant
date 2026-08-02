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
    # British estate-road types. "Cardigan Chase" reached the index because this list stopped
    # at the obvious half-dozen; residential streets in the UK rarely use those.
    r"chase|crescent|cres\.?|croft|mews|rise|grove|gardens|gdns\.?|green|walk|row|hill|"
    r"view|meadow|meadows|fields|wharf|quay|parade|broadway|circus|vale|dene|copse|spinney|"
    r"stra(?:ss|ß)e|str\.?|platz|weg|gasse|allee|ring|damm|ufer|"
    r"rue|chemin|impasse|quai|"
    r"via|viale|piazza|corso|largo|"
    r"calle|avenida|plaza|paseo|carrer|"
    r"straat|laan|plein|gracht|kade|singel|dijk|hof|park|"
    r"gatan|vägen|väg|"
    # Accented forms. "P. D. Løvs Allé 1" survived because the list held only "allee".
    r"all[ée]e?|"
    r"ulica|aleja"
)

# Lithuanian and Latvian write the street type as an abbreviation between name and number:
# "Garšvės g. 96C". Nothing in the lists above has that shape, so a 400-exchange review sample
# found one intact. Small markets sit in the tail — they are the last shapes to be sampled and
# the last to be covered.
ABBREVIATED_STREET = r"g|pr|al|iela|gatv[ėe]"

# Types that stand as their own word between the name and the number, as Nordic and Dutch
# addresses do. Kept separate from STREET_TYPES on purpose — that list contains ordinary
# English nouns, which in this shape would swallow product names.
NORDIC_STREET_TYPES = (
    r"all[ée]e?|gatan|gata|gate|vej|vei|väg|vägen|veien|plads|torv|torget|"
    r"straat|laan|plein|weg"
)

# Types that attach to the end of a compound street name rather than standing alone.
COMPOUND_STREET_TYPES = (
    r"stra(?:ss|ß)e|str|platz|weg|gasse|allee|ring|damm|ufer|"
    r"straat|laan|plein|gracht|kade|singel|dijk|hof|"
    r"gatan|vägen|väg"
)

# Ordering below is load-bearing and was got wrong once. Every numeric rule competes for the
# same digits, so the *most specific* must run first or a broader rule consumes its input and
# leaves the specific identifier standing. A human review of fifty indexed exchanges found
# "Holunderweg [PHONE] Essen": the phone rule had eaten "12 45143" out of the middle of an
# address, which stopped the address rule matching and left the street and city in the index.
# Addresses and postcodes therefore run before phone numbers, not after.
PATTERNS: list[tuple[str, str, re.Pattern[str]]] = [
    # Links first. They carry per-recipient tracking tokens that resolve back to one customer,
    # and their query strings are full of digits that every rule below would happily mangle.
    ("LINK", "[LINK]", re.compile(r"<?https?://\S+|\bwww\.[\w.-]+\.\w{2,}\S*", re.IGNORECASE)),
    # Order and tracking identifiers next: they contain digits that later rules would eat.
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
    # A person's name carrying their job title. Third parties named in corporate gift orders
    # — "Ben Majoe (Partner)" — are neither the ticket's customer nor its agent, so matching
    # known names never sees them. The title is what makes this safe to redact by shape: a
    # product name never arrives with "(Director)" after it.
    ("CUSTOMER", "[CUSTOMER]", re.compile(
        # The name must be capitalised; the title need not be — people write "Gemma Lloan,
        # manager" as readily as "(Director)". Hence the scoped case-insensitive group.
        r"\b[A-Z][\w'’-]{1,}(?:\s+[A-Z][\w'’-]{1,}){1,2}\s*"
        r"[(,]\s*(?i:Director|Manager|CEO|CFO|CTO|COO|President|Vice President|Partner|Founder"
        r"|Head of [\w\s]{2,20}|Chair(?:man|woman)?|Advis[eo]r|Principal)\b\)?"
        r"|\b[A-Z]{2,}(?:\s+[A-Z]{2,}){1,2}\s+"
        r"(?:DIRECTOR|MANAGER|CEO|PRESIDENT|PARTNER|FOUNDER|CHAIRMAN)\b")),
    ("ADDRESS", "[ADDRESS]", re.compile(
        # "14 Alderney Street" — number, name, type.
        rf"\b\d{{1,5}}[a-z]?[,\s]+[\w'’.-]+(?:\s+[\w'’.-]+)?\s+(?:{STREET_TYPES})\b"
        # "5 Rue Lafayette", "12 Via Roma" — number, type, name.
        rf"|\b\d{{1,5}}[a-z]?[,\s]+(?:{STREET_TYPES})\s+[\w'’.-]+(?:\s+[\w'’.-]+)?\b"
        # "Hauptstrasse 12", "Keizersgracht 210" — compound name, then number.
        rf"|\b[\w'’.-]*(?:{COMPOUND_STREET_TYPES})\s+\d{{1,5}}[a-z]?\b"
        # A compound street name with no number at all. Without this an address whose number
        # was already consumed — or simply written without one — leaves the street standing,
        # which with a city is enough to find a household.
        rf"|\b[\w'’.-]{{3,}}(?:{COMPOUND_STREET_TYPES})\b"
        # "Garšvės g. 96C" — name, abbreviated type, number. The number is required here: "g."
        # is two characters and would otherwise match half the corpus.
        rf"|\b[\w'’ėįųūžčšāēī.-]{{3,}}\s+(?:{ABBREVIATED_STREET})\.\s*\d{{1,5}}[a-z]?\b"
        # "Løvs Allé 1" — name, spaced type, number. Deliberately a narrower type list than
        # STREET_TYPES: that one holds English words like park, green, row and view, which in
        # this shape would take product names with them.
        rf"|\b[\w'’æøåäöüé.-]{{2,}}\s+(?:{NORDIC_STREET_TYPES})\s+\d{{1,5}}[a-z]?\b",
        re.IGNORECASE)),
    ("POSTCODE", "[POSTCODE]", re.compile(
        r"\b[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}\b"    # UK
        r"|\b\d{5}(?:-\d{4})?\b"                    # US ZIP, DE, ES, IT, FR
        r"|\b\d{4}\s?[A-Z]{2}\b"                    # NL
        # DK, AT, BE, CH, NO write four digits followed by the town: "2200 København". A bare
        # four-digit run is far too common to redact — it is a year, a price, an order count —
        # so the town is required as the anchor.
        # Same-line spacing only. `\s+` spans newlines, which made every four-digit number at
        # the end of a line match the greeting on the next one — "2019\nHello". A postcode is
        # never separated from its town by a line break.
        r"|\b\d{4}[ ]{1,2}[A-ZÆØÅÄÖÜÉ][\wæøåäöüé-]{2,}\b"
        r"|\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b",           # CA
        re.IGNORECASE)),
    ("PHONE", "[PHONE]", re.compile(
        r"(?<![\w#])\+?\d[\d\s().-]{7,17}\d(?![\w])")),
    ("PHONE", "[PHONE]", re.compile(
        # "zero seven nine one double two ..." — four or more number words in a row.
        rf"\b(?:(?:{NUMBER_WORDS})[\s,-]+){{3,}}(?:{NUMBER_WORDS})\b",
        re.IGNORECASE)),
]

PLACEHOLDER = re.compile(
    r"\[(?:CUSTOMER|AGENT|EMAIL|PHONE|ORDER|TRACKING|ADDRESS|POSTCODE|IBAN|CARD|LINK|SIGNATURE|QUOTED)\]")

# Where a message stops being what someone wrote and starts being machine-generated tail:
# a forwarded chain, a carrier's legal notice, a Shopify order-confirmation table. Everything
# from the first of these onward is dropped.
#
# This is a privacy control and a quality control at once. Those blocks are where the street
# addresses, per-recipient tracking links and third-party names survive — and as exemplars
# they are worse than useless, because a 6,000-character customs boilerplate retrieved as
# "how we answer this" teaches the model nothing except how to pad.
QUOTED_CHAIN = re.compile(
    r"^\s*-{3,}\s*(?:Forwarded message|Original Message|Ursprüngliche Nachricht)"
    # Unanchored: a reply often runs the quoted header onto the end of the last sentence.
    r"|From:\s.{0,200}?\bSent:"
    r"|^\s*(?:Von|De|Da):\s.{0,200}?\b(?:Gesendet|Envoyé|Enviado|Inviato):"
    r"|^.{0,120}?\b(?:wrote|schrieb|a écrit|escribió|ha scritto|kirjoitti|schreef):\s*$"
    # Order-confirmation and shipping tables.
    r"|^\s*(?:Bestell(?:ü|ue)bersicht|Order summary|Résumé de la commande|Resumen del pedido)"
    r"|^\s*(?:Kundeninformationen|Customer information|Lieferadresse|Rechnungsadresse"
    r"|Shipping [Aa]ddress|Billing [Aa]ddress|Delivery [Aa]ddress)"
    # Confidentiality footers. Deliberately unanchored: these arrive mid-line as often as at
    # the start of one, and cutting from the phrase keeps whatever real message preceded it.
    r"|\bThis e-?mail(?: transmission)?(?: and any attachments)? may contain"
    r"|\bmay contain confidential|\bprivileged information|\bintended recipient"
    r"|Šiame pranešime esanti informacija"
    r"|Diese E-?Mail (?:kann|enthält) vertrauliche"
    # Regulated-industry disclaimers, which arrive as several hundred words naming the sender's
    # employer, licence numbers and awards. Rare, but each one is a dossier on one person.
    r"|\bSecurities and advisory services|\bRegistered Investment Advis|\bmember FINRA"
    r"|\bInsurance License|\bnot a guarantee of future",
    re.IGNORECASE | re.MULTILINE)

# A corporate signature written on one line, delimited by pipes rather than newlines:
#
#     Regards Priya | Senior Estate Planning Specialist  Northwind Wealth | Toronto, Ontario
#
# The line-based signature stripper cannot see these — there is no line whose whole content is
# a sign-off. A second human review found one in the sample after the first round of fixes, and
# it is the most identifying thing in the corpus: job title plus employer plus city is often
# one person, and none of it matches an identifier pattern. 2.4 % of exchanges carry one.
INLINE_SIGNOFF = re.compile(
    r"\b(?:(?:kind|best|warm)\s+regards|regards|sincerely|thanks(?:\s+again)?|thank you|cheers"
    r"|mit freundlichen gr[uü](?:ss|ß)en|viele gr[uü](?:ss|ß)e|cordialement|saludos"
    r"|cordiali saluti|met vriendelijke groet(?:en)?)\b",
    re.IGNORECASE)

SIGNATURE_MARKERS = re.compile(
    r"\b(?:Senior|Sr\.?|Junior|Jr\.?|Head of|Director|Manager|Specialist|Consultant|Broker"
    r"|Analyst|Officer|President|Partner|Associate|Advis[eo]r|Coordinator|Executive"
    r"|LLC|Ltd\.?|Inc\.?|GmbH|LLP|PLC|S\.A\.|B\.V\.|UAB|A/S|Oy)\b")

# "My shipping address is:" — what follows is an address block, whatever shape it takes.
#
# Enumerating street types will always lag reality; a fourth review sample found "Cardigan
# Chase" surviving because that list stopped at the obvious half-dozen. Where the customer
# announces an address, the announcement is more reliable than the pattern, so the block that
# follows is dropped without trying to parse it.
ADDRESS_INTRO = re.compile(
    r"^.{0,90}?\b(?:shipping|delivery|billing|postal|home|new|correct)?\s*"
    r"address(?:es)?\s*(?:is|are|:)\s*:?\s*$"
    r"|^\s*(?:deliver|send|ship)\s+(?:it|this|them|the (?:parcel|order|item))?\s*to\s*:\s*$"
    # "Could you please change it to:" — an announcement that does not use the word address.
    r"|^.{0,90}?\b(?:change|update|correct|amend)\s+(?:it|this|the address)\s*to\s*:\s*$"
    r"|^\s*(?:Lieferadresse|Rechnungsadresse|Versandadresse|Adresse|Anschrift"
    r"|Adresse de livraison|Dirección|Indirizzo|Adres)\s*:?\s*$",
    re.IGNORECASE | re.MULTILINE)

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


# Text around an engraving instruction, where a third party's name tends to appear.
ENGRAVING_CONTEXT = re.compile(
    r"\b(?:engrav\w*|gravur\w*|monogram\w*|initials)\b[^.\n]{0,70}", re.IGNORECASE)

NAME_PAIR = re.compile(r"\b[A-Z][a-z]{2,}\s+[A-Z][a-z]{2,}\b")

# A capitalised pair starting with one of these is a sentence, not a person — "The Divine",
# "Please Note". Without this the withhold fires on the product names it exists to protect.
NAME_LEAD_STOPWORD = re.compile(
    r"^(?:The|This|That|These|Those|Your|Our|Their|His|Her|Please|Thank|Thanks|Kind|Best"
    r"|Dear|Hello|With|From|Sent|Font|Style|Order|Both|Same|Also|Just|Only|Would|Could|Can"
    r"|Der|Die|Das|Ihre|Ihr|Mit|Vielen|Sehr|Guten)\b")

# Typeface and product names have the same shape as a person's name and must not trigger a
# withhold — losing them would defeat the purpose of keeping personalisation exemplars at all.
FONT_OR_PRODUCT = re.compile(
    r"\b(?:Monotype|Engravers|Times New|Corsiva|Corsica|Script MT|Edwardian|Lucida|Brush"
    r"|Divine Comedy|Madame Bovary|Dark Brown|Light Brown|Leather|Style|Font|Roman|Italic)\b",
    re.IGNORECASE)


@dataclass(frozen=True)
class Finding:
    kind: str
    value: str


def redact(text: str, known_names: list[str] | None = None) -> str:
    """Replace personal data with typed placeholders."""
    if not text:
        return text

    # Order matters, and getting it wrong is subtle. Structured identifiers are redacted
    # before names, because a name inside an address — marie.dupont@example.com — would
    # otherwise be replaced first, leaving "[CUSTOMER].[CUSTOMER]@example.com" that the email
    # rule can no longer match as an address. The fail-closed check caught exactly that on a
    # real batch, as residual fragments like "e.@gmail.com".
    redacted = _strip_quoted_chain(text)
    redacted = _redact_signature_blocks(redacted)
    redacted = _redact_address_blocks(redacted)
    redacted = _redact_inline_signatures(redacted)

    for _, placeholder, pattern in PATTERNS:
        redacted = pattern.sub(placeholder, redacted)

    redacted = _redact_names(redacted, known_names or [])

    # Collapse runs the rules produced, e.g. "[ADDRESS], [POSTCODE] [POSTCODE]".
    return re.sub(r"(\[[A-Z]+\])(?:[\s,]+\1)+", r"\1", redacted)


def residual_identifiers(text: str) -> list[Finding]:
    """Re-detect over redacted text. Any finding means the batch must not be indexed.

    Run as a separate pass rather than folded into `redact` on purpose: this is the check that
    has to fail loudly, and a check that shares state with the thing it verifies is not a check.
    """
    if not text:
        return []

    # Placeholders are the expected output, not a leak. Replaced with a space rather than
    # nothing: removing them glues neighbouring fragments together and manufactures things
    # that look like identifiers but are not.
    masked = PLACEHOLDER.sub(" ", text)

    findings: list[Finding] = []
    for kind, _, pattern in PATTERNS:
        for match in pattern.finditer(masked):
            findings.append(Finding(kind, match.group(0).strip()))
    return findings


def engraved_third_party_name(text: str) -> str | None:
    """Return the phrase suggesting a third party's name is being engraved, or None.

    This is a *withholding* check, not a redaction rule, and the distinction is the point.
    Personalisation orders carry the names of people who are neither the customer nor the
    agent — "please engrave the name Benedict Msuya" — so name matching cannot reach them, and
    detecting arbitrary names by shape would take product names with it. "The Divine Comedy",
    "Madame Bovary Red" and "Monotype Corsiva" all sit beside engraving words and are precisely
    what these exemplars exist to teach.

    So the affected exchanges are dropped instead. They are 1.7 % of the corpus and roughly a
    tenth of personalisation exchanges; the rest of that topic is kept.
    """
    # Whitespace is normalised first. The context window stops at a line break, so without this
    # the same exchange withholds or does not depending on where the original mail wrapped.
    for match in ENGRAVING_CONTEXT.finditer(re.sub(r"\s+", " ", text)):
        for name in NAME_PAIR.finditer(match.group(0)):
            candidate = name.group(0)
            if NAME_LEAD_STOPWORD.match(candidate) or FONT_OR_PRODUCT.search(candidate):
                continue
            return candidate
    return None


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


def _redact_address_blocks(text: str) -> str:
    """Replace the lines following an address announcement with a single placeholder.

    The block runs from the announcement to the first blank line after it has started, capped
    at eight lines so a stray colon cannot swallow a message. Leading blank lines are skipped:
    people press return after the colon.
    """
    lines = text.split("\n")
    output: list[str] = []
    index = 0
    while index < len(lines):
        line = lines[index]
        output.append(line)
        index += 1

        if not ADDRESS_INTRO.match(line):
            continue

        while index < len(lines) and not lines[index].strip():
            index += 1

        consumed = 0
        while index < len(lines) and lines[index].strip() and consumed < 8:
            index += 1
            consumed += 1
        if consumed:
            output.append("[ADDRESS]")

    return "\n".join(output)


def _redact_inline_signatures(text: str) -> str:
    """Cut a pipe-delimited signature out of the middle of a line.

    Both conditions are required — two or more pipes *and* a title or company marker. Pipes
    alone appear in ordinary text and in tables; the pair is what distinguishes a signature
    from a sentence, and the cost of guessing wrong is deleting a customer's actual question.
    """
    lines = []
    for line in text.split("\n"):
        if line.count("|") >= 2 and SIGNATURE_MARKERS.search(line):
            signoff = INLINE_SIGNOFF.search(line)
            cut = signoff.end() if signoff else line.index("|")
            line = f"{line[:cut].rstrip()} [SIGNATURE]"
        lines.append(line)
    return "\n".join(lines)


def _strip_quoted_chain(text: str) -> str:
    """Drop everything from the first quoted-chain or boilerplate marker onward."""
    match = QUOTED_CHAIN.search(text)
    if match is None:
        return text
    return text[: match.start()].rstrip() + "\n[QUOTED]"


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
