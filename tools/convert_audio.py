# Convertit les .mp3 d'Assets (sous-dossiers compris, ex. Assets/karaoke/) en .wav 16 bits mono 44,1 kHz, lisibles par
# le mod (ModAssets.LoadWav). Ne refait que les fichiers nouveaux ou modifiés. Ces .mp3 ne sont pas déployés.
# Les musiques (Assets/karaoke/) sont converties en 22 kHz pour rester légères. Le jeu ne sait pas décoder le MP3
# lui-même (DownloadHandlerAudioClip retiré de sa compilation) : tout passe par du WAV.
# Usage : python tools/convert_audio.py DaleMod/Assets
import os, subprocess, sys
import imageio_ffmpeg

root = sys.argv[1] if len(sys.argv) > 1 else "DaleMod/Assets"
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
n = 0
for d, _, files in os.walk(root):
    music = os.path.basename(d).lower() == "karaoke"   # musiques : 22 kHz (deux fois plus léger, inaudible sur les enceintes du jeu)
    for f in files:
        if not f.lower().endswith(".mp3"): continue
        src = os.path.join(d, f)
        dst = os.path.splitext(src)[0] + ".wav"
        if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src): continue
        subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", src, "-ac", "1", "-ar", "22050" if music else "44100", "-c:a", "pcm_s16le", dst], check=True)
        print("converti :", dst)
        n += 1
print(f"{n} fichier(s) converti(s)")
