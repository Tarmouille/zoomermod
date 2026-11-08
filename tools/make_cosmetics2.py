# Cosmétiques ZoomerMod, 2e lot : chapeaux (sombrero, casque viking, casque de Solaire), images à projeter sur les
# vêtements (logos, soleil de Solaire), textures (paille, cotte de mailles), icônes.
# Sortie : DaleMod/Assets/cosmetics/
#
# Repère des chapeaux : origine au SOMMET de la tête (centre), x à droite, y en haut, z vers l'avant, en mètres pour une
# tête de 0,28 m de large (mis à l'échelle en jeu). Un groupe "g" par matière (sous-maillage).
# Images à projeter : .rgba = largeur (int32), hauteur (int32), puis les pixels RGBA ligne par ligne, de haut en bas.
import math, os, struct, random
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, '..', 'DaleMod', 'Assets', 'cosmetics')
SCRATCH = os.environ.get('COSMETIC_SRC', '')

def write_rgba(img, name):
    img = img.convert('RGBA')
    w, h = img.size
    with open(os.path.join(OUT, name), 'wb') as f:
        f.write(struct.pack('<ii', w, h))
        f.write(img.tobytes())

# ------------------------------------------------------------------ maillages
class Obj:
    def __init__(self): self.groups = []
    def group(self, name):
        g = (name, [], [])
        self.groups.append(g)
        return g
    def save(self, name):
        lines = [f'# {name} — ZoomerMod (tools/make_cosmetics2.py)']
        off = 0
        for gname, verts, faces in self.groups:
            lines.append(f'g {gname}')
            lines += [f'v {x:.5f} {y:.5f} {z:.5f}' for x, y, z in verts]
            lines += ['f ' + ' '.join(str(off + i + 1) for i in f) for f in faces]
            off += len(verts)
        open(os.path.join(OUT, name), 'w').write('\n'.join(lines) + '\n')

def lathe(g, profile, seg=24, a0=0.0, a1=2 * math.pi, closed=True):
    """Révolution autour de y : profile = [(rayon, y), ...] ; les faces regardent vers l'extérieur."""
    verts, faces = g[1], g[2]
    base = len(verts)
    n = seg if closed else seg + 1
    for i in range(n):
        a = a0 + (a1 - a0) * i / seg
        for r, y in profile:
            verts.append((r * math.sin(a), y, r * math.cos(a)))
    m = len(profile)
    for i in range(seg):
        i2 = (i + 1) % n if closed else i + 1
        for j in range(m - 1):
            a, b = base + i * m + j, base + i * m + j + 1
            c, d = base + i2 * m + j + 1, base + i2 * m + j
            faces.append((a, d, c, b))

def box(g, cx, cy, cz, sx, sy, sz):
    verts, faces = g[1], g[2]
    b = len(verts)
    for dx in (-1, 1):
        for dy in (-1, 1):
            for dz in (-1, 1):
                verts.append((cx + dx * sx / 2, cy + dy * sy / 2, cz + dz * sz / 2))
    for q in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        faces.append(tuple(b + i for i in q))

