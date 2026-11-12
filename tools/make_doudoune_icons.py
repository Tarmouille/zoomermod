# Icônes de la doudoune sans manches (DaleMod/Doudoune.cs) : fermée / ouverte, et pastilles des deux coloris
from PIL import Image, ImageDraw
import os

OUT = os.path.join(os.path.dirname(__file__), "..", "DaleMod", "Assets", "cosmetics")
S = 4   # suréchantillonnage


def icon(col, open_, path):
    W = 256 * S
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    p = lambda pts: [(x * S, y * S) for x, y in pts]
    dark = tuple(int(c * 0.55) for c in col)
    light = tuple(min(255, int(c * 1.6 + 30)) for c in col)
    # corps : épaules, emmanchures creusées, bas droit
    body = [(92, 40), (164, 40), (196, 58), (190, 96), (200, 130), (200, 226), (56, 226), (56, 130), (66, 96), (60, 58)]
    if open_:
        # chemise blanche et cravate dans l'ouverture
        d.polygon(p([(104, 44), (152, 44), (150, 226), (106, 226)]), fill=(236, 236, 240, 255))
        d.polygon(p([(121, 50), (135, 50), (131, 62), (125, 62)]), fill=(150, 24, 30, 255))
        d.polygon(p([(125, 62), (131, 62), (138, 170), (128, 184), (118, 170)]), fill=(170, 30, 36, 255))
        left = [(92, 40), (110, 40), (114, 226), (56, 226), (56, 130), (66, 96), (60, 58)]
        right = [(146, 40), (164, 40), (196, 58), (190, 96), (200, 130), (200, 226), (142, 226)]
        parts = [left, right]
    else:
        parts = [body]
    for part in parts:
        d.polygon(p(part), fill=col + (255,), outline=dark + (255,))
    # boudins : coutures horizontales, reflet au milieu de chaque boudin
    mask = Image.new("L", (W, W), 0)
    md = ImageDraw.Draw(mask)
    for part in parts:
        md.polygon(p(part), fill=255)
    lines = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    ld = ImageDraw.Draw(lines)
    y = 62
    while y < 226:
        ld.line(p([(40, y), (216, y)]), fill=dark + (255,), width=3 * S)
        ld.line(p([(40, y + 12), (216, y + 12)]), fill=light + (110,), width=4 * S)
        y += 26
    im.paste(lines, (0, 0), Image.composite(lines, Image.new("RGBA", (W, W), (0, 0, 0, 0)), mask).split()[3])
    # col montant
    if open_:
        d.polygon(p([(92, 40), (110, 40), (108, 22), (90, 26)]), fill=col + (255,), outline=dark + (255,))
        d.polygon(p([(146, 40), (164, 40), (166, 26), (148, 22)]), fill=col + (255,), outline=dark + (255,))
        d.line(p([(112, 40), (116, 226)]), fill=dark + (255,), width=4 * S)
        d.line(p([(144, 40), (140, 226)]), fill=dark + (255,), width=4 * S)
    else:
        d.polygon(p([(92, 40), (164, 40), (166, 22), (90, 22)]), fill=col + (255,), outline=dark + (255,))
        d.line(p([(128, 22), (128, 226)]), fill=(120, 120, 126, 255), width=3 * S)
        d.rectangle(p([(124, 26), (132, 40)]), fill=(150, 150, 156, 255))
    im = im.resize((256, 256), Image.LANCZOS)
    im.save(path)


icon((34, 34, 38), False, os.path.join(OUT, "icon_doudoune.png"))
icon((34, 34, 38), True, os.path.join(OUT, "icon_doudoune_ouverte.png"))

# Pastilles : un morceau de matelassé du coloris (noms d'après la couleur, comme dans Doudoune.Colors)
for col in ((38, 38, 42), (26, 36, 82)):
    W = 128
    sw = Image.new("RGBA", (W, W))
    for y in range(W):
        f = (y % 32 + 0.5) / 32
        seam = min(f, 1 - f)
        import math
        k = 0.55 + 0.45 * min(1, seam / 0.09) + 0.5 * math.sin(math.pi * f) ** 8
        for x in range(W):
            sw.putpixel((x, y), tuple(min(255, int(c * k * 1.6)) for c in col) + (255,))
    sw.save(os.path.join(OUT, "swatch_doudoune_%d_%d_%d.png" % col))
