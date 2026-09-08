#!/usr/bin/env python3
"""Parse walkthrough/GUIDE.md into build/steps.json and slice the tall screenshots.

Run this, then `npm install docx && node build.js` in this directory, to regenerate
MOTS-Supplier-Portal-Walkthrough.docx from the current guide and screenshots.

Screenshots are 1440 px wide and anything from 1001 to 6760 px tall (a full-page capture of a long
admin screen). Scaled to one page width, a 6760 px capture would be an inch and a half wide and
unreadable, so anything taller than it is wide is cut into equal horizontal bands with a small
overlap and shown one band per page. Cutting is honest in a way that shrinking is not: nothing is
hidden, and each band is legible at 100%.
"""
import json
import math
import os
import re
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "build")
IMG_OUT = os.path.join(OUT, "img")
os.makedirs(IMG_OUT, exist_ok=True)

PAGE_IMG_WIDTH_IN = 6.3          # A4 content width (8.27in) less 0.9in margins each side, with a hair to spare
SAME_PAGE_MAX_ASPECT = 1.05      # height/width that still fits under the step's text
SLICE_TARGET_ASPECT = 1.30       # each band on its own page
OVERLAP_PX = 48                  # so a row of a table is never cut in half unseen

guide = open(os.path.join(ROOT, "walkthrough/GUIDE.md")).read()
blocks = re.split(r"\n### ", guide)[1:]
assert len(blocks) == 96, len(blocks)

steps = []
for b in blocks:
    lines = b.strip().split("\n")
    num, title = re.match(r"(\d+)\.\s+(.*)", lines[0]).groups()
    fields = {}
    for line in lines:
        m = re.match(r"- \*\*(.+?):\*\* (.*)", line)
        if m:
            fields[m.group(1)] = m.group(2).strip()
    img = re.search(r"!\[.*?\]\((screenshots/.+?)\)", b).group(1)
    src = os.path.join(ROOT, "walkthrough", img)
    w, h = Image.open(src).size
    aspect = h / w

    parts = []
    if aspect <= SAME_PAGE_MAX_ASPECT:
        parts.append({"file": src, "w_in": PAGE_IMG_WIDTH_IN, "h_in": PAGE_IMG_WIDTH_IN * aspect,
                      "same_page": True, "label": None})
    else:
        n = max(2, math.ceil(aspect / SLICE_TARGET_ASPECT))
        band = math.ceil(h / n)
        im = Image.open(src)
        for i in range(n):
            top = max(0, i * band - (OVERLAP_PX if i else 0))
            bottom = min(h, (i + 1) * band)
            crop = im.crop((0, top, w, bottom))
            name = f"{num}-part{i + 1}.png"
            crop.save(os.path.join(IMG_OUT, name))
            ph = crop.size[1] / w
            parts.append({
                "file": os.path.join(IMG_OUT, name),
                "w_in": PAGE_IMG_WIDTH_IN,
                "h_in": PAGE_IMG_WIDTH_IN * ph,
                "same_page": False,
                "label": f"Screen {num}, part {i + 1} of {n}"
                         + (" — top of the page" if i == 0 else
                            (" — bottom of the page" if i == n - 1 else " — continued")),
            })

    screen = fields.get("Screen", "")
    m = re.match(r"(.*?)\s+—\s+`(.+?)`", screen)
    steps.append({
        "num": num,
        "title": title,
        "persona": fields.get("Persona", ""),
        "screen": m.group(1) if m else screen,
        "route": m.group(2) if m else "",
        "happened": fields.get("What just happened", ""),
        "next": fields.get("What the user does next", ""),
        "parts": parts,
    })

json.dump(steps, open(os.path.join(OUT, "steps.json"), "w"), indent=1)
sliced = sum(1 for s in steps if not s["parts"][0]["same_page"])
pages = sum(len(s["parts"]) for s in steps)
print(f"{len(steps)} steps, {sliced} screenshots sliced, {pages} image placements")
