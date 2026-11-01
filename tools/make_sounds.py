# Sons de remplacement synthétisés (WAV 16 bits mono 44,1 kHz) : splash, splotch, snore, crackle.
# Seul crackle.wav est encore utilisé : splash, plotch et snore1/2 sont des vrais sons fournis par l'utilisateur.
# Usage : python tools/make_sounds.py DaleMod/Assets
import os, sys, wave
import numpy as np

SR = 44100
rng = np.random.default_rng(7)


def lowpass(x, a):
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc += a * (v - acc)
        y[i] = acc
    return y


def env(n, attack, decay):
    t = np.arange(n) / SR
    return np.minimum(1.0, t / attack) * np.exp(-t / decay)


def save(path, x):
    x = x / (np.max(np.abs(x)) + 1e-9) * 0.9
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())


def splash():
    n = int(0.9 * SR)
    body = lowpass(rng.normal(size=n), 0.25) * env(n, 0.005, 0.18)
    drops = np.zeros(n)
    for _ in range(40):   # gouttes qui retombent
        i = rng.integers(int(0.1 * SR), n - 2000)
        f = rng.uniform(900, 2200)
        k = np.arange(1500)
        drops[i:i + 1500] += np.sin(2 * np.pi * f * k / SR * (1 + k / 3000)) * np.exp(-k / 250) * 0.25
    return body + drops


def splotch():
    n = int(0.25 * SR)
    k = np.arange(n)
    squish = lowpass(rng.normal(size=n), 0.12) * env(n, 0.01, 0.06)
    bloop = np.sin(2 * np.pi * (300 - 900 * k / n) * k / SR) * env(n, 0.003, 0.05) * 0.6
    return squish + bloop


def snore():
    n = int(3.2 * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    inhale = (t < 1.4)
    buzz = np.sign(np.sin(2 * np.pi * 38 * t)) * 0.6 + lowpass(rng.normal(size=n), 0.08)
    shape = np.where(inhale, np.sin(np.pi * np.clip(t / 1.4, 0, 1)) ** 2, 0)
    out += lowpass(buzz, 0.05) * shape
    exhale = (t > 1.7) & (t < 2.9)
    out += lowpass(rng.normal(size=n), 0.03) * np.where(exhale, np.sin(np.pi * np.clip((t - 1.7) / 1.2, 0, 1)), 0) * 0.5
    return out


def crackle():
    n = int(2.0 * SR)
    out = lowpass(rng.normal(size=n), 0.6) * 0.04 + np.sin(2 * np.pi * 50 * np.arange(n) / SR) * 0.05   # bourdonnement
    for _ in range(70):   # claquements d'étincelles
        i = rng.integers(0, n - 800)
        L = rng.integers(100, 700)
        out[i:i + L] += rng.normal(size=L) * np.exp(-np.arange(L) / (L / 4)) * rng.uniform(0.4, 1.0)
    return out


if __name__ == "__main__":
    d = sys.argv[1] if len(sys.argv) > 1 else "."
    # Seul crackle est généré : ne pas écraser les vrais sons (splash.wav, plotch.wav, snore1/2.wav)
    for name, f in [("crackle", crackle)]:
        save(os.path.join(d, name + ".wav"), f())
        print(name + ".wav")
