"""Draw the ribbon button icons (16 and 32 px PNG) for every command in
scripts/ribbon.json into packaging/icons/. build-cuix.ps1 embeds them in the CUIX.

Each icon is a white glyph on a rounded square in its panel's colour. No text in any
icon (Autodesk's icon guideline). Drawn at 128 px and downsampled for anti-aliasing."""
import json
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "packaging", "icons")
S = 128
W = (255, 255, 255, 255)
LW = 9


def hexrgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def line(d, pts, w=LW):
    d.line(pts, fill=W, width=w, joint="curve")
    for x, y in (pts[0], pts[-1]):
        d.ellipse((x - w / 2, y - w / 2, x + w / 2, y + w / 2), fill=W)


def curve(fn, x0, x1, n=40):
    return [(x0 + (x1 - x0) * i / n, fn(i / n)) for i in range(n + 1)]


def drop(d, cx, cy, r):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=W)
    d.polygon([(cx - r * 0.92, cy - r * 0.4), (cx, cy - r * 2.3), (cx + r * 0.92, cy - r * 0.4)], fill=W)


def check(d, x, y, s, w=LW + 2, col=W):
    d.line([(x, y), (x + s * 0.35, y + s * 0.35), (x + s, y - s * 0.45)], fill=col, width=w, joint="curve")


def doc(d):
    d.polygon([(34, 22), (78, 22), (96, 40), (96, 106), (34, 106)], outline=W, width=LW)
    d.line([(76, 22), (76, 42), (96, 42)], fill=W, width=LW - 3)


def label_tag(d):
    d.rectangle((70, 78, 106, 104), fill=W)


G = {}


def glyph(name):
    def reg(f):
        G[name] = f
        return f
    return reg


@glyph("pipe")
def _(d):
    d.rounded_rectangle((18, 46, 110, 82), radius=18, outline=W, width=LW)
    d.ellipse((92, 46, 110, 82), outline=W, width=LW - 3)


@glyph("pipe_label")
def _(d):
    d.rounded_rectangle((16, 30, 96, 60), radius=15, outline=W, width=LW)
    label_tag(d)


@glyph("gauge")
def _(d):
    d.arc((22, 30, 106, 114), 180, 360, fill=W, width=LW)
    a = math.radians(-40)
    line(d, [(64, 72), (64 + 34 * math.cos(a), 72 + 34 * math.sin(a))])
    d.ellipse((56, 64, 72, 80), fill=W)


@glyph("gauge_label")
def _(d):
    d.arc((14, 22, 90, 98), 180, 360, fill=W, width=LW)
    line(d, [(52, 60), (74, 40)])
    label_tag(d)


@glyph("ruler")
def _(d):
    d.polygon([(20, 84), (84, 20), (108, 44), (44, 108)], outline=W, width=LW)
    for i in range(1, 5):
        x, y = 20 + i * 13, 84 - i * 13
        d.line([(x, y), (x + 12, y + 12)], fill=W, width=6)


@glyph("bars")
def _(d):
    for i, h in enumerate((30, 50, 66, 84)):
        x = 22 + i * 22
        d.rectangle((x, 106 - h, x + 14, 106), fill=W)


@glyph("network")
def _(d):
    pts = [(30, 30), (98, 40), (64, 96), (28, 92)]
    for a, b in ((0, 1), (1, 2), (2, 3), (0, 2)):
        d.line([pts[a], pts[b]], fill=W, width=LW - 2)
    for x, y in pts:
        d.ellipse((x - 11, y - 11, x + 11, y + 11), fill=W)


@glyph("check")
def _(d):
    d.ellipse((18, 18, 110, 110), outline=W, width=LW)
    check(d, 40, 66, 48)


@glyph("pencil")
def _(d):
    d.polygon([(30, 86), (82, 34), (98, 50), (46, 102)], fill=W)
    d.polygon([(30, 86), (46, 102), (22, 110)], fill=W)


@glyph("schematic")
def _(d):
    for x, y in ((20, 24), (74, 24), (47, 80)):
        d.rectangle((x, y, x + 34, y + 26), outline=W, width=LW - 2)
    line(d, [(37, 50), (60, 80)], LW - 3)
    line(d, [(91, 50), (68, 80)], LW - 3)


