# Génère Assets/tupperware_full.obj (fermé, bolo visible à travers le plastique) et
# Assets/tupperware_dirty.obj (ouvert, couvercle relevé, taché), dans un cube unité centré
# (x = largeur, y = hauteur, z = profondeur), coins arrondis, avec UV (vt).
# Groupes = matériaux : voir les textures dans Tupperware.cs (tools/make_textures.py les génère).
#   python make_tupperware.py <dossier Assets>
import math, os, sys

class Obj:
    def __init__(self): self.V, self.T, self.G = [], [], {}
    def face(self, g, pts, uvs):
        idx = []
        for p, t in zip(pts, uvs):
            self.V.append(p); self.T.append(t)
            idx.append((len(self.V), len(self.T)))
        self.G.setdefault(g, []).append(idx)
    def save(self, path, title):
        with open(path, "w", encoding="utf-8") as f:
            f.write(f"# DaleMod - {title} (généré par tools/make_tupperware.py)\n")
            for x, y, z in self.V: f.write(f"v {x:.4f} {y:.4f} {z:.4f}\n")
            for u, v in self.T: f.write(f"vt {u:.4f} {v:.4f}\n")
            for g, faces in self.G.items():
                f.write(f"g {g}\n")
                for q in faces: f.write("f " + " ".join(f"{a}/{b}" for a, b in q) + "\n")

def rounded_rect(hw, hd, r, seg=4):
    """Contour (x, z) d'un rectangle à coins arrondis, sens trigonométrique."""
    pts = []
    for cx, cz, a0 in [(hw - r, hd - r, 0), (-hw + r, hd - r, 90), (-hw + r, -hd + r, 180), (hw - r, -hd + r, 270)]:
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts

def perimeter_u(ring, tiles=1.0):
    """u cumulé le long du contour (pour enrouler une texture autour de la boîte)."""
    us, acc = [0.0], 0.0
    for i in range(1, len(ring) + 1):
        a, b = ring[i - 1], ring[i % len(ring)]
        acc += math.dist(a, b)
        us.append(acc)
    return [u / acc * tiles for u in us]

def band(o, g, ring, y0, y1, v0=0.0, v1=1.0, tiles=4.0, xf=lambda p: p):
    us = perimeter_u(ring, tiles)
    n = len(ring)
    for i in range(n):
        a, b = ring[i], ring[(i + 1) % n]
        o.face(g, [xf((a[0], y0, a[1])), xf((b[0], y0, b[1])), xf((b[0], y1, b[1])), xf((a[0], y1, a[1]))],
               [(us[i], v0), (us[i + 1], v0), (us[i + 1], v1), (us[i], v1)])

def cap(o, g, ring, y, hw, hd, xf=lambda p: p):
    """Face plane (éventail) avec UV projetées sur x/z."""
    c = (0.0, 0.0)
    n = len(ring)
    for i in range(n):
        a, b = ring[i], ring[(i + 1) % n]
        pts = [c, a, b]
        o.face(g, [xf((p[0], y, p[1])) for p in pts], [((p[0] / hw + 1) / 2, (p[1] / hd + 1) / 2) for p in pts])

def ring_between(o, g, outer, inner, y, tiles=4.0):
    """Bord supérieur entre la paroi extérieure et intérieure."""
    us = perimeter_u(outer, tiles)
    n = len(outer)
    for i in range(n):
        a, b, c, d = outer[i], outer[(i + 1) % n], inner[(i + 1) % n], inner[i]
        o.face(g, [(a[0], y, a[1]), (b[0], y, b[1]), (c[0], y, c[1]), (d[0], y, d[1])],
               [(us[i], 0), (us[i + 1], 0), (us[i + 1], 0.1), (us[i], 0.1)])

HW, HD, R = 0.5, 0.5, 0.14       # demi-largeur, demi-profondeur, rayon des coins
YB, YT = -0.5, 0.22              # fond, bord supérieur de la boîte
W = 0.035                        # épaisseur des parois
OUT = rounded_rect(HW, HD, R)
INN = rounded_rect(HW - W, HD - W, R - W)

def full(path):
    o = Obj()
    band(o, "plastic", OUT, YB, YB + 0.10, 0, 0.2)
    band(o, "food", OUT, YB + 0.10, YT - 0.10, 0, 1.0, tiles=3.0)          # bolo vue à travers le plastique
    band(o, "plastic", OUT, YT - 0.10, YT, 0.2, 0.4)
    cap(o, "plastic", OUT, YB, HW, HD)                                      # fond
    # couvercle : un peu plus large, bord + dessus nervuré
    LID = rounded_rect(HW + 0.025, HD + 0.025, R + 0.02)
    band(o, "lid", LID, YT, YT + 0.09, 0.0, 0.1, tiles=2.0)
    cap(o, "lid", LID, YT + 0.09, HW + 0.025, HD + 0.025)
    # clips sur les 4 côtés
    for sx, sz, w, d in [(0, HD + 0.035, 0.36, 0.02), (0, -HD - 0.035, 0.36, 0.02), (HW + 0.035, 0, 0.02, 0.30), (-HW - 0.035, 0, 0.02, 0.30)]:
        clip = [(sx - w / 2, sz - d / 2), (sx + w / 2, sz - d / 2), (sx + w / 2, sz + d / 2), (sx - w / 2, sz + d / 2)]
        band(o, "lid", clip, YT - 0.12, YT + 0.09, 0.05, 0.12, tiles=0.2)
    o.save(path, "tupperware plein")

def dirty(path):
    o = Obj()
    band(o, "dirty", OUT, YB, YT, 0, 1.0, tiles=3.0)                        # paroi extérieure tachée
    band(o, "dirty", INN, YB + W, YT, 0, 1.0, tiles=3.0)                    # paroi intérieure
    ring_between(o, "dirty", OUT, INN, YT)
    cap(o, "dirty", OUT, YB, HW, HD)                                        # dessous
    cap(o, "sauce", INN, YB + W, HW - W, HD - W)                            # fond intérieur : sauce séchée
    # couvercle relevé à ~105°, articulé sur le bord arrière (+z)
    LID = rounded_rect(HW + 0.025, HD + 0.025, R + 0.02)
    ang = math.radians(105)
    hinge_z, hinge_y = HD + 0.025, YT
    def lid_xf(p):
        x, y, z = p
        dz, dy = z - hinge_z, y - hinge_y          # repère local du couvercle fermé, relatif à la charnière
        cz = dz * math.cos(ang) - dy * math.sin(ang)
        cy = dz * math.sin(ang) + dy * math.cos(ang)
        return (x, hinge_y + cy, hinge_z + cz)
    band(o, "lid", LID, YT, YT + 0.09, 0.0, 0.1, tiles=2.0, xf=lid_xf)
    cap(o, "lid", LID, YT + 0.09, HW + 0.025, HD + 0.025, xf=lid_xf)
    cap(o, "lid", LID, YT, HW + 0.025, HD + 0.025, xf=lid_xf)
    o.save(path, "tupperware sale")

out = sys.argv[1]
full(os.path.join(out, "tupperware_full.obj"))
dirty(os.path.join(out, "tupperware_dirty.obj"))
