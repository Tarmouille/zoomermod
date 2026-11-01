# Image "photocopie de fesses" (tâche Photocopie de fesses) : rendu de photocopieuse (gris, grain, traînées, bords sombres)
# d'un postérieur cartoon. Usage : python tools/make_buttcopy.py DaleMod/Assets/buttcopy.png [aperçu.png]
import random, sys
from PIL import Image, ImageDraw, ImageFilter

W, H = 512, 512
rnd = random.Random(3)
img = Image.new("L", (W, H), 238)
d = ImageDraw.Draw(img)

# Postérieur : deux fesses (calques séparés fusionnés en gardant le plus clair -> sillon naturel au milieu),
# ombrées vers les bords comme écrasées sur la vitre, plis sous chaque fesse
from PIL import ImageChops
def cheek(cx, cy, rx, ry):
    layer = Image.new("L", (W, H), 0)
    ld = ImageDraw.Draw(layer)
    for k in range(48, 0, -1):
        t = k / 48
        ld.ellipse((cx - rx * t, cy - ry * t, cx + rx * t, cy + ry * t), fill=int(70 + 140 * (1 - t) ** 0.6))
    return layer

cheeks = ImageChops.lighter(cheek(W * 0.355, H * 0.53, 128, 158), cheek(W * 0.645, H * 0.53, 128, 158))
mask = cheeks.point(lambda v: 255 if v > 0 else 0)
img.paste(cheeks, (0, 0), mask)
d = ImageDraw.Draw(img)
d.line([(W * 0.5, H * 0.27), (W * 0.497, H * 0.45), (W * 0.503, H * 0.62), (W * 0.5, H * 0.72)], fill=35, width=6)   # la raie
d.arc((W * 0.12, H * 0.55, W * 0.49, H * 0.9), 25, 150, fill=60, width=5)   # plis sous les fesses
d.arc((W * 0.51, H * 0.55, W * 0.88, H * 0.9), 30, 155, fill=60, width=5)

# Effet photocopieuse : bords noirs (couvercle ouvert), grain, traînées verticales, contraste dur
img = img.filter(ImageFilter.GaussianBlur(2))
px = img.load()
for y in range(H):
    for x in range(W):
        v = px[x, y] + rnd.randint(-22, 22)
        edge = min(x, y, W - 1 - x, H - 1 - y)
        if edge < 40: v -= int((40 - edge) * 4.5)
        px[x, y] = max(0, min(255, v))
d = ImageDraw.Draw(img)
for _ in range(3):
    x = rnd.randint(0, W)
    d.line((x, 0, x + rnd.randint(-3, 3), H), fill=rnd.randint(150, 205), width=rnd.randint(1, 3))
img = img.point(lambda v: 0 if v < 55 else (255 if v > 225 else v))
# Noir et blanc tramé (Floyd-Steinberg) : l'imprimante du jeu n'accepte que les couleurs de la palette de Paint
img = img.convert("1").convert("RGB")
img.save(sys.argv[1] if len(sys.argv) > 1 else "buttcopy.png")
if len(sys.argv) > 2: img.save(sys.argv[2])
print("ok")
