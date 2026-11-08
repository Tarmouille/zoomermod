# Cosmétiques ZoomerMod : textures (chemise "des tests", léopard), moustache ronde + bouc (OBJ), icônes de l'écran
# de personnalisation. Sortie : DaleMod/Assets/cosmetics/
#
# Repère des OBJ "visage" : x à droite, y en haut, z = distance AU-DESSUS de la peau (le mod plaque chaque sommet sur
# le visage du personnage), origine = bout du nez, en mètres pour une tête de 0,28 m de large (mis à l'échelle en jeu).
import math, os, random
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), '..', 'DaleMod', 'Assets', 'cosmetics')
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- textures raccordables
def tiled(size, draw_fn, bg, seed):
    """Dessine sur une toile 3x3 puis garde le centre : les motifs qui débordent se raccordent."""
    big = Image.new('RGB', (size * 3, size * 3), bg)
    d = ImageDraw.Draw(big)
    rnd = random.Random(seed)
    shapes = draw_fn(rnd, size)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for s in shapes:
                s(d, size + dx * size, size + dy * size)
    return big.crop((size, size, 2 * size, 2 * size))

def leaf(cx, cy, length, width, angle, fill, vein, slits):
    """Feuille de bananier : ellipse pointue, nervure centrale, entailles obliques."""
    def f(d, ox, oy):
        ca, sa = math.cos(angle), math.sin(angle)
        def P(u, v):  # u le long de la feuille (0..1), v en travers (-1..1)
            x = (u - 0.5) * length
            y = v * width * math.sin(math.pi * min(max(u, 0), 1)) ** 0.8
            return (ox + cx + x * ca - y * sa, oy + cy + x * sa + y * ca)
        pts = [P(i / 40, 1) for i in range(41)] + [P(1 - i / 40, -1) for i in range(41)]
        d.polygon(pts, fill=fill)
        for k in range(slits):
            u = 0.12 + 0.76 * k / max(1, slits - 1)
            side = 1 if k % 2 == 0 else -1
            d.line([P(u, 0), P(u + 0.07, side * 1.05)], fill=vein, width=3)
        d.line([P(0.0, 0), P(1.0, 0)], fill=vein, width=4)
    return f

def fan(cx, cy, radius, angle, spread, blades, fill, edge):
    """Palme en éventail : lames fines rayonnantes."""
    def f(d, ox, oy):
        for i in range(blades):
            a = angle - spread / 2 + spread * i / (blades - 1)
            r = radius * (0.75 + 0.25 * math.sin(i * 1.7) ** 2)
            tip = (ox + cx + r * math.cos(a), oy + cy + r * math.sin(a))
            n = (-math.sin(a), math.cos(a))
            w = radius * 0.045
            base = (ox + cx, oy + cy)
            mid = (ox + cx + 0.55 * r * math.cos(a), oy + cy + 0.55 * r * math.sin(a))
            d.polygon([base, (mid[0] + n[0] * w, mid[1] + n[1] * w), tip, (mid[0] - n[0] * w, mid[1] - n[1] * w)], fill=fill, outline=edge)
    return f

def jdg_shapes(rnd, S):
    greens = [((46, 125, 50), (24, 80, 30)), ((86, 160, 60), (40, 105, 35)), ((30, 100, 45), (15, 65, 28))]
    shapes = []
    # palmes d'abord (en dessous), puis les grandes feuilles
    for _ in range(9):
        col = rnd.choice([((250, 225, 70), (205, 170, 40)), ((215, 175, 95), (160, 120, 60)), ((240, 205, 60), (190, 150, 35))])
        shapes.append(fan(rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(S * 0.12, S * 0.2), rnd.uniform(0, 6.28), rnd.uniform(2.2, 3.4), rnd.randint(11, 17), col[0], col[1]))
    for _ in range(6):
        g = rnd.choice(greens)
        shapes.append(leaf(rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(S * 0.35, S * 0.55), rnd.uniform(S * 0.06, S * 0.09), rnd.uniform(0, 6.28), g[0], g[1], 9))
    for _ in range(6):   # petites feuilles fines
        g = rnd.choice(greens)
        shapes.append(leaf(rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(S * 0.18, S * 0.26), rnd.uniform(S * 0.02, S * 0.03), rnd.uniform(0, 6.28), g[0], g[1], 0))
    return shapes

