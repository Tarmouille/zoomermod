# Coupe "Ponpons" (deux chignons hauts, raie au milieu, deux mèches qui tombent devant les oreilles) pour ZoomerMod.
# Sortie : DaleMod/Assets/cosmetics/ponpons.obj et icon_ponpons.png
#
# Repère des chapeaux (origine au SOMMET de la tête, x à droite, y en haut, z vers l'avant, tête de 0,28 m de large).
# En jeu (Ponpons.cs), chaque sommet est reporté sur la tête réelle : direction depuis le centre du crâne (0, -0.14, 0),
# et hauteur au-dessus de la peau = sa distance à ce centre moins 0,14. Groupes : "calotte", "meches", "chignons"
# (les chignons sont retirés sous un chapeau ; la calotte est écrasée par le jeu comme ses propres coupes).
#   python tools/make_ponpons.py [aperçu.png]
import math, os, random, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, '..', 'DaleMod', 'Assets', 'cosmetics')
R0 = 0.14
C0 = np.array([0.0, -R0, 0.0])

groups = []
def group(name):
    g = (name, [], [])
    groups.append(g)
    return g

def sph(theta, phi, h):
    """Point à hauteur h au-dessus de la tête de référence ; theta depuis le haut, phi = 0 devant, 90° à droite."""
    d = np.array([math.sin(theta) * math.sin(phi), math.cos(theta), math.sin(theta) * math.cos(phi)])
    return C0 + d * (R0 + h), d

def limit(phi):
    """Bord de la calotte (angle depuis le haut) : front dégagé, tempes, nuque plus bas."""
    c = math.cos(phi)
    return math.radians(62 + (98 - 62) * (1 - c)) if c > 0 else math.radians(98 + (122 - 98) * (-c))

# ---------------------------------------------------------------- calotte
cap = group('calotte')
S, R = 16, 6
cap[1].append(tuple(sph(0, 0, 0.014)[0]))
for k in range(1, R + 2):
    for j in range(S):
        phi = 2 * math.pi * j / S
        t = min(k, R) / R
        h = 0.014 - 0.009 * t if k <= R else -0.002          # dernier anneau : rentré dans la peau (pas de jour au bord)
        if j == 0 and 1 <= k <= 3: h *= 0.45                   # raie au milieu, du front au sommet
        cap[1].append(tuple(sph(limit(phi) * t, phi, h)[0]))
for j in range(S):
    cap[2].append((0, 1 + j, 1 + (j + 1) % S))
for k in range(R):
    for j in range(S):
        a, b = 1 + k * S + j, 1 + k * S + (j + 1) % S
        cap[2].append((a, a + S, b + S, b))

# ---------------------------------------------------------------- chignons
rnd = random.Random(7)
buns = group('chignons')
def frame(d):
    ref = np.array([0, 0, 1.0]) if abs(d[2]) < 0.9 else np.array([1.0, 0, 0])
    u = np.cross(d, ref); u /= np.linalg.norm(u)
    return u, np.cross(d, u)

def lumpy(g, center, d, r, lat=5, lon=9):
    u, v = frame(d)
    verts, faces = g[1], g[2]
    b = len(verts)
    verts.append(tuple(center + d * r * 0.9))
    for i in range(1, lat):
        a = math.pi * i / lat
        for j in range(lon):
            p = 2 * math.pi * (j + 0.5 * (i % 2)) / lon
            rr = r * (1 + rnd.uniform(-0.12, 0.12))
            q = center + d * rr * math.cos(a) * 0.9 + (u * math.cos(p) + v * math.sin(p)) * rr * math.sin(a)
            verts.append(tuple(q))
    verts.append(tuple(center - d * r * 0.9))
    last = len(verts) - 1
    for j in range(lon):
        faces.append((b, b + 1 + j, b + 1 + (j + 1) % lon)[::-1])
    for i in range(lat - 2):
        for j in range(lon):
            a0, a1 = b + 1 + i * lon + j, b + 1 + i * lon + (j + 1) % lon
            faces.append((a0, a1, a1 + lon, a0 + lon)[::-1])
    for j in range(lon):
        faces.append((last, b + 1 + (lat - 2) * lon + (j + 1) % lon, b + 1 + (lat - 2) * lon + j)[::-1])

def torus(g, center, d, rr, tube, seg=9, sides=4, twist=0.0):
    u, v = frame(d)
    verts, faces = g[1], g[2]
    b = len(verts)
    for i in range(seg):
        a = 2 * math.pi * i / seg
        radial = u * math.cos(a) + v * math.sin(a)
        for k in range(sides):
            s = 2 * math.pi * k / sides + twist * a
            verts.append(tuple(center + radial * (rr + tube * math.cos(s)) + d * tube * math.sin(s)))
    for i in range(seg):
        for k in range(sides):
            a, bb = b + i * sides + k, b + i * sides + (k + 1) % sides
            c, dd = b + ((i + 1) % seg) * sides + (k + 1) % sides, b + ((i + 1) % seg) * sides + k
            faces.append((a, dd, c, bb))

def tube(g, path, radius, sides=3):
    verts, faces = g[1], g[2]
    b = len(verts)
    n = len(path)
    for i, p in enumerate(path):
        t = np.array(path[min(i + 1, n - 1)]) - np.array(path[max(i - 1, 0)]); t /= np.linalg.norm(t)
        u, v = frame(t)
        for k in range(sides):
            a = 2 * math.pi * k / sides
            verts.append(tuple(np.array(p) + (u * math.cos(a) + v * math.sin(a)) * radius))
    for i in range(n - 1):
        for k in range(sides):
            a, bb = b + i * sides + k, b + i * sides + (k + 1) % sides
            faces.append((a, bb, bb + sides, a + sides))

