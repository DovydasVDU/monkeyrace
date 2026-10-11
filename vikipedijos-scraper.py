#!/usr/bin/env python3
"""Lietuviškos Vikipedijos straipsnių rinkimas į MonkeyRace sakinių bazę.

Pavyzdžiai:
  python vikipedijos-scraper.py --page "Dirbtinis intelektas"
  python vikipedijos-scraper.py --pages "Dirbtinis intelektas" "Klimato kaita"
  python vikipedijos-scraper.py --selftest

Vikipedijos tekstas platinamas pagal CC BY-SA 4.0. Kiekvienas įrašas išsaugo
šaltinio URL, licenciją ir priskyrimo tekstą.
"""
import argparse
import csv
import hashlib
import json
import random
import re
import string
import time
import unicodedata
from pathlib import Path
from urllib.parse import quote, unquote

API = "https://lt.wikipedia.org/w/api.php"
USER_AGENT = "MonkeyRaceWikipediaBot/0.1 (kontaktas: itsmadjoke@gmail.com)"
CACHE = Path("cache-wikipedia")
LICENSE = "CC BY-SA 4.0"
ALLOWED = set(string.ascii_letters + string.digits + "ĄČĘĖĮŠŲŪŽąčęėįšųūž .,!?:;-'\"()")
DOT = "\u2024"
PROTECT = [re.compile(p) for p in [
    r"\b(?:pvz|Nr|žr|Žr|pav|Dr|Prof|Šv|kn|plg|psl)\.",
    r"\bt\. ?[yt]\.",
    r"\b\d{1,4}\s?(?:m|d|a|val|min|p)\.",
    rf"\b[A-ZĄČĘĖĮŠŲŪŽ]\.(?=\s*[A-ZĄČĘĖĮŠŲŪŽ])",
]]
BOUNDARY = re.compile(r"""[.!?]+["')]*\s+(?=["'(]?[A-ZĄČĘĖĮŠŲŪŽ0-9])""")


def normalize(text: str) -> str:
    text = unicodedata.normalize("NFC", text).replace("\u00ad", "")
    text = re.sub(r"[„“”«»‟〝〞]", '"', text)
    text = re.sub(r"[‘’‚‛`´]", "'", text)
    text = re.sub(r"[–—−‒―]", "-", text).replace("…", "...")
    text = re.sub(r"\[\d+\]", "", text)
    text = re.sub(r"[\u00a0\u2000-\u200b\u202f\u205f\u3000]", " ", text)
    return re.sub(r"\s+", " ", text).strip()


def split_sentences(text: str) -> list[str]:
    for pattern in PROTECT:
        text = pattern.sub(lambda match: match.group(0).replace(".", DOT), text)
    result, start = [], 0
    for match in BOUNDARY.finditer(text):
        result.append(text[start:match.end()].strip())
        start = match.end()
    result.append(text[start:].strip())
    return [item.replace(DOT, ".") for item in result if item]


def chunks(sentences: list[str], rng: random.Random) -> list[str]:
    result, index = [], 0
    while index < len(sentences):
        size = rng.randint(1, min(4, len(sentences) - index))
        result.append(" ".join(sentences[index:index + size]))
        index += size
    return result


def api_get(params: dict, delay: float) -> dict:
    import requests

    CACHE.mkdir(exist_ok=True)
    key = hashlib.md5(json.dumps(params, sort_keys=True).encode()).hexdigest()
    cache_file = CACHE / f"{key}.json"
    if cache_file.exists():
        return json.loads(cache_file.read_text(encoding="utf-8"))
    response = requests.get(API, params=params, headers={"User-Agent": USER_AGENT}, timeout=30)
    response.raise_for_status()
    data = response.json()
    if "error" in data:
        raise RuntimeError(data["error"].get("info", "Vikipedijos API klaida"))
    cache_file.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    time.sleep(delay)
    return data


def fetch_page(title: str, delay: float) -> tuple[str, str]:
    data = api_get({
        "action": "parse", "page": title, "prop": "text", "redirects": 1,
        "format": "json", "formatversion": 2,
    }, delay)
    parsed = data["parse"]
    return parsed["title"], parsed["text"]


def html_to_paragraphs(html: str) -> list[str]:
    from bs4 import BeautifulSoup

    soup = BeautifulSoup(html, "html.parser")
    for selector in [
        "table", "sup.reference", ".mw-editsection", ".navbox", ".metadata",
        ".infobox", ".sidebar", ".reflist", ".references", "style", "script",
        ".mw-empty-elt", "ol.references", "dl",
    ]:
        for element in soup.select(selector):
            element.decompose()
    for element in soup.find_all(["h1", "h2", "h3", "h4", "h5", "h6", "li"]):
        element.decompose()
    for element in soup.find_all("br"):
        element.replace_with(" ")
    paragraphs = []
    for paragraph in soup.find_all("p"):
        text = normalize(paragraph.get_text(" ", strip=True))
        if len(text) >= 20:
            paragraphs.append(text)
    return paragraphs


def evaluate(text: str, min_chars: int, max_chars: int) -> tuple[str, list[str], list[str]]:
    reasons, flags = [], []
    if len(text) < min_chars:
        reasons.append("per trumpas")
    if len(text) > max_chars:
        reasons.append("per ilgas")
    if any(char not in ALLOWED for char in text):
        reasons.append("netinkami simboliai")
    if not text[:1].isupper() and not text[:1].isdigit():
        reasons.append("prasideda ne iš didžiosios")
    if not text.endswith((".", "!", "?", '."', '!"', '?"', ".)", "...")):
        reasons.append("nesibaigia sakinio pabaiga")
    if text.count("(") != text.count(")"):
        reasons.append("nesubalansuoti skliaustai")
    if text.count('"') % 2:
        reasons.append("nesubalansuotos kabutės")
    if re.search(r"\d", text):
        flags.append("digits")
    if "(" in text:
        flags.append("parens")
    if ":" in text or ";" in text:
        flags.append("colon_semicolon")
    if sum(1 for word in text.split()[1:] if word[:1].isupper()) > 3:
        flags.append("many_caps")
    return text, reasons, flags


