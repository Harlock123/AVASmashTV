#!/usr/bin/env python3
"""Synthesises AVASmashTV's sound effects, voices and tunes into ../src/AVASmashTV/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine or recording: every effect is built here from square, saw, sine and
noise voices. The host's lines are spoken by the formant voice in speech.py. The music in the arena is a new
composition; the title plays the Galop infernal (the "Can-can") from Jacques Offenbach's Orpheus in the Underworld
(1858), which is in the public domain. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASmashTV", "Sounds")
RNG = random.Random(1999)


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


def place(out, at, part, gain=1.0):
    """Adds a sound into a longer one, starting at a time in seconds."""
    start = int(at * RATE)
    for i, v in enumerate(part):
        if start + i >= len(out):
            break
        out[start + i] += v * gain


def lowpass(samples, cutoff):
    k = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    out, y = [], 0.0
    for s in samples:
        y += (s - y) * k
        out.append(y)
    return out


def highpass(samples, cutoff):
    low = lowpass(samples, cutoff)
    return [s - l for s, l in zip(samples, low)]


def echo(samples, delay=0.11, feedback=0.3, taps=3):
    """A studio's slap of echo."""
    out = samples + silence(delay * taps)
    d = int(delay * RATE)
    for t in range(1, taps + 1):
        g = feedback ** t
        for i, v in enumerate(samples):
            out[i + d * t] += v * g
    return out


