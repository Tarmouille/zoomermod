# Cosmétiques ZoomerMod, 3e lot : chapeaux (couronne, bonnet de bouffon, casque audio, chapeau de
# pirate, haut-de-forme), objets du visage (lunettes 3D, monocle, yeux UwU, masque de Glucks), icônes. Sortie : DaleMod/Assets/cosmetics/
#
# Chapeaux : repère de make_cosmetics2.py (origine au SOMMET de la tête, x à droite, y en haut, z vers l'avant, tête de
# 0,28 m de large). Objets du visage : repère "visage" de Cosmetics.cs (origine au bout du nez, yeux à y = 0,05 et
# x = ±EYE ; z = hauteur au-dessus de la peau ou décalage, selon l'objet, voir FaceGear.cs). Un groupe "g" par matière ;
# le masque a des coordonnées de texture (vt).
import math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageOps

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, '..', 'DaleMod', 'Assets', 'cosmetics')
SRC = os.path.join(HERE, 'src')
EYE = 0.06          # demi-écart des yeux prévu (FaceGear recale en x sur les yeux réels)
EYE_Y = 0.05


class Obj:
    def __init__(self): self.groups = []

    def group(self, name):
        g = (name, [], [], [])   # nom, sommets, faces, uv
        self.groups.append(g)
        return g

    def save(self, name):
        lines = [f'# {name} — ZoomerMod (tools/make_cosmetics3.py)']
        off = 0
        for gname, verts, faces, uvs in self.groups:
            lines.append(f'g {gname}')
            lines += [f'v {x:.5f} {y:.5f} {z:.5f}' for x, y, z in verts]
            if uvs: lines += [f'vt {u:.5f} {v:.5f}' for u, v in uvs]
            if uvs: lines += ['f ' + ' '.join(f'{off + i + 1}/{off + i + 1}' for i in f) for f in faces]
            else: lines += ['f ' + ' '.join(str(off + i + 1) for i in f) for f in faces]
            off += len(verts)
        open(os.path.join(OUT, name), 'w').write('\n'.join(lines) + '\n')


def lathe(g, profile, seg=24, closed=True):
    verts, faces = g[1], g[2]
    base = len(verts)
    for i in range(seg):
        a = 2 * math.pi * i / seg
        for r, y in profile:
            verts.append((r * math.sin(a), y, r * math.cos(a)))
    m = len(profile)
    for i in range(seg):
        i2 = (i + 1) % seg
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


