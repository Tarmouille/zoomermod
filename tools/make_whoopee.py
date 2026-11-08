# Génère le coussin péteur de la tâche "Pascal Prout" (cube unité centré, x/z = largeur/profondeur, y = hauteur ;
# dimensions réelles = l'échelle donnée dans WhoopeeCushion.cs) :
#   whoopee.obj : poche ronde gonflée (dessus bombé, dessous presque plat), bourrelet de soudure sur le pourtour,
#                 embout aplati qui dépasse vers +x, petit motif sur le dessus
# Groupes = matériaux (couleurs dans WhoopeeCushion.cs). Faces doublées (recto/verso) : visibles sous tous les angles.
#   python make_whoopee.py <dossier Assets>
import math, os, sys

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
            f.write(f"# DaleMod - {title} (généré par tools/make_whoopee.py)\n")
            for x, y, z in self.V: f.write(f"v {x:.4f} {y:.4f} {z:.4f}\n")
            for g, faces in self.G.items():
                f.write(f"g {g}\n")
                for q in faces: f.write("f " + " ".join(str(i) for i in q) + "\n")

N = 28                 # segments du pourtour
CX, R = -0.09, 0.41    # centre (décalé : l'embout dépasse vers +x) et rayon de la poche
BOTTOM, SEAM = -0.5, -0.18

def ring(r, y, cx=CX):
    return [(cx + r * math.cos(2 * math.pi * i / N), y, r * math.sin(2 * math.pi * i / N)) for i in range(N)]

def band(o, g, lo, hi):
    for i in range(N):
        j = (i + 1) % N
        o.face(g, [lo[i], lo[j], hi[j], hi[i]])

def cap(o, g, rg, centre):
    for i in range(N):
        o.face(g, [centre, rg[i], rg[(i + 1) % N]])

def whoopee(path):
    o = Obj()
    # Dessous : presque plat, arrondi vers la soudure
    under = [(R * 0.80, BOTTOM), (R * 0.95, BOTTOM + 0.10), (R * 0.99, SEAM - 0.06)]
    cap(o, "rubber_dark", ring(*under[0]), (CX, BOTTOM, 0))
    prev = ring(*under[0])
    for r, y in under[1:]:
        cur = ring(r, y); band(o, "rubber", prev, cur); prev = cur
    # Bourrelet de soudure (un peu plus large, plus foncé)
    seam_lo, seam_hi = ring(R * 1.03, SEAM - 0.03), ring(R * 1.03, SEAM + 0.03)
    band(o, "seam", prev, seam_lo); band(o, "seam", seam_lo, seam_hi)
    prev = seam_hi
    # Dessus bombé : profil en dôme (gonflé), dernier anneau = motif central
    steps = 7
    for k in range(1, steps + 1):
        t = k / steps
        r = R * math.cos(t * math.pi / 2 * 0.92)
        y = SEAM + 0.03 + (0.5 - SEAM - 0.03) * math.sin(t * math.pi / 2) ** 0.8
        cur = ring(r, y)
        band(o, "rubber" if k < steps else "rubber_light", prev, cur)
        prev = cur
    cap(o, "rubber_light", prev, (CX, 0.5, 0))
    # Embout aplati : tube elliptique qui part de la soudure vers +x, évasé côté poche
    M = 12
    def tube(x, ry, rz, yc):
        return [(x, yc + ry * math.sin(2 * math.pi * i / M), rz * math.cos(2 * math.pi * i / M)) for i in range(M)]
    secs = [tube(CX + R * 0.80, 0.17, 0.10, SEAM), tube(CX + R * 1.12, 0.13, 0.072, SEAM),
            tube(0.45, 0.13, 0.065, SEAM), tube(0.5, 0.15, 0.075, SEAM)]
    for a, b in zip(secs, secs[1:]):
        g = "nozzle" if b is not secs[1] else "rubber"
        for i in range(M):
            j = (i + 1) % M
            o.face(g, [a[i], a[j], b[j], b[i]])
    cap_ring = secs[-1]
    for i in range(M):   # bout de l'embout (trou sombre)
        o.face("hole", [(0.5, SEAM, 0), cap_ring[i], cap_ring[(i + 1) % M]])
    o.save(path, "coussin péteur rose")

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "DaleMod", "Assets")
    whoopee(os.path.join(out, "whoopee.obj"))
    print("whoopee.obj écrit")
