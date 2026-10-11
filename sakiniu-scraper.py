#!/usr/bin/env python3
"""MonkeyRace sakinių scraperis: lt.wikisource.org -> kandidatų failai peržiūrai.

Pavyzdžiai:
  python scrape_sentences.py --search "Sename dvare"
  python scrape_sentences.py --page "Sename dvare" --author "Šatrijos Ragana" \
        --work "Sename dvare" --subpages --license PD
  python scrape_sentences.py --selftest

Rezultatas aplanke out/<kūrinys>/:
  candidates.jsonl  - tinkamos 1–4 sakinių ištraukos
  candidates.csv    - tas pats peržiūrai Excel'yje
  rejected.jsonl    - atmestos ištraukos su priežastimi

Jei nurodyto kūrinio katalogas jau egzistuoja, sukuriamas naujas katalogas su
priesaga, todėl ankstesni rezultatai niekada neperrašomi.

Sunkumo nesiskaičiuoja: tai atskiras žingsnis vėliau.
"""
import argparse
import csv
import hashlib
import json
import random
import re
import string
import sys
import time
import unicodedata
from pathlib import Path
from urllib.parse import quote, unquote

API = "https://lt.wikisource.org/w/api.php"
# Wikimedia reikalauja aprašomo User-Agent su kontaktu. PAKEISK PRIEŠ PALEIDŽIANT.
USER_AGENT = "MonkeyRaceTextBot/0.1 (kontaktas: itsmadjoke@gmail.com)"
CACHE = Path("cache")

LT_UP = "ĄČĘĖĮŠŲŪŽ"
LT_LOW = "ąčęėįšųūž"
UPPER = "A-Z" + LT_UP
DOT = "\u2024"  # laikinas taškas apsaugotoms santrumpoms

ALLOWED = set(string.ascii_letters + string.digits + LT_UP + LT_LOW + " .,!?:;-'\"()")

# Santrumpos, po kurių taškas NĖRA sakinio pabaiga.
PROTECT = [re.compile(p) for p in [
    r"\b(?:pvz|Nr|žr|Žr|pav|Dr|Prof|Šv|kn|vad|red|plg|psl|apyl|ats)\.",
    r"\bt\. ?[yt]\.",
    r"\bir (?:kt|pan)\.",
    rf"\b[{UPPER}]\.(?=\s*[{UPPER}])",  # inicialai: J. Basanavičius
    r"\b\d{1,4}\s?(?:m|d|a|val|min|p)\.",  # 1922 m., 5 d., XX a.
]]
BOUNDARY = re.compile(rf"[.!?]+[\"')]*\s+(?=[\"'(]?[{UPPER}0-9])")


# ---------------------------------------------------------------- tekstas

def normalize(s: str) -> str:
    s = unicodedata.normalize("NFC", s)
    s = s.replace("\u00ad", "")
    s = re.sub(r"[„“”«»‟〝〞]", '"', s)
    s = re.sub(r"[‘’‚‛`´]", "'", s)
    s = re.sub(r"[–—−‒―]", "-", s)
    s = s.replace("…", "...")
    s = re.sub(r"[\u00a0\u2000-\u200b\u202f\u205f\u3000]", " ", s)
    s = re.sub(r"\[\d+\]", "", s)  # išnašų žymės [1]
    return re.sub(r"\s+", " ", s).strip()


def split_sentences(text: str) -> list[str]:
    def protect(m):
        return m.group(0).replace(".", DOT)

    for p in PROTECT:
        text = p.sub(protect, text)
    out, start = [], 0
    for m in BOUNDARY.finditer(text):
        out.append(text[start:m.end()].strip())
        start = m.end()
    out.append(text[start:].strip())
    return [s.replace(DOT, ".") for s in out if s]


