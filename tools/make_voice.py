#!/usr/bin/env python3
"""Génère la voix VIIRUS de ViiCord (« petit robot enfant »).
Outils requis : kokoro-onnx, soundfile, sox, ffmpeg.
Modèle (licence Apache-2.0) : kokoro-v1.0.onnx + voices-v1.0.bin
https://github.com/thewh1teagle/kokoro-onnx/releases/tag/model-files-v1.0
Usage : python3 make_voice.py dossier_sortie
"""
import os, subprocess, sys, tempfile
import soundfile as sf
from kokoro_onnx import Kokoro

LINES = {
    "Viirus_Message": "Nouveau message.",
    "Viirus_Rejoint": "Un utilisateur a rejoint.",
    "Viirus_Parti": "Un utilisateur est parti.",
    "Viirus_Appel_Sortant": "Appel en cours.",
    "Viirus_Connexion": "Connexion en cours.",
    "Viirus_Deconnexion": "Déconnexion.",
    "Viirus_Micro_Coupe": "Micro coupé.",
    "Viirus_Micro_Active": "Micro démuté.",
    "Viirus_Casque_Coupe": "Casque coupé.",
    "Viirus_Casque_Active": "Son rétabli.",
    "Viirus_Appel_Entrant": "Appel entrant.",
}
out = sys.argv[1] if len(sys.argv) > 1 else "out"
os.makedirs(out, exist_ok=True)
k = Kokoro("model.onnx", "voices.bin")
run = lambda *a: subprocess.run(a, check=True)
t = tempfile.mkdtemp()
for name, text in LINES.items():
    s, sr = k.create(text, voice="ff_siwis", speed=1.0, lang="fr-fr")
    sf.write(f"{t}/raw.wav", s, sr)
    run("sox", f"{t}/raw.wav", "-r", "44100", "-c", "1", f"{t}/m.wav", "pitch", "450")
    run("ffmpeg", "-loglevel", "error", "-y", "-i", f"{t}/m.wav", "-af",
        "highpass=f=200,equalizer=f=3500:t=q:w=1:g=3,loudnorm=I=-17:TP=-2", "-ar", "44100", "-ac", "1", f"{t}/n.wav")
    run("sox", f"{t}/n.wav", f"{t}/t.wav", "silence", "1", "0.02", "0.5%", "reverse", "silence", "1", "0.02", "0.5%", "reverse", "pad", "0", "0.15")
    run("sox", f"{t}/t.wav", "-b", "16", f"{out}/{name}.wav", "pad", "0.02", "0")
    print("ok", name)