def leopard_shapes(rnd, S):
    """Rosettes : anneau irrégulier cassé en 3 à 5 croissants sombres autour d'un centre plus foncé que le fond,
    plus de petites taches pleines entre elles."""
    shapes = []
    pts = []
    tries = 0
    while len(pts) < 42 and tries < 6000:
        tries += 1
        p = (rnd.uniform(0, S), rnd.uniform(0, S))
        if all(min(abs(p[0] - q[0]), S - abs(p[0] - q[0])) ** 2 + min(abs(p[1] - q[1]), S - abs(p[1] - q[1])) ** 2 > (S * 0.12) ** 2 for q in pts):
            pts.append(p)
    for (cx, cy) in pts:
        rx = rnd.uniform(S * 0.035, S * 0.052); ry = rx * rnd.uniform(0.75, 1.0)
        rot = rnd.uniform(0, 6.28)
        n = rnd.randint(3, 5)
        cuts = sorted(rnd.uniform(0, 6.28) for _ in range(n))
        blobs = []   # chaque croissant = chapelet de taches irrégulières le long de l'anneau
        for k in range(n):
            a0 = cuts[k] + rnd.uniform(0.2, 0.4)
            a1 = (cuts[(k + 1) % n] + (6.28 if k == n - 1 else 0)) - rnd.uniform(0.2, 0.4)
            m = max(2, int((a1 - a0) / 0.45))
            for q in range(m + 1):
                a = a0 + (a1 - a0) * q / m
                size = (0.22 + 0.16 * math.sin(math.pi * q / m)) * rnd.uniform(0.8, 1.2)
                blobs.append((a, rnd.uniform(0.95, 1.08), size, rnd.uniform(0.7, 1.3)))
        def ros(d, ox, oy, cx=cx, cy=cy, rx=rx, ry=ry, rot=rot, blobs=blobs):
            def P(a, k):
                x, y = rx * k * math.cos(a), ry * k * math.sin(a)
                return (ox + cx + x * math.cos(rot) - y * math.sin(rot), oy + cy + x * math.sin(rot) + y * math.cos(rot))
            d.polygon([P(6.28 * i / 24, 0.92) for i in range(24)], fill=(196, 128, 52))   # centre à peine plus foncé
            for a, k, size, el in blobs:
                px, py = P(a, k)
                r = rx * size
                d.ellipse([px - r * el, py - r / el, px + r * el, py + r / el], fill=(36, 23, 13))
        shapes.append(ros)
    for _ in range(55):   # petites taches pleines irrégulières
        cx, cy, r = rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(S * 0.006, S * 0.014)
        sx, sy = rnd.uniform(0.7, 1.3), rnd.uniform(0.7, 1.3)
        shapes.append(lambda d, ox, oy, cx=cx, cy=cy, r=r, sx=sx, sy=sy: d.ellipse([ox + cx - r * sx, oy + cy - r * sy, ox + cx + r * sx, oy + cy + r * sy], fill=(42, 27, 16)))
    return shapes

S = 512
jdg = tiled(S, jdg_shapes, (245, 196, 92), 7).filter(ImageFilter.SMOOTH)
jdg.save(os.path.join(OUT, 'shirt_jdg.png'))
leo = tiled(S, leopard_shapes, (222, 168, 82), 3).filter(ImageFilter.SMOOTH)
leo.save(os.path.join(OUT, 'leopard.png'))

