"""Fixture suite for redaction.

**Every fixture is synthetic.** The shapes are modelled on real threads — signature blocks,
an address inside quoted email history, a phone number written in words — but every identity
is invented. The suite is committed to the repository, and it must not become another copy of
customer data.

Two properties are checked per case:

1. The identifiers listed for the case are gone.
2. `residual_identifiers` finds nothing afterwards — the fail-closed check that decides
   whether a batch may be indexed.

Run:  python tools/ingest/test_redaction.py
"""

from __future__ import annotations

import sys

from redaction import engraved_third_party_name, redact, residual_identifiers

# (name, text, known_names, must_disappear)
CASES: list[tuple[str, str, list[str], list[str]]] = [
    (
        "plain email address",
        "You can reach me at marta.kowalczyk@example.invalid any time.",
        [],
        ["marta.kowalczyk@example.invalid"],
    ),
    (
        "house order format",
        "My order #US#14532 still has not arrived after three weeks.",
        [],
        ["#US#14532"],
    ),
    (
        "german order format",
        "Bestellnummer 4471925 wurde noch nicht geliefert.",
        [],
        ["4471925"],
    ),
    (
        "order number with prefix wording",
        "Order number: DE#3917 was cancelled but I was still charged.",
        [],
        ["DE#3917"],
    ),
    (
        "international phone with spaces",
        "Call me back on +44 7700 900461 before Friday please.",
        [],
        ["+44 7700 900461"],
    ),
    (
        "phone with punctuation",
        "My number is (555) 019-2837, I am usually free after six.",
        [],
        ["(555) 019-2837"],
    ),
    (
        "phone written in words",
        "My mobile is oh seven seven double two nine one four in case the courier calls.",
        [],
        ["oh seven seven double two nine one four"],
    ),
    (
        "customer name known from the ticket",
        "Hi, this is Sammy Nguyen and I would like to return my bag.",
        ["Sammy Nguyen"],
        ["Sammy Nguyen", "Sammy"],
    ),
    (
        "first name only in greeting",
        "Hello Priya, thanks for getting back to me so quickly.",
        ["Priya Raghunathan"],
        ["Priya"],
    ),
    (
        "uk street address",
        "Please redeliver to 14 Alderney Street, London before Thursday.",
        [],
        ["14 Alderney Street"],
    ),
    (
        "german street address",
        "Die Lieferadresse ist Hauptstrasse 12, 10115 Berlin.",
        [],
        ["Hauptstrasse 12", "10115"],
    ),
    (
        "dutch address and postcode",
        "Ship it to Keizersgracht 210, 1016 DW Amsterdam instead.",
        [],
        ["Keizersgracht 210", "1016 DW"],
    ),
    (
        "us zip code",
        "The billing address zip is 94103 and the card was declined.",
        [],
        ["94103"],
    ),
    (
        "uk postcode",
        "My postcode is SW1A 2AA, the parcel went to the wrong one.",
        [],
        ["SW1A 2AA"],
    ),
    (
        "canadian postcode",
        "Delivery to M5V 3L9 was attempted twice according to the courier.",
        [],
        ["M5V 3L9"],
    ),
    (
        "iban in a refund request",
        "Please refund to GB29 NWBK 6016 1331 9268 19 rather than the card.",
        [],
        ["GB29 NWBK 6016 1331 9268 19"],
    ),
    (
        "card number",
        "The charge on 4111 1111 1111 1111 was taken twice.",
        [],
        ["4111 1111 1111 1111"],
    ),
    (
        "tracking number",
        "The GLS tracking number 04215509876543 shows no movement since Monday.",
        [],
        ["04215509876543"],
    ),
    (
        "signature block with title and address",
        "Thanks for the update, that works for me.\n\n"
        "Kind regards,\n"
        "Tomasz Wieczorek\n"
        "Procurement Lead, Northgate Trading Ltd\n"
        "8 Ferndale Road, Manchester M14 7RT\n"
        "tomasz.wieczorek@example.invalid | +44 161 496 0187",
        ["Tomasz Wieczorek"],
        ["Tomasz Wieczorek", "Northgate Trading", "8 Ferndale Road",
         "tomasz.wieczorek@example.invalid", "+44 161 496 0187", "M14 7RT"],
    ),
    (
        "german signature block",
        "Vielen Dank für die schnelle Antwort.\n\n"
        "Mit freundlichen Grüßen\n"
        "Annika Vogel\n"
        "Musterweg 3\n"
        "80331 München\n"
        "annika.vogel@example.invalid",
        ["Annika Vogel"],
        ["Annika Vogel", "Musterweg 3", "80331", "annika.vogel@example.invalid"],
    ),
    (
        "address inside quoted email history",
        "Yes that is still correct.\n\n"
        "> On 3 June, Customer Care wrote:\n"
        "> We have your address as 27 Larkspur Avenue, Bristol BS6 5TL\n"
        "> and your phone as 0117 496 0233. Please confirm.",
        [],
        ["27 Larkspur Avenue", "BS6 5TL", "0117 496 0233"],
    ),
    (
        "multiple identifiers in one sentence",
        "Order #FR#2065 to 5 Rue Lafayette, 75009 Paris, phone +33 1 70 39 84 22.",
        [],
        ["#FR#2065", "5 Rue Lafayette", "75009", "+33 1 70 39 84 22"],
    ),
    (
        "agent name in a reply",
        "Hi, this is Dario from the support team, I have refunded the order for you.",
        [("Dario Pellegrini", "[AGENT]")],
        ["Dario"],
    ),
    (
        "name appearing mid sentence",
        "I already explained to Ingrid that the strap arrived broken.",
        ["Ingrid Halvorsen"],
        ["Ingrid"],
    ),
    # The five below are regressions. A human read fifty exchanges drawn from the live index
    # and found each of these surviving redaction, after every automated check had passed.
    (
        "street name orphaned when the phone rule ate the house number",
        # The phone rule matched "12 45143" across the number and postcode, which stopped the
        # address rule matching and left street and city standing in the index.
        "Lieferung bitte an Holunderweg 12 45143 Essen, Deutschland.",
        [],
        ["Holunderweg", "45143"],
    ),
    (
        "compound street name written without a number",
        "Das Paket wurde an die Bahnhofstrasse geliefert, nicht an meine Adresse.",
        [],
        ["Bahnhofstrasse"],
    ),
    (
        "per-recipient tracking link",
        # The token in the path resolves back to a single recipient, so the link is an
        # identifier even though it matches no identifier pattern.
        "Order status: https://example.invalid/_t/c/v3/AABoFExLSG2DsyCbvQviZPHptVeV",
        [],
        ["AABoFExLSG2DsyCbvQviZPHptVeV"],
    ),
    (
        "forwarded chain dropped wholesale",
        "Could you send the invoice?\n"
        "---------- Forwarded message ---------\n"
        "From: Carrier <ops@example.invalid> Sent: 12 November 2025\n"
        "Deliver to Lindenweg 8, 40213 Duesseldorf.",
        [],
        ["Lindenweg", "40213", "Duesseldorf", "ops@example.invalid"],
    ),
    (
        # Found by a second human review, after the first round of fixes had been applied.
        # Title plus employer plus city is often exactly one person, and none of it matches
        # an identifier pattern — no automated check would ever have raised it.
        "corporate signature written inline with pipes",
        "How do I arrange an exchange? Regards Priya | Senior Estate Planning Specialist  "
        "Northwind Wealth | Toronto, Ontario | Tel: 416 555 0142",
        [],
        ["Senior Estate Planning Specialist", "Northwind Wealth", "Toronto"],
    ),
    (
        # Found by a fourth review sample. "Chase" was not in the street-type list, so the
        # address rule never fired, and the writer's first name was not the account name.
        "address block announced by the customer",
        "Replacement screws would be much appreciated. My shipping address is:\n"
        "\n"
        "Matthew Ellery\n"
        "3 Cardigan Chase\n"
        "Kidderminster DY10 4RQ\n"
        "\n"
        "Thanks for your help.",
        [],
        ["Matthew", "Ellery", "Cardigan Chase", "DY10 4RQ", "Kidderminster"],
    ),
    (
        "british estate road type",
        "The courier left it at 12 Bramley Croft instead of my house.",
        [],
        ["Bramley Croft"],
    ),
    (
        "name carrying a job title",
        "Please prepare two: Ben Majoe (Partner) and CLARA WEISS DIRECTOR on the second bag.",
        [],
        ["Ben Majoe", "CLARA WEISS"],
    ),
    (
        "quoted header running on from the previous sentence",
        "Thanks for confirming. From: Support <help@example.invalid> Sent: Friday 3 April "
        "To: Rafael Ortega Deliver to Lindenweg 8.",
        [],
        ["help@example.invalid", "Rafael Ortega", "Lindenweg"],
    ),
    (
        "confidentiality footer arriving mid-line",
        "Please refund the order. This e-mail and any attachments may contain confidential "
        "information belonging to Cetera Advisors LLC, registration 0132305.",
        [],
        ["Cetera Advisors LLC", "0132305"],
    ),
    (
        "order confirmation table dropped wholesale",
        "Thanks for the update!\n"
        "Shipping Address\n"
        "Annelie Bergstrom\n"
        "Solventilsgatan 4, 21120 Malmo\n",
        [],
        ["Solventilsgatan", "21120", "Malmo", "Annelie"],
    ),
]

