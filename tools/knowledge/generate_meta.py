"""Generate knowledge/_meta lists from the corpus itself, so they cannot drift from it."""
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

(META / "topics.json").write_text(json.dumps({
    "$comment": "The valid topic slugs. Generated from knowledge/policy/ by "
                "tools/knowledge/generate_meta.py — edit the corpus, not this file.",
    "topics": [{"slug": s, "title": TITLES[s]} for s in sorted(topics)],
}, indent=2) + "\n", encoding="utf-8")

print(f"markets: {len(markets)}  topics: {len(topics)}")
for m in markets:
    print(f"  {m['code']:<7} {m['storefront']}")
