"""Generate knowledge/_meta lists from the corpus itself, so they cannot drift from it.

Topic vocabularies are **per corpus**, not global. The three corpora genuinely taxonomise
differently — policy is organised by published page (`faqs`, `impressum`), templates by the
support team's own categories (`refunds`, `fedex`), internal by procedure (`warranty-discounts`,
`odoo-orders`). Forcing one shared list would either lose distinctions or invent mappings
nobody uses.
"""
import json, re
from collections import defaultdict
from pathlib import Path
from urllib.parse import urlparse

POLICY = Path("knowledge/policy")
META = Path("knowledge/_meta")

TITLES = {
    "faqs": "FAQs",
    "shipping-and-returns": "Shipping and Returns",
    "warranty": "Warranty",
    "terms-and-conditions": "Terms & Conditions",
    "privacy-policy": "Privacy Policy",
    "cookies-policy": "Cookies Policy",
    "personalization": "Personalization",
    "international": "International",
    "withdrawal-instructions": "Withdrawal Instructions",
    "impressum": "Impressum",
}
NAMES = {
    "US": "United States", "EU": "European Union", "UK": "United Kingdom",
    "DE": "Germany", "FR": "France", "ES": "Spain", "IT": "Italy",
    "NL": "Netherlands", "PL": "Poland", "SE": "Sweden", "CA": "Canada",
    "AU_NZ": "Australia and New Zealand", "SG": "Singapore",
    "GLOBAL": "Global (fallback)",
}

KNOWLEDGE = Path("knowledge")
CORPORA = {"policy": "policy", "template": "templates", "internal": "internal"}

hosts = defaultdict(set)
topics = set()
for path in sorted(POLICY.rglob("*.md")):
    market, topic = path.parent.name, path.stem
    topics.add(topic)
    text = path.read_text(encoding="utf-8")
    if m := re.search(r"^source_url:\s*(\S+)", text, re.M):
        hosts[market].add(urlparse(m.group(1)).netloc)

markets = []
for code in sorted(hosts):
    found = sorted(hosts[code])
    if len(found) != 1:
        raise SystemExit(f"{code} maps to {len(found)} storefronts: {found}")
    markets.append({"code": code, "name": NAMES[code], "storefront": found[0]})

missing = topics - TITLES.keys()
if missing:
    raise SystemExit(f"topics with no title: {sorted(missing)}")

META.mkdir(parents=True, exist_ok=True)
(META / "markets.json").write_text(json.dumps({
    "$comment": "The 14 valid market codes. Generated from knowledge/policy/ by "
                "tools/knowledge/generate_meta.py — edit the corpus, not this file. "
                "'storefront' is evidence for R-6: market maps 1:1 onto storefront domain.",
    "markets": markets,
}, indent=2) + "\n", encoding="utf-8")

by_corpus = {}
for corpus, folder in CORPORA.items():
    root = KNOWLEDGE / folder
    # Read the declared topic rather than inferring it from the path: policy nests by
    # market, templates nest by topic, internal is flat. Front-matter is the contract.
    slugs = sorted({
        m.group(1).strip()
        for p in root.rglob("*.md")
        if (m := re.search(r"^topic:\s*(\S+)", p.read_text(encoding="utf-8"), re.M))
    }) if root.exists() else []
    by_corpus[corpus] = [
        {"slug": s, "title": TITLES.get(s, s.replace("-", " ").title())} for s in slugs
    ]

(META / "topics.json").write_text(json.dumps({
    "$comment": "Valid topic slugs per corpus. Generated from knowledge/ by "
                "tools/knowledge/generate_meta.py — edit the corpus, not this file. "
                "The vocabularies differ deliberately: the three corpora taxonomise "
                "differently and a shared list would invent mappings nobody uses.",
    "topicsByCorpus": by_corpus,
}, indent=2) + "\n", encoding="utf-8")

print(f"markets: {len(markets)}")
for corpus, entries in by_corpus.items():
    print(f"  {corpus:<9} {len(entries)} topics")
for m in markets:
    print(f"  {m['code']:<7} {m['storefront']}")