for side in (-1, 1):
    phi = side * math.radians(80)
    base, d = sph(math.radians(40), phi, 0.010)
    lumpy(buns, base + d * 0.042, d, 0.048)                      # le chignon
    torus(buns, base + d * 0.012, d, 0.040, 0.013, twist=1.0)     # la mèche enroulée à sa base
    u, v = frame(d)
    for k in range(3):                                             # boucles folles autour
        a0 = 2 * math.pi * k / 3 + 0.4 * side
        path = []
        for i in range(6):
            a = a0 + 1.4 * i / 5
            rad = 0.044 + 0.010 * math.sin(math.pi * i / 5)
            path.append(base + d * (0.030 + 0.012 * math.sin(math.pi * i / 5)) + (u * math.cos(a) + v * math.sin(a)) * rad)
        tube(buns, path, 0.0035)

# ---------------------------------------------------------------- mèches devant les oreilles
strands = group('meches')
for side in (-1, 1):
    for n, (phi0, length, amp) in enumerate([(52, 0.19, 0.012), (64, 0.15, 0.009)]):
        phi = side * math.radians(phi0)
        th0 = limit(phi) - math.radians(8)
        verts, faces = strands[1], strands[2]
        b = len(verts)
        N = 8
        for i in range(N + 1):
            t = i / N
            th = th0 + (math.radians(150) - th0) * t * (length / 0.19)   # descend le long de la joue
            ph = phi + side * amp / R0 * math.sin(t * math.pi * 2.2 + n)  # ondule
            w = 0.007 * (1 - 0.75 * t)                                  # s'affine vers la pointe
            p, d = sph(th, ph, 0.006)
            tang = np.array([math.cos(ph), 0.0, -math.sin(ph)])
            verts.append(tuple(p - tang * w)); verts.append(tuple(p + tang * w))
        for i in range(N):
            a = b + 2 * i
            faces.append((a, a + 1, a + 3, a + 2))

# ---------------------------------------------------------------- écriture
lines = ['# ponpons.obj — ZoomerMod (tools/make_ponpons.py)']
off = 0
for name, verts, faces in groups:
    lines.append(f'g {name}')
    lines += [f'v {x:.5f} {y:.5f} {z:.5f}' for x, y, z in verts]
    lines += ['f ' + ' '.join(str(off + i + 1) for i in f) for f in faces]
    off += len(verts)
open(os.path.join(OUT, 'ponpons.obj'), 'w').write('\n'.join(lines) + '\n')
print('ponpons.obj :', sum(len(g[2]) for g in groups), 'faces')

# ---------------------------------------------------------------- icône
im = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); dr = ImageDraw.Draw(im)
hair, dark = (92, 64, 40, 255), (60, 40, 24, 255)
dr.ellipse([58, 70, 198, 230], fill=(236, 196, 160, 255))           # visage
dr.chord([52, 62, 204, 222], 180, 360, fill=hair)                    # calotte
dr.line([(128, 64), (128, 104)], fill=dark, width=4)                 # raie
for cx in (64, 192):
    dr.ellipse([cx - 40, 22, cx + 40, 102], fill=hair, outline=dark, width=4)
    dr.arc([cx - 26, 36, cx + 26, 88], 200, 520, fill=dark, width=4)
for x0, s in ((62, 1), (194, -1)):
    dr.line([(x0, 140), (x0 - 6 * s, 170), (x0 + 4 * s, 200), (x0 - 4 * s, 228)], fill=hair, width=7)
im.save(os.path.join(OUT, 'icon_ponpons.png'))

# ---------------------------------------------------------------- aperçu (tête = sphère de référence)
if len(sys.argv) > 1:
    tris = []
    def add(pts, col):
        for i in range(1, len(pts) - 1): tris.append((np.array([pts[0], pts[i], pts[i + 1]]), col))
    for k in range(12):
        for j in range(24):
            q = [C0 + (R0 - 0.003) * np.array([math.sin(t) * math.sin(p), math.cos(t), math.sin(t) * math.cos(p)])
                 for t, p in ((math.pi * k / 12, 2 * math.pi * j / 24), (math.pi * (k + 1) / 12, 2 * math.pi * j / 24),
                              (math.pi * (k + 1) / 12, 2 * math.pi * (j + 1) / 24), (math.pi * k / 12, 2 * math.pi * (j + 1) / 24))]
            add(q, (236, 196, 160))
    cols = {'calotte': (110, 76, 48), 'chignons': (125, 86, 54), 'meches': (100, 70, 44)}
    for name, verts, faces in groups:
        for f in faces: add([np.array(verts[i]) for i in f], cols[name])
    W = 600
    img = Image.new('RGB', (W * 3, W), (40, 44, 52)); d = ImageDraw.Draw(img)
    light = np.array([0.3, 0.8, 0.5]); light /= np.linalg.norm(light)
    for v, yaw in enumerate((0, 50, 180)):
        a = math.radians(yaw)
        rot = np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])
        items = []
        for pts, col in tris:
            p = (pts - C0) @ rot.T
            n = np.cross(p[1] - p[0], p[2] - p[0]); ln = np.linalg.norm(n)
            if ln == 0: continue
            sh = 0.4 + 0.6 * abs((n / ln) @ light)
            items.append((p[:, 2].mean(), p, tuple(int(c * sh) for c in col)))
        items.sort(key=lambda x: x[0])
        for _, p, col in items:
            d.polygon([(v * W + W / 2 + x * 1500, W / 2 - y * 1500) for x, y, _ in p], fill=col)
    img.save(sys.argv[1])