def tube(g, path, radii, sides=8, cap=True):
    verts, faces = g[1], g[2]
    base = len(verts)
    n = len(path)
    if not hasattr(radii, '__len__'): radii = [radii] * n
    for i, p in enumerate(path):
        a = np.array(path[max(i - 1, 0)]); b = np.array(path[min(i + 1, n - 1)])
        t = b - a; t = t / (np.linalg.norm(t) or 1)
        ref = np.array([0, 0, 1.0]) if abs(t[2]) < 0.9 else np.array([1.0, 0, 0])
        u = np.cross(t, ref); u /= np.linalg.norm(u); v = np.cross(t, u)
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            verts.append(tuple(np.array(p) + (u * math.cos(ang) + v * math.sin(ang)) * radii[i]))
    for i in range(n - 1):
        for k in range(sides):
            a = base + i * sides + k; b = base + i * sides + (k + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    if cap:
        for end, idx in ((path[0], 0), (path[-1], n - 1)):
            verts.append(tuple(end)); c = len(verts) - 1
            for k in range(sides):
                faces.append((base + idx * sides + k, base + idx * sides + (k + 1) % sides, c))


def sphere(g, cx, cy, cz, r, seg=10, rings=6):
    verts, faces = g[1], g[2]
    base = len(verts)
    for i in range(rings + 1):
        a = math.pi * i / rings
        for j in range(seg):
            b = 2 * math.pi * j / seg
            verts.append((cx + r * math.sin(a) * math.cos(b), cy + r * math.cos(a), cz + r * math.sin(a) * math.sin(b)))
    for i in range(rings):
        for j in range(seg):
            a, b = base + i * seg + j, base + i * seg + (j + 1) % seg
            faces.append((a, b, b + seg, a + seg))


def ribbon(g, path, width, normal=(0, 0, 1)):
    """Bande plate le long d'un chemin, dans le plan perpendiculaire à normal (pour les traits posés sur la peau)."""
    verts, faces = g[1], g[2]
    base = len(verts)
    n = len(path); nz = np.array(normal, float)
    for i, p in enumerate(path):
        a = np.array(path[max(i - 1, 0)]); b = np.array(path[min(i + 1, n - 1)])
        t = b - a; t /= (np.linalg.norm(t) or 1)
        s = np.cross(nz, t); s /= (np.linalg.norm(s) or 1)
        for e in (-width / 2, width / 2):
            verts.append(tuple(np.array(p) + s * e))
    for i in range(n - 1):
        faces.append((base + 2 * i, base + 2 * i + 1, base + 2 * i + 3, base + 2 * i + 2))


def disc(g, cx, cy, cz, rx, ry, seg=16):
    verts, faces = g[1], g[2]
    base = len(verts)
    verts.append((cx, cy, cz))
    for k in range(seg):
        a = 2 * math.pi * k / seg
        verts.append((cx + rx * math.cos(a), cy + ry * math.sin(a), cz))
    for k in range(seg):
        faces.append((base, base + 1 + k, base + 1 + (k + 1) % seg))


# ================================================================== chapeaux
# --- Couronne en or : bandeau, pointes, perles au bout, pierres
o = Obj()
gold = o.group('or')
verts, faces = gold[1], gold[2]
N, R0, R1, YB, YT, SP = 40, 0.128, 0.138, -0.06, -0.015, 0.06
b0 = len(verts)
for i in range(N):
    a = 2 * math.pi * i / N
    top = YT + (SP if i % 5 == 0 else 0.0)      # une pointe tous les 5 pas
    verts.append((R0 * math.sin(a), YB, R0 * math.cos(a)))
    verts.append((R1 * math.sin(a), top, R1 * math.cos(a)))
for i in range(N):
    j = (i + 1) % N
    faces.append((b0 + 2 * i, b0 + 2 * j, b0 + 2 * j + 1, b0 + 2 * i + 1))
lathe(gold, [(R0 - 0.002, YB - 0.004), (R0 + 0.008, YB - 0.004), (R0 + 0.008, YB + 0.012), (R0 - 0.002, YB + 0.012)], seg=40)
pearl = o.group('perle')
for i in range(0, N, 5):
    a = 2 * math.pi * i / N
    sphere(pearl, R1 * math.sin(a), YT + SP + 0.008, R1 * math.cos(a), 0.011, seg=8, rings=5)
ruby, saph = o.group('rubis'), o.group('saphir')
for k in range(8):
    a = 2 * math.pi * (k + 0.5) / 8
    r = R0 + 0.012
    sphere(ruby if k % 2 == 0 else saph, r * math.sin(a), YB + 0.022, r * math.cos(a), 0.011, seg=8, rings=4)
o.save('couronne.obj')

# --- Bonnet de bouffon : calotte, bandeau, deux cornes molles qui retombent, grelots
o = Obj()
pink, violet, band, bell = o.group('rose'), o.group('violet'), o.group('bande'), o.group('grelot')
cap = [(0.158 * math.cos(t), -0.085 + 0.11 * math.sin(t)) for t in [i * (math.pi / 2) / 8 for i in range(9)]][::-1]
lathe(pink, cap, seg=28)
lathe(band, [(0.166, -0.11), (0.168, -0.095), (0.166, -0.075)], seg=28)
for sx, grp in ((1, pink), (-1, violet)):
    path, radii = [], []
    for i in range(16):
        t = i / 15
        x = sx * (0.05 + 0.20 * t)
        yy = 0.0 + 0.11 * math.sin(t * math.pi * 0.85) - 0.07 * t * t
        z = -0.01 - 0.02 * t
        path.append((x, yy, z))
        radii.append(0.065 * (1 - t) ** 0.9 + 0.008)
    tube(grp, path, radii, sides=12)
    sphere(bell, path[-1][0] + sx * 0.012, path[-1][1] - 0.02, path[-1][2], 0.024, seg=10, rings=6)
path, radii = [], []                                   # 3e corne au milieu, vers l'arrière
for i in range(14):
    t = i / 13
    path.append((0.0, 0.01 + 0.12 * math.sin(t * math.pi * 0.8) - 0.05 * t * t, -0.03 - 0.17 * t))
    radii.append(0.06 * (1 - t) ** 0.9 + 0.008)
tube(violet, path, radii, sides=12)
sphere(bell, path[-1][0], path[-1][1] - 0.02, path[-1][2] - 0.01, 0.024, seg=10, rings=6)
o.save('bouffon.obj')

# --- Casque audio : arceau, écouteurs, coussins, micro devant la bouche
o = Obj()
black, pad, mic = o.group('noir'), o.group('coussin'), o.group('micro')
arc = []
for i in range(21):
    t = -math.pi / 2 + math.pi * i / 20
    arc.append((0.172 * math.sin(t), -0.10 + 0.135 * math.cos(t), 0.0))
tube(black, arc, 0.012, sides=8)
for sx in (1, -1):
    cup = [(sx * 0.163, -0.135, 0.0), (sx * 0.19, -0.135, 0.0)]
    tube(black, cup, 0.052, sides=16)
    tube(pad, [(sx * 0.148, -0.135, 0.0), (sx * 0.165, -0.135, 0.0)], 0.048, sides=16)
path = []
for i in range(12):
    t = i / 11
    path.append((0.19 - 0.12 * t, -0.15 - 0.08 * t, 0.02 + 0.14 * math.sin(t * math.pi / 2)))
tube(black, path, 0.006, sides=6, cap=False)
sphere(mic, path[-1][0], path[-1][1], path[-1][2], 0.016, seg=10, rings=6)
o.save('casque_audio.obj')

# --- Chapeau de pirate (bicorne) : calotte, deux ailes en demi-lune devant et derrière, galon doré, tête de mort
o = Obj()
blk, gal, skull = o.group('noir'), o.group('galon'), o.group('crane')
lathe(blk, [(0.155, -0.075), (0.15, 0.0), (0.12, 0.05), (0.0, 0.06)], seg=28)
W, H0, YB = 0.27, 0.19, -0.075
for side in (1, -1):
    verts, faces = blk[1], blk[2]
    b0 = len(verts)
    n = 16
    for i in range(n + 1):
        x = -W + 2 * W * i / n
        e = math.sqrt(max(0.0, 1 - (x / W) ** 2))
        top = YB + H0 * e ** 0.8
        for yy in (YB, top):
            h = (yy - YB) / H0
            z = side * (0.165 * e + 0.035 * h)
            verts.append((x, yy, z))
    for i in range(n):
        faces.append((b0 + 2 * i, b0 + 2 * i + 2, b0 + 2 * i + 3, b0 + 2 * i + 1))
    edge = []
    for i in range(n + 1):
        x = -W + 2 * W * i / n
        e = math.sqrt(max(0.0, 1 - (x / W) ** 2))
        top = YB + H0 * e ** 0.8
        edge.append((x, top, side * (0.165 * e + 0.035 * (top - YB) / H0)))
    tube(gal, edge, 0.008, sides=6)
# tête de mort sur l'aile de devant (posée un peu en avant du tissu)
zf = 0.165 + 0.035 * 0.45 + 0.012
disc(skull, 0.0, YB + 0.10, zf, 0.034, 0.03, seg=16)
box(skull, 0.0, YB + 0.072, zf - 0.001, 0.036, 0.022, 0.004)
for s in (1, -1):
    box(skull, 0.0, YB + 0.04, zf - 0.003, 0.002, 0.002, 0.002)
    verts, faces = skull[1], skull[2]
    b0 = len(verts)
    for (x, yy) in ((-0.05 * s, YB + 0.02), (-0.045 * s, YB + 0.03), (0.05 * s, YB + 0.065), (0.045 * s, YB + 0.055)):
        verts.append((x, yy, zf - 0.004))
    faces.append((b0, b0 + 1, b0 + 2, b0 + 3))
eyes = o.group('noir_yeux')
for s in (1, -1):
    disc(eyes, s * 0.013, YB + 0.102, zf + 0.002, 0.009, 0.009, seg=10)
o.save('pirate.obj')

# --- Haut-de-forme : bord recourbé, cylindre légèrement évasé, dessus, ruban
o = Obj()
blk, rib = o.group('noir'), o.group('ruban')
lathe(blk, [(0.150, -0.075), (0.19, -0.078), (0.215, -0.072), (0.225, -0.06), (0.222, -0.056), (0.21, -0.066), (0.185, -0.07), (0.150, -0.068)], seg=32)
lathe(blk, [(0.138, -0.07), (0.132, 0.05), (0.14, 0.17), (0.135, 0.178), (0.0, 0.18)], seg=32)
lathe(rib, [(0.141, -0.065), (0.1385, -0.025)], seg=32)
o.save('haut_de_forme.obj')

# ================================================================== visage
# --- Lunettes 3D en carton : monture blanche, verre rouge à gauche, cyan à droite, branches vers les oreilles
o = Obj()
frame, red, cyan = o.group('carton'), o.group('rouge'), o.group('cyan')
ZF, T = 0.012, 0.004
FW, LY0, LY1 = 0.125, EYE_Y - 0.03, EYE_Y + 0.032
box(frame, 0.0, LY1 + 0.008, ZF, 2 * FW, 0.016, T)                      # barre du haut
box(frame, 0.0, LY0 - 0.006, ZF, 2 * FW, 0.012, T)                      # barre du bas
box(frame, 0.0, EYE_Y, ZF, 0.022, LY1 - LY0, T)                          # pont
for s in (1, -1):
    box(frame, s * (FW - 0.006), EYE_Y, ZF, 0.012, LY1 - LY0, T)         # côtés
    box(red if s < 0 else cyan, s * (0.011 + (FW - 0.012 - 0.011) / 2), EYE_Y, ZF - 0.001,
        FW - 0.012 - 0.011, LY1 - LY0, 0.002)
    tube(frame, [(s * FW, LY1, ZF), (s * 0.148, LY1, -0.03), (s * 0.150, LY1 - 0.005, -0.12), (s * 0.145, LY1 - 0.03, -0.15)], 0.004, sides=4)
o.save('lunettes3d.obj')

# --- Monocle (oeil droit) : anneau doré, verre léger, chaînette qui descend vers le col
o = Obj()
ring, glass, chain = o.group('or'), o.group('verre'), o.group('chaine')
ringp = [(EYE + 0.034 * math.cos(2 * math.pi * i / 24), EYE_Y + 0.034 * math.sin(2 * math.pi * i / 24), 0.006) for i in range(25)]
tube(ring, ringp, 0.004, sides=6, cap=False)
disc(glass, EYE, EYE_Y, 0.004, 0.031, 0.031, seg=20)
path = []
for i in range(14):
    t = i / 13
    path.append((EYE + 0.02 + 0.06 * t, EYE_Y - 0.03 - 0.17 * t + 0.05 * t * t, 0.004 - 0.02 * t))
tube(chain, path, 0.0022, sides=4, cap=False)
o.save('monocle.obj')

# --- Yeux UwU : deux "U" noirs à la place des yeux, bouche en "w", joues roses (traits plats posés sur la peau)
o = Obj()
ink, blush = o.group('encre'), o.group('joues')
for s in (1, -1):
    path = []
    for i in range(13):
        a = math.pi + math.pi * i / 12          # demi-cercle du bas
        path.append((s * EYE + 0.02 * math.cos(a), EYE_Y + 0.004 + 0.022 * math.sin(a), 0.002))
    path = [(s * EYE - 0.02, EYE_Y + 0.02, 0.002)] + path + [(s * EYE + 0.02, EYE_Y + 0.02, 0.002)]
    ribbon(ink, path, 0.009)
    disc(blush, s * (EYE + 0.022), EYE_Y - 0.04, 0.0015, 0.02, 0.011, seg=14)
w = []
for i in range(17):
    t = i / 16
    x = -0.022 + 0.044 * t
    w.append((x, -0.03 - 0.009 * abs(math.sin(t * 2 * math.pi)), 0.002))
ribbon(ink, w, 0.006)
o.save('uwu.obj')

# --- Masque en papier (façon déguisement de l'espion) : contour à pans, visage imprimé, coordonnées de texture
OUTLINE = [(0.0, 0.150), (0.075, 0.140), (0.118, 0.095), (0.128, 0.035), (0.120, -0.025), (0.090, -0.075),
           (0.045, -0.108), (0.0, -0.118)]
OUTLINE = OUTLINE + [(-x, y) for x, y in OUTLINE[-2:0:-1]]
TX0, TX1, TY0, TY1 = -0.135, 0.135, -0.125, 0.155       # zone couverte par la texture
o = Obj()
paper = o.group('papier')
verts, faces, uvs = paper[1], paper[2], paper[3]
cx, cy = 0.0, 0.02
RINGS = 8
verts.append((cx, cy, 0.0)); uvs.append(((cx - TX0) / (TX1 - TX0), (cy - TY0) / (TY1 - TY0)))
dense = []                                                # contour densifié (8 points par côté)
for k in range(len(OUTLINE)):
    (x0, y0), (x1, y1) = OUTLINE[k], OUTLINE[(k + 1) % len(OUTLINE)]
    for j in range(4):
        t = j / 4
        dense.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
M = len(dense)
for r in range(1, RINGS + 1):
    t = r / RINGS
    for (x, yy) in dense:
        px, py = cx + (x - cx) * t, cy + (yy - cy) * t
        verts.append((px, py, 0.0)); uvs.append(((px - TX0) / (TX1 - TX0), (py - TY0) / (TY1 - TY0)))
for k in range(M):
    faces.append((0, 1 + (k + 1) % M, 1 + k))
for r in range(1, RINGS):
    a0, b0 = 1 + (r - 1) * M, 1 + r * M
    for k in range(M):
        k2 = (k + 1) % M
        faces.append((a0 + k, a0 + k2, b0 + k2, b0 + k))
o.save('masque.obj')
# Contour du masque pour FaceGear (élastique) : bords gauche/droite à la hauteur des yeux
print('masque : bords à x = ±0.128')

# Texture du masque : papier blanc, visage imprimé dans un contour intérieur, petits triangles noirs aux coins
TW, TH = 540, 560
def to_px(x, y): return ((x - TX0) / (TX1 - TX0) * TW, (TY1 - y) / (TY1 - TY0) * TH)
tex = Image.new('RGB', (TW, TH), (244, 242, 236))
photo = Image.open(os.path.join(SRC, 'glucks_face.jpg')).convert('RGB')
photo = ImageOps.mirror(photo)                           # vu de face : la droite du personnage est à gauche de l'image
# Yeux sur la photo (après miroir) à la hauteur des yeux du personnage ; échelle choisie pour que le menton tienne
PW = photo.width
le, re = (PW - 615, 497), (PW - 405, 495)
px_per_unit = TW / (TX1 - TX0)
sx = (0.095 * px_per_unit) / (re[0] - le[0])   # visage entier, menton compris, dans le masque
sy = sx * 0.9
photo = photo.resize((int(photo.width * sx), int(photo.height * sy)), Image.LANCZOS)
ex, ey = (le[0] + re[0]) / 2 * sx, (le[1] + re[1]) / 2 * sy
tx, ty = to_px(0.0, EYE_Y)
photo = ImageOps.autocontrast(photo, cutoff=1)
inner = Image.new('L', (TW, TH), 0)
shrink = 0.86
ImageDraw.Draw(inner).polygon([to_px(cx + (x - cx) * shrink, cy + (y - cy) * shrink) for x, y in OUTLINE], fill=255)
inner = inner.filter(ImageFilter.GaussianBlur(3))
layer = Image.new('RGB', (TW, TH), (244, 242, 236))
layer.paste(photo, (int(tx - ex), int(ty - ey)))
tex = Image.composite(layer, tex, inner)
dr = ImageDraw.Draw(tex)
ImageDraw.Draw(tex).line([to_px(cx + (x - cx) * shrink, cy + (y - cy) * shrink) for x, y in OUTLINE + OUTLINE[:1]], fill=(60, 55, 50), width=3)
for (x, y) in (OUTLINE[2], OUTLINE[4], OUTLINE[-2], OUTLINE[-4]):   # petits triangles noirs
    px, py = to_px(cx + (x - cx) * 0.93, cy + (y - cy) * 0.93)
    dr.polygon([(px - 7, py + 6), (px + 7, py + 6), (px, py - 7)], fill=(25, 25, 25))
tex.save(os.path.join(OUT, 'masque_glucks.png'))

# ================================================================== icônes
def new(): return Image.new('RGBA', (256, 256), (0, 0, 0, 0))
def save(im, name): im.save(os.path.join(OUT, name))

im = new(); d = ImageDraw.Draw(im)                                       # couronne
d.polygon([(40, 200), (40, 80), (85, 140), (128, 60), (171, 140), (216, 80), (216, 200)], fill=(240, 190, 40, 255), outline=(170, 120, 20, 255))
for x, c in ((80, (200, 20, 40)), (128, (30, 70, 200)), (176, (200, 20, 40))):
    d.ellipse([x - 12, 160, x + 12, 184], fill=c + (255,))
for x, yy in ((40, 80), (128, 60), (216, 80)):
    d.ellipse([x - 10, yy - 12, x + 10, yy + 8], fill=(250, 245, 230, 255))
save(im, 'icon_couronne.png')
im = new(); d = ImageDraw.Draw(im)                                       # bonnet de bouffon
d.pieslice([60, 110, 196, 250], 180, 360, fill=(235, 90, 170, 255))
d.polygon([(70, 180), (20, 90), (40, 150), (110, 170)], fill=(235, 90, 170, 255))
d.polygon([(186, 180), (236, 90), (216, 150), (146, 170)], fill=(130, 60, 190, 255))
d.ellipse([8, 76, 36, 104], fill=(245, 200, 50, 255)); d.ellipse([220, 76, 248, 104], fill=(245, 200, 50, 255))
d.rectangle([58, 170, 198, 190], fill=(245, 200, 50, 255))
save(im, 'icon_bouffon.png')
im = new(); d = ImageDraw.Draw(im)                                       # casque audio
d.arc([50, 30, 206, 200], 180, 360, fill=(40, 40, 44, 255), width=16)
d.rounded_rectangle([30, 110, 76, 190], 14, fill=(40, 40, 44, 255)); d.rounded_rectangle([180, 110, 226, 190], 14, fill=(40, 40, 44, 255))
d.line([(54, 180), (90, 220), (130, 226)], fill=(40, 40, 44, 255), width=6); d.ellipse([124, 214, 146, 236], fill=(70, 70, 76, 255))
save(im, 'icon_casque_audio.png')
im = new(); d = ImageDraw.Draw(im)                                       # pirate
d.chord([10, 60, 246, 260], 180, 360, fill=(25, 25, 28, 255), outline=(220, 170, 40, 255), width=6)
d.ellipse([108, 92, 148, 128], fill=(240, 240, 235, 255)); d.rectangle([116, 124, 140, 138], fill=(240, 240, 235, 255))
d.ellipse([115, 102, 125, 112], fill=(25, 25, 28, 255)); d.ellipse([131, 102, 141, 112], fill=(25, 25, 28, 255))
d.line([(96, 146), (160, 120)], fill=(240, 240, 235, 255), width=7); d.line([(96, 120), (160, 146)], fill=(240, 240, 235, 255), width=7)
save(im, 'icon_pirate.png')
im = new(); d = ImageDraw.Draw(im)                                       # haut-de-forme
d.rectangle([70, 30, 186, 190], fill=(25, 25, 28, 255)); d.rectangle([70, 160, 186, 180], fill=(110, 20, 30, 255))
d.ellipse([20, 180, 236, 215], fill=(25, 25, 28, 255))
save(im, 'icon_haut_de_forme.png')
im = new(); d = ImageDraw.Draw(im)                                       # lunettes 3D
d.rectangle([14, 90, 242, 170], fill=(245, 245, 245, 255), outline=(180, 180, 180, 255), width=3)
d.rectangle([28, 102, 118, 158], fill=(220, 30, 40, 255)); d.rectangle([138, 102, 228, 158], fill=(30, 190, 220, 255))
save(im, 'icon_lunettes3d.png')
im = new(); d = ImageDraw.Draw(im)                                       # monocle
d.ellipse([60, 40, 180, 160], outline=(230, 180, 40, 255), width=12); d.ellipse([72, 52, 168, 148], fill=(200, 225, 240, 120))
d.line([(170, 140), (200, 190), (210, 240)], fill=(230, 180, 40, 255), width=5)
save(im, 'icon_monocle.png')
im = new(); d = ImageDraw.Draw(im)                                       # UwU
d.ellipse([10, 10, 246, 246], fill=(250, 215, 185, 255))
for x in (78, 178):
    d.arc([x - 34, 60, x + 34, 130], 0, 180, fill=(20, 20, 20, 255), width=12)
    d.line([(x - 34, 92), (x - 34, 70)], fill=(20, 20, 20, 255), width=12); d.line([(x + 34, 92), (x + 34, 70)], fill=(20, 20, 20, 255), width=12)
    d.ellipse([x - 36, 140, x + 36, 170], fill=(245, 140, 160, 255))
d.line([(98, 170), (113, 196), (128, 176), (143, 196), (158, 170)], fill=(20, 20, 20, 255), width=8)
save(im, 'icon_uwu.png')
icon = tex.copy().convert('RGBA')                                        # masque
mask = Image.new('L', (TW, TH), 0); ImageDraw.Draw(mask).polygon([to_px(x, y) for x, y in OUTLINE], fill=255)
icon.putalpha(mask)
bb = mask.getbbox(); icon = icon.crop(bb); icon.thumbnail((236, 236), Image.LANCZOS)
im = new(); im.alpha_composite(icon, ((256 - icon.width) // 2, (256 - icon.height) // 2)); save(im, 'icon_masque.png')
print('ok')
