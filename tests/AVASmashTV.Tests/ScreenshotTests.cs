using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASmashTV.Engine;
using AVASmashTV.Models;
using AVASmashTV.Views;
using Controls = AVASmashTV.Engine.Controls;

namespace AVASmashTV.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SMASH_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smashtv-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(_dir));
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SMASH_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASmashTV-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>The engine and the drawing clock on together, with the contestants kept in the show.</summary>
    private static void Run(GameEngine g, GameCanvas c, int steps, AttractPilot?[] pilots, bool safe = true)
    {
        for (int i = 0; i < steps; i++)
        {
            g.Tick(pilots[0]?.Decide(g, 0) ?? default, pilots.Length > 1 ? pilots[1]?.Decide(g, 1) ?? default : default);
            c.Advance(Field.Step);
            foreach (var p in g.Players.Where(p => p.Joined))
            {
                if (p.Lives < 2) p.Lives = 3;
                if (safe && p.Invulnerable <= 0 && p.ShieldTime <= 0) p.Invulnerable = 0.01;
            }
        }
    }

    [AvaloniaFact]
    public void The_title()
    {
        var (w, c, g) = Open();
        g.Idle(60 * 2);
        c.Advance(2.3);
        Shot(w, c, "title");
        w.Close();
    }

    [AvaloniaFact]
    public void A_crowded_firefight()
    {
        var (w, c, g) = Open(seed: 5);
        g.StartGame(2);
        g.JumpTo(1, 9);
        var pilots = new[] { new AttractPilot(2), new AttractPilot(3) };
        g.PutItem(ItemKind.Spread, g.Players[0].Pos);
        Run(g, c, 60 * 3, pilots);
        // The doors bang open all round and the mob pours in.
        var kinds = new[] { EnemyKind.Grunt, EnemyKind.Grunt, EnemyKind.Orb, EnemyKind.Grunt, EnemyKind.Blade, EnemyKind.Grunt, EnemyKind.Snake, EnemyKind.Grunt };
        for (int i = 0; i < 60 * 8; i++)
        {
            if (i % 14 == 0 && i < 60 * 3)
            {
                var k = kinds[i / 14 % kinds.Length];
                g.SpawnBurst(k, (Side)(i / 14 % 4), k == EnemyKind.Grunt ? 6 : 2);
            }
            Run(g, c, 1, pilots);
            if (i > 60 * 2 && g.Phase == Phase.Fighting && g.Enemies.Count(e => e.Entering == 0) >= 30 && g.Shots.Count(s => !s.Hostile) >= 8
                && g.Effects.Any(e => e.Kind == EffectKind.Splat) && g.Players.All(p => p.Alive)) break;
        }
        g.Players[0].Invulnerable = g.Players[1].Invulnerable = 0;
        Shot(w, c, "firefight");
        w.Close();
    }

    [AvaloniaFact]
    public void Power_ups_and_prizes()
    {
        var (w, c, g) = Open(seed: 6);
        g.StartGame(2);
        g.Calm();
        g.JumpTo(0, 4);
        var (a, b) = (g.Players[0], g.Players[1]);
        a.Pos = new Vec(9, 11);
        b.Pos = new Vec(19, 8);
        g.PutItem(ItemKind.Shield, a.Pos);
        g.PutItem(ItemKind.Spread, a.Pos);
        g.PutItem(ItemKind.Rockets, b.Pos);
        g.Tick(default);
        var floor = new (ItemKind, double, double)[]
        {
            (ItemKind.Car, 4, 4), (ItemKind.Toaster, 7, 3.2), (ItemKind.Vcr, 13, 4.5), (ItemKind.Tv, 23.5, 3.5), (ItemKind.Gold, 25, 14),
            (ItemKind.Cash, 4.5, 15), (ItemKind.Cash, 15, 15.5), (ItemKind.Key, 21, 16), (ItemKind.Grenades, 3, 9.5), (ItemKind.Speed, 25, 9),
            (ItemKind.ExtraLife, 14, 12.5), (ItemKind.Spread, 10, 16),
        };
        foreach (var (k, x, y) in floor) g.PutItem(k, new Vec(x, y));
        foreach (var (k, x, y) in new[] { (EnemyKind.Grunt, 6.5, 12.0), (EnemyKind.Grunt, 11.5, 13.2), (EnemyKind.Grunt, 8.5, 8.4), (EnemyKind.Orb, 23.0, 6.0), (EnemyKind.Blade, 18.0, 13.0), (EnemyKind.Tank, 25.0, 11.5) })
            g.PutEnemy(k, new Vec(x, y));
        for (int i = 0; i < 24; i++)
        {
            g.Tick(new Controls(0, 0, 1, -1), new Controls(0, 0, 1, 0));
            c.Advance(Field.Step);
            foreach (var it in g.Floor) it.Life = 9;
        }
        g.Effects.RemoveAll(e => e.Kind == EffectKind.Score);
        Shot(w, c, "power-ups");
        w.Close();
    }

    [AvaloniaFact]
    public void The_map_between_rooms()
    {
        var (w, c, g) = Open(seed: 7);
        g.StartGame(1);
        g.Calm();
        g.JumpTo(0, 0);
        var p = g.Players[0];
        void Leave(int dx, int dy)
        {
            g.ClearRoom();
            p.Pos = Field.Centre;
            for (int i = 0; i < 400 && g.Phase == Phase.Cleared; i++) g.Tick(new Controls(dx, dy));
        }
        Leave(1, 0);
        g.Go();
        g.SetKeys(1);
        Leave(0, -1);
        g.Go();
        Leave(1, 0);
        Assert.Equal(Phase.Map, g.Phase);
        Assert.Equal([0, 1, 4, 7], g.Route);
        g.Players[0].Score = 48500;
        for (int i = 0; i < 70; i++) { g.Tick(default); c.Advance(Field.Step); }
        Shot(w, c, "map");
        w.Close();
    }

    [AvaloniaFact]
    public void The_host_has_a_word()
    {
        var (w, c, g) = Open(seed: 8);
        g.StartGame(1);
        g.JumpTo(0, 6);
        var pilots = new AttractPilot?[] { new AttractPilot(8) };
        Run(g, c, 60 * 9, pilots);
        var p = g.Players[0];
        g.PutItem(ItemKind.Car, p.Pos);
        Run(g, c, 1, pilots);
        Run(g, c, 20, pilots);
        Assert.Equal("A BRAND NEW CAR!", g.Message);
        p.Invulnerable = 0;
        Shot(w, c, "host");
        w.Close();
    }

    [AvaloniaFact]
    public void The_first_boss()
    {
        var (w, c, g) = Open(seed: 9);
        g.StartGame(2);
        g.JumpTo(0, Show.Circuits[0].BossRoom);
        var pilots = new[] { new AttractPilot(4), new AttractPilot(5) };
        g.Players[0].Pos = new Vec(6, 15);
        g.Players[1].Pos = new Vec(21, 14);
        Run(g, c, 60 * 4, pilots);
        var b = g.Boss!;
        // One cannon already shot off; the rest still on him.
        b.Part("LEFT CANNON")!.Health = 0;
        for (int i = 0; i < 60 * 8; i++)
        {
            foreach (var part in b.Parts.Where(x => x.Name != "LEFT CANNON")) part.Health = part.MaxHealth;
            Run(g, c, 1, pilots);
            if (i > 60 && b.Mode == 0 && g.Shots.Count(s => s.Hostile) >= 6 && g.Shots.Count(s => !s.Hostile) >= 6 && g.Players.All(p => p.Alive)) break;
        }
        foreach (var p in g.Players) p.Invulnerable = 0;
        Shot(w, c, "boss");
        w.Close();
    }

    [AvaloniaFact]
    public void The_final_tally()
    {
        var (w, c, g) = Open(seed: 10);
        g.StartGame(2);
        g.Calm();
        g.JumpTo(2, 9);
        var rng = new Random(3);
        foreach (var p in g.Players)
        {
            foreach (var k in new[] { ItemKind.Cash, ItemKind.Toaster, ItemKind.Vcr, ItemKind.Tv, ItemKind.Gold, ItemKind.Car })
                for (int n = rng.Next(p.Index == 0 ? 2 : 1, k == ItemKind.Car ? 3 : 9); n > 0; n--)
                {
                    g.PutItem(k, p.Pos);
                    g.Tick(default);
                }
            p.Kills = 380 + rng.Next(200);
            p.Score += p.Kills * 150;
        }
        g.SetKeys(Show.KeysNeeded - 2);
        g.Players[0].Prizes[ItemKind.Key] = 3;
        g.Players[1].Prizes[ItemKind.Key] = 1;
        g.ClearRoom();
        for (int i = 0; i < 60 * 9 && g.Phase != Phase.Tally; i++) g.Tick(default);
        for (int i = 0; i < 60 * 4; i++) { g.Tick(default); c.Advance(Field.Step); }
        Assert.Equal(Phase.Tally, g.Phase);
        Shot(w, c, "tally");
        w.Close();
    }

    [AvaloniaFact]
    public void Game_over_and_the_high_scores()
    {
        var (w, c, g) = Open(seed: 6);
        g.StartGame(1);
        g.Go();
        var p = g.Players[0];
        p.Score = 312400;
        p.Lives = 1;
        g.PutEnemy(EnemyKind.Grunt, p.Pos);
        for (int i = 0; i < 60 * 30 && g.State == GameState.Playing; i++) g.Tick(default);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (var ch in "LON") g.Initials!.Type(ch);
        g.Idle(5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(20);
        c.Advance(0.1);
        Shot(w, c, "high-scores");
        w.Close();
    }
}