# Text that must survive: over-redaction destroys the thing being collected.
MUST_SURVIVE: list[tuple[str, str, list[str]]] = [
    ("return window", "You may return your purchase within 30 days of delivery.", ["30 days"]),
    ("refund amount", "We have refunded EUR 45.00 to your original payment method.", ["45.00"]),
    ("percentage", "We can offer a 20% discount on your next order.", ["20%"]),
    ("warranty duration", "Our products carry a lifetime warranty against defects.", ["lifetime warranty"]),
    ("business days", "Delivery within the United States takes 1-5 business days.", ["1-5 business days"]),
]


# The withhold check for personalisation orders has to fire on a name and stay silent on a
# typeface or a product, because those share a name's shape and sit in the same sentence.
ENGRAVED_WITHHOLD: list[tuple[str, str, bool]] = [
    ("name to be engraved",
     "Could you engrave it with the name Benedict Msuya before shipping?", True),
    ("quoted name and a font in one sentence",
     "Please engrave 'Aisha Siddiqua' in font style Monotype Corsiva.", True),
    ("font only",
     "Use the Engravers MT font style for the monogram, no name needed.", False),
    ("product title only",
     "The engraving should read the same as The Divine Comedy edition.", False),
    ("initials only",
     "Monogram Style embossed, in gold, with the initials S.R. please.", False),
]