@glyph("rain")
def _(d):
    d.ellipse((20, 26, 64, 64), fill=W)
    d.ellipse((46, 16, 100, 64), fill=W)
    d.rectangle((40, 44, 92, 64), fill=W)
    for x in (36, 60, 84):
        line(d, [(x, 78), (x - 8, 104)], 7)


@glyph("idf")
def _(d):
    line(d, [(20, 18), (20, 108), (110, 108)], 7)
    for k in (0.6, 1.0, 1.5):
        d.line(curve(lambda t: 104 - 80 * k / (1 + 6 * t) + 0 * t, 26, 106), fill=W, width=6)


@glyph("clock")
def _(d):
    d.ellipse((18, 18, 110, 110), outline=W, width=LW)
    line(d, [(64, 64), (64, 34)])
    line(d, [(64, 64), (86, 76)])


@glyph("soil")
def _(d):
    for i, y in enumerate((30, 58, 86)):
        d.rectangle((18, y, 110, y + 20), fill=W if i != 1 else None, outline=W, width=6)


@glyph("infiltrate")
def _(d):
    d.rectangle((16, 64, 112, 72), fill=W)
    for x in (34, 64, 94):
        line(d, [(x, 20), (x, 52)], 7)
        line(d, [(x, 82), (x, 108)], 7)
        d.polygon([(x - 10, 98), (x + 10, 98), (x, 112)], fill=W)


@glyph("drop")
def _(d):
    drop(d, 64, 78, 30)


@glyph("unit_hydro")
def _(d):
    line(d, [(18, 106), (110, 106)], 7)
    d.polygon([(22, 104), (52, 26), (104, 104)], outline=W, width=LW)


@glyph("hydrograph")
def _(d):
    line(d, [(18, 18), (18, 106), (110, 106)], 7)
    d.line(curve(lambda t: 104 - 78 * (t * 3.2) ** 2 * math.exp(2 * (1 - t * 3.2)) / math.exp(0), 22, 108),
           fill=W, width=LW)


@glyph("route")
def _(d):
    d.line(curve(lambda t: 100 - 70 * math.exp(-((t - 0.3) / 0.13) ** 2), 16, 112), fill=W, width=6)
    d.line(curve(lambda t: 100 - 46 * math.exp(-((t - 0.6) / 0.2) ** 2), 16, 112), fill=W, width=LW)
    line(d, [(48, 22), (84, 22)], 6)
    d.polygon([(84, 12), (100, 22), (84, 32)], fill=W)


@glyph("calendar")
def _(d):
    d.rectangle((20, 28, 108, 106), outline=W, width=LW)
    d.rectangle((20, 28, 108, 46), fill=W)
    for r in range(3):
        for c in range(4):
            d.rectangle((32 + c * 18, 56 + r * 16, 42 + c * 18, 64 + r * 16), fill=W)


@glyph("pond")
def _(d):
    d.polygon([(14, 40), (34, 96), (94, 96), (114, 40)], outline=W, width=LW)
    d.polygon([(26, 66), (34, 90), (94, 90), (102, 66)], fill=W)


@glyph("prepost")
def _(d):
    line(d, [(16, 106), (112, 106)], 6)
    d.line(curve(lambda t: 104 - 84 * math.exp(-((t - 0.35) / 0.15) ** 2), 18, 110), fill=W, width=6)
    d.line(curve(lambda t: 104 - 44 * math.exp(-((t - 0.55) / 0.25) ** 2), 18, 110), fill=W, width=LW + 2)


@glyph("plant")
def _(d):
    line(d, [(64, 108), (64, 50)])
    d.chord((22, 30, 70, 78), 180, 0, fill=W)
    d.chord((58, 14, 106, 62), 180, 0, fill=W)
    d.rectangle((18, 100, 110, 110), fill=W)


@glyph("reeds")
def _(d):
    for x, h in ((34, 66), (56, 84), (78, 72), (98, 56)):
        line(d, [(x, 100), (x, 100 - h)], 7)
        d.ellipse((x - 7, 100 - h - 2, x + 7, 100 - h + 22), fill=W)
    d.line(curve(lambda t: 104 + 4 * math.sin(t * 12), 14, 114), fill=W, width=6)


