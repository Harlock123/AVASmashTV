using AVASmashTV.Models;

namespace AVASmashTV.Engine;

/// <summary>
/// The computer contestant for attract mode and the tests, twin-stick like anyone else. The fire stick
/// points at whatever is most worth shooting (close, and nearest one of the eight lines a shot can take);
/// the move stick is pushed about by everything in the room: away from the mob, the mines and the walls,
/// sideways out of the path of anything flying at it, and toward the prizes and power-ups when the way is
/// clear. Once the room's clear it heads for an exit, the door to a key room if there is one.
/// </summary>
public sealed class AttractPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private double _wanderAngle;
    private double _wanderTime;
    private int _exitRoom = int.MinValue;
    private Side _exit;

    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g, int index)
    {
        var me = g.Players[index];
        if (!me.Alive || g.Phase is not (Phase.Ready or Phase.Fighting or Phase.Cleared)) return default;
        var pos = me.Pos;

        // ----- the fire stick -----
        var aim = Target(g, pos);
        var (fx, fy) = aim is { } a ? Eight(a - pos) : (0, 0);

        // ----- the move stick -----
        if (g.Phase == Phase.Cleared)
        {
            // Nothing left to hurt it: straight to what's left on the floor, then out.
            if (Goal(g, me) is not { } there) return new Controls(0, 0, fx, fy);
            var (gx, gy) = Eight(there - pos, 0.2);
            return new Controls(gx, gy, fx, fy);
        }
        var push = new Vec(0, 0);
        foreach (var e in g.Enemies)
        {
            var away = pos - e.Pos;
            double d = Math.Max(0.3, away.Length - e.Radius);
            double reach = e.Kind switch { EnemyKind.Mine => 2.2, EnemyKind.Tank => 6, EnemyKind.Blade => 5, _ => 5.5 };
            if (d > reach) continue;
            double w = e.Kind == EnemyKind.Mine ? 2.5 : 4;
            push += away.Normal * (w / (d * d));
            if (e.Kind == EnemyKind.Snake)
                foreach (var s in e.SegmentPositions())
                {
                    var off = pos - s;
                    double ds = Math.Max(0.3, off.Length - 0.4);
                    if (ds < 3) push += off.Normal * (2 / (ds * ds));
                }
        }
        foreach (var s in g.Shots.Where(s => s.Hostile))
        {
            var rel = pos - s.Pos;
            var v = s.Vel.Normal;
            double ahead = rel.X * v.X + rel.Y * v.Y;
            if (ahead < -0.5 || ahead > 6) continue;
            var across = rel - v * ahead;
            double miss = across.Length;
            if (miss > 1.6) continue;
            // Step out of its path, to whichever side we're already on.
            var side = miss < 1e-3 ? v.Rotate(Math.PI / 2) : across.Normal;
            push += side * (3.5 / Math.Max(0.4, ahead + 0.5));
        }
        if (g.Boss is { Dead: false } boss)
        {
            var away = pos - boss.Pos;
            double d = Math.Max(0.5, away.Length - 3);
            if (d < 7) push += away.Normal * (14 / (d * d));
            foreach (var c in boss.CoilPositions())
            {
                var off = pos - c;
                double dc = Math.Max(0.3, off.Length - 0.75);
                if (dc < 3) push += off.Normal * (3 / (dc * dc));
            }
        }
        // The walls (and worse, the corners).
        double margin = 2.5;
        if (pos.X < margin) push += new Vec(1.5 / Math.Max(0.3, pos.X), 0);
        if (pos.X > Field.Width - margin) push -= new Vec(1.5 / Math.Max(0.3, Field.Width - pos.X), 0);
        if (pos.Y < margin) push += new Vec(0, 1.5 / Math.Max(0.3, pos.Y));
        if (pos.Y > Field.Height - margin) push -= new Vec(0, 1.5 / Math.Max(0.3, Field.Height - pos.Y));
        // Where it would like to be.
        var goal = Goal(g, me);
        if (goal is { } to)
        {
            var d = to - pos;
            push += d.Normal * 1.1;
        }
        else
        {
            // Nothing in mind: keep moving, drifting back toward the middle.
            _wanderTime -= Field.Step;
            if (_wanderTime <= 0) { _wanderTime = 1 + _rng.NextDouble(); _wanderAngle = _rng.NextDouble() * Math.PI * 2; }
            push += Vec.FromAngle(_wanderAngle) * 0.3 + (Field.Centre - pos) * 0.04;
        }
        // Never into a wall: up against one, only along it or away.
        if (pos.X < 1 && push.X < 0) push = push with { X = 0.6 };
        if (pos.X > Field.Width - 1 && push.X > 0) push = push with { X = -0.6 };
        if (pos.Y < 1 && push.Y < 0) push = push with { Y = 0.6 };
        if (pos.Y > Field.Height - 1 && push.Y > 0) push = push with { Y = -0.6 };
        var (mx, my) = push.Length < 0.15 ? (0, 0) : Eight(push, 0.38);
        return new Controls(mx, my, fx, fy);
    }

    /// <summary>What to shoot at: near, and as near one of the eight lines as can be.</summary>
    private static Vec? Target(GameEngine g, Vec pos)
    {
        Vec? best = null;
        double cost = double.MaxValue;
        void Consider(Vec at, double bias)
        {
            var d = at - pos;
            double len = d.Length;
            if (len > 16 || len < 1e-6) return;
            var (ex, ey) = Eight(d);
            var q = new Vec(ex, ey).Normal;
            double off = Math.Abs(q.X * d.Y - q.Y * d.X);
            double c = len + off * 4 + bias;
            if (c < cost) { cost = c; best = at; }
        }
        foreach (var e in g.Enemies)
        {
            if (e.Entering > 0 && e.Kind != EnemyKind.Grunt) continue;
            Consider(e.Pos, e.Kind == EnemyKind.Mine ? 6 : 0);
        }
        if (g.Boss is { Dead: false } b)
            foreach (var p in b.Parts.Where(p => !p.Gone && p.Layer == b.Exposed)) Consider(b.Pos + p.Offset, -2);
        return best;
    }

    /// <summary>Somewhere worth going: the exit when the room's clear, or something on the floor if it's safe to fetch.</summary>
    private Vec? Goal(GameEngine g, Player me)
    {
        if (g.Phase == Phase.Cleared)
        {
            // Pick up what's left, then out.
            var prize = g.Floor.Where(i => (i.Pos - me.Pos).Length < 8).OrderBy(i => (i.Pos - me.Pos).Length).FirstOrDefault();
            if (prize is not null) { Doing = "picking up"; return prize.Pos; }
            if (_exitRoom != g.RoomIndex * 100 + g.CircuitIndex)
            {
                _exitRoom = g.RoomIndex * 100 + g.CircuitIndex;
                var open = Enum.GetValues<Side>().Where(s => g.ExitTo[(int)s] != -1).ToList();
                if (open.Count == 0) return null;
                var keyed = open.Where(s => g.ExitTo[(int)s] >= 0 && g.Circuit.Rooms[g.ExitTo[(int)s]].Key).ToList();
                _exit = keyed.Count > 0 ? keyed[_rng.Next(keyed.Count)] : open[_rng.Next(open.Count)];
            }
            if (g.ExitTo[(int)_exit] == -1) return null;
            Doing = "leaving";
            // Line up with the doorway, then through it.
            var front = Field.Door(_exit) + Field.Inward(_exit) * 1.5;
            return Field.InDoorway(_exit, me.Pos) && (me.Pos - Field.Door(_exit)).Length < 2.5 || (me.Pos - front).Length < 0.3
                ? Field.Door(_exit) - Field.Inward(_exit) * 2
                : front;
        }
        Doing = "fighting";
        Item? best = null;
        double bestCost = double.MaxValue;
        foreach (var i in g.Floor)
        {
            double d = (i.Pos - me.Pos).Length;
            if (d > 12 || i.Life < d / me.Speed) continue;
            // Not into the thick of them.
            int near = g.Enemies.Count(e => e.Kind != EnemyKind.Mine && (e.Pos - i.Pos).Length < 2.5);
            if (near > (i.Kind == ItemKind.Key ? 2 : 0)) continue;
            double cost = d - (i.Kind.IsPowerUp() ? 4 : i.Kind == ItemKind.Key ? 6 : 0);
            if (cost < bestCost) { bestCost = cost; best = i; }
        }
        if (best is not null) Doing = "fetching " + best.Kind;
        return best?.Pos;
    }

    /// <summary>A direction as one of the eight a stick can give.</summary>
    private static (int, int) Eight(Vec d, double dead = 0.38)
    {
        var n = d.Normal;
        return (Math.Abs(n.X) > dead ? Math.Sign(n.X) : 0, Math.Abs(n.Y) > dead ? Math.Sign(n.Y) : 0);
    }
}
