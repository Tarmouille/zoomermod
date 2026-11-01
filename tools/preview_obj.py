# Aperçu rapide d'un .obj (rendu à plat, algorithme du peintre) : python preview_obj.py in.obj out.png
import sys, math
import numpy as np
from PIL import Image, ImageDraw
# Aperçu : python preview_obj.py in.obj out.png [sx,sy,sz]  (échelle = localScale utilisée dans le jeu)
COLORS = {"powder": (242, 242, 238), "plastic": (200, 214, 226), "zip": (210, 40, 40),
          "plastic_dirty": (214, 200, 178), "lid": (70, 140, 220), "lid_dark": (40, 95, 170),
          "sauce": (170, 45, 25), "pasta": (240, 205, 110), "meat": (120, 55, 30), "smear": (150, 70, 40),
          "cup": (236, 232, 222), "label": (230, 110, 140), "foil": (190, 195, 200), "mold": (95, 150, 60),
          "dough": (215, 170, 105), "cheese": (240, 200, 90), "crust": (180, 120, 60), "pepperoni": (170, 40, 30)}
SCALE = np.array([float(x) for x in sys.argv[3].split(",")]) if len(sys.argv) > 3 else np.array([0.09, 0.03, 0.06])
V, F, g = [], [], "powder"
for line in open(sys.argv[1]):
    p = line.split()
    if not p: continue
    if p[0] == "v": V.append([float(x) for x in p[1:4]])
    elif p[0] == "g": g = p[1]
    elif p[0] == "f": F.append(([int(x.split("/")[0]) - 1 for x in p[1:]], g))
V = np.array(V) * SCALE
V -= V.mean(0); V /= np.abs(V).max()
def rot(a, b):
    ca, sa, cb, sb = math.cos(a), math.sin(a), math.cos(b), math.sin(b)
    ry = np.array([[ca, 0, sa], [0, 1, 0], [-sa, 0, ca]])
    rx = np.array([[1, 0, 0], [0, cb, -sb], [0, sb, cb]])
    return rx @ ry
W = 900
img = Image.new("RGB", (W * 2, W), (40, 44, 52))
d = ImageDraw.Draw(img)
light = np.array([0.4, 0.8, -0.5]); light /= np.linalg.norm(light)
for k, (a, b) in enumerate([(math.radians(-35), math.radians(-30)), (math.radians(150), math.radians(-25))]):
    P = V @ rot(a, b).T
    tris = []
    for idx, grp in F:
        pts = P[idx]
        n = np.cross(pts[1] - pts[0], pts[2] - pts[0]); ln = np.linalg.norm(n)
        if ln == 0: continue
        n /= ln
        shade = 0.35 + 0.65 * abs(n @ light)
        col = tuple(int(c * shade) for c in COLORS.get(grp, (180, 180, 180)))
        tris.append((pts[:, 2].mean(), [(W * k + W / 2 + x * W * 0.4, W / 2 - y * W * 0.4) for x, y, _ in pts], col))
    for _, poly, col in sorted(tris, key=lambda t: -t[0]):
        d.polygon(poly, fill=col, outline=col)
img.save(sys.argv[2])
