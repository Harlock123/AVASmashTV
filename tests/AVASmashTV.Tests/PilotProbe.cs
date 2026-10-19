using AVASmashTV.Engine;

namespace AVASmashTV.Tests;

/// <summary>How the computer contestants get on through the whole show (set SMASH_PROBE to run it; SMASH_PROBE=2 sends in two).</summary>
public sealed class PilotProbe(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smashtv-probe-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void How_the_computer_contestants_do()
    {
        if (Environment.GetEnvironmentVariable("SMASH_PROBE") is not { } mode) return;
        int players = mode == "2" ? 2 : 1;
        for (int seed = 1; seed <= 4; seed++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var g = new GameEngine(seed, new HighScoreTable(_dir));
            g.StartGame(players);
            var pilots = new[] { new AttractPilot(seed), new AttractPilot(seed + 100) };
            var deaths = new List<string>();
            var wasDown = new bool[2];
            var rooms = new List<string>();
            string room = "";
            double roomStart = 0;
            int t = 0;
            for (; t < 60 * 60 * 40 && g.State == GameState.Playing; t++)
            {
                g.Tick(pilots[0].Decide(g, 0), players > 1 ? pilots[1].Decide(g, 1) : default);
                if (g.Phase == Phase.Fighting && g.Room.Name != room)
                {
                    room = g.Room.Name;
                    roomStart = t / 60.0;
                }
                if (g.Phase == Phase.Cleared && rooms.LastOrDefault()?.StartsWith(room) != true) rooms.Add($"{room} {t / 60.0 - roomStart:0}s");
                for (int i = 0; i < players; i++)
                {
                    var p = g.Players[i];
                    if (p.Down && !wasDown[i])
                    {
                        var near = g.Enemies.OrderBy(e => (e.Pos - p.Pos).Length).FirstOrDefault();
                        var shot = g.Shots.Where(s => s.Hostile).OrderBy(s => (s.Pos - p.Pos).Length).FirstOrDefault();
                        deaths.Add($"{g.Room.Name}: {(shot is not null && (shot.Pos - p.Pos).Length < 1 ? shot.Kind.ToString() : near?.Kind.ToString() ?? g.Boss?.Name ?? "?")}");
                    }
                    wasDown[i] = p.Down;
                    // Lives without end, to see how far they get.
                    if (p.Lives < 2) p.Lives = 3;
                }
            }
            output.WriteLine($"seed {seed}: circuit {g.CircuitIndex + 1}, {g.RoomsCleared} rooms in {t / 3600.0:0.0} min, keys {g.Keys}, won {g.Won}, {g.Ending}, score {g.Players[0].Score:N0}, {deaths.Count} deaths, {sw.ElapsedMilliseconds} ms");
            output.WriteLine("  rooms: " + string.Join(", ", rooms));
            output.WriteLine("  deaths: " + string.Join("; ", deaths.GroupBy(d => d).OrderByDescending(gp => gp.Count()).Take(10).Select(gp => $"{gp.Key} x{gp.Count()}")));
        }
    }
}
