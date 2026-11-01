# Génère Assets/cokebag.obj : petit sachet zip low-poly, dans un cube unité
# (x = largeur, y = épaisseur, z = longueur ; le zip est côté +z). Groupes : powder, plastic, zip.
import math, sys
out = sys.argv[1]
V, groups = [], {"powder": [], "plastic": [], "zip": []}

def v(x, y, z):
    V.append((x, y, z)); return len(V)

def quad(g, a, b, c, d): groups[g].append((a, b, c, d))

# Partie pleine de poudre : coussin dont l'épaisseur s'annule sur les bords
N, M = 10, 10
z0, z1 = -0.44, 0.30
x0, x1 = -0.44, 0.44
def puff(i, j):
    u, w = i / N, j / M
    return 0.5 * (math.sin(math.pi * u) * math.sin(math.pi * w)) ** 0.6
top = [[v(x0 + (x1 - x0) * i / N, puff(i, j), z0 + (z1 - z0) * j / M) for j in range(M + 1)] for i in range(N + 1)]
bot = [[v(x0 + (x1 - x0) * i / N, -puff(i, j) * 0.6, z0 + (z1 - z0) * j / M) for j in range(M + 1)] for i in range(N + 1)]
for i in range(N):
    for j in range(M):
        quad("powder", top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j])
        quad("powder", bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1])

def box(g, x0, x1, y0, y1, za, zb):
    p = {(a, b, c): v((x0, x1)[a], (y0, y1)[b], (za, zb)[c]) for a in (0, 1) for b in (0, 1) for c in (0, 1)}
    for f in [((0,0,0),(0,0,1),(0,1,1),(0,1,0)), ((1,0,0),(1,1,0),(1,1,1),(1,0,1)),
              ((0,0,0),(1,0,0),(1,0,1),(0,0,1)), ((0,1,0),(0,1,1),(1,1,1),(1,1,0)),
              ((0,0,0),(0,1,0),(1,1,0),(1,0,0)), ((0,0,1),(1,0,1),(1,1,1),(0,1,1))]:
        quad(g, *[p[k] for k in f])

box("plastic", -0.5, 0.5, -0.02, 0.02, -0.5, 0.40)   # feuille de plastique (rebord soudé + haut vide)
box("zip",     -0.5, 0.5, -0.07, 0.07, 0.40, 0.50)   # fermeture zip

with open(out, "w") as f:
    f.write("# DaleMod - sachet (généré par tools/make_bag.py)\n")
    for x, y, z in V: f.write(f"v {x:.4f} {y:.4f} {z:.4f}\n")
    for g, faces in groups.items():
        f.write(f"g {g}\n")
        for q in faces: f.write("f " + " ".join(map(str, q)) + "\n")
