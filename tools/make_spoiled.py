# Génère les aliments périmés de la tâche "Inventaire du frigo" (dans un cube unité centré, x/z = largeur/profondeur,
# y = hauteur ; les dimensions réelles sont l'échelle donnée dans FridgeCleanup.cs) :
#   yogurt.obj : pot de yaourt évasé, étiquette, opercule entrouvert, moisissure verte qui déborde
#   pizza.obj  : part de pizza (pointe vers -z), croûte, fromage, pepperoni, taches de moisi
# Groupes = matériaux (couleurs dans FridgeCleanup.cs). Faces doublées (recto/verso) : visibles sous tous les angles.
#   python make_spoiled.py <dossier Assets>
import math, os, random, sys

class Obj:
    def __init__(self): self.V, self.G = [], {}
    def face(self, g, pts):
        i0 = len(self.V)
        self.V.extend(pts)
        idx = list(range(i0 + 1, i0 + len(pts) + 1))
        faces = self.G.setdefault(g, [])
        faces.append(idx)
        faces.append(idx[::-1])   # verso
    def save(self, path, title):
        with open(path, "w", encoding="utf-8") as f:
            f.write(f"# DaleMod - {title} (généré par tools/make_spoiled.py)\n")
            for x, y, z in self.V: f.write(f"v {x:.4f} {y:.4f} {z:.4f}\n")
            for g, faces in self.G.items():
                f.write(f"g {g}\n")
                for q in faces: f.write("f " + " ".join(str(i) for i in q) + "\n")

def ring(r, y, n, wobble=0.0, rnd=None):
    return [(r * (1 + (rnd.uniform(-wobble, wobble) if rnd else 0)) * math.cos(2 * math.pi * i / n), y,
             r * (1 + (rnd.uniform(-wobble, wobble) if rnd else 0)) * math.sin(2 * math.pi * i / n)) for i in range(n)]

def band(o, g, lo, hi):
    n = len(lo)
    for i in range(n):
        j = (i + 1) % n
        o.face(g, [lo[i], lo[j], hi[j], hi[i]])

def disc(o, g, rg, centre):
    n = len(rg)
    for i in range(n):
        o.face(g, [centre, rg[i], rg[(i + 1) % n]])

def blob(o, g, cx, cy, cz, r, rnd, n=7, h=None):
    """Petite bosse irrégulière (cône aplati) posée en (cx, cy, cz)."""
    h = h if h is not None else r * 0.6
    base = [(cx + r * (1 + rnd.uniform(-0.3, 0.3)) * math.cos(2 * math.pi * i / n), cy,
             cz + r * (1 + rnd.uniform(-0.3, 0.3)) * math.sin(2 * math.pi * i / n)) for i in range(n)]
    disc(o, g, base, (cx, cy + h, cz))

def yogurt(path):
    rnd = random.Random(4)
    o, N = Obj(), 20
    bottom, top = -0.5, 0.42
    r0, r1 = 0.36, 0.5
    def r_at(y): return r0 + (r1 - r0) * (y - bottom) / (top - bottom)
    ys = [bottom, -0.25, 0.22, top]                      # pot / étiquette / pot
    rings = [ring(r_at(y), y, N) for y in ys]
    band(o, "cup", rings[0], rings[1])
    band(o, "label", rings[1], rings[2])
    band(o, "cup", rings[2], rings[3])
    disc(o, "cup", rings[0], (0, bottom, 0))
    rim = ring(r1 * 1.04, top + 0.02, N)                 # rebord
    band(o, "cup", rings[3], rim)
    # Opercule : à plat sur les 2/3, le dernier tiers relevé (entrouvert)
    foil_y = top + 0.03
    for i in range(N):
        j = (i + 1) % N
        a, b = rim[i], rim[j]
        lift_a = 0.35 * max(0.0, math.cos(2 * math.pi * i / N)) ** 2
        lift_b = 0.35 * max(0.0, math.cos(2 * math.pi * j / N)) ** 2
        o.face("foil", [(0, foil_y, 0), (a[0], foil_y + lift_a, a[2]), (b[0], foil_y + lift_b, b[2])])
    # Moisissure : coussin vert sous l'opercule relevé + bosses qui débordent
    disc(o, "mold", ring(0.42, foil_y - 0.005, N, 0.15, rnd), (0.1, foil_y + 0.04, 0))
    for _ in range(9):
        a = rnd.uniform(-1.2, 1.2)
        d = rnd.uniform(0.15, 0.48)
        blob(o, "mold", d * math.cos(a), foil_y + 0.01, d * math.sin(a), rnd.uniform(0.06, 0.12), rnd)
    for _ in range(3):   # coulures vertes sur le flanc
        a = rnd.uniform(-0.8, 0.8)
        x, z = r1 * 1.01 * math.cos(a), r1 * 1.01 * math.sin(a)
        o.face("mold", [(x, top, z - 0.05), (x, top, z + 0.05), (x * 0.97, top - rnd.uniform(0.15, 0.3), z)])
    o.save(path, "yaourt périmé")

