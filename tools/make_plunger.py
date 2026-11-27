# Génère la ventouse de la tâche janitor "Toilettes bouchées" (Plunger.cs), en mètres (échelle 1 dans le mod) :
#   plunger.obj : cloche en caoutchouc rouge (lèvre évasée en bas, dôme, col), manche en bois rond au bout arrondi.
# Tenue cloche rouge EN HAUT, manche dans la main : modèle retourné à l'enregistrement (demi-tour autour de x), puis
# remonté de LIFT (la main du jeu tient les objets un peu au-dessus de leur pivot : la prise, à 8 cm du bout du
# manche, tombe dans la main).
# 46 cm de haut, cloche de 13 cm de diamètre. Groupes = matériaux (couleurs dans Plunger.cs).
# Faces doublées (recto/verso) : visibles sous tous les angles.
#   python make_plunger.py <dossier Assets>
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
            f.write(f"# DaleMod - {title} (généré par tools/make_plunger.py)\n")
            for x, y, z in self.V: f.write(f"v {x:.4f} {-y + LIFT:.4f} {-z:.4f}\n")   # retournée, remontée
            for g, faces in self.G.items():
                f.write(f"g {g}\n")
                for q in faces: f.write("f " + " ".join(str(i) for i in q) + "\n")

N = 24
TOP = 0.08            # bout du manche, au-dessus de la prise
BOTTOM = TOP - 0.46   # bord de la cloche
LIFT = 0.06           # remontée dans la main (m)

def ring(r, y):
    return [(r * math.cos(2 * math.pi * i / N), y, r * math.sin(2 * math.pi * i / N)) for i in range(N)]

def loft(o, profile):
    # profile : [(rayon, hauteur, groupe de la bande qui y mène)]
    prev = ring(*profile[0][:2])
    for r, y, g in profile[1:]:
        cur = ring(r, y)
        for i in range(N):
            j = (i + 1) % N
            o.face(g, [prev[i], prev[j], cur[j], cur[i]])
        prev = cur
    return prev

def cap(o, g, rg, centre):
    for i in range(N):
        o.face(g, [centre, rg[i], rg[(i + 1) % N]])

def plunger(path):
    o = Obj()
    b = BOTTOM
    # Cloche : intérieur sombre (vu par en dessous), lèvre évasée, dôme, col autour du manche
    inner = [(0.052, b + 0.004), (0.045, b + 0.03), (0.032, b + 0.055), (0.0, b + 0.065)]
    first = ring(*inner[0])
    prev = first
    for r, y in inner[1:-1]:
        cur = ring(r, y)
        for i in range(N):
            j = (i + 1) % N
            o.face("rubber_inner", [prev[i], prev[j], cur[j], cur[i]])
        prev = cur
    cap(o, "rubber_inner", prev, (0.0, inner[-1][1], 0.0))
    outer = [
        (0.052, b + 0.004, None),
        (0.066, b + 0.000, "rubber_dark"),    # lèvre
        (0.065, b + 0.010, "rubber_dark"),
        (0.062, b + 0.030, "rubber"),
        (0.055, b + 0.050, "rubber"),
        (0.043, b + 0.066, "rubber"),
        (0.028, b + 0.078, "rubber"),
        (0.020, b + 0.086, "rubber"),         # col
        (0.019, b + 0.100, "rubber_dark"),
        (0.0125, b + 0.104, "rubber_dark"),
    ]
    loft(o, outer)
    # Manche en bois, rentré dans le col, bout arrondi
    handle = [(0.0125, b + 0.095, None), (0.0125, TOP - 0.012, "wood")]
    end = loft(o, handle)
    tip = [(0.0125, TOP - 0.012, None), (0.0105, TOP - 0.004, "wood_end"), (0.006, TOP, "wood_end")]
    end = loft(o, tip)
    cap(o, "wood_end", end, (0.0, TOP + 0.001, 0.0))
    o.save(path, "ventouse")

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "..", "DaleMod", "Assets")
    plunger(os.path.join(out, "plunger.obj"))
    print("plunger.obj écrit")