def chunk_sentences(sentences: list[str], min_sentences: int,
                    max_sentences: int, rng: random.Random) -> list[str]:
    """Sujungia gretimus sakinius į atsitiktinio dydžio 1–4 sakinių ištraukas."""
    chunks = []
    i = 0
    while i < len(sentences):
        size = rng.randint(min_sentences, min(max_sentences, len(sentences) - i))
        chunks.append(" ".join(sentences[i:i + size]))
        i += size
    return chunks


def html_to_paragraphs(html: str) -> list[str]:
    from bs4 import BeautifulSoup

    soup = BeautifulSoup(html, "html.parser")
    for sel in ["sup.reference", ".mw-editsection", "style", "script", ".noprint",
                ".ws-noexport", "table", ".toc", "#toc", ".references",
                ".pagenum", ".pagenumber", ".mw-empty-elt"]:
        for el in soup.select(sel):
            el.decompose()
    for br in soup.find_all("br"):
        br.replace_with(" ")
    paras = []
    for p in soup.find_all("p"):
        if p.find_parent(class_="poem"):  # eilėraščių praleidžiame
            continue
        t = normalize(p.get_text(""))
        if len(t) >= 20:
            paras.append(t)
    return paras


# ---------------------------------------------------------------- filtrai

def evaluate(s: str, min_chars: int, max_chars: int):
    """Grąžina (sakinys be pradžios brūkšnio, atmetimo priežastys, vėliavėlės)."""
    reasons, flags = [], []
    if s.startswith("- "):
        s = s[2:]
        flags.append("dialogue")
    n = len(s)
    if n < min_chars:
        reasons.append("per trumpas")
    if n > max_chars:
        reasons.append("per ilgas")
    bad = sorted({ch for ch in s if ch not in ALLOWED})
    if bad:
        reasons.append("netinkami simboliai: " + "".join(bad))
    head = s.lstrip("\"'(")
    if not head or not (head[0].isupper() or head[0].isdigit()):
        reasons.append("prasideda ne iš didžiosios")
    if not s.endswith((".", "!", "?", '."', '!"', '?"', ".)", "...")):
        reasons.append("nesibaigia sakinio pabaiga")
    if s.count('"') % 2:
        reasons.append("nesubalansuotos kabutės")
    if s.count("(") != s.count(")"):
        reasons.append("nesubalansuoti skliaustai")
    if "http" in s.lower():
        reasons.append("nuoroda")
    if re.search(r"\d", s):
        flags.append("digits")
    if '"' in s:
        flags.append("quotes")
    if "(" in s:
        flags.append("parens")
    if ":" in s or ";" in s:
        flags.append("colon_semicolon")
    if "..." in s:
        flags.append("ellipsis")
    words = s.split()
    if sum(1 for w in words[1:] if w[:1].isupper()) > 3:
        flags.append("many_caps")
    return s, reasons, flags


# ---------------------------------------------------------------- tinklas

def api_get(params: dict, delay: float):
    import requests

    CACHE.mkdir(exist_ok=True)
    key = hashlib.md5(json.dumps(params, sort_keys=True).encode()).hexdigest()
    f = CACHE / f"{key}.json"
    if f.exists():
        return json.loads(f.read_text("utf-8"))
    r = requests.get(API, params=params, headers={"User-Agent": USER_AGENT}, timeout=30)
    r.raise_for_status()
    data = r.json()
    if "error" in data:
        raise RuntimeError(data["error"].get("info", "API klaida"))
    f.write_text(json.dumps(data, ensure_ascii=False), "utf-8")
    time.sleep(delay)  # mandagumas serveriui
    return data


def search_titles(query: str, delay: float, limit: int = 10) -> list[str]:
    data = api_get(dict(action="query", list="search", srsearch=query,
                        srlimit=limit, format="json", formatversion=2), delay)
    return [h["title"] for h in data["query"]["search"]]


def fetch_page(title: str, delay: float) -> dict:
    data = api_get(dict(action="parse", page=title, prop="text|links",
                        redirects=1, format="json", formatversion=2), delay)
    return data["parse"]


