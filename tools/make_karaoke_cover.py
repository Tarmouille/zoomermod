# Pochette du CD du karaoké (dessin original, façon synthwave) : ciel dégradé, soleil rayé, grille au sol, titre et artiste.
# Usage : python tools/make_karaoke_cover.py DaleMod/Assets/karaoke/rise.png
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

out = sys.argv[1]
S = 512
img = Image.new("RGB", (S, S))
d = ImageDraw.Draw(img)

# Ciel : violet nuit -> magenta à l'horizon
horizon = int(S * 0.62)
for y in range(horizon):
    t = y / horizon
    d.line([(0, y), (S, y)], fill=(int(20 + 150 * t * t), int(8 + 20 * t), int(45 + 70 * t)))
# Sol : presque noir
d.rectangle([0, horizon, S, S], fill=(12, 4, 24))

# Soleil : dégradé jaune -> rose, coupé de bandes horizontales vers le bas
sun = Image.new("RGB", (S, S))
sd = ImageDraw.Draw(sun)
cx, cy, r = S // 2, horizon - 20, 150
for y in range(cy - r, cy + r):
    t = (y - (cy - r)) / (2 * r)
    sd.line([(0, y), (S, y)], fill=(255, int(220 - 160 * t), int(70 + 90 * t)))
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)
y, gap = cy - 10, 4
while y < cy + r:
    md.rectangle([0, y, S, y + gap], fill=0)
    y += gap + 12
    gap += 2
md.rectangle([0, horizon, S, S], fill=0)
glow = mask.filter(ImageFilter.GaussianBlur(18))
img.paste((255, 80, 160), (0, 0), glow.point(lambda v: v // 2))
img.paste(sun, (0, 0), mask)

# Grille en perspective
d = ImageDraw.Draw(img)
grid = (255, 40, 200)
for i in range(1, 12):
    t = (i / 11) ** 2.2
    yy = horizon + int((S - horizon) * t)
    d.line([(0, yy), (S, yy)], fill=grid, width=2)
for i in range(-10, 11):
    d.line([(cx + i * 14, horizon), (cx + i * 110, S)], fill=grid, width=2)
d.line([(0, horizon), (S, horizon)], fill=(255, 120, 230), width=3)


def font(names, size):
    for n in names:
        try:
            return ImageFont.truetype(n, size)
        except OSError:
            pass
    return ImageFont.load_default()


def text_center(txt, f, y, fill, glow_col):
    w = d.textbbox((0, 0), txt, font=f)[2]
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(layer).text(((S - w) // 2, y), txt, font=f, fill=glow_col)
    img.paste(layer.filter(ImageFilter.GaussianBlur(6)), (0, 0), layer.filter(ImageFilter.GaussianBlur(6)))
    d.text(((S - w) // 2, y), txt, font=f, fill=fill)


title = font(["impact.ttf", "arialbd.ttf"], 64)
artist = font(["bahnschrift.ttf", "arialbd.ttf"], 30)
text_center("RISE OF THE", title, 22, (230, 250, 255), (0, 220, 255, 255))
text_center("MACHINES", title, 88, (230, 250, 255), (0, 220, 255, 255))
text_center("KARL CASEY", artist, S - 52, (255, 230, 250), (255, 40, 200, 255))

img.save(out)
print("pochette :", out)
