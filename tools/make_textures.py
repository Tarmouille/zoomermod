# Génère les textures des tupperwares (PNG 256x256) dans le dossier donné :
#   tupp_food.png    bolo vue à travers le plastique : sauce, spaghetti entremêlés, viande, léger voile
#   tupp_plastic.png plastique dépoli (propre)
#   tupp_lid.png     couvercle bleu à nervures
#   tupp_dirty.png   plastique taché (intérieur / parois du tupperware sale)
#   tupp_sauce.png   fond de sauce séchée avec quelques spaghetti oubliés
#   python make_textures.py <dossier Assets>
import math, os, random, sys
from PIL import Image, ImageDraw, ImageFilter

S = 256
out = sys.argv[1]
rnd = random.Random(42)

def noise_layer(base, amp, blur=1.5):
    img = Image.new("RGB", (S, S), base)
    px = img.load()
    for y in range(S):
        for x in range(S):
            n = rnd.uniform(-amp, amp)
            px[x, y] = tuple(max(0, min(255, int(c + n))) for c in base)
    return img.filter(ImageFilter.GaussianBlur(blur))

def strands(d, n, colors, width, length=(60, 160)):
    """Spaghetti : courbes sinueuses, avec reflet clair et bord sombre."""
    for _ in range(n):
        x, y = rnd.uniform(-20, S + 20), rnd.uniform(-20, S + 20)
        a = rnd.uniform(0, math.tau)
        pts = []
        for _ in range(int(rnd.uniform(*length) / 4)):
            a += rnd.uniform(-0.35, 0.35)
            x += 4 * math.cos(a); y += 4 * math.sin(a)
            pts.append((x, y))
        if len(pts) < 2: continue
        dark, mid, light = colors
        d.line(pts, fill=dark, width=width + 2, joint="curve")
        d.line(pts, fill=mid, width=width, joint="curve")
        d.line([(px - 0.6, py - 0.6) for px, py in pts], fill=light, width=max(1, width // 3))

def blobs(d, n, color, r=(3, 9)):
    for _ in range(n):
        x, y, rr = rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(*r)
        d.ellipse([x - rr, y - rr * rnd.uniform(0.6, 1), x + rr, y + rr], fill=color)

# --- bolo vue à travers le plastique ---
food = noise_layer((150, 40, 22), 18, 2)
d = ImageDraw.Draw(food)
strands(d, 70, ((150, 105, 40), (238, 200, 110), (252, 232, 170)), 5)
blobs(d, 60, (110, 45, 22), (3, 8))           # viande hachée
blobs(d, 35, (175, 50, 25), (4, 10))          # sauce par-dessus les pâtes
strands(d, 25, ((150, 105, 40), (238, 200, 110), (252, 232, 170)), 5)
blobs(d, 25, (95, 120, 50), (1, 2))           # herbes
food = food.filter(ImageFilter.GaussianBlur(0.8))
veil = Image.new("RGB", (S, S), (235, 235, 235))
food = Image.blend(food, veil, 0.18)          # voile du plastique
food.save(os.path.join(out, "tupp_food.png"))

# --- plastique dépoli ---
plastic = noise_layer((228, 232, 234), 10, 2.5)
d = ImageDraw.Draw(plastic)
for i in range(0, S, 32):                      # légères marques de moulage
    d.line([(0, i), (S, i)], fill=(218, 222, 226), width=1)
plastic.save(os.path.join(out, "tupp_plastic.png"))

# --- couvercle bleu à nervures ---
lid = noise_layer((62, 132, 214), 8, 2)
d = ImageDraw.Draw(lid)
m = 28
d.rounded_rectangle([m, m, S - m, S - m], radius=26, outline=(40, 95, 175), width=6)
d.rounded_rectangle([m + 12, m + 12, S - m - 12, S - m - 12], radius=18, outline=(92, 160, 230), width=3)
for i in range(m + 30, S - m - 30, 14):        # nervures centrales
    d.line([(m + 30, i), (S - m - 30, i)], fill=(52, 115, 200), width=3)
    d.line([(m + 30, i + 2), (S - m - 30, i + 2)], fill=(95, 165, 232), width=1)
lid.save(os.path.join(out, "tupp_lid.png"))

# --- plastique sale (taché orange, typique de la bolo) ---
dirty = noise_layer((222, 200, 170), 12, 2.5)
d = ImageDraw.Draw(dirty)
for _ in range(14):                            # auréoles orangées
    x, y, r = rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(15, 45)
    d.ellipse([x - r, y - r * 0.7, x + r, y + r * 0.7], fill=(214, 160, 110))
for _ in range(10):                            # coulures
    x = rnd.uniform(0, S); y = rnd.uniform(0, S * 0.5)
    d.line([(x, y), (x + rnd.uniform(-6, 6), y + rnd.uniform(30, 90))], fill=(170, 80, 40), width=int(rnd.uniform(3, 7)))
blobs(d, 25, (160, 60, 30), (2, 6))
dirty = dirty.filter(ImageFilter.GaussianBlur(1.6))
dirty.save(os.path.join(out, "tupp_dirty.png"))

# --- fond de sauce séchée ---
sauce = noise_layer((140, 38, 20), 20, 2)
d = ImageDraw.Draw(sauce)
blobs(d, 50, (115, 30, 15), (5, 14))
blobs(d, 30, (175, 70, 35), (3, 8))
strands(d, 6, ((150, 105, 40), (238, 200, 110), (252, 232, 170)), 5)
sauce = sauce.filter(ImageFilter.GaussianBlur(1.0))
sauce.save(os.path.join(out, "tupp_sauce.png"))
print("textures écrites dans", out)