def check_engraved_withhold() -> list[str]:
    """A withhold that fires on everything is a corpus deleter; one that never fires is decor."""
    problems: list[str] = []
    for name, text, should_fire in ENGRAVED_WITHHOLD:
        fired = engraved_third_party_name(text) is not None
        if fired != should_fire:
            verb = "did not fire" if should_fire else "fired"
            problems.append(f"engraved-name withhold {verb} on '{name}'")
    return problems


def check_fail_closed() -> list[str]:
    """The check must reject text that skipped redaction — otherwise it is decoration.

    A pipeline whose safety check cannot fail is worse than one with no check, because it
    reports success either way.
    """
    problems: list[str] = []
    unredacted = (
        "Hi, this is Sammy Nguyen, order #US#14532, phone +44 7700 900461, "
        "email sammy@example.invalid, deliver to 14 Alderney Street, London SW1A 2AA."
    )

    findings = residual_identifiers(unredacted)
    kinds = {finding.kind for finding in findings}
    for expected in ("ORDER", "PHONE", "EMAIL", "ADDRESS", "POSTCODE"):
        if expected not in kinds:
            problems.append(f"fail-closed check missed {expected} in unredacted text")

    if residual_identifiers(redact(unredacted, ["Sammy Nguyen"])):
        problems.append("fail-closed check fires on correctly redacted text (false positive)")

    return problems


def main() -> int:
    failures: list[str] = []

    print(f"== redaction fixtures ({len(CASES)} cases) ==")
    for name, text, known, must_go in CASES:
        redacted = redact(text, known)
        leaked = [value for value in must_go if value.lower() in redacted.lower()]
        residual = residual_identifiers(redacted)

        if leaked:
            failures.append(f"{name}: still present after redaction: {leaked}")
        if residual:
            failures.append(
                f"{name}: fail-closed check found "
                f"{[(f.kind, f.value) for f in residual]} in {redacted!r}")

        status = "ok" if not leaked and not residual else "FAIL"
        print(f"  [{status}] {name}")

    print(f"\n== text that must survive ({len(MUST_SURVIVE)} cases) ==")
    for name, text, must_keep in MUST_SURVIVE:
        redacted = redact(text, [])
        lost = [value for value in must_keep if value.lower() not in redacted.lower()]
        if lost:
            failures.append(f"{name}: over-redacted, lost {lost} -> {redacted!r}")
        print(f"  [{'ok' if not lost else 'FAIL'}] {name}")

    print(f"\n== engraved-name withhold ({len(ENGRAVED_WITHHOLD)} cases) ==")
    engraved_problems = check_engraved_withhold()
    failures.extend(engraved_problems)
    for name, _, should_fire in ENGRAVED_WITHHOLD:
        expected = "withholds" if should_fire else "keeps"
        broken = any(f"'{name}'" in problem for problem in engraved_problems)
        print(f"  [{'FAIL' if broken else 'ok'}] {expected}: {name}")

    print("\n== fail-closed check ==")
    fail_closed_problems = check_fail_closed()
    failures.extend(fail_closed_problems)
    print(f"  [{'ok' if not fail_closed_problems else 'FAIL'}] "
          "rejects unredacted text, accepts redacted text")

    print(f"\n{len(failures)} failure(s)")
    for failure in failures:
        print(f"  - {failure}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
