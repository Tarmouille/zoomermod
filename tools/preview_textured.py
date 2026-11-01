# Aperçu texturé d'un .obj (rastérisation avec z-buffer, UV, une texture par groupe) :
#   python preview_textured.py in.obj out.png sx,sy,sz dossier_textures groupe=fichier.png ...
import math, os, sys
import numpy as np
from PIL import Image

src, dst, scale, texdir = sys.argv[1], sys.argv[2], np.array([float(x) for x in sys.argv[3].split(",")]), sys.argv[4]
texmap = dict(a.split("=") for a in sys.argv[5:])
tex = {g: np.asarray(Image.open(os.path.join(texdir, f)).convert("RGB"), dtype=np.float32) for g, f in texmap.items()}

V, T, F, g = [], [], [], "default"
for line in open(src, encoding="utf-8"):
    p = line.split()
    if not p: continue
    if p[0] == "v": V.append([float(x) for x in p[1:4]])
    elif p[0] == "vt": T.append([float(x) for x in p[1:3]])
    elif p[0] == "g": g = p[1]
    elif p[0] == "f":
        idx = [tuple(int(k) - 1 for k in t.split("/")) for t in p[1:]]
        for i in range(1, len(idx) - 1): F.append((idx[0], idx[i], idx[i + 1], g))
V = np.array(V) * scale; V -= V.mean(0); V /= np.abs(V).max()
T = np.array(T)

W = 700
light = np.array([0.35, 0.8, -0.45]); light /= np.linalg.norm(light)

def render(yaw, pitch):
    ca, sa, cb, sb = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)
    R = np.array([[1, 0, 0], [0, cb, -sb], [0, sb, cb]]) @ np.array([[ca, 0, sa], [0, 1, 0], [-sa, 0, ca]])
    P = V @ R.T
    img = np.zeros((W, W, 3), np.float32); img[:] = (40, 44, 52)
    zb = np.full((W, W), np.inf)
    sx = W / 2 + P[:, 0] * W * 0.42
    sy = W / 2 - P[:, 1] * W * 0.42
    for a, b, c, grp in F:
        ia, ib, ic = a[0], b[0], c[0]
        n = np.cross(P[ib] - P[ia], P[ic] - P[ia]); ln = np.linalg.norm(n)
        if ln == 0: continue
        shade = 0.35 + 0.65 * abs((n / ln) @ light)
        x = np.array([sx[ia], sx[ib], sx[ic]]); y = np.array([sy[ia], sy[ib], sy[ic]]); z = np.array([P[ia, 2], P[ib, 2], P[ic, 2]])
        x0, x1 = int(max(0, x.min())), int(min(W - 1, x.max()) + 1)
        y0, y1 = int(max(0, y.min())), int(min(W - 1, y.max()) + 1)
        if x1 <= x0 or y1 <= y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-9: continue
        l0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
        l1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
        l2 = 1 - l0 - l1
        m = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
        if not m.any(): continue
        zz = l0 * z[0] + l1 * z[1] + l2 * z[2]
        sub = zb[y0:y1, x0:x1]
        m &= zz < sub
        if not m.any(): continue
        sub[m] = zz[m]
        if grp in tex and len(T):
            t = tex[grp]; th, tw = t.shape[:2]
            uv = np.array([T[a[1]], T[b[1]], T[c[1]]])
            u = (l0 * uv[0, 0] + l1 * uv[1, 0] + l2 * uv[2, 0]) % 1.0
            v = (l0 * uv[0, 1] + l1 * uv[1, 1] + l2 * uv[2, 1]) % 1.0
            col = t[((1 - v) * (th - 1)).astype(int), (u * (tw - 1)).astype(int)]
        else:
            col = np.full(gx.shape + (3,), 200.0)
        img[y0:y1, x0:x1][m] = col[m] * shade
    return img

out = np.concatenate([render(math.radians(-35), math.radians(-28)), render(math.radians(140), math.radians(-22))], axis=1)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(dst)
