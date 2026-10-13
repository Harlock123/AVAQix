#!/usr/bin/env python3
"""Synthesises AVAQix's sound effects and tunes into ../src/AVAQix/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAQix", "Sounds")
RNG = random.Random(1981)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def drone(seconds=2.0):
    """The qix's drone: two buzzing voices whose pitch wobbles, made to loop without a seam (every
    voice and every wobble goes round a whole number of times in the loop)."""
    n = int(RATE * seconds)
    voices = [(70, 4, 3, "saw", 0.5), (105, 6, 5, "square", 0.22), (141, 9, 2, "tri", 0.3)]
    out = []
    phases = [0.0] * len(voices)
    for i in range(n):
        t = i / RATE
        s = 0.0
        for j, (f0, depth, wobbles, kind, gain) in enumerate(voices):
            f = f0 + depth * math.sin(2 * math.pi * wobbles * t / seconds)
            phases[j] += f / RATE
            s += voice(kind, phases[j]) * gain
        # A slow throb over the whole loop.
        s *= 0.75 + 0.25 * math.sin(2 * math.pi * 4 * t / seconds)
        out.append(s)
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    write("Hum.wav", drone(), 0.55)
    write("DrawFast.wav", tone(0.045, 900, 1150, "pulse", release=1.2), 0.22)
    write("DrawSlow.wav", tone(0.07, 520, 430, "square", release=1.0), 0.22)
    write("Claim.wav", cat(tone(0.18, 260, 1300, "tri", release=0.4),
                           *[tone(0.05, hz(n), kind="pulse", release=0.5) for n in ["C6", "E6", "G6", "C7"]]), 0.42)
    write("Fuse.wav", tone(0.12, 3000, 1500, "noise", release=0.8), 0.3)
    write("Death.wav", mix(tone(1.3, 1400, 50, "square", release=0.6, vibrato=0.08, vib_rate=22),
                           tone(1.3, 900, 200, "noise", release=0.9)), 0.6)
    write("SparxRelease.wav", cat(*[tone(0.09, f, kind="square", release=0.3) for f in [880, 660, 880, 660]]), 0.4)
    write("SuperSparx.wav", cat(*[tone(0.16, 500, 1400, "saw", release=0.2) for _ in range(4)]), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="square", release=0.4) for n in ["E5", "G#5", "B5", "E6"] * 3]), 0.45)

    # The title tune: a slow, eerie figure in A minor that climbs and falls back (new), loops.
    write("TitleTune.wav", tune(108,
        [("A4", 1), ("E5", 1), ("C5", 0.5), ("B4", 0.5), ("A4", 1),
         ("F5", 1), ("E5", 0.5), ("D5", 0.5), ("C5", 1), ("B4", 1),
         ("A4", 1), ("C5", 1), ("E5", 0.5), ("G5", 0.5), ("F5", 1),
         ("E5", 1), ("D5", 0.5), ("B4", 0.5), ("A4", 2)],
        [(n, 2) for n in ["A2", "A2", "F2", "E2", "A2", "C3", "D3", "E2"]], kind="tri"), 0.6)
    write("StartTune.wav", tune(160,
        [("A4", 0.5), ("C5", 0.5), ("E5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E5", 0.5), ("A5", 1.5)],
        [("A2", 1), ("E3", 1), ("A2", 2)]), 0.6)
    write("RoundClear.wav", tune(150,
        [("C5", 0.25), ("E5", 0.25), ("G5", 0.25), ("C6", 0.25), ("B5", 0.5), ("G5", 0.5), ("C6", 1.5), ("-", 1),
         ("E6", 0.25), ("D6", 0.25), ("C6", 0.25), ("G5", 0.25), ("C6", 2)],
        [("C3", 1), ("G2", 1), ("C3", 2), ("F2", 1), ("G2", 1), ("C3", 2)]), 0.6)
    write("Split.wav", tune(170,
        [("E5", 0.25), ("B5", 0.25), ("E6", 0.25), ("B5", 0.25)] * 2 +
        [("G#5", 0.5), ("B5", 0.5), ("E6", 0.5), ("G#6", 0.5), ("B6", 2)],
        [("E2", 1), ("E3", 1), ("E2", 1), ("B2", 1), ("E2", 3)]), 0.6)
    write("GameOver.wav", tune(84,
        [("E5", 1), ("C5", 1), ("A4", 1), ("B4", 1), ("G#4", 1), ("A4", 2), ("-", 1)],
        [("A2", 2), ("F2", 2), ("E2", 2), ("A2", 2)], kind="tri"), 0.65)
    print("sounds written to", os.path.normpath(OUT))


if __name__ == "__main__":
    main()
