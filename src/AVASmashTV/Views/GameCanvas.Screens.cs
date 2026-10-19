using Avalonia;
using Avalonia.Media;
using AVASmashTV.Engine;
using AVASmashTV.Models;

namespace AVASmashTV.Views;

public sealed partial class GameCanvas
{
    private static void Shade(DrawingContext dc, uint colour = 0xD8000008) => dc.FillRectangle(Brush(colour), FloorRect.Deflate(16));

    /// <summary>Words broken into lines no wider than a width.</summary>
    private static List<string> Wrap(string text, double size, double width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && TextWidth(next, size) > width) { lines.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    /// <summary>The show's name in lights.</summary>
    private void DrawLogo(DrawingContext dc, Point c, double size)
    {
        // Rays behind it, turning slowly.
        using (At(dc, c, 1, _clock * 0.15))
            for (int i = 0; i < 16; i++)
                using (dc.PushTransform(Matrix.CreateRotation(i * Math.PI / 8)))
                    dc.DrawGeometry(Brush(i % 2 == 0 ? 0x30FFE14Au : 0x20FF3D3Du), null, Poly(new Point(0, 0), new Point(520, -60), new Point(520, 60)));
        Text(dc, "AVA", c + new Point(0, -size * 0.62), size * 0.36, Cyan, center: true);
        for (int i = 6; i >= 1; i--) Text(dc, "SMASH TV", c + new Point(i, i), size, Brush(i > 3 ? 0xFF5A0A0Au : 0xFFB01E1Eu), center: true);
        Text(dc, "SMASH TV", c, size, Yellow, center: true);
    }

    // A sample of each of the cast, to show on the title.
    private static readonly Enemy[] Cast = MakeCast();

    private static Enemy[] MakeCast()
    {
        var kinds = new[] { EnemyKind.Grunt, EnemyKind.Orb, EnemyKind.Snake, EnemyKind.Blade, EnemyKind.Tank, EnemyKind.Mine, EnemyKind.Shrapnel };
        var cast = new Enemy[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            var e = new Enemy { Kind = kinds[i], Pos = new Vec(2.6 + i * 3.8, 9.35), Vel = new Vec(1, 0.3), Turret = -0.5, Fuse = 5 };
            if (e.Kind == EnemyKind.Snake)
            {
                e.Segments = 4;
                for (int t = 0; t < 40; t++) e.Trail.Add(e.Pos - new Vec(t * 0.06, Math.Sin(t * 0.25) * 0.25));
            }
            cast[i] = e;
        }
        return cast;
    }

    private static readonly string[] CastNames = ["GRUNTS", "ORBS", "SNAKES", "BLADES", "TANKS", "MINES", "SHRAPNEL"];

    private void DrawTitle(DrawingContext dc)
    {
        Shade(dc, 0xC8000008);
        double cx = FloorRect.Center.X;
        DrawLogo(dc, new Point(cx, 222), 150);
        Text(dc, "THE YEAR IS 1999. THE NUMBER ONE SHOW ON TELEVISION:", new Point(cx, 312), 30, White, center: true);
        Text(dc, "FIGHT FOR YOUR LIFE!  WIN FABULOUS PRIZES!", new Point(cx, 344), 36, Pink, center: true);
        foreach (var e in Cast)
        {
            e.Age = _clock;
            var at = P(e.Pos);
            using (dc.PushTransform(Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateScale(1.45, 1.45) * Matrix.CreateTranslation(at.X, at.Y)))
                DrawEnemy(dc, e);
        }
        for (int i = 0; i < Cast.Length; i++) Text(dc, CastNames[i], P(Cast[i].Pos) + new Point(0, 44), 22, Dim, center: true);
        // The prizes, and the power-ups.
        var prizes = new[] { ItemKind.Cash, ItemKind.Toaster, ItemKind.Vcr, ItemKind.Tv, ItemKind.Gold, ItemKind.Car, ItemKind.Key };
        var powers = new[] { ItemKind.Spread, ItemKind.Rockets, ItemKind.Grenades, ItemKind.Shield, ItemKind.Speed, ItemKind.ExtraLife };
        Text(dc, "PRIZES", new Point(FloorRect.X + 70, 520), 26, Green, center: true);
        for (int i = 0; i < prizes.Length; i++) DrawItem(dc, prizes[i], new Point(FloorRect.X + 150 + i * 46, 520), 1.1);
        Text(dc, "POWER-UPS", new Point(FloorRect.X + 520, 520), 26, Cyan, center: true);
        for (int i = 0; i < powers.Length; i++) DrawItem(dc, powers[i], new Point(FloorRect.X + 610 + i * 44, 520), 1.05);
        Text(dc, $"COLLECT {Show.KeysNeeded} KEYS TO ENTER THE PLEASURE DOME", new Point(cx, 566), 28, Yellow, center: true);
        if (_clock % 1 < 0.65) Text(dc, "PRESS 1 OR ENTER: ONE PLAYER    PRESS 2: TWO PLAYERS", new Point(cx, 616), 36, Yellow, center: true);
        Text(dc, "PLAYER 1: W A S D MOVE, ARROW KEYS FIRE     PLAYER 2: I J K L MOVE, NUMBER PAD FIRE", new Point(cx, 654), 23, White, center: true);
        Text(dc, "© 2026 LONNIE WATSON  ·  IN THE STYLE OF THE 1990 ARCADE GAME", new Point(cx, 690), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        double cx = FloorRect.Center.X;
        Shade(dc);
        DrawLogo(dc, new Point(cx, 196), 90);
        Text(dc, "THE BIG WINNERS", new Point(cx, 264), 52, Pink, center: true);
        Text(dc, "RANK       WINNINGS   NAME   ROOMS", new Point(cx, 306), 28, Dim, center: true);
        var table = Engine.HighScores.Entries;
        for (int i = 0; i < table.Count; i++)
        {
            var e = table[i];
            bool fresh = i == Engine.NewEntryIndex;
            if (fresh && _clock % 0.5 > 0.35) continue;
            var brush = fresh ? Yellow : i == 0 ? Orange : White;
            Text(dc, $"{i + 1,2}.   {"$" + e.Score.ToString("N0"),12}    {e.Initials}    {e.Stage,3}", new Point(cx, 344 + i * 32), 32, brush, center: true);
        }
        if (_clock % 1 < 0.65) Text(dc, "PRESS 1 OR 2 TO PLAY", new Point(cx, 680), 34, Yellow, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var entry = Engine.Initials;
        if (entry is null) return;
        double cx = FloorRect.Center.X, cy = FloorRect.Center.Y;
        var p = Engine.Players[Math.Max(0, Engine.EntryPlayer)];
        var box = new Rect(cx - 320, cy - 170, 640, 340);
        dc.FillRectangle(Brush(0xF0000000), box);
        dc.DrawRectangle(null, Pen(p.Colour, 3), box.Deflate(4));
        Text(dc, $"{p.Name}: A NEW HIGH SCORE!", new Point(cx, cy - 124), 48, Yellow, center: true);
        Text(dc, $"${p.Score:N0}", new Point(cx, cy - 72), 48, White, center: true);
        for (int i = 0; i < HighScoreTable.InitialsLength; i++)
        {
            var at = new Point(cx - 64 + i * 64, cy + 20);
            bool cursor = i == entry.Cursor;
            Text(dc, entry.Initials[i].ToString(), at, 90, cursor ? Yellow : White, center: true);
            if (cursor && _clock % 0.6 < 0.4) dc.FillRectangle(Yellow, new Rect(at.X - 20, at.Y + 40, 40, 6));
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE.  ENTER WHEN DONE", new Point(cx, cy + 126), 26, Dim, center: true);
    }

    // ----- the map between rooms ------------------------------------------------------------------

    private void DrawMap(DrawingContext dc)
    {
        var g = Engine;
        var circuit = g.Circuit;
        Shade(dc, 0xE8060612);
        double cx = FloorRect.Center.X;
        Text(dc, $"CIRCUIT {circuit.Number}:  {circuit.Name}", new Point(cx, 150), 52, Yellow, center: true);
        const double left = 112, top = 196, cw = 200, ch = 140;
        Point Centre(RoomDef r) => new(left + cw / 2 + r.Col * cw, top + ch / 2 + r.Row * ch);
        var route = g.Route;
        bool Travelled(int a, int b)
        {
            for (int i = 1; i < route.Count; i++) if (route[i - 1] == a && route[i] == b) return true;
            return false;
        }
        bool flash = (int)(_clock * 3) % 2 == 0;
        // The corridors between the rooms.
        for (int i = 0; i < circuit.Rooms.Length; i++)
            foreach (int next in circuit.Rooms[i].Links)
            {
                bool done = Travelled(i, next);
                bool last = route.Count >= 2 && route[^2] == i && route[^1] == next;
                var a = Centre(circuit.Rooms[i]);
                var b = Centre(circuit.Rooms[next]);
                dc.DrawLine(Pen(0xFF0A0A10, 14), a, b);
                dc.DrawLine(Pen(done ? (last && flash ? 0xFFFFFFFFu : 0xFFFFE14Au) : 0xFF3A3A56u, done ? 8 : 6), a, b);
                // An arrow halfway, pointing the way the route goes.
                var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var dir = new Vec(b.X - a.X, b.Y - a.Y).Normal;
                var side = new Point(-dir.Y * 7, dir.X * 7);
                var tip = mid + new Point(dir.X * 8, dir.Y * 8);
                var back = mid - new Point(dir.X * 6, dir.Y * 6);
                dc.DrawGeometry(Brush(done ? 0xFFFFE14Au : 0xFF5A5A7Au), null, Poly(tip, back + side, back - side));
            }
        // The rooms.
        for (int i = 0; i < circuit.Rooms.Length; i++)
        {
            var r = circuit.Rooms[i];
            var c = Centre(r);
            int at = route.IndexOf(i);
            bool ahead = at == route.Count - 1, visited = at >= 0 && !ahead;
            uint fill = r.IsDome ? 0xFF3A0A30u : r.Boss is not null ? 0xFF3A0A10u : visited ? 0xFF3A2E0Au : 0xFF14142Au;
            uint edge = ahead ? (flash ? 0xFFFFFFFFu : 0xFFFFE14Au) : r.IsDome ? 0xFFFF5AC8u : r.Boss is not null ? 0xFFFF3D3Du : visited ? 0xFFFFE14Au : 0xFF3A3A5Au;
            var box = new Rect(c.X - 80, c.Y - 44, 160, 88);
            dc.DrawRectangle(Brush(fill), Pen(edge, ahead ? 4 : 2.5), box, 10, 10);
            var lines = Wrap(r.Name, 24, 148);
            for (int l = 0; l < lines.Count; l++)
                Text(dc, lines[l], new Point(c.X, c.Y - (lines.Count - 1) * 11 + l * 22 + (r.Boss is not null ? 8 : 0)), 24, visited || ahead ? White : Dim, center: true);
            if (r.Boss is not null) Text(dc, r.IsDome ? (g.Keys >= Show.KeysNeeded ? "DOME: OPEN" : "DOME: LOCKED") : "BOSS", new Point(c.X, c.Y - 28), 22, r.IsDome ? Pink : Red, center: true);
            if (r.Key && !visited) DrawKey(dc, new Point(box.Right - 14, box.Top + 14), 0.5);
            if (ahead) Text(dc, i == route[0] && route.Count == 1 ? "START" : "NEXT", new Point(c.X, box.Bottom + 12), 22, Yellow, center: true);
        }
        // The keys so far, and the winnings.
        Text(dc, "KEYS", new Point(left + 30, 672), 30, Yellow);
        for (int k = 0; k < Math.Max(Show.KeysNeeded, g.Keys); k++)
        {
            var kp = new Point(left + 110 + k * 34, 672);
            if (k < g.Keys) DrawKey(dc, kp, 0.7);
            else dc.DrawEllipse(null, Pen(0xFF4A4A60, 2), kp, 8, 8);
        }
        int winnings = g.Players.Where(p => p.Joined).Sum(p => p.Score);
        Text(dc, $"WINNINGS ${winnings:N0}", new Point(912 - 20, 672), 32, Green, right: true);
        DrawSpeech(dc);
    }

    // ----- the final tally ------------------------------------------------------------------------

    private static readonly ItemKind[] TallyRows = [ItemKind.Cash, ItemKind.Toaster, ItemKind.Vcr, ItemKind.Tv, ItemKind.Gold, ItemKind.Car, ItemKind.Key];

    private void DrawTally(DrawingContext dc)
    {
        var g = Engine;
        Shade(dc, 0xE8060612);
        double cx = FloorRect.Center.X;
        DrawLogo(dc, new Point(cx, 178), 70);
        Text(dc, "THE FINAL TALLY", new Point(cx, 232), 54, Pink, center: true);
        if (g.Ending is { } why) Text(dc, why, new Point(cx, 270), 30, g.Won ? Green : Dim, center: true);
        var players = g.Players.Where(p => p.Joined).ToList();
        double t = g.PhaseTime;
        for (int n = 0; n < players.Count; n++)
        {
            var p = players[n];
            double x0 = players.Count == 1 ? cx - 190 : FloorRect.X + 60 + n * 440;
            Text(dc, p.Name, new Point(x0 + 190, 312), 36, Brush(p.Colour), center: true);
            for (int i = 0; i < TallyRows.Length; i++)
            {
                if (t < 0.4 + i * 0.35) break;
                var k = TallyRows[i];
                int count = p.Prizes.GetValueOrDefault(k);
                double y = 356 + i * 40;
                DrawItem(dc, k, new Point(x0 + 30, y), 1);
                Text(dc, k.Title(), new Point(x0 + 64, y), 28, count > 0 ? White : Dim);
                Text(dc, $"× {count}", new Point(x0 + 260, y), 28, count > 0 ? Yellow : Dim, right: true);
                Text(dc, $"${count * k.Value():N0}", new Point(x0 + 390, y), 28, count > 0 ? Green : Dim, right: true);
            }
            if (t > 0.4 + TallyRows.Length * 0.35)
            {
                Text(dc, $"KILLS {p.Kills}", new Point(x0 + 20, 652), 30, White);
                Text(dc, $"${p.Score:N0}", new Point(x0 + 390, 652), 40, Yellow, right: true);
            }
        }
    }

    // ----- the host -------------------------------------------------------------------------------

    /// <summary>What the host says, in a speech bubble beside his grinning face.</summary>
    private void DrawSpeech(DrawingContext dc)
    {
        if (Engine.Message is not { } msg) return;
        var head = new Point(FloorRect.X + 56, FloorRect.Y + 62);
        var lines = Wrap(msg, 32, 600);
        double w = lines.Max(l => TextWidth(l, 32)) + 36, h = lines.Count * 30 + 22;
        var bubble = new Rect(head.X + 50, head.Y - 46, w, h);
        dc.DrawEllipse(Brush(0xFF2A2A40), Pen(0xFFFFE14A, 3), head, 40, 40);
        using (dc.PushClip(new RoundedRect(new Rect(head.X - 38, head.Y - 38, 76, 76), 38)))
        {
            Box(dc, 0xFF1A1A24, head.X - 30, head.Y + 22, 60, 30);
            Box(dc, 0xFFF4F4F8, head.X - 6, head.Y + 22, 12, 20);
            DrawHostHead(dc, head + new Point(0, 0), 0.95);
        }
        dc.DrawGeometry(Brush(0xFFFFFFF4), Pen(0xFF14141C, 3), Poly(new Point(bubble.X + 2, bubble.Y + 18), new Point(head.X + 34, head.Y + 6), new Point(bubble.X + 2, bubble.Y + 38)));
        dc.DrawRectangle(Brush(0xFFFFFFF4), Pen(0xFF14141C, 3), bubble, 14, 14);
        dc.FillRectangle(Brush(0xFFFFFFF4), new Rect(bubble.X - 1, bubble.Y + 20, 6, 16));
        for (int i = 0; i < lines.Count; i++) Text(dc, lines[i], new Point(bubble.X + 18, bubble.Y + 24 + i * 30), 32, Brush(0xFF14141C));
    }
}
