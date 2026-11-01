# Logo-texte "GlucksTube" pour la fenêtre du lecteur vidéo (remplace celui de MeowTube) :
# photo ronde de glucks.png + "Glucks" en noir + "Tube" en blanc sur une pastille rouge, façon site de vidéos.
# Usage : python tools/make_glucks_logo.py DaleMod/Assets/glucks.png DaleMod/Assets/gluckstube_logo.png [aperçu.png]
import sys
from PIL import Image, ImageDraw, ImageFont

src, out = sys.argv[1], sys.argv[2]
H = 128                      # hauteur du logo (px)
RED = (230, 33, 23, 255)


def font(size):
    for name in ("arialbd.ttf", "seguisb.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


f = font(int(H * 0.62))
probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
w1 = probe.textbbox((0, 0), "Glucks", font=f)
w2 = probe.textbbox((0, 0), "Tube", font=f)
pad = int(H * 0.12)
face_d = H
pill_w = (w2[2] - w2[0]) + pad * 2
W = face_d + pad + (w1[2] - w1[0]) + pad // 2 + pill_w + pad // 2
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Photo dans un rond, avec un liseré rouge
face = Image.open(src).convert("RGBA").resize((face_d, face_d), Image.LANCZOS)
mask = Image.new("L", (face_d * 4, face_d * 4), 0)
ImageDraw.Draw(mask).ellipse((0, 0, face_d * 4 - 1, face_d * 4 - 1), fill=255)
mask = mask.resize((face_d, face_d), Image.LANCZOS)
img.paste(face, (0, 0), mask)
d.ellipse((2, 2, face_d - 3, face_d - 3), outline=RED, width=max(3, H // 32))

# "Glucks" puis "Tube" sur pastille rouge arrondie
x = face_d + pad
ty = (H - (w1[3] - w1[1])) // 2 - w1[1]
d.text((x, ty), "Glucks", font=f, fill=(25, 25, 25, 255))
x += (w1[2] - w1[0]) + pad // 2
d.rounded_rectangle((x, int(H * 0.12), x + pill_w, int(H * 0.88)), radius=int(H * 0.2), fill=RED)
d.text((x + pad - w2[0], ty), "Tube", font=f, fill=(255, 255, 255, 255))

img.save(out)
print(out, img.size)
if len(sys.argv) > 3:   # aperçu sur fond clair (comme une fenêtre du PC)
    bg = Image.new("RGBA", (W + 40, H + 40), (240, 240, 240, 255))
    bg.alpha_composite(img, (20, 20))
    bg.save(sys.argv[3])