def scrape(title: str, delay: float, min_chars: int, max_chars: int,
           rng: random.Random) -> tuple[list[dict], list[dict]]:
    page_title, html = fetch_page(title, delay)
    accepted, rejected, seen = [], [], set()
    for paragraph_index, paragraph in enumerate(html_to_paragraphs(html)):
        for body in chunks(split_sentences(paragraph), rng):
            body, reasons, flags = evaluate(body, min_chars, max_chars)
            key = body.casefold()
            if not reasons and key in seen:
                reasons.append("dublikatas")
            record = {
                "id": hashlib.md5(key.encode()).hexdigest()[:12],
                "body": body, "page": page_title,
                "para": paragraph_index,
                "source_url": "https://lt.wikipedia.org/wiki/" + quote(page_title.replace(" ", "_")),
                "source_type": "wikipedia", "license": LICENSE,
                "attribution": f'Šaltinis: lietuviškoji Vikipedija, straipsnis „{page_title}“',
                "sentence_count": len(split_sentences(body)),
                "char_count": len(body), "word_count": len(body.split()),
                "flags": ["wikipedia", "informational"] + flags,
            }
            if reasons:
                record["reasons"] = reasons
                rejected.append(record)
            else:
                seen.add(key)
                accepted.append(record)
    return accepted, rejected


def safe_name(name: str) -> str:
    name = re.sub(r'[<>:"/\\|?*]', "_", name).strip(" .")
    return re.sub(r"\s+", " ", name)[:120] or "nepavadinta"


def output_dir(root: Path, titles: list[str]) -> Path:
    root.mkdir(parents=True, exist_ok=True)
    base = root / ("Vikipedija - " + safe_name(titles[0]) if len(titles) == 1
                   else "Vikipedija - pasirinkti straipsniai")
    candidate, index = base, 2
    while candidate.exists():
        candidate = root / f"{base.name}-{index}"
        index += 1
    candidate.mkdir()
    return candidate


def write_outputs(folder: Path, accepted: list[dict], rejected: list[dict]) -> None:
    with (folder / "candidates.jsonl").open("w", encoding="utf-8") as file:
        for record in accepted:
            file.write(json.dumps(record, ensure_ascii=False) + "\n")
    with (folder / "rejected.jsonl").open("w", encoding="utf-8") as file:
        for record in rejected:
            file.write(json.dumps(record, ensure_ascii=False) + "\n")
    with (folder / "candidates.csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.writer(file, delimiter=";")
        writer.writerow(["keep", "id", "body", "sentence_count", "char_count",
                         "word_count", "flags", "page", "source_url", "license"])
        for record in accepted:
            writer.writerow(["", record["id"], record["body"], record["sentence_count"],
                             record["char_count"], record["word_count"],
                             ",".join(record["flags"]), record["page"],
                             record["source_url"], record["license"]])


def selftest() -> None:
    assert len(split_sentences("Tai sakinys. Kitas sakinys!")) == 2
    assert html_to_paragraphs("<table><tr><td>Blogai</td></tr></table>"
                              "<p>Šis sakinys turi likti ištraukoje.</p>") == [
                                  "Šis sakinys turi likti ištraukoje."
                              ]
    assert all(1 <= len(split_sentences(item)) <= 4
               for item in chunks(["Vienas.", "Du.", "Trys.", "Keturi.", "Penki."],
                                  random.Random(1)))
    print("selftest OK")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--page", help="Vienas Vikipedijos straipsnio pavadinimas arba URL")
    parser.add_argument("--pages", nargs="+", help="Keli Vikipedijos straipsnių pavadinimai arba URL")
    parser.add_argument("--min", type=int, default=40, dest="min_chars")
    parser.add_argument("--max", type=int, default=260, dest="max_chars")
    parser.add_argument("--delay", type=float, default=1.0)
    parser.add_argument("--seed", type=int)
    parser.add_argument("--out", default="out")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()
    if args.selftest:
        return selftest()
    titles = args.pages or ([args.page] if args.page else [])
    if not titles:
        parser.error("Nurodyk --page, --pages arba --selftest.")
    titles = [unquote(title.split("/wiki/")[-1]).replace("_", " ") for title in titles]
    rng = random.Random(args.seed)
    accepted, rejected = [], []
    failed = []
    for title in titles:
        try:
            current, refused = scrape(title, args.delay, args.min_chars, args.max_chars, rng)
        except RuntimeError as error:
            failed.append((title, str(error)))
            print(f"{title}: praleistas dėl API klaidos: {error}")
            continue
        accepted.extend(current)
        rejected.extend(refused)
        print(f"{title}: tinkami {len(current)} · atmesti {len(refused)}")
    if not accepted and not rejected:
        raise SystemExit("Nepavyko surinkti nė vieno straipsnio.")
    folder = output_dir(Path(args.out), titles)
    write_outputs(folder, accepted, rejected)
    print(f"Iš viso: tinkami {len(accepted)} · atmesti {len(rejected)} · failai: {folder}\\")
    if failed:
        print("Nepavykę straipsniai: " + "; ".join(title for title, _ in failed))


if __name__ == "__main__":
    main()