def page_url(title: str) -> str:
    return "https://lt.wikisource.org/wiki/" + quote(title.replace(" ", "_"))


# ---------------------------------------------------------------- pagrindinis

def process(title, author, work, license_, min_chars, max_chars, delay, subpages, seen,
            min_sentences, max_sentences, rng):
    ok, rejected = [], []
    parse = fetch_page(title, delay)
    pages = [(parse["title"], parse["text"])]
    if subpages:
        base = parse["title"] + "/"
        subs = []
        for ln in parse.get("links", []):
            t = ln.get("title", "")
            if t.startswith(base) and ln.get("exists", True) and t not in subs:
                subs.append(t)
        for t in subs:
            p = fetch_page(t, delay)
            pages.append((p["title"], p["text"]))
    for ptitle, html in pages:
        for pi, para in enumerate(html_to_paragraphs(html)):
            sentences = split_sentences(para)
            for sent in chunk_sentences(sentences, min_sentences, max_sentences, rng):
                body, reasons, flags = evaluate(sent, min_chars, max_chars)
                key = body.lower()
                if not reasons and key in seen:
                    reasons.append("dublikatas")
                rec = {"id": hashlib.md5(key.encode()).hexdigest()[:12], "body": body,
                       "author": author, "work": work, "page": ptitle, "para": pi,
                       "source_url": page_url(ptitle), "license": license_,
                       "sentence_count": len(split_sentences(body)),
                       "char_count": len(body), "word_count": len(body.split()),
                       "flags": flags}
                if reasons:
                    rec["reasons"] = reasons
                    rejected.append(rec)
                else:
                    seen.add(key)
                    ok.append(rec)
    return ok, rejected


def safe_folder_name(name: str) -> str:
    name = re.sub(r'[<>:"/\\|?*]', "_", name).strip(" .")
    name = re.sub(r"\s+", " ", name)
    return name[:120] or "nepavadintas-kurinys"


def new_output_dir(root: Path, work: str, title: str) -> Path:
    """Sukuria naują kūrinio katalogą, neperrašydamas ankstesnio paleidimo."""
    root.mkdir(parents=True, exist_ok=True)
    base = root / safe_folder_name(work or title)
    candidate = base
    index = 2
    while candidate.exists():
        candidate = root / f"{base.name}-{index}"
        index += 1
    candidate.mkdir()
    return candidate


def write_outputs(out_dir: Path, ok: list, rejected: list):
    with open(out_dir / "candidates.jsonl", "w", encoding="utf-8") as f:
        for r in ok:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    with open(out_dir / "rejected.jsonl", "w", encoding="utf-8") as f:
        for r in rejected:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    # utf-8-sig + ';' -> Excel su lietuviškomis raidėmis atsidaro teisingai
    with open(out_dir / "candidates.csv", "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["keep", "id", "body", "sentence_count", "char_count",
                    "word_count", "flags", "author", "work", "source_url", "license"])
        for r in ok:
            w.writerow(["", r["id"], r["body"], r["sentence_count"], r["char_count"],
                        r["word_count"], ",".join(r["flags"]), r["author"], r["work"],
                        r["source_url"], r["license"]])