def pizza(path):
    rnd = random.Random(9)
    o = Obj()
    tip, back = (0.0, -0.5), 0.5                          # pointe en -z, croûte en +z
    half_w = 0.5
    y0, y1 = -0.5, 0.0                                   # pâte : bas -> haut
    arc = [(half_w * math.sin(t), back - 0.12 * (1 - math.cos(t * 1.4))) for t in [i / 8 * 1.1 - 0.55 for i in range(9)]]
    arc = [(x * 0.5 / max(abs(arc[0][0]), 1e-6), z) for x, z in arc]
    outline = [tip] + arc                                # triangle arrondi côté croûte
    # Dessous et dessus de la pâte, flancs
    for i in range(1, len(outline) - 1):
        a, b = outline[i], outline[i + 1]
        o.face("dough", [(tip[0], y0, tip[1]), (a[0], y0, a[1]), (b[0], y0, b[1])])
        o.face("cheese", [(tip[0], y1, tip[1]), (a[0], y1, a[1]), (b[0], y1, b[1])])
    loop = outline + [outline[0]]
    for i in range(len(loop) - 1):
        a, b = loop[i], loop[i + 1]
        o.face("dough", [(a[0], y0, a[1]), (b[0], y0, b[1]), (b[0], y1, b[1]), (a[0], y1, a[1])])
    # Croûte : bourrelet le long de l'arc
    for i in range(len(arc) - 1):
        a, b = arc[i], arc[i + 1]
        for k in range(4):
            t0, t1 = math.pi * k / 4, math.pi * (k + 1) / 4
            def p(pt, t): return (pt[0], y1 + 0.35 * math.sin(t), pt[1] - 0.09 + 0.09 * math.cos(t) * -1)
            o.face("crust", [p(a, t0), p(b, t0), p(b, t1), p(a, t1)])
    # Pepperoni : disques posés sur le fromage
    for cx, cz in [(-0.13, 0.22), (0.15, 0.16), (0.0, -0.08), (0.02, 0.36)]:
        pts = [(cx + 0.09 * math.cos(2 * math.pi * i / 12), y1 + 0.06, cz + 0.09 * math.sin(2 * math.pi * i / 12)) for i in range(12)]
        disc(o, "pepperoni", pts, (cx, y1 + 0.07, cz))
    # Moisi : taches vertes irrégulières (bosses) un peu partout sur le dessus
    for _ in range(8):
        t = rnd.uniform(0.2, 0.95)
        w = rnd.uniform(-0.8, 0.8) * t * 0.45
        z = tip[1] + t * (back - 0.15 - tip[1])
        blob(o, "mold", w, y1 + 0.09, z, rnd.uniform(0.05, 0.11), rnd, h=0.06)
    o.save(path, "part de pizza périmée")

if __name__ == "__main__":
    d = sys.argv[1] if len(sys.argv) > 1 else "."
    yogurt(os.path.join(d, "yogurt.obj"))
    pizza(os.path.join(d, "pizza.obj"))
    print("yogurt.obj, pizza.obj")