# ---------------------------------------------------------------- maillages "visage"
def tube(path, radii, sides=8, flat=0.65, lift=None):
    """Tube le long d'un chemin du plan xy (posé sur la peau) : section ellipse aplatie en z. Renvoie (sommets, faces)."""
    verts, faces = [], []
    n = len(path)
    for i, (x, y) in enumerate(path):
        a = path[max(i - 1, 0)]; b = path[min(i + 1, n - 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        L = math.hypot(tx, ty) or 1
        nx, ny = -ty / L, tx / L   # normale dans le plan
        r = radii[i]
        for k in range(sides):
            t = 2 * math.pi * k / sides
            verts.append((x + nx * r * math.cos(t), y + ny * r * math.cos(t), r * flat * (1 + math.sin(t)) + (lift[i] if lift else 0)))
    for i in range(n - 1):
        for k in range(sides):
            a = i * sides + k; b = i * sides + (k + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    # bouchons
    verts.append((*path[0], radii[0] * flat)); c0 = len(verts) - 1
    verts.append((*path[-1], radii[-1] * flat)); c1 = len(verts) - 1
    for k in range(sides):
        faces.append((c0, (k + 1) % sides, k))
        faces.append((c1, (n - 1) * sides + k, (n - 1) * sides + (k + 1) % sides))
    return verts, faces

def write_obj(name, parts):
    lines = [f'# {name} — ZoomerMod (tools/make_cosmetics.py)']
    off = 0
    for group, (verts, faces) in parts:
        lines.append(f'g {group}')
        lines += [f'v {x:.5f} {y:.5f} {z:.5f}' for x, y, z in verts]
        lines += ['f ' + ' '.join(str(off + i + 1) for i in f) for f in faces]
        off += len(verts)
    open(os.path.join(OUT, name), 'w').write('\n'.join(lines) + '\n')

def mirror(vf):
    v, f = vf
    return [(-x, y, z) for x, y, z in v], [tuple(reversed(q)) for q in f]

# Moustache en guidon qui s'enroule en cercle vers le haut : moitié droite puis miroir
half = []
for i in range(14):                      # du milieu (sous le nez) vers l'extérieur, légère descente
    u = i / 13
    half.append((0.002 + 0.040 * u, -0.007 - 0.006 * math.sin(math.pi * u * 0.9)))
cx, cy, R = 0.050, 0.005, 0.0125         # boucle : centre, rayon
start = math.atan2(half[-1][1] - cy, half[-1][0] - cx)
for i in range(1, 25):                    # tour presque complet, vers le haut et vers l'intérieur
    a = start + (2 * math.pi * 0.92) * i / 24
    rr = R * (1 - 0.25 * i / 24)
    half.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
radii = [0.0105 - 0.0068 * (i / (len(half) - 1)) ** 0.8 for i in range(len(half))]
lift = [0.003 * max(0.0, (i - 13) / (len(half) - 14)) for i in range(len(half))]
right = tube(half, radii, lift=lift)
left = mirror(right)
# Petit bouc sous la lèvre : pointe vers le bas
bp = [(0.0, -0.026 - 0.022 * i / 10) for i in range(11)]
br = [0.0115 * (1 - 0.8 * (i / 10) ** 1.3) for i in range(11)]
bouc = tube(bp, br, sides=10, flat=0.55)
write_obj('moustache_ronde.obj', [('moustache_d', right), ('moustache_g', left), ('bouc', bouc)])

# ---------------------------------------------------------------- icônes (écran de personnalisation)
def icon_moustache():
    im = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    k = 1700   # m -> px
    def draw(path, radii, col, sx=1):
        for (x, y), r in zip(path, radii):
            px, py = 128 + sx * x * k, 96 - y * k
            d.ellipse([px - r * k, py - r * k, px + r * k, py + r * k], fill=col)
    for sx in (1, -1):
        draw(half, radii, (70, 50, 35, 255), sx)
    draw(bp, br, (70, 50, 35, 255))
    return im

def icon_nose():
    im = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.ellipse([58, 58, 198, 198], fill=(205, 25, 30, 255))
    d.ellipse([88, 80, 128, 112], fill=(255, 140, 140, 255))
    return im

def swatch(tex):
    return tex.resize((128, 128)).convert('RGBA')

icon_moustache().save(os.path.join(OUT, 'icon_moustache.png'))
icon_nose().save(os.path.join(OUT, 'icon_nose.png'))
swatch(jdg).save(os.path.join(OUT, 'swatch_jdg.png'))
swatch(leo).save(os.path.join(OUT, 'swatch_leopard.png'))
print('ok', OUT)

# Icône du peignoir : silhouette ouverte devant, léopard
def icon_robe():
    im = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
    mask = Image.new('L', (256, 256), 0)
    d = ImageDraw.Draw(mask)
    d.polygon([(88, 40), (60, 60), (28, 150), (52, 158), (70, 110), (66, 236), (190, 236), (186, 110), (204, 158), (228, 150), (196, 60), (168, 40), (128, 120)], fill=255)
    im.paste(leo.resize((256, 256)), (0, 0), mask)
    d2 = ImageDraw.Draw(im)
    d2.line([(88, 40), (128, 120), (168, 40)], fill=(60, 35, 20, 255), width=6)
    d2.line([(128, 120), (128, 150)], fill=(60, 35, 20, 255), width=4)
    d2.rectangle([66, 140, 190, 150], fill=(150, 90, 40, 255))
    d2.line([(128, 150), (110, 236)], fill=(60, 35, 20, 255), width=4)
    d2.line([(128, 150), (146, 236)], fill=(60, 35, 20, 255), width=4)
    return im

icon_robe().save(os.path.join(OUT, 'icon_robe.png'))

# Cuir : grain fin + nuances d'usure, raccordable (bruit périodique), brun et noir
import numpy as np
def periodic_noise(size, cells, rnd):
    g = rnd.random((cells, cells))
    big = np.tile(g, (3, 3))
    im = Image.fromarray((big * 255).astype('uint8')).resize((size * 3, size * 3), Image.BICUBIC)
    return np.asarray(im, dtype=float)[size:2 * size, size:2 * size] / 255.0

def leather(base, seed):
    rnd = np.random.default_rng(seed)
    n = 0.45 * periodic_noise(S, 6, rnd) + 0.3 * periodic_noise(S, 24, rnd) + 0.25 * periodic_noise(S, 96, rnd)
    pores = periodic_noise(S, 220, rnd)
    v = 0.82 + 0.28 * (n - 0.5) - 0.10 * (pores > 0.78)
    arr = np.clip(np.array(base, dtype=float)[None, None, :] * v[:, :, None], 0, 255).astype('uint8')
    return Image.fromarray(arr, 'RGB').filter(ImageFilter.SMOOTH)

for name, base, seed in (('leather', (112, 66, 36), 5), ('leather_black', (44, 40, 38), 9)):
    img = leather(base, seed)
    img.save(os.path.join(OUT, name + '.png'))
    swatch(img).save(os.path.join(OUT, 'swatch_' + name + '.png'))
