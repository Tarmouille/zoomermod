# Texture "écran mouillé" pour la Douche froide : gouttes sur l'objectif, avec bord sombre, intérieur à peine teinté,
# reflet clair en haut à gauche et point lumineux en bas (comme une lentille), plus quelques coulures.
# Usage : python tools/make_water_overlay.py DaleMod/Assets/water_drops.png [aperçu.png]
import math, sys
import numpy as np
from PIL import Image

W, H = 1600, 900
rng = np.random.default_rng(3)
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
rgba = np.zeros((H, W, 4), np.float32)   # couleur prémultipliée + alpha


def over(color, alpha, mask):
    """Peint color (r,g,b 0..1) avec l'opacité alpha*mask par-dessus ce qui existe déjà."""
    a = np.clip(alpha * mask, 0, 1)[..., None]
    c = np.array(color, np.float32)
    rgba[..., :3] = c * a + rgba[..., :3] * (1 - a)
    rgba[..., 3:] = a + rgba[..., 3:] * (1 - a)


def drop(cx, cy, r, stretch=1.0, wobble=0.12):
    """Une goutte : r = rayon en px ; stretch > 1 = goutte qui coule (allongée vers le bas)."""
    x0, x1 = int(max(cx - r * 1.6, 0)), int(min(cx + r * 1.6, W))
    y0, y1 = int(max(cy - r * 1.6, 0)), int(min(cy + r * 1.6 * stretch, H))
    if x0 >= x1 or y0 >= y1:
        return
    X, Y = xx[y0:y1, x0:x1] - cx, yy[y0:y1, x0:x1] - cy
    Y = np.where(Y > 0, Y / stretch, Y)                    # plus longue vers le bas
    ang = np.arctan2(Y, X)
    k = rng.uniform(0, 2 * math.pi, 3)
    rr = r * (1 + wobble * (np.sin(2 * ang + k[0]) * 0.5 + np.sin(3 * ang + k[1]) * 0.3 + np.sin(5 * ang + k[2]) * 0.2))
    d = np.sqrt(X * X + Y * Y) / rr                         # 0 au centre, 1 au bord
    inside = np.clip((1 - d) * r / 1.2, 0, 1)               # bord antialiasé

    sub = (slice(y0, y1), slice(x0, x1))
    global rgba
    full = rgba
    rgba = full[sub]
    # intérieur : très légère teinte froide, plus marquée vers le bord (épaisseur de l'eau)
    over((0.78, 0.86, 0.95), 0.10 + 0.22 * d ** 3, inside)
    # bord sombre (la goutte "réfracte" le décor), plus épais et plus sombre en bas, presque absent en haut
    low = np.clip(0.5 + Y / rr * 0.8, 0.15, 1)
    rim = np.clip(1 - np.abs(d - 0.88) / (0.08 + 0.08 * low), 0, 1) * inside
    over((0.08, 0.12, 0.17), 0.5 * low, rim)
    # reflet : croissant clair en haut à gauche
    hx, hy = X / rr + 0.38, Y / rr + 0.42
    hl = np.clip(1 - np.sqrt(hx * hx * 1.6 + hy * hy * 3.0) / 0.32, 0, 1)
    over((1, 1, 1), 0.85, hl ** 1.5 * inside)
    # point lumineux en bas à droite (la lumière concentrée par la goutte)
    cx2, cy2 = X / rr - 0.25, Y / rr - 0.5
    caustic = np.clip(1 - np.sqrt(cx2 * cx2 * 2.5 + cy2 * cy2 * 4) / 0.35, 0, 1)
    over((0.92, 0.97, 1), 0.35, caustic ** 2 * inside)
    full[sub] = rgba
    rgba = full


# grosses gouttes, moyennes, gouttelettes ; quelques coulures avec un sillage de micro-gouttes
for _ in range(14):
    drop(rng.uniform(0, W), rng.uniform(0, H), rng.uniform(26, 48))
for _ in range(55):
    drop(rng.uniform(0, W), rng.uniform(0, H), rng.uniform(10, 24))
for _ in range(130):
    drop(rng.uniform(0, W), rng.uniform(0, H), rng.uniform(2.5, 6), wobble=0.05)


def streak(cx, top, bottom, w):
    """Sillage laissé par une goutte qui a coulé : bande fine et continue, ondulée, qui s'élargit vers le bas."""
    global rgba
    x0, x1 = int(max(cx - w * 3, 0)), int(min(cx + w * 3, W))
    y0, y1 = int(max(top, 0)), int(min(bottom, H))
    if x0 >= x1 or y0 >= y1:
        return
    X, Y = xx[y0:y1, x0:x1], yy[y0:y1, x0:x1]
    t = (Y - top) / max(bottom - top, 1)
    centre = cx + np.sin(Y / 23.0 + cx) * 1.5
    half = w * (0.35 + 0.65 * t)
    d = np.abs(X - centre) / half
    inside = np.clip((1 - d) * half, 0, 1)
    sub = (slice(y0, y1), slice(x0, x1))
    full = rgba
    rgba = full[sub]
    over((0.8, 0.88, 0.96), 0.12, inside)
    over((0.08, 0.12, 0.17), 0.28, np.clip(1 - np.abs(d - 0.85) / 0.2, 0, 1) * inside)
    over((1, 1, 1), 0.35, np.clip(1 - np.abs(X - centre + half * 0.35) / 1.0, 0, 1) * inside)
    full[sub] = rgba
    rgba = full


for _ in range(8):
    cx, cy, r = rng.uniform(0, W), rng.uniform(H * 0.3, H), rng.uniform(12, 20)
    length = rng.uniform(90, 280)
    streak(cx, cy - length, cy - r * 0.5, r * 0.45)
    drop(cx, cy, r, stretch=1.5)

out = rgba.copy()
a = out[..., 3:]
out[..., :3] = np.where(a > 1e-4, out[..., :3] / np.maximum(a, 1e-4), 0)   # dé-prémultiplié pour le PNG
img = Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA")
img.save(sys.argv[1] if len(sys.argv) > 1 else "water_drops.png")

if len(sys.argv) > 2:   # aperçu sur un fond de bureau stylisé
    bg = np.zeros((H, W, 3), np.float32)
    bg[:] = np.linspace(0.85, 0.55, H)[:, None, None] * np.array([0.93, 0.9, 0.82])
    bg[int(H * 0.62):] = [0.35, 0.3, 0.27]
    for i in range(6):
        x = 120 + i * 250
        bg[int(H * 0.35):int(H * 0.62), x:x + 180] = [0.25, 0.3, 0.38]
        bg[int(H * 0.38):int(H * 0.56), x + 12:x + 168] = [0.45, 0.65, 0.85]
    comp = bg * (1 - a) + out[..., :3] * a
    Image.fromarray((np.clip(comp, 0, 1) * 255).astype(np.uint8)).save(sys.argv[2])