def tube(g, path, radii, sides=8):
    verts, faces = g[1], g[2]
    base = len(verts)
    n = len(path)
    for i, p in enumerate(path):
        a = np.array(path[max(i - 1, 0)]); b = np.array(path[min(i + 1, n - 1)])
        t = b - a; t = t / (np.linalg.norm(t) or 1)
        ref = np.array([0, 0, 1.0]) if abs(t[2]) < 0.9 else np.array([1.0, 0, 0])
        u = np.cross(t, ref); u /= np.linalg.norm(u); v = np.cross(t, u)
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            q = np.array(p) + (u * math.cos(ang) + v * math.sin(ang)) * radii[i]
            verts.append(tuple(q))
    for i in range(n - 1):
        for k in range(sides):
            a = base + i * sides + k; b = base + i * sides + (k + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    verts.append(tuple(path[-1])); tip = len(verts) - 1
    for k in range(sides):
        faces.append((base + (n - 1) * sides + k, base + (n - 1) * sides + (k + 1) % sides, tip))

# --- Sombrero : couronne haute arrondie ; bord d'une seule pièce (dessus, bout relevé, dessous) ; bande
o = Obj()
straw = o.group('paille')
crown = [(0.152, -0.075), (0.145, -0.01), (0.125, 0.06), (0.095, 0.11), (0.055, 0.135), (0.0, 0.14)]
lathe(straw, crown, seg=32)
brim = [(0.152, -0.07), (0.21, -0.082), (0.26, -0.078), (0.285, -0.06), (0.293, -0.036), (0.287, -0.03), (0.272, -0.062), (0.24, -0.09), (0.20, -0.094), (0.152, -0.085)]
lathe(straw, brim, seg=32)
band = o.group('bande')
lathe(band, [(0.155, -0.068), (0.149, -0.03)], seg=32)
o.save('sombrero.obj')

# --- Casque viking : dôme, bandeau et croix en bronze, nasal, cornes
o = Obj()
steel = o.group('acier')
prof = [(0.158 * math.cos(t), -0.125 + 0.15 * math.sin(t)) for t in [i * (math.pi / 2) / 8 for i in range(9)]]
lathe(steel, prof[::-1], seg=28)
bronze = o.group('bronze')
lathe(bronze, [(0.163, -0.13), (0.163, -0.10)][::-1], seg=28)
for a in (0.0, math.pi / 2):   # bandes en croix sur le dôme
    for sgn in (1, -1):
        pts = []
        for i in range(9):
            t = i * (math.pi / 2) / 8
            r, y = 0.161 * math.cos(t), -0.125 + 0.153 * math.sin(t)
            pts.append((sgn * r * math.sin(a), y, sgn * r * math.cos(a)))
        verts, faces = bronze[1], bronze[2]
        b = len(verts)
        side = np.array([math.cos(a), 0, -math.sin(a)])
        for (x, y, z) in pts:
            for e in (-0.012, 0.012):
                verts.append((x + side[0] * e, y, z + side[2] * e))
        for i in range(len(pts) - 1):
            faces.append((b + i * 2, b + i * 2 + 1, b + i * 2 + 3, b + i * 2 + 2))
box(bronze, 0.0, -0.16, 0.168, 0.022, 0.07, 0.008)   # nasal
horn = o.group('corne')
for sx in (1, -1):
    path = []
    for i in range(12):
        t = i / 11
        x = sx * (0.115 + 0.13 * t)
        y = -0.06 + 0.03 * t + 0.13 * t * t
        z = 0.02 - 0.03 * t
        path.append((x, y, z))
    tube(horn, path, [0.034 * (1 - t) ** 0.8 + 0.003 for t in [i / 11 for i in range(12)]], sides=10)
o.save('viking.obj')

# --- Casque de Solaire : seau, dessus bombé, collerette évasée, fente des yeux, bande rivetée, plumet
o = Obj()
steel = o.group('acier')
lathe(steel, [(0.0, 0.03), (0.09, 0.028), (0.15, 0.015), (0.162, -0.0), (0.166, -0.22), (0.168, -0.235)][::-1], seg=28)
lathe(steel, [(0.168, -0.235), (0.185, -0.255), (0.20, -0.275)][::-1], seg=28)   # collerette
lathe(steel, [(0.20, -0.277), (0.16, -0.277)][::-1], seg=28)                   # dessous de la collerette
slit = o.group('fente')
# fente horizontale sur l'avant (panneau sombre posé sur la surface), et petite fente verticale
def front_panel(g, y0, y1, half_angle, r):
    verts, faces = g[1], g[2]
    b = len(verts)
    n = 8
    for i in range(n + 1):
        a = -half_angle + 2 * half_angle * i / n
        for y in (y0, y1):
            verts.append((r * math.sin(a), y, r * math.cos(a)))
    for i in range(n):
        faces.append((b + i * 2, b + i * 2 + 1, b + i * 2 + 3, b + i * 2 + 2))
front_panel(slit, -0.125, -0.103, 0.55, 0.1765)
plate = o.group('plaque')
front_panel(plate, -0.142, -0.086, 0.70, 0.1712)   # plaque autour de la fente (en relief, derrière la fente)
box(plate, 0.0, -0.18, 0.172, 0.03, 0.09, 0.008)   # bande verticale sous la fente
rivet = o.group('rivet')
for y in (-0.155, -0.18, -0.205):
    box(rivet, 0.0, y, 0.177, 0.012, 0.012, 0.008)
plume = o.group('plumet')
path = [(0.0, 0.028 + 0.06 * t, -0.02 - 0.05 * t * t) for t in [i / 10 for i in range(11)]]
path = [(0.0, 0.028 + 0.17 * t, -0.03 - 0.10 * t * t) for t in [i / 10 for i in range(11)]]
tube(plume, path, [0.018 * math.sin(math.pi * (0.15 + 0.85 * t)) + 0.002 for t in [i / 10 for i in range(11)]], sides=6)
o.save('solaire_helm.obj')

# ------------------------------------------------------------------ textures
S = 256
rnd = random.Random(3)
# Paille tressée
img = Image.new('RGB', (S, S), (214, 178, 102))
d = ImageDraw.Draw(img)
for y in range(0, S, 8):
    for x in range(0, S, 16):
        off = 8 if (y // 8) % 2 else 0
        c = rnd.randint(-18, 12)
        d.rectangle([x + off, y, x + off + 13, y + 6], fill=(226 + c, 190 + c, 114 + c))
        d.line([x + off, y + 7, x + off + 15, y + 7], fill=(160, 125, 60))
img.filter(ImageFilter.SMOOTH).save(os.path.join(OUT, 'straw.png'))
# Bande du sombrero : zigzags
img = Image.new('RGB', (S, 64), (175, 30, 35))
d = ImageDraw.Draw(img)
for x in range(-32, S + 32, 32):
    d.polygon([(x, 64), (x + 16, 8), (x + 32, 64)], fill=(30, 120, 70))
    d.polygon([(x + 8, 64), (x + 16, 34), (x + 24, 64)], fill=(240, 200, 60))
img.save(os.path.join(OUT, 'sombrero_band.png'))
# Cotte de mailles : anneaux en quinconce, raccordable
img = Image.new('RGB', (S, S), (58, 60, 64))
d = ImageDraw.Draw(img)
step = 16
for j in range(-1, S // step + 2):
    for i in range(-1, S // step + 2):
        cx = i * step + (step // 2 if j % 2 else 0); cy = j * step * 0.75
        d.ellipse([cx - 9, cy - 9, cx + 9, cy + 9], outline=(150, 152, 158), width=3)
        d.arc([cx - 9, cy - 9, cx + 9, cy + 9], 200, 330, fill=(205, 207, 212), width=2)
chain = img.filter(ImageFilter.SMOOTH)
chain.save(os.path.join(OUT, 'chainmail.png'))
write_rgba(chain, 'chainmail.rgba')

# ------------------------------------------------------------------ images projetées
# Soleil de Solaire : disque orange-jaune, rayons alternés, visage
W = 512
img = Image.new('RGBA', (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
c = W / 2
for k in range(16):
    a = 2 * math.pi * k / 16 - math.pi / 2
    long = k % 2 == 0
    r1 = W * (0.48 if long else 0.40)
    w = 0.13 if long else 0.10
    tip = (c + r1 * math.cos(a), c + r1 * math.sin(a))
    l = (c + W * 0.2 * math.cos(a - w), c + W * 0.2 * math.sin(a - w))
    r = (c + W * 0.2 * math.cos(a + w), c + W * 0.2 * math.sin(a + w))
    d.polygon([l, tip, r], fill=(225, 92, 30, 255) if long else (245, 170, 40, 255))
d.ellipse([c - W * 0.22, c - W * 0.22, c + W * 0.22, c + W * 0.22], fill=(252, 196, 50, 255), outline=(200, 80, 25, 255), width=8)
d.arc([c - W * 0.12, c - W * 0.02, c + W * 0.12, c + W * 0.13], 20, 160, fill=(170, 60, 20, 255), width=8)      # bouche
for sx in (-1, 1):
    d.ellipse([c + sx * W * 0.08 - 14, c - W * 0.07 - 10, c + sx * W * 0.08 + 14, c - W * 0.07 + 10], fill=(170, 60, 20, 255))
write_rgba(img, 'sun.rgba')
img.save(os.path.join(OUT, 'preview_sun.png'))

# Logo du t-shirt : rouge lumineux sur fond noir -> transparence d'après l'intensité du rouge
src = os.path.join(SCRATCH, 'logo_tee.png') if SCRATCH else ''
if src and os.path.exists(src):
    a = np.asarray(Image.open(src).convert('RGB'), dtype=float)
    lum = a.max(axis=2)
    alpha = np.clip((lum - 25) * 2.2, 0, 255)
    rgba = np.dstack([a[:, :, 0], a[:, :, 1], a[:, :, 2], alpha]).astype('uint8')
    im = Image.fromarray(rgba, 'RGBA')
    im.save(os.path.join(OUT, 'logo_tee.png'))
write_rgba(Image.open(os.path.join(OUT, 'logo_tee.png')).resize((512, 512)), 'logo_tee.rgba')
write_rgba(Image.open(os.path.join(OUT, 'carpenter_back.png')).resize((512, 512)), 'carpenter_back.rgba')

# ------------------------------------------------------------------ icônes
def icon_from(img, name, bg=None):
    im = img.convert('RGBA').resize((256, 256))
    if bg is not None:
        b = Image.new('RGBA', (256, 256), bg); b.alpha_composite(im); im = b
    im.save(os.path.join(OUT, name))

def icon_sombrero():
    im = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.ellipse([10, 140, 246, 200], fill=(214, 178, 102, 255), outline=(150, 115, 55, 255), width=4)
    d.polygon([(80, 165), (100, 60), (156, 60), (176, 165)], fill=(222, 186, 110, 255))
    d.ellipse([96, 48, 160, 76], fill=(222, 186, 110, 255))
    d.rectangle([84, 140, 172, 160], fill=(175, 30, 35, 255))
    return im
def icon_viking():
    im = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.pieslice([48, 60, 208, 220], 180, 360, fill=(150, 152, 158, 255))
    d.rectangle([48, 132, 208, 150], fill=(176, 120, 50, 255))
    d.rectangle([120, 60, 136, 190], fill=(176, 120, 50, 255))
    d.polygon([(50, 130), (10, 60), (30, 50), (62, 112)], fill=(235, 225, 200, 255))
    d.polygon([(206, 130), (246, 60), (226, 50), (194, 112)], fill=(235, 225, 200, 255))
    return im
def icon_solaire():
    im = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.rectangle([70, 50, 186, 200], fill=(150, 152, 158, 255))
    d.polygon([(70, 200), (186, 200), (220, 240), (36, 240)], fill=(130, 132, 138, 255))
    d.rectangle([80, 100, 176, 112], fill=(20, 20, 20, 255))
    d.rectangle([122, 112, 134, 196], fill=(120, 122, 128, 255))
    d.polygon([(128, 50), (150, 10), (140, 52)], fill=(200, 30, 30, 255))
    return im
icon_sombrero().save(os.path.join(OUT, 'icon_sombrero.png'))
icon_viking().save(os.path.join(OUT, 'icon_viking.png'))
icon_solaire().save(os.path.join(OUT, 'icon_solaire.png'))
icon_from(Image.open(os.path.join(OUT, 'logo_tee.png')), 'swatch_tee_logo.png', (20, 20, 20, 255))
icon_from(Image.open(os.path.join(OUT, 'carpenter_back.png')), 'swatch_hoodie_carpenter.png', (25, 25, 25, 255))
icon_from(img_sun := Image.open(os.path.join(OUT, 'preview_sun.png')), 'swatch_solaire.png', (240, 236, 225, 255))
icon_from(Image.open(os.path.join(OUT, 'chainmail.png')), 'swatch_chainmail.png')
print('ok')

# Tabard de Solaire : moitié gauche = devant, moitié droite = dos ; crème, soleil, bord du bas un peu effiloché
TW, TH = 1024, 1400
tab = Image.new('RGB', (TW, TH), (236, 230, 212))
d = ImageDraw.Draw(tab)
rnd2 = random.Random(11)
for k in range(4000):   # salissures légères
    x, y = rnd2.randrange(TW), rnd2.randrange(TH)
    c = rnd2.randint(205, 228)
    d.point((x, y), fill=(c, c - 6, c - 22))
sunimg = Image.open(os.path.join(OUT, 'preview_sun.png')).convert('RGBA').resize((380, 380))
for cx in (TW // 4, 3 * TW // 4):
    tab.paste(sunimg, (cx - 190, 170), sunimg)
    for x in range(cx - 256, cx + 256, 6):   # bas effiloché
        h = rnd2.randint(0, 40)
        d.rectangle([x, TH - h, x + 5, TH], fill=(205, 196, 172))
tab.filter(ImageFilter.SMOOTH).save(os.path.join(OUT, 'tabard.png'))
icon_from(tab.crop((0, 0, TW // 2, TH)).resize((256, 256)), 'icon_tabard.png')
print('tabard ok')

# Gilet du Dude (The Big Lebowski) : motif en bandes, pour le peignoir. Image raccordable en largeur ; la hauteur couvre
# le vêtement du col (haut) aux genoux (bas) ; maille beige à côtes verticales.
LW, LH = 512, 1024
beige, brown, dark = (200, 174, 134), (118, 80, 52), (64, 40, 28)
leb = Image.new('RGB', (LW, LH), beige)
d = ImageDraw.Draw(leb)
for x in range(0, LW, 8):   # côtes de la maille
    d.line([x, 0, x, LH], fill=(184, 158, 118), width=2)
def stripe(y0, y1, col=brown):
    d.rectangle([0, int(y0 * LH), LW, int(y1 * LH)], fill=col)
def meander(yc, h):
    """Frise en grecque (crochets en escalier)."""
    y0, y1 = int((yc - h / 2) * LH), int((yc + h / 2) * LH)
    step = 64; t = max(5, (y1 - y0) // 5)
    for x in range(0, LW, step):
        d.rectangle([x, y1 - t, x + step * 0.7, y1], fill=dark)
        d.rectangle([x + step * 0.7 - t, y0, x + step * 0.7, y1], fill=dark)
        d.rectangle([x + step * 0.3, y0, x + step * 0.7, y0 + t], fill=dark)
        d.rectangle([x + step * 0.3, y0, x + step * 0.3 + t, y0 + (y1 - y0) * 0.6], fill=dark)
def motif_band(y0, y1):
    """Grande bande : losange creux avec croix au centre, spirales carrées de part et d'autre, tirets."""
    Y0, Y1 = int(y0 * LH), int(y1 * LH); cy = (Y0 + Y1) // 2; hh = (Y1 - Y0) // 2
    for x in range(0, LW, 24): d.rectangle([x, Y0 - 14, x + 10, Y0 - 8], fill=dark); d.rectangle([x, Y1 + 8, x + 10, Y1 + 14], fill=dark)
    cx = LW // 2
    w = int(hh * 1.5)
    pts = [(cx, cy - hh), (cx + w, cy), (cx, cy + hh), (cx - w, cy)]
    d.polygon(pts, outline=dark); d.line(pts + [pts[0]], fill=dark, width=8)
    for dx, dy in ((0, 0),):   # croix centrale
        d.rectangle([cx - 30, cy - 10, cx + 30, cy + 10], fill=dark); d.rectangle([cx - 10, cy - 30, cx + 10, cy + 30], fill=dark)
    for sx in (-1, 1):          # croix secondaires dans le losange
        d.rectangle([cx + sx * w // 2 - 12, cy - 12, cx + sx * w // 2 + 12, cy + 12], fill=dark)
    for ex in (0, LW):          # spirales carrées aux bords (raccord sur la largeur)
        for oy in (-hh // 2, hh // 2):
            x0, y0b = ex - 40, cy + oy - 40
            for k, r in enumerate((40, 26, 12)):
                d.rectangle([x0 + 40 - r, y0b + 40 - r, x0 + 40 + r, y0b + 40 + r], outline=dark, width=7)
meander(0.11, 0.045)
stripe(0.155, 0.175)
motif_band(0.21, 0.37)
stripe(0.405, 0.425)
meander(0.465, 0.045)
stripe(0.51, 0.53)
stripe(0.90, 0.915)
leb = leb.filter(ImageFilter.SMOOTH)
leb.save(os.path.join(OUT, 'preview_lebowski.png'))
write_rgba(leb, 'lebowski.rgba')
icon_from(leb.crop((0, 60, 512, 572)), 'swatch_lebowski.png')
print('lebowski ok')

# --- Bonnet de Noël : bande de fourrure, cône rouge qui retombe sur le côté, pompon blanc
o = Obj()
fur = o.group('fourrure')
lathe(fur, [(0.150, -0.10), (0.162, -0.092), (0.165, -0.072), (0.160, -0.052), (0.148, -0.048)], seg=28)
red = o.group('rouge')
# cône : anneaux le long d'une courbe qui monte puis retombe vers l'arrière-droite
rings = 10
verts, faces = red[1], red[2]
base = len(verts)
centers = []
for i in range(rings + 1):
    t = i / rings
    cx = 0.10 * t * t
    cy = -0.05 + 0.15 * math.sin(t * math.pi * 0.62)
    cz = -0.05 * t
    r = 0.147 * (1 - t) + 0.010
    centers.append((cx, cy, cz))
    for k in range(20):
        a = 2 * math.pi * k / 20
        verts.append((cx + r * math.sin(a), cy, cz + r * math.cos(a)))
for i in range(rings):
    for k in range(20):
        a = base + i * 20 + k; b = base + i * 20 + (k + 1) % 20
        faces.append((a, a + 20, b + 20, b))
pom = o.group('pompon')
px, py, pz = centers[-1]
prof = [(0.035 * math.sin(t), 0.035 * math.cos(t)) for t in [i * math.pi / 6 for i in range(7)]]
vb = len(pom[1])
lathe(pom, [(r, y) for r, y in prof], seg=12)
pom[1][vb:] = [(x + px + 0.01, y + py, z + pz) for x, y, z in pom[1][vb:]]
o.save('santa_hat.obj')

def icon_santa():
    im = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    d.polygon([(50, 190), (206, 190), (200, 90), (130, 40), (90, 80)], fill=(200, 25, 30, 255))
    d.polygon([(130, 40), (215, 70), (205, 110)], fill=(185, 20, 25, 255))
    d.rounded_rectangle([36, 180, 220, 225], 18, fill=(245, 245, 245, 255))
    d.ellipse([200, 90, 245, 135], fill=(245, 245, 245, 255))
    return im
icon_santa().save(os.path.join(OUT, 'icon_santa.png'))
sw = Image.new('RGBA', (128, 128), (190, 20, 28, 255)); d = ImageDraw.Draw(sw); d.rectangle([0, 92, 128, 128], fill=(245, 245, 245, 255))
sw.save(os.path.join(OUT, 'swatch_noel.png'))
print('noel ok')