def selftest():
    sample = ("Eiti pas bobutę - kaip tai miela! Ne tik dėl to, kad ji moka gražių pasakų, "
              "bet ir dėl to, kad ten viskas taip keista, taip nematyta, taip kitoniška, "
              "negu aplink mane. Rodos man, kad nuėjus ten įžengiu į pasakų šalį. Gyvena "
              "bobutė sename mūro name, kuriame kitąsyk buvo bravoras, mažame kambarėlyje "
              "su vienu giliai įleistu langu, mažomis, saulės nudažytomis rutelėmis. "
              "Kambarėlio sienos laikraščiais išlipytos, asla švariai iššluota, visados "
              "žaliais ajerais išbarstyta. Tur būt, nuo tų ajerų ir nuo ryšulių žolių, "
              "prikabinėtų palubėse, visas kambarėlis pilnas kažkokio meilaus, ypatingo kvapo.")
    assert len(split_sentences(normalize(sample))) == 6
    tricky = "Žr. pav. 3. Jis atėjo 1922 m. rudenį. J. Basanavičius kalbėjo, t. y. daug."
    assert len(split_sentences(tricky)) == 3, split_sentences(tricky)
    rng = random.Random(1)
    chunks = chunk_sentences(["Vienas.", "Du.", "Trys.", "Keturi."], 1, 4, rng)
    assert sum(len(split_sentences(chunk)) for chunk in chunks) == 4
    assert all(1 <= len(split_sentences(chunk)) <= 4 for chunk in chunks)
    assert normalize("„Labas“ – tarė… jis") == '"Labas" - tarė... jis'
    paras = html_to_paragraphs(
        "<div><p>Pirmas sakinys su <i>kursyvu</i>, kablelis.<sup class='reference'>[1]</sup></p>"
        "<div class='poem'><p>Eilėraščio eilutė ilga ilga ilga.</p></div></div>")
    assert paras == ["Pirmas sakinys su kursyvu, kablelis."], paras
    s, reasons, flags = evaluate('- Eik namo!', 5, 100)
    assert s == "Eik namo!" and not reasons and flags == ["dialogue"]
    assert evaluate("trumpas", 40, 260)[1]
    print("selftest OK")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--search", help="Ieško puslapių pavadinimų Vikišaltinyje ir išeina")
    ap.add_argument("--page", help="Puslapio pavadinimas (iš --search)")
    ap.add_argument("--url", help="Vikišaltinio nuoroda vietoje --page")
    ap.add_argument("--author", default="")
    ap.add_argument("--work", default="")
    ap.add_argument("--subpages", action="store_true", help="Imti ir poskyrius (Pavadinimas/...)")
    ap.add_argument("--license", default="PD-PATIKRINTI", help="Pvz. PD, kai pats patikrinai")
    ap.add_argument("--min", type=int, default=40, dest="min_chars")
    ap.add_argument("--max", type=int, default=260, dest="max_chars")
    ap.add_argument("--min-sentences", type=int, default=1,
                    help="Mažiausias sakinių skaičius vienoje ištraukoje (1–4)")
    ap.add_argument("--max-sentences", type=int, default=4,
                    help="Didžiausias sakinių skaičius vienoje ištraukoje (1–4)")
    ap.add_argument("--seed", type=int, help="Neprivaloma atsitiktinumo sėkla pakartojamam rezultatui")
    ap.add_argument("--delay", type=float, default=1.0, help="Pauzė tarp užklausų, s")
    ap.add_argument("--out", default="out")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()

    if a.selftest:
        return selftest()
    if "TAVO_EL_PASTAS" in USER_AGENT:
        sys.exit("Pirma pakeisk USER_AGENT kontaktą failo viršuje.")
    if a.search:
        for t in search_titles(a.search, a.delay):
            print(t)
        return
    title = a.page or (unquote(a.url.split("/wiki/", 1)[1]).replace("_", " ") if a.url else None)
    if not title:
        sys.exit("Nurodyk --page, --url arba --search.")
    if not 1 <= a.min_sentences <= a.max_sentences <= 4:
        sys.exit("--min-sentences ir --max-sentences turi būti tarp 1 ir 4, min <= max.")
    rng = random.Random(a.seed)
    ok, rejected = process(title, a.author, a.work, a.license, a.min_chars, a.max_chars,
                           a.delay, a.subpages, set(), a.min_sentences,
                           a.max_sentences, rng)
    out_dir = new_output_dir(Path(a.out), a.work, title)
    write_outputs(out_dir, ok, rejected)
    print(f"Tinkami: {len(ok)} · atmesti: {len(rejected)} · failai: {out_dir}/")


if __name__ == "__main__":
    main()