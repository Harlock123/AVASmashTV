# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A twin-stick arena shooter in the style of Williams' 1990 arcade game Smash TV, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. One or two contestants fight through three circuits of studio rooms to the bosses and the
Pleasure Dome. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are
drawn in code, and the sounds, the host's voice and the music are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASmashTV.slnx                      # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASmashTV.slnx                       # every test runs headless: no window opens
dotnet run --project src/AVASmashTV               # play it; --no-audio for silence
SMASH_SHOTS=$PWD/Images dotnet test tests/AVASmashTV.Tests --filter ScreenshotTests   # refresh the README's pictures
SMASH_PROBE=1 dotnet test tests/AVASmashTV.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                                  # how the computer gets on through the show (SMASH_PROBE=2: two players)
python3 tools/make_sounds.py                      # rebuild src/AVASmashTV/Sounds (standard-library Python only)
```

`tools/speech.py` is a small formant speech synthesiser that `make_sounds.py` imports for the voices.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASmashTV/Models/`: `Arena.cs` has `Vec`, the room's measures (`Field`, including the fixed step) and its
  doors; `Sprites.cs` holds the contestants' pixel art.
- `src/AVASmashTV/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, initials, high scores), the phases of the show
    and the fixed-step loop.
  - `GameEngine.Play.cs` plays a room: circuits and routes, contestants, shots and weapons, prizes, power-ups and
    keys; `GameEngine.Enemies.cs` moves the mob; `GameEngine.Bosses.cs` has the layered bosses.
  - `Entities.cs` has the players, weapons, enemies, shots, prizes and the twin-stick `Controls` record.
  - `AttractPilot.cs` is the computer contestant, used by the attract mode and the tests.
  - `InputHandler.cs` (keys, two players) and `ControllerManager.cs` (SDL2 gamepads, one per player) produce
    `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASmashTV/Views/`: `GameCanvas` (split over partial files: drawing, the cast as shapes in
  `GameCanvas.Art.cs`, and the screens) draws a fixed 1024 x 768 picture scaled to the window and runs the 60 Hz
  timer. Ctrl+S saves a screenshot to the Pictures folder.
- `src/AVASmashTV/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASmashTV.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, input, screenshot and
  Shift-release tests, and the pilot probe.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`) from an accumulator, independent of
  the frame rate, so tests can play deterministically from a seed. Draw gameplay random numbers from the
  engine's seeded `_rng` (`new Random(seed)`), never from `Random.Shared`. The views' own `Random`s are only for
  sparkle and must not touch the game.
- **Engine first, drawing second.** Gameplay belongs in `Engine/` and `Models/`, where tests can reach it without
  a window. `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** Both sticks use the engine's sign: down the screen is +1, up is -1. Test
  new controls through `InputHandler` (`InputTests`), not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` and `InputTests` cover this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVASmashTV`); tests load the
  table from a fresh folder under the system temp path and delete it afterwards. The screenshot tests use a
  silent `SoundManager`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change rooms, enemies or bosses, run the probe above to see the computer still gets through.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds and voices go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