@glyph("drop_check")
def _(d):
    drop(d, 50, 80, 28)
    check(d, 70, 92, 38, LW, W)


@glyph("box_size")
def _(d):
    d.rectangle((28, 40, 100, 100), outline=W, width=LW)
    line(d, [(28, 22), (100, 22)], 6)
    for x in (28, 100):
        d.line([(x, 14), (x, 30)], fill=W, width=6)


@glyph("train")
def _(d):
    for x in (14, 50, 86):
        d.rectangle((x, 46, x + 28, 82), fill=W)
    for x in (42, 78):
        d.line([(x, 64), (x + 8, 64)], fill=W, width=6)


@glyph("star")
def _(d):
    pts = []
    for i in range(10):
        r = 48 if i % 2 == 0 else 20
        a = math.radians(-90 + i * 36)
        pts.append((64 + r * math.cos(a), 68 + r * math.sin(a)))
    d.polygon(pts, fill=W)


@glyph("sediment")
def _(d):
    d.polygon([(14, 106), (50, 52), (72, 74), (88, 60), (114, 106)], fill=W)
    for x, y in ((40, 24), (64, 34), (88, 22)):
        d.ellipse((x - 7, y - 7, x + 7, y + 7), fill=W)


@glyph("basin")
def _(d):
    d.polygon([(14, 36), (36, 100), (92, 100), (114, 36)], outline=W, width=LW)
    for x, y in ((48, 84), (64, 78), (80, 86), (58, 92), (72, 92)):
        d.ellipse((x - 7, y - 7, x + 7, y + 7), fill=W)


@glyph("hgl")
def _(d):
    d.line([(16, 100), (112, 82)], fill=W, width=LW)
    for i in range(6):
        x = 16 + i * 17
        d.line([(x, 46 - i * 3), (x + 10, 46 - i * 3 - 2)], fill=W, width=7)


@glyph("wave")
def _(d):
    d.line(curve(lambda t: 50 + 14 * math.sin(t * 2 * math.pi * 1.5), 14, 114), fill=W, width=LW)
    line(d, [(14, 98), (114, 82)], 7)


@glyph("profile")
def _(d):
    d.line([(14, 34), (44, 50), (76, 46), (114, 66)], fill=W, width=6)
    d.rectangle((26, 82, 102, 98), outline=W, width=6)
    line(d, [(14, 112), (114, 112)], 5)


@glyph("export")
def _(d):
    d.rectangle((18, 46, 74, 108), outline=W, width=LW)
    line(d, [(54, 64), (104, 22)])
    d.polygon([(82, 16), (112, 14), (110, 44)], fill=W)


@glyph("import")
def _(d):
    d.rectangle((18, 46, 74, 108), outline=W, width=LW)
    line(d, [(104, 22), (54, 64)])
    d.polygon([(40, 52), (42, 82), (70, 78)], fill=W)


@glyph("culvert")
def _(d):
    d.polygon([(10, 100), (40, 42), (88, 42), (118, 100)], fill=W)
    d.ellipse((44, 62, 84, 102), fill=(0, 0, 0, 0))


@glyph("grate")
def _(d):
    d.rectangle((22, 22, 106, 106), outline=W, width=LW)
    for x in (44, 64, 84):
        d.line([(x, 30), (x, 98)], fill=W, width=7)


@glyph("pump")
def _(d):
    d.ellipse((20, 30, 92, 102), outline=W, width=LW)
    d.rectangle((82, 30, 112, 50), fill=W)
    d.polygon([(46, 50), (46, 84), (76, 66)], fill=W)


@glyph("magnifier")
def _(d):
    d.ellipse((18, 18, 82, 82), outline=W, width=LW + 2)
    line(d, [(76, 76), (106, 106)], LW + 5)


@glyph("clipboard")
def _(d):
    d.rectangle((24, 24, 104, 110), outline=W, width=LW)
    d.rectangle((46, 14, 82, 34), fill=W)
    check(d, 42, 74, 44, LW)


