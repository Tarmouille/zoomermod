# Gros nœud noir porté derrière la tête (chapeau ZoomerMod qui garde les cheveux). Sortie : DaleMod/Assets/cosmetics/
# Repère des chapeaux (make_cosmetics2.py) : origine au SOMMET de la tête, x à droite, y en haut, z vers l'avant,
# tête de 0,28 m de large et de profondeur (arrière du crâne à z = -0,14, les cheveux en plus).
import math, os
from PIL import Image, ImageDraw

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, '..', 'DaleMod', 'Assets', 'cosmetics')

groups = []
def group(name):
    g = (name, [], [])
    groups.append(g)
    return g

Y0, Z0 = -0.075, -0.185          # centre du nœud : derrière la tête, sous le sommet, en retrait des cheveux
def back(x, y, z):
    # suit l'arrondi de l'arrière du crâne : les bouts reviennent vers l'avant
    return (x, Y0 + y, Z0 + z + 0.9 * x * x)

def loft(g, sections, seg=14):
    # sections : (cx, cy, cz, demi-hauteur, demi-épaisseur, angle) ; ellipses dans le plan y/z, fermées aux bouts
    verts, faces = g[1], g[2]
    b0 = len(verts)
    for cx, cy, cz, hh, hd, a in sections:
        for k in range(seg):
            t = 2 * math.pi * k / seg
            y, z = hh * math.cos(t), hd * math.sin(t)
            y, x = y * math.cos(a), cx - y * math.sin(a)   # section inclinée (pans qui tombent en biais)
            verts.append(back(x, cy + y, cz + z))
    n = len(sections)
    for i in range(n - 1):
        for k in range(seg):
            a0, a1 = b0 + i * seg + k, b0 + i * seg + (k + 1) % seg
            faces.append((a0, a1, a1 + seg, a0 + seg))
    for i, end in ((0, b0), (n - 1, b0 + (n - 1) * seg)):
        c = sections[i]
        verts.append(back(c[0], c[1], c[2]))
        ci = len(verts) - 1
        for k in range(seg):
            faces.append((ci, end + k, end + (k + 1) % seg))

def sheet(g, pts):
    # grille de points (lignes x colonnes) -> quads ; rendu des deux côtés par le jeu (CosmeticExtras.Build)
    verts, faces = g[1], g[2]
    b0 = len(verts); rows, cols = len(pts), len(pts[0])
    for row in pts: verts += [back(*p) for p in row]
    for i in range(rows - 1):
        for j in range(cols - 1):
            a = b0 + i * cols + j
            faces.append((a, a + 1, a + cols + 1, a + cols))

def se(c, e):   # superellipse : sections plus plates qu'une ellipse (ruban de satin)
    return math.copysign(abs(c) ** e, c)

# Inspiré d'une barrette nœud en satin : boucles plates qui s'élargissent vers un bord extérieur droit, pincées par une
# petite bande au centre, et deux longs pans larges qui tombent en s'écartant, coupés en biais.
noir, noeud, pan = group('noir'), group('noeud'), group('pan')   # pan : suit la coiffure (CosmeticExtras.HairBack)
W, H0, H1 = 0.118, 0.017, 0.058      # longueur d'une boucle, demi-hauteur au centre et au bord
for s in (1, -1):
    secs = []
    n = 12
    for i in range(n + 1):
        t = i / n
        x = s * (0.012 + W * t)
        hh = H0 + (H1 - H0) * t ** 0.75
        hd = 0.006 + 0.016 * math.sin(math.pi * min(1.0, t * 0.8 + 0.15))   # gonflée au milieu, plate au bord
        secs.append((x, 0.006 * t, -0.004 * t, hh, hd, 0))
    verts, faces = noir[1], noir[2]
    b0 = len(verts); seg = 16
    for cx, cy, cz, hh, hd, a in secs:
        for k in range(seg):
            th = 2 * math.pi * k / seg
            verts.append(back(cx, cy + hh * se(math.cos(th), 0.45), cz + hd * se(math.sin(th), 0.7)))
    for i in range(n):
        for k in range(seg):
            a0, a1 = b0 + i * seg + k, b0 + i * seg + (k + 1) % seg
            faces.append((a0, a1, a1 + seg, a0 + seg))
    for end in (b0, b0 + n * seg):   # bord extérieur droit (et côté bande) fermés à plat
        c = secs[0] if end == b0 else secs[-1]
        verts.append(back(c[0], c[1], c[2])); ci = len(verts) - 1
        for k in range(seg): faces.append((ci, end + k, end + (k + 1) % seg))

    # pan : du dessous de la bande vers le bas, en s'écartant ; côté intérieur plus long (coupe en biais)
    rows, cols = 12, 6
    pts = []
    for i in range(rows + 1):
        v = i / rows
        row = []
        for j in range(cols + 1):
            u = -1 + 2 * j / cols                     # -1 : bord intérieur, +1 : bord extérieur
            hw = 0.028 + 0.022 * v
            cx = s * (0.02 + 0.055 * v)
            L = 0.25 - 0.05 * (u + 1) / 2              # intérieur 0,25 m, extérieur 0,20 m
            y = -0.012 - L * v
            z = -0.012 - 0.05 * v - 0.005 * (1 - u * u)   # s'éloigne du dos, léger bombé
            row.append((cx + s * u * hw, y, z))
        pts.append(row)
    sheet(pan, pts)

# bande centrale qui pince les boucles (en relief)
verts, faces = noeud[1], noeud[2]
b0 = len(verts); seg = 16; n = 6
for i in range(n + 1):
    t = i / n
    x = -0.02 + 0.04 * t
    bulge = 0.85 + 0.15 * math.sin(math.pi * t)
    for k in range(seg):
        th = 2 * math.pi * k / seg
        verts.append(back(x, 0.002 + 0.025 * bulge * se(math.cos(th), 0.5), -0.006 + 0.021 * bulge * se(math.sin(th), 0.6)))
for i in range(n):
    for k in range(seg):
        a0, a1 = b0 + i * seg + k, b0 + i * seg + (k + 1) % seg
        faces.append((a0, a1, a1 + seg, a0 + seg))

lines = ['# noeud.obj — ZoomerMod (tools/make_noeud.py)']
off = 0
for name, verts, faces in groups:
    lines.append(f'g {name}')
    lines += [f'v {x:.5f} {y:.5f} {z:.5f}' for x, y, z in verts]
    lines += ['f ' + ' '.join(str(off + i + 1) for i in f) for f in faces]
    off += len(verts)
open(os.path.join(OUT, 'noeud.obj'), 'w').write('\n'.join(lines) + '\n')

# icône
im = Image.new('RGBA', (256, 256), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
blk, edge = (22, 22, 24, 255), (90, 90, 96, 255)
d.polygon([(128, 128), (100, 180), (88, 236), (118, 232), (128, 150)], fill=blk, outline=edge)
d.polygon([(128, 128), (156, 180), (168, 236), (138, 232), (128, 150)], fill=blk, outline=edge)
d.polygon([(128, 128), (60, 60), (18, 70), (8, 128), (18, 186), (60, 196)], fill=blk, outline=edge)
d.polygon([(128, 128), (196, 60), (238, 70), (248, 128), (238, 186), (196, 196)], fill=blk, outline=edge)
d.ellipse([104, 100, 152, 156], fill=(34, 34, 38, 255), outline=edge, width=3)
im.save(os.path.join(OUT, 'icon_noeud.png'))
print('noeud ok')
