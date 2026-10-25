"""Bake only menu characters into a static SDF atlas; no font file ships at runtime.

Dependencies: requirements-menu-font.txt. The pinned upstream source and license
are recorded in Mod/Assets/MenuFont.source.json. Run from any working directory.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Artifacts/python-tools"))

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont
from scipy.ndimage import distance_transform_edt

ASSETS = ROOT / "Mod/Assets"
SIZE, PADDING, SAMPLE, WIDTH = 64, 8, 4, 1024


def characters() -> str:
    catalog = json.loads((ASSETS / "MenuText.json").read_text(encoding="utf-8-sig"))
    if set(catalog) != {"en", "zh-CN"} or set(catalog["en"]) != set(catalog["zh-CN"]):
        raise ValueError("Translation keys must match in English and Simplified Chinese")
    # ASCII covers page numbers, percentages and the existing internal scene labels.
    # Only characters actually present in the Chinese copy are retained beyond ASCII.
    return "".join(sorted(set(map(chr, range(32, 127))) | {"°"} |
                          {c for s in catalog["zh-CN"].values() for c in s if not c.isspace()}))


def bake(source: Path) -> None:
    provenance = json.loads((ASSETS / "MenuFont.source.json").read_text(encoding="utf-8-sig"))
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if digest != provenance["sha256"].lower():
        raise ValueError("Font source SHA256 does not match the pinned source")
    chars = characters()
    font = TTFont(source)
    cmap = font.getBestCmap()
    missing = [c for c in chars if ord(c) not in cmap]
    if missing:
        raise ValueError(f"Source font lacks {missing!r}")
    face = ImageFont.truetype(str(source), SIZE * SAMPLE)
    tiles, glyphs = [], []
    x = y = row_height = 0
    for char in chars:
        left, top, right, bottom = face.getbbox(char, anchor="ls")
        left, top = math.floor(left / SAMPLE), math.floor(top / SAMPLE)
        right, bottom = math.ceil(right / SAMPLE), math.ceil(bottom / SAMPLE)
        w, h = right - left, bottom - top
        advance = font["hmtx"].metrics[cmap[ord(char)]][0] * SIZE / font["head"].unitsPerEm
        if w == 0 or h == 0:
            glyphs.append(dict(Unicode=ord(char), X=0, Y=0, Width=0, Height=0,
                               BearingX=0, BearingY=0, Advance=advance))
            continue
        tw, th = w + PADDING * 2, h + PADDING * 2
        if x + tw > WIDTH:
            x, y, row_height = 0, y + row_height, 0
        mask = Image.new("L", (tw * SAMPLE, th * SAMPLE))
        ImageDraw.Draw(mask).text(((PADDING-left)*SAMPLE, (PADDING-top)*SAMPLE),
                                 char, font=face, fill=255, anchor="ls")
        inside = np.asarray(mask) >= 128
        distance = np.where(inside, distance_transform_edt(inside)-.5,
                            .5-distance_transform_edt(~inside)) / SAMPLE
        distance = distance.reshape(th, SAMPLE, tw, SAMPLE).mean(axis=(1, 3))
        sdf = np.clip(np.rint(128 + distance * (127 / PADDING)), 0, 255).astype(np.uint8)
        tiles.append((x, y, sdf))
        glyphs.append(dict(Unicode=ord(char), X=x+PADDING, Y=y+PADDING,
                           Width=w, Height=h, BearingX=left, BearingY=-top, Advance=advance))
        x, row_height = x + tw, max(row_height, th)
    height = 1 << (y + row_height - 1).bit_length()
    atlas = np.zeros((height, WIDTH), dtype=np.uint8)
    for tx, ty, tile in tiles:
        atlas[ty:ty+tile.shape[0], tx:tx+tile.shape[1]] = tile
    for glyph in glyphs:
        if glyph["Height"]:
            # TMP uses bottom-left texture coordinates; PNG/Pillow use top-left.
            glyph["Y"] = height - glyph["Y"] - glyph["Height"]
    scale = SIZE / font["head"].unitsPerEm
    raw = np.flipud(atlas).tobytes()
    data = dict(Version=1, Family="MGVR Menu SC", PointSize=SIZE, Padding=PADDING,
                Width=WIDTH, Height=height, Ascent=font["hhea"].ascent*scale,
                Descent=font["hhea"].descent*scale,
                LineHeight=(font["hhea"].ascent-font["hhea"].descent+font["hhea"].lineGap)*scale,
                CapHeight=font["OS/2"].sCapHeight*scale, MeanLine=font["OS/2"].sxHeight*scale,
                Characters=chars, Glyphs=glyphs, AtlasSHA256=hashlib.sha256(raw).hexdigest())
    (ASSETS / "MenuFont.zh-CN.json").write_text(json.dumps(data, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    (ASSETS / "MenuFont.zh-CN.alpha").write_bytes(gzip.compress(raw, compresslevel=9, mtime=0))
    report = ROOT / "Artifacts/Reports/MenuFont"
    report.mkdir(parents=True, exist_ok=True)
    Image.fromarray(atlas).save(report / "atlas-preview.png")
    # Preview the baked pixels rather than rendering with the complete source font.
    # This catches flipped atlas rows, incorrect bearings and missing glyph tiles.
    catalog = json.loads((ASSETS / "MenuText.json").read_text(encoding="utf-8-sig"))
    lookup = {g["Unicode"]: g for g in glyphs}
    lines = [v for k, v in catalog["zh-CN"].items() if k != "FixedControls"]
    lines += catalog["zh-CN"]["FixedControls"].splitlines()
    preview = Image.new("RGB", (1400, 80+len(lines)*44), (249, 232, 205))
    factor = 28 / SIZE
    for row, line in enumerate(lines):
        cursor, baseline = 32.0, 48 + row*44
        for char in line:
            glyph = lookup[ord(char)]
            if glyph["Width"] and glyph["Height"]:
                sy = height-glyph["Y"]-glyph["Height"]
                sdf = Image.fromarray(atlas[sy:sy+glyph["Height"], glyph["X"]:glyph["X"]+glyph["Width"]])
                sdf = sdf.resize((max(1, round(glyph["Width"]*factor)), max(1, round(glyph["Height"]*factor))), Image.Resampling.BILINEAR)
                mask = Image.fromarray(np.clip((np.asarray(sdf, dtype=float)-128)*2*PADDING*factor+127.5, 0, 255).astype(np.uint8))
                preview.paste((58, 50, 38), (round(cursor+glyph["BearingX"]*factor),
                                           round(baseline-glyph["BearingY"]*factor)), mask)
            cursor += glyph["Advance"]*factor
    preview.save(report / "text-preview.png")
    print(json.dumps(dict(glyphCount=len(glyphs), chineseCharacters=sum('\u4e00' <= c <= '\u9fff' for c in chars),
                          atlas=f"{WIDTH}x{height}", gpuBytes=len(raw),
                          compressedBytes=(ASSETS / "MenuFont.zh-CN.alpha").stat().st_size), ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="Pinned NotoSansCJKsc-Regular.otf (build only)")
    bake(parser.parse_args().source.resolve())