def bell(seconds, f, gain=1.0):
    """A struck bell: a few inharmonic partials, each fading at its own pace."""
    n = int(RATE * seconds)
    parts = [(1.0, 1.0, 3.0), (2.76, 0.5, 5.0), (5.4, 0.25, 8.0), (2.0, 0.35, 4.0)]
    out = []
    for i in range(n):
        t = i / RATE
        s = sum(a * math.exp(-t * d) * math.sin(2 * math.pi * f * m * t) for m, a, d in parts)
        out.append(s * gain * min(1.0, t / 0.002))
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def line(bpm, notes, kind, gain, release, attack=0.004, vibrato=0.0):
    """A melody as (note, beats) pairs; '-' is a rest, and a note may be a chord of names joined by '+'."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
            continue
        names = note.split("+")
        out.extend(mix(*[tone(d, hz(n), kind=kind, release=release, attack=attack, vibrato=vibrato, gain=gain / len(names) ** 0.5) for n in names]))
    return out


# ----- the effects ---------------------------------------------------------------------------------------------

def gunshot():
    return mix(tone(0.055, 1800, 400, "noise", release=1.6, gain=0.9), tone(0.05, 180, 70, "square", release=1.4, gain=0.6))


def splat():
    """A wet cartoon splurch."""
    goo = lowpass(tone(0.22, 1400, 200, "noise", release=0.8), 1600)
    plop = tone(0.2, 340, 70, "sine", release=0.6, vibrato=0.25, vib_rate=28)
    return mix(goo, plop)


def explosion(seconds=0.9, low=50):
    rumble = lowpass(tone(seconds, 900, 80, "noise", release=0.55), 1200)
    crack = tone(0.08, 3000, 800, "noise", release=1.2, gain=0.6)
    thump = tone(seconds * 0.5, 110, low, "square", release=0.9, gain=0.5)
    return mix(rumble, crack, thump)


def crowd(seconds=3.2, seed=7):
    """A studio audience roaring: a swell of murmur, and voices whooping over it."""
    rng = random.Random(seed)
    n = int(seconds * RATE)
    roar = lowpass(lowpass([rng.uniform(-1, 1) for _ in range(n)], 900), 1400)
    for i in range(n):
        t = i / RATE
        swell = min(1.0, t / 0.35) * (1 if t < seconds - 1.2 else max(0.0, (seconds - t) / 1.2))
        roar[i] *= swell * (0.8 + 0.2 * math.sin(2 * math.pi * 0.7 * t))
    for _ in range(26):
        at = rng.uniform(0.05, seconds - 1.0)
        f = rng.uniform(260, 620)
        place(roar, at, tone(rng.uniform(0.25, 0.5), f, f * rng.uniform(1.2, 1.6), "saw", attack=0.04, release=0.8, vibrato=0.03, vib_rate=6, gain=0.06))
    return mix(roar, applause(seconds, seed + 1, density=40, gain=0.5))


def applause(seconds=3.0, seed=3, density=70, gain=1.0):
    """Hands clapping, lots of them, dying away."""
    rng = random.Random(seed)
    out = silence(seconds)
    t = 0.0
    while t < seconds - 0.05:
        fall = max(0.0, 1 - t / seconds) ** 0.6
        clap = highpass(tone(0.025, 4000, 1500, "noise", release=2.2), 700)
        place(out, t, clap, gain * rng.uniform(0.5, 1.0) * fall)
        t += rng.expovariate(density * (0.4 + fall))
    return out


def host(phonemes, seed=5, pitch=118, speed=0.95):
    """The host on his microphone: a showman's voice, a touch of studio echo."""
    words = say(phonemes, rate=RATE, pitch=pitch, speed=speed, size=1.02, seed=seed)
    return echo(words, delay=0.09, feedback=0.22, taps=2)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    write("Shoot.wav", gunshot(), 0.32)
    write("Spread.wav", mix(gunshot(), tone(0.07, 900, 300, "pulse", release=1.4, gain=0.4)), 0.34)
    write("Rocket.wav", mix(lowpass(tone(0.4, 300, 2200, "noise", release=0.7), 2500), tone(0.35, 140, 420, "saw", release=0.8, gain=0.4)), 0.42)
    write("Grenade.wav", mix(tone(0.13, 240, 90, "square", release=1.0), tone(0.1, 600, 200, "noise", release=1.2, gain=0.5)), 0.42)
    write("Splat.wav", splat(), 0.42)
    write("Explosion.wav", explosion(), 0.6)
    write("Hit.wav", tone(0.035, 700, 380, "square", release=1.0), 0.25)
    write("Clank.wav", mix(*[tone(0.16, f, kind="sine", release=1.8, gain=g) for f, g in [(1180, 0.6), (1730, 0.5), (2640, 0.35), (3910, 0.2)]], tone(0.02, 5000, kind="noise", release=1.0, gain=0.3)), 0.32)
    # The prize "ding": a bright bell, twice.
    write("Prize.wav", mix(bell(0.7, hz("E6")), cat(silence(0.09), bell(0.7, hz("B6"), 0.8))), 0.5)
    write("PowerUp.wav", cat(*[tone(0.05, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6"]], tone(0.25, hz("C7"), kind="square", release=0.8, vibrato=0.02, vib_rate=12)), 0.42)
    write("Key.wav", mix(cat(*[tone(0.06, hz(n), kind="tri", release=0.4) for n in ["G5", "B5", "D6", "G6", "B6", "D7"]]), cat(silence(0.3), bell(0.9, hz("G6")))), 0.48)
    write("OneUp.wav", cat(*[tone(d, hz(n), kind="square", release=0.4) for n, d in [("E5", 0.08), ("G5", 0.08), ("E6", 0.08), ("C6", 0.08), ("D6", 0.08), ("G6", 0.25)]]), 0.42)
    write("Door.wav", mix(lowpass(tone(0.35, 400, 60, "noise", release=0.7), 900), tone(0.22, 85, 38, "square", release=0.9, gain=0.7), cat(silence(0.03), tone(0.25, 260, 180, "saw", release=1.5, vibrato=0.08, vib_rate=40, gain=0.25))), 0.55)
    write("Crowd.wav", crowd(), 0.6)
    write("Applause.wav", mix(applause(3.4), crowd(3.4, seed=11)), 0.55)
    write("PlayerDie.wav", mix(tone(0.7, 520, 90, "saw", release=0.6, vibrato=0.06, vib_rate=11), splat(), lowpass(tone(0.4, 600, 100, "noise", release=0.6, gain=0.5), 1500)), 0.55)
    write("BossHit.wav", mix(tone(0.08, 170, 110, "square", release=1.0), tone(0.05, 1200, 600, "noise", release=1.5, gain=0.5)), 0.32)
    write("BossPart.wav", mix(explosion(1.2, 35), cat(silence(0.05), [s * 0.5 for s in tone(0.5, 1180, 900, "sine", release=1.5)])), 0.65)

    # The host's lines.
    write("SayBigBucks.wav", host("B IH1 G B AH1 K S ! , B IH1 G P R AY1 Z IH Z !", seed=3, pitch=124), 0.75)
    write("SayLoveIt.wav", host("AY1 , L AH1 V IH T !", seed=5, pitch=132, speed=0.85), 0.75)
    write("SayGoodLuck.wav", host("G UH1 D L AH1 K , Y UW L N IY1 D IH T !", seed=8), 0.75)
    write("SayWhatAHaul.wav", host("W AH1 T AH HH AO1 L !", seed=9, pitch=126, speed=0.9), 0.75)
    write("SayRatings.wav", host("R EY1 T IH NG Z , TH R UW1 DH AH R UW1 F !", seed=12), 0.75)
    write("SayShowsOver.wav", host("SH OW1 Z OW1 V ER , F OW1 K S !", seed=4, pitch=112), 0.75)

    write("Music.wav", arena_music(), 0.5)
    write("TitleTune.wav", cancan(), 0.6)
    # The show's sting: brassy chords, up and out.
    write("StartTune.wav", mix(
        line(150, [("C5+E5+G5", 0.5), ("C5+E5+G5", 0.5), ("-", 0.25), ("F5+A5+C6", 0.75), ("G5+B5+D6", 0.75), ("C6+E6+G6", 2.5)], "saw", 0.5, 0.5, attack=0.02, vibrato=0.006),
        line(150, [("C3", 1), ("-", 0.25), ("F2", 0.75), ("G2", 0.75), ("C2", 2.5)], "tri", 0.7, 0.4)), 0.6)
    # Game over: the sad trombone.
    write("GameOver.wav", lowpass(cat(*[tone(d, hz(n), kind="saw", attack=0.03, release=r, vibrato=v, vib_rate=5.5, gain=0.9) for n, d, r, v in
                                         [("G3", 0.45, 0.3, 0.0), ("F#3", 0.45, 0.3, 0.0), ("F3", 0.45, 0.3, 0.0), ("E3", 1.6, 0.6, 0.03)]]), 1400), 0.65)
    print("sounds written to", os.path.normpath(OUT))


# ----- the music ----------------------------------------------------------------------------------------------

def drums(bpm, bars):
    """Four on the floor: a kick every beat, a snare on two and four, hats in between."""
    beat = 60.0 / bpm
    out = silence(bars * 4 * beat)
    kick = mix(tone(0.16, 150, 42, "sine", attack=0.001, release=0.8), tone(0.01, 2000, 500, "noise", release=1.0, gain=0.3))
    snare = mix(highpass(tone(0.16, 6000, 2500, "noise", release=1.2), 900), tone(0.08, 220, 160, "tri", release=1.0, gain=0.4))
    hat = highpass(tone(0.035, 9000, kind="noise", release=1.6, gain=0.5), 5000)
    for b in range(bars * 4):
        place(out, b * beat, kick, 0.9)
        if b % 2 == 1:
            place(out, b * beat, snare, 0.55)
        place(out, b * beat + beat / 2, hat, 0.35)
        if b % 4 == 3:
            place(out, b * beat + beat * 0.75, hat, 0.25)
    return out


def arena_music():
    """The arena's music: a driving electronic tune in A minor, new for this game. Loops after sixteen bars."""
    bpm = 138
    chords = ["A", "A", "F", "G", "A", "A", "F", "E", "D", "A", "F", "G", "D", "A", "E", "E"]
    roots = {"A": ("A1", "A2", "E2"), "F": ("F1", "F2", "C2"), "G": ("G1", "G2", "D2"), "E": ("E1", "E2", "B1"), "D": ("D2", "D3", "A2")}
    bass_notes = []
    for c in chords:
        low, high, fifth = roots[c]
        bass_notes += [(n, 0.5) for n in [low, low, high, low, low, high, fifth, high]]
    bass = lowpass(line(bpm, bass_notes, "saw", 0.8, 0.9), 900)
    arps = ["A4 C5 E5 A5 G5 E5 C5 E5", "A5 B5 C6 B5 A5 G5 E5 G5", "F5 A5 C6 A5 F5 E5 F5 A5", "G5 B5 D6 B5 G5 F5 D5 B4",
            "A4 C5 E5 A5 G5 E5 C5 E5", "C6 B5 A5 G5 A5 C6 E6 C6", "F5 A5 C6 F6 E6 C6 A5 F5", "E5 G#5 B5 E6 D6 B5 G#5 B5"]
    lead_notes = [(n, 0.5) for bar in arps for n in bar.split()]
    lead_notes += [("D6", 1), ("A5", 0.5), ("F5", 0.5), ("D6", 1), ("E6", 1),
                   ("C6", 1.5), ("B5", 0.5), ("A5", 2),
                   ("A5", 1), ("C6", 1), ("F6", 1), ("E6", 1),
                   ("D6", 1.5), ("C6", 0.5), ("B5", 1), ("G5", 1),
                   ("D6", 1), ("F6", 1), ("E6", 0.5), ("D6", 0.5), ("C6", 1),
                   ("E6", 1), ("C6", 1), ("A5", 2),
                   ("B5", 1), ("G#5", 1), ("E5", 1), ("G#5", 1),
                   ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 0.5), ("G#5", 1), ("-", 1)]
    lead = lowpass(line(bpm, lead_notes, "pulse", 0.32, 0.5), 3800)
    pads = lowpass(line(bpm, [({"A": "A3+C4+E4", "F": "F3+A3+C4", "G": "G3+B3+D4", "E": "E3+G#3+B3", "D": "D3+F3+A3"}[c], 4) for c in chords], "saw", 0.22, 0.1, attack=0.08), 1200)
    return mix(bass, lead, pads, drums(bpm, len(chords)))


def cancan():
    """Offenbach's Galop infernal, the Can-can (1858, public domain): the tune twice, the second time up an octave, over an oom-pah."""
    bpm = 152
    phrase = [("C5", 1), ("D5", 0.25), ("F5", 0.25), ("E5", 0.25), ("D5", 0.25),
              ("G5", 0.5), ("G5", 0.5), ("G5", 0.25), ("A5", 0.25), ("E5", 0.25), ("F5", 0.25),
              ("D5", 0.5), ("D5", 0.5), ("D5", 0.25), ("F5", 0.25), ("E5", 0.25), ("D5", 0.25),
              ("C5", 0.25), ("C6", 0.25), ("B5", 0.25), ("A5", 0.25), ("G5", 0.25), ("F5", 0.25), ("E5", 0.25), ("D5", 0.25),
              ("C5", 1), ("D5", 0.25), ("F5", 0.25), ("E5", 0.25), ("D5", 0.25),
              ("G5", 0.5), ("G5", 0.5), ("G5", 0.25), ("A5", 0.25), ("E5", 0.25), ("F5", 0.25),
              ("D5", 0.5), ("G5", 0.25), ("A5", 0.25), ("G5", 0.25), ("F5", 0.25), ("E5", 0.25), ("D5", 0.25),
              ("C5", 0.5), ("G4", 0.5), ("C5", 1)]
    up = [(n[:-1] + str(int(n[-1]) + 1), d) for n, d in phrase]
    lead = line(bpm, phrase + up, "pulse", 0.5, 0.7)
    harmony = ["C", "C", "G", "C", "C", "C", "G", "C"] * 2
    oom = {"C": ("C3", "G3", "C4+E4"), "G": ("G2", "D3", "B3+F4")}
    bass, chords = [], []
    for h in harmony:
        root, fifth, chord = oom[h]
        bass += [(root, 0.5), ("-", 0.5), (fifth, 0.5), ("-", 0.5)]
        chords += [("-", 0.5), (chord, 0.5), ("-", 0.5), (chord, 0.5)]
    return mix(lead, line(bpm, bass, "tri", 0.7, 0.5), line(bpm, chords, "square", 0.16, 0.9), drums_galop(bpm, len(harmony)))


def drums_galop(bpm, bars):
    """A bright snare on the off-beats, two beats to a bar."""
    beat = 60.0 / bpm
    out = silence(bars * 2 * beat)
    snare = highpass(tone(0.07, 6000, 3000, "noise", release=1.5), 1500)
    for b in range(bars * 2):
        place(out, b * beat + beat / 2, snare, 0.3)
    return out


if __name__ == "__main__":
    main()