@glyph("document")
def _(d):
    doc(d)
    for y in (60, 76, 92):
        d.line([(46, y), (84, y)], fill=W, width=6)


@glyph("document_pdf")
def _(d):
    doc(d)
    d.rectangle((44, 62, 86, 92), fill=W)


@glyph("nodes")
def _(d):
    for (a, b) in (((32, 32), (96, 64)), ((32, 96), (96, 64)), ((32, 32), (32, 96))):
        d.line([a, b], fill=W, width=6)
    for x, y in ((32, 32), (32, 96), (96, 64)):
        d.rectangle((x - 14, y - 14, x + 14, y + 14), fill=W)


@glyph("save")
def _(d):
    d.polygon([(20, 20), (92, 20), (108, 36), (108, 108), (20, 108)], outline=W, width=LW)
    d.rectangle((38, 20, 82, 46), fill=W)
    d.rectangle((36, 70, 92, 108), outline=W, width=6)


@glyph("folder")
def _(d):
    d.polygon([(14, 30), (50, 30), (60, 42), (114, 42), (114, 102), (14, 102)], fill=W)


@glyph("map")
def _(d):
    d.polygon([(14, 30), (48, 18), (80, 30), (114, 18), (114, 98), (80, 110), (48, 98), (14, 110)],
              outline=W, width=LW - 2)
    d.line([(48, 18), (48, 98)], fill=W, width=6)
    d.line([(80, 30), (80, 110)], fill=W, width=6)


@glyph("dollar")
def _(d):
    d.arc((30, 22, 98, 66), 90, 330, fill=W, width=LW + 1)
    d.arc((30, 62, 98, 106), 270, 150, fill=W, width=LW + 1)
    line(d, [(64, 10), (64, 118)], 7)


@glyph("key")
def _(d):
    d.ellipse((14, 40, 60, 86), outline=W, width=LW + 2)
    line(d, [(58, 63), (112, 63)], LW + 2)
    d.rectangle((90, 63, 100, 84), fill=W)
    d.rectangle((104, 63, 112, 78), fill=W)


@glyph("badge")
def _(d):
    d.ellipse((30, 14, 98, 82), outline=W, width=LW)
    d.polygon([(42, 74), (36, 114), (64, 98), (92, 114), (86, 74)], fill=W)
    check(d, 48, 48, 32, 8)


@glyph("info")
def _(d):
    d.ellipse((18, 18, 110, 110), outline=W, width=LW)
    d.ellipse((56, 32, 72, 48), fill=W)
    d.rectangle((57, 56, 71, 94), fill=W)


def render(colour, gname):
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=22, fill=hexrgb(colour))
    g = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    G[gname](ImageDraw.Draw(g))
    # glyph pixels that were "erased" (alpha 0 fill on a white shape) cut through
    im.alpha_composite(g)
    if gname == "culvert":  # opening shows the background colour
        ImageDraw.Draw(im).ellipse((44, 62, 84, 102), fill=hexrgb(colour))
    return im


def main():
    spec = json.load(open(os.path.join(HERE, "ribbon.json"), encoding="utf-8"))
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".png"):
            os.remove(os.path.join(OUT, f))
    n = 0
    for p in spec:
        for b in p["buttons"]:
            big = render(p["colour"], b["glyph"])
            stem = "hc_" + b["cmd"][3:].lower()
            for px in (16, 32):
                big.resize((px, px), Image.LANCZOS).save(os.path.join(OUT, f"{stem}_{px}.png"))
            n += 1
    # contact sheet for eyeballing
    sheet = Image.new("RGBA", (14 * 72, 4 * 72 + 8), (240, 240, 240, 255))
    i = 0
    for p in spec:
        for b in p["buttons"]:
            ic = Image.open(os.path.join(OUT, "hc_" + b["cmd"][3:].lower() + "_32.png"))
            sheet.paste(ic.resize((64, 64), Image.NEAREST), ((i % 14) * 72 + 4, (i // 14) * 72 + 4), ic.resize((64, 64), Image.NEAREST))
            i += 1
    sheet.save(os.path.join(ROOT, "packaging", "icons-sheet.png"))
    print(f"{n} commands -> {2 * n} icons in {OUT}")


if __name__ == "__main__":
    main()
