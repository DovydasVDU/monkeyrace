"""Sujungia out/<kūrinys>/candidates.jsonl į Data/texts-lt.jsonl, kurį paleidžiant importuoja TextImporter.

Paleidimas: python tekstai-i-duomenis.py
Naujas kūrinys be autoriaus sustabdo skriptą: įrašyk jį į AUTHORS.
"""
import glob
import json
import os

AUTHORS = {
    "Kliudžiau": "Jonas Biliūnas",
    "Laimės žiburys": "Jonas Biliūnas",
    "Lazda": "Jonas Biliūnas",
    "Ubagas": "Jonas Biliūnas",
    "Brisiaus galas": "Jonas Biliūnas",
    "Vagis": "Jonas Biliūnas",
    "Sename dvare": "Šatrijos Ragana",
    "Vincas Stonis": "Šatrijos Ragana",
    "Atbudimas": "Lazdynų Pelėda",
    "Iš sodietės atsiminimų": "Lazdynų Pelėda",
    "Upės dovana": "Lazdynų Pelėda",
    "Dėdės ir dėdienės": "Vaižgantas",
}
WIKIPEDIA = "Vikipedija"

root = os.path.dirname(os.path.abspath(__file__))
seen = set()
rows = []
# tik aplankai: out/candidates.jsonl šaknyje yra senesnis "Vagis" paleidimas
for path in sorted(glob.glob(os.path.join(root, "out", "*", "candidates.jsonl"))):
    for line in open(path, encoding="utf-8"):
        item = json.loads(line)
        if item["id"] in seen:  # Vikipedijos aplankai kartojasi
            continue
        seen.add(item["id"])
        is_wiki = item.get("source_type") == "wikipedia"
        author = item.get("author") or (WIKIPEDIA if is_wiki else AUTHORS.get(item["page"]))
        if not author:
            raise SystemExit(f"Nėra autoriaus kūriniui „{item['page']}“ ({path}) – įrašyk į AUTHORS")
        rows.append({
            "id": item["id"],
            "body": item["body"],
            "author": author,
            "work": item.get("work") or item["page"],
            "sourceUrl": item["source_url"],
            "license": item["license"],
            "flags": item["flags"],
        })

with open(os.path.join(root, "Data", "texts-lt.jsonl"), "w", encoding="utf-8", newline="\n") as out:
    for row in rows:
        out.write(json.dumps(row, ensure_ascii=False) + "\n")
print(f"{len(rows)} tekstų -> Data/texts-lt.jsonl")
