using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AVASmashTV.Engine;
using AVASmashTV.Models;
using Effect = AVASmashTV.Engine.Effect;

namespace AVASmashTV.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), IPen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASmashTV/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
        {
            b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
            BrushCache[argb] = b;
        }
        return b;
    }

    private static IPen Pen(uint argb, double thickness)
    {
        if (!PenCache.TryGetValue((argb, thickness), out var p))
        {
            p = new ImmutablePen(argb, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            PenCache[(argb, thickness)] = p;
        }
        return p;
    }

    /// <summary>A colour with its opacity scaled (0 to 1).</summary>
    private static uint Fade(uint argb, double alpha) => (uint)(Math.Clamp(alpha, 0, 1) * (argb >> 24)) << 24 | (argb & 0xFFFFFF);

    private static readonly IBrush White = Brush(0xFFF2F2F2);
    private static readonly IBrush Yellow = Brush(0xFFFFE14A);
    private static readonly IBrush Red = Brush(0xFFFF3D3D);
    private static readonly IBrush Orange = Brush(0xFFFF8A1E);
    private static readonly IBrush Cyan = Brush(0xFF4FD7FF);
    private static readonly IBrush Green = Brush(0xFF7CFF6A);
    private static readonly IBrush Pink = Brush(0xFFFF5AC8);
    private static readonly IBrush Dim = Brush(0xFF9A9AB8);
    private static readonly IBrush Black = Brush(0xFF000000);

    /// <summary>Each circuit's studio floor: two tiles, the lines between them, and the paint of the show's logo.</summary>
    private static readonly (uint A, uint B, uint Line, uint Paint)[] Floors =
    [
        (0xFF283654, 0xFF22304C, 0xFF34466C, 0xFF3C5486),
        (0xFF26443E, 0xFF203A35, 0xFF325A52, 0xFF3E7266),
        (0xFF3A2852, 0xFF322248, 0xFF4C366C, 0xFF5E4486),
        (0xFF52263E, 0xFF482036, 0xFF7A5A2E, 0xFF9A7232),
    ];

    /// <summary>Arena units to the screen.</summary>
    private static Point P(Vec v) => new(FloorX + v.X * Unit, FloorY + v.Y * Unit);
    private static Point P(double x, double y) => new(FloorX + x * Unit, FloorY + y * Unit);

    private static readonly Rect FloorRect = new(FloorX, FloorY, Field.Width * Unit, Field.Height * Unit);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        int theme = g.State is GameState.Title or GameState.HighScores ? (int)(_clock / 12) % 3 : g.Room.IsDome ? 3 : g.CircuitIndex;
        DrawWalls(dc, theme);
        using (dc.PushClip(FloorRect))
        {
            DrawFloor(dc, theme);
            switch (g.State)
            {
                case GameState.Title: DrawTitle(dc); break;
                case GameState.HighScores: DrawHighScores(dc); break;
                case GameState.EnterInitials: Shade(dc); break;
                default:
                    if (g.Phase == Phase.Map) DrawMap(dc);
                    else if (g.Phase == Phase.Tally) DrawTally(dc);
                    else DrawPlay(dc);
                    break;
            }
            switch (g.State)
            {
                case GameState.Paused: Banner(dc, "PAUSED", "ESC TO QUIT, ANY OTHER KEY TO GO ON"); break;
                case GameState.GameOver: Banner(dc, g.Won ? "WHAT A SHOW!" : "GAME  OVER", g.Won ? "YOU WON IT ALL" : "THANKS FOR PLAYING"); break;
                case GameState.EnterInitials: DrawInitials(dc); break;
            }
        }
        DrawDoors(dc);
        DrawHud(dc);
    }

    // ----- the arena ------------------------------------------------------------------------------

    private void DrawWalls(DrawingContext dc, int theme)
    {
        var outer = new Rect(FloorX - WallPx, FloorY - WallPx, Field.Width * Unit + 2 * WallPx, Field.Height * Unit + 2 * WallPx);
        dc.FillRectangle(Brush(0xFF1C1C26), outer);
        dc.DrawRectangle(null, Pen(0xFF4A4A5E, 3), outer.Deflate(1.5));
        dc.DrawRectangle(null, Pen(0xFF0A0A10, 4), FloorRect.Inflate(2));
        // Panels.
        for (double x = outer.X + 8; x < outer.Right - 40; x += 64)
        {
            dc.FillRectangle(Brush(0xFF262634), new Rect(x, outer.Y + 6, 56, WallPx - 12));
            dc.FillRectangle(Brush(0xFF262634), new Rect(x, FloorRect.Bottom + 6, 56, WallPx - 12));
        }
        for (double y = outer.Y + WallPx + 8; y < FloorRect.Bottom - 40; y += 64)
        {
            dc.FillRectangle(Brush(0xFF262634), new Rect(outer.X + 6, y, WallPx - 12, 56));
            dc.FillRectangle(Brush(0xFF262634), new Rect(FloorRect.Right + 6, y, WallPx - 12, 56));
        }
        // The marquee: bulbs chasing round the walls.
        uint lit = theme switch { 1 => 0xFFFFB04Au, 2 => 0xFFFF6AD8u, 3 => 0xFFFFE14Au, _ => 0xFFFFE89Au };
        int phase = (int)(_clock * 8);
        int i = 0;
        void Bulb(double x, double y)
        {
            bool on = (i++ + phase) % 4 != 0;
            if (on) dc.DrawEllipse(Brush(Fade(lit, 0.25)), null, new Point(x, y), 7, 7);
            dc.DrawEllipse(Brush(on ? lit : 0xFF4A4436u), null, new Point(x, y), 3.5, 3.5);
        }
        double top = FloorRect.Y - 8, bottom = FloorRect.Bottom + 8, left = FloorRect.X - 8, right = FloorRect.Right + 8;
        for (double x = left; x <= right; x += 24) Bulb(x, top);
        for (double y = top + 24; y <= bottom; y += 24) Bulb(right, y);
        for (double x = right - 24; x >= left; x -= 24) Bulb(x, bottom);
        for (double y = bottom - 24; y > top; y -= 24) Bulb(left, y);
        // The cameras in the corners.
        foreach (var (cx, cy, dir) in new[] { (outer.X + 24, outer.Y + 24, 1), (outer.Right - 24, outer.Y + 24, -1), (outer.X + 24, outer.Bottom - 24, 1), (outer.Right - 24, outer.Bottom - 24, -1) })
        {
            dc.DrawEllipse(Brush(0xFF14141C), Pen(0xFF5A5A70, 2), new Point(cx, cy), 18, 18);
            dc.FillRectangle(Brush(0xFF3A3A4A), new Rect(cx - 10, cy - 7, 20, 14));
            dc.FillRectangle(Brush(0xFF14141C), new Rect(cx + (dir > 0 ? 8 : -14), cy - 4, 6, 8));
            dc.DrawEllipse(Brush((int)(_clock * 2) % 2 == 0 ? 0xFFFF3D3Du : 0xFF5A1A1Au), null, new Point(cx - 6 * dir, cy - 4), 2.5, 2.5);
        }
    }

    private void DrawFloor(DrawingContext dc, int theme)
    {
        var (a, b, line, paint) = Floors[theme];
        dc.FillRectangle(Brush(a), FloorRect);
        for (int x = 0; x < Field.Width; x += 2)
            for (int y = 0; y < Field.Height; y += 2)
                if (((x + y) / 2 & 1) == 1) dc.FillRectangle(Brush(b), new Rect(P(x, y), new Size(2 * Unit, 2 * Unit)));
        for (int x = 2; x < Field.Width; x += 2) dc.FillRectangle(Brush(line), new Rect(P(x, 0).X - 1, FloorRect.Y, 2, FloorRect.Height));
        for (int y = 2; y < Field.Height; y += 2) dc.FillRectangle(Brush(line), new Rect(FloorRect.X, P(0, y).Y - 1, FloorRect.Width, 2));
        // The show's logo, painted on the floor.
        var c = P(Field.Centre);
        dc.DrawEllipse(null, Pen(paint, 10), c, 5.2 * Unit, 4.2 * Unit);
        dc.DrawEllipse(null, Pen(paint, 3), c, 6 * Unit, 5 * Unit);
        Text(dc, "SMASH TV", c + new Point(0, -4), 120, Brush(paint), center: true);
        Text(dc, "AVA", c + new Point(0, -78), 44, Brush(paint), center: true);
        // The doorways' warning stripes.
        foreach (var s in Enum.GetValues<Side>())
        {
            var d = P(Field.Door(s));
            bool across = s is Side.North or Side.South;
            double w = Field.DoorWidth * Unit, inward = s is Side.North or Side.West ? 1 : -1;
            for (int k = 0; k < 6; k++)
            {
                double t = -w / 2 + k * w / 6;
                var r = across ? new Rect(d.X + t, d.Y + (inward > 0 ? 0 : -10), w / 12, 10) : new Rect(d.X + (inward > 0 ? 0 : -10), d.Y + t, 10, w / 12);
                dc.FillRectangle(Brush(0x90FFE14A), r);
            }
        }
    }

    /// <summary>The four doors: shut steel, sliding open as a wave comes through, and green with an arrow once they're an exit.</summary>
    private void DrawDoors(DrawingContext dc)
    {
        var g = Engine;
        bool playing = g.State is not (GameState.Title or GameState.HighScores) && g.Phase is not (Phase.Map or Phase.Tally);
        foreach (var s in Enum.GetValues<Side>())
        {
            int i = (int)s;
            bool exit = playing && g.Phase == Phase.Cleared && g.ExitTo[i] != -1;
            double open = !playing ? 0 : exit ? 1 : Math.Clamp(g.DoorOpen[i] * 3, 0, 1);
            var d = P(Field.Door(s));
            bool across = s is Side.North or Side.South;
            double w = Field.DoorWidth * Unit;
            var frame = across
                ? new Rect(d.X - w / 2 - 8, s == Side.North ? FloorRect.Y - WallPx : FloorRect.Bottom, w + 16, WallPx)
                : new Rect(s == Side.West ? FloorRect.X - WallPx : FloorRect.Right, d.Y - w / 2 - 8, WallPx, w + 16);
            dc.FillRectangle(Brush(0xFF0A0A0E), frame);
            dc.DrawRectangle(null, Pen(exit ? 0xFF7CFF6Au : 0xFFFFE14Au, 3), frame.Deflate(2));
            // Darkness beyond, and the two halves of the shutter sliding apart.
            var hole = frame.Deflate(6);
            dc.FillRectangle(Brush(exit ? 0xFF0E2A10u : 0xFF050508u), hole);
            for (int k = 1; k < 4; k++)
            {
                uint glow = exit ? 0xFF1E5A22u : 0xFF16161Eu;
                var lane = across ? new Rect(hole.X, hole.Y + k * hole.Height / 4 - 1, hole.Width, 2) : new Rect(hole.X + k * hole.Width / 4 - 1, hole.Y, 2, hole.Height);
                dc.FillRectangle(Brush(glow), lane);
            }
            if (open < 1)
            {
                double gap = open * (across ? hole.Width : hole.Height) / 2;
                if (across)
                {
                    double half = hole.Width / 2 - gap;
                    DrawShutter(dc, new Rect(hole.X, hole.Y, half, hole.Height), true);
                    DrawShutter(dc, new Rect(hole.Right - half, hole.Y, half, hole.Height), true);
                }
                else
                {
                    double half = hole.Height / 2 - gap;
                    DrawShutter(dc, new Rect(hole.X, hole.Y, hole.Width, half), false);
                    DrawShutter(dc, new Rect(hole.X, hole.Bottom - half, hole.Width, half), false);
                }
            }
            if (exit && (int)(_clock * 4) % 2 == 0)
            {
                // An arrow pointing out of the room.
                var c = hole.Center;
                var outward = Field.Inward(s) * -1;
                var tip = c + new Point(outward.X * 16, outward.Y * 16);
                var back = c - new Point(outward.X * 12, outward.Y * 12);
                var side = new Point(-outward.Y * 14, outward.X * 14);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(tip, true);
                    ctx.LineTo(back + side);
                    ctx.LineTo(back - side);
                    ctx.EndFigure(true);
                }
                dc.DrawGeometry(Green, null, geo);
            }
        }
    }

    private static void DrawShutter(DrawingContext dc, Rect r, bool across)
    {
        if (r.Width <= 0.5 || r.Height <= 0.5) return;
        dc.FillRectangle(Brush(0xFF5A5E6E), r);
        if (across)
            for (double x = r.X + 6; x < r.Right - 2; x += 10) dc.FillRectangle(Brush(0xFF3A3E4C), new Rect(x, r.Y, 3, r.Height));
        else
            for (double y = r.Y + 6; y < r.Bottom - 2; y += 10) dc.FillRectangle(Brush(0xFF3A3E4C), new Rect(r.X, y, r.Width, 3));
        dc.DrawRectangle(null, Pen(0xFF8A8EA0, 1.5), r);
    }

    // ----- the play -------------------------------------------------------------------------------

    private void DrawPlay(DrawingContext dc)
    {
        var g = Engine;
        foreach (var it in g.Floor)
        {
            if (it.Life < 3 && (int)(_clock * 8) % 2 == 0) continue;
            var c = P(it.Pos);
            dc.DrawEllipse(Brush(0x50000000), null, c + new Point(0, 17), 17, 5);
            double bob = Math.Sin(_clock * 4 + it.Pos.X) * 2;
            DrawItem(dc, it.Kind, c + new Point(0, bob), 1.3);
        }
        foreach (var e in g.Enemies.Where(e => e.Kind == EnemyKind.Mine)) DrawEnemy(dc, e);
        if (g.Boss is { } boss) DrawBoss(dc, boss);
        foreach (var e in g.Enemies.Where(e => e.Kind != EnemyKind.Mine)) DrawEnemy(dc, e);
        foreach (var p in g.Players) if (p.Active) DrawPlayer(dc, p);
        foreach (var s in g.Shots) DrawShot(dc, s);
        DrawEffects(dc);
        DrawSpeech(dc);
        if (g.Boss is { Dead: false } b) DrawBossBar(dc, b);

        switch (g.Phase)
        {
            case Phase.Ready:
                Banner(dc, g.Room.Name, $"CIRCUIT {g.CircuitIndex + 1}  ·  {(g.Room.Boss is null ? $"ROOM {g.Route.Count}" : "BOSS")}");
                break;
            case Phase.Cleared when g.PhaseTime < 2:
                Banner(dc, g.Room.Boss is null ? "ROOM CLEAR!" : $"{BossName(g.Room.Boss.Value)} IS SCRAP!", null);
                break;
            case Phase.Cleared when g.ExitTo.Any(x => x != -1) && (int)(_clock * 2) % 2 == 0:
                Text(dc, "PICK UP THE PRIZES, AND TAKE AN EXIT", P(Field.Width / 2, Field.Height - 1.4), 34, Green, center: true);
                break;
            case Phase.Dying:
                Banner(dc, "SHOW'S OVER", "ALL OUT OF LIVES");
                break;
        }
        if (g.State == GameState.Attract && (int)(_clock * 2) % 2 == 0 && g.Phase != Phase.Ready)
            Text(dc, "DEMONSTRATION  ·  PRESS 1 OR 2 TO PLAY", P(Field.Width / 2, Field.Height - 0.7), 32, Red, center: true);
    }

    private static string BossName(BossKind k) => k switch { BossKind.Tankenstein => "TANKENSTEIN", BossKind.Vipervision => "VIPERVISION", _ => "MONTY" };

    private void DrawPlayer(DrawingContext dc, Player p)
    {
        var c = P(p.Pos);
        if (p.Down)
        {
            // Flat on the floor, seeing stars.
            dc.DrawEllipse(Brush(Fade(p.Colour, 0.5)), null, c + new Point(0, 6), 18, 8);
            for (int i = 0; i < 4; i++)
            {
                double a = _clock * 5 + i * Math.PI / 2;
                Star(dc, c + new Point(Math.Cos(a) * 16, -14 + Math.Sin(a) * 5), 5, 0xFFFFE14A);
            }
            return;
        }
        if (p.Invulnerable > 0 && (int)(_clock * 12) % 2 == 0) return;
        dc.DrawEllipse(Brush(0x60000000), null, c + new Point(0, 15), 15, 5);
        if (p.SpeedTime > 0)
            for (int i = 1; i <= 3; i++) dc.DrawLine(Pen(Fade(0xFFFFE14A, 0.6 - i * 0.15), 3), c + new Point(-p.Facing.X * (10 + i * 7) - 6, -p.Facing.Y * (10 + i * 7) + 4 * i - 8), c + new Point(-p.Facing.X * (16 + i * 7) - 6, -p.Facing.Y * (16 + i * 7) + 4 * i - 8));
        bool step = (int)(p.Walk * 8) % 2 == 0;
        var sprite = step ? Sprites.ContestantA : Sprites.ContestantB;
        // The gun, behind when aiming up.
        if (p.Aim.Y < -0.1) DrawGun(dc, c, p);
        DrawSprite(dc, sprite, c - new Point(20, 24), p.Facing.X < -0.1, 2.5, p.Index == 1 ? Sprites.Player2 : null);
        if (p.Aim.Y >= -0.1) DrawGun(dc, c, p);
        if (p.ShieldTime > 0)
        {
            double fade = p.ShieldTime < 2 && (int)(_clock * 10) % 2 == 0 ? 0.3 : 1;
            dc.DrawEllipse(Brush(Fade(0x304FD7FF, fade)), Pen(Fade(0xA04FD7FF, fade), 2), c, GameEngine.ShieldReach * Unit * 0.75, GameEngine.ShieldReach * Unit * 0.75);
            for (int i = 0; i < 3; i++)
            {
                double a = _clock * 7 + i * Math.PI * 2 / 3;
                var o = c + new Point(Math.Cos(a) * GameEngine.ShieldReach * Unit * 0.85, Math.Sin(a) * GameEngine.ShieldReach * Unit * 0.85);
                dc.DrawEllipse(Brush(Fade(0xFFFFFFFF, fade)), Pen(Fade(0xFF4FD7FF, fade), 3), o, 6, 6);
            }
        }
    }

    private static void DrawGun(DrawingContext dc, Point c, Player p)
    {
        var from = c + new Point(p.Aim.X * 4, p.Aim.Y * 4 - 2);
        var to = c + new Point(p.Aim.X * 26, p.Aim.Y * 26 - 2);
        uint colour = p.Weapon switch { Weapon.Spread => 0xFFFF8A1Eu, Weapon.Rockets => 0xFFE83A3Au, Weapon.Grenades => 0xFF7CD83Au, _ => 0xFF3A3A44u };
        dc.DrawLine(Pen(0xFF14141C, 8), from, to);
        dc.DrawLine(Pen(colour, 5), from, to);
        if (p.Firing && (int)(p.Reload * 100) % 2 == 0)
            Star(dc, to + new Point(p.Aim.X * 6, p.Aim.Y * 6), 7, 0xFFFFF0A0);
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        var c = P(s.Pos);
        var v = s.Vel.Normal;
        switch (s.Kind)
        {
            case ShotKind.Bullet:
                dc.DrawLine(Pen(0xFFFFE14A, 5), c - new Point(v.X * 9, v.Y * 9), c);
                dc.DrawLine(Pen(0xFFFFFFFF, 2.5), c - new Point(v.X * 5, v.Y * 5), c);
                break;
            case ShotKind.Rocket:
                dc.DrawLine(Pen(Fade(0xFFFF8A1E, 0.7), 7), c - new Point(v.X * 22, v.Y * 22), c - new Point(v.X * 8, v.Y * 8));
                dc.DrawLine(Pen(0xFFE8E8F0, 8), c - new Point(v.X * 10, v.Y * 10), c + new Point(v.X * 4, v.Y * 4));
                dc.DrawEllipse(Red, null, c + new Point(v.X * 5, v.Y * 5), 4, 4);
                break;
            case ShotKind.Grenade:
                double hop = Math.Sin(s.Age / s.Life * Math.PI) * 14;
                dc.DrawEllipse(Brush(0x50000000), null, c, 6, 3);
                dc.DrawEllipse(Brush(0xFF3A7A1E), Pen(0xFF14141C, 1.5), c + new Point(0, -hop), 6, 6);
                dc.DrawEllipse(Brush(0xFF7CD83A), null, c + new Point(-2, -hop - 2), 2, 2);
                break;
            case ShotKind.Shell:
                dc.DrawEllipse(Brush(0xFF14141C), Pen(0xFFFF3D3D, 2.5), c, 7, 7);
                dc.DrawEllipse(Brush(0xFFFF8A8A), null, c + new Point(-2, -2), 2, 2);
                break;
            case ShotKind.Fireball:
                dc.DrawEllipse(Brush(0x60FF6A1A), null, c, 13, 13);
                dc.DrawEllipse(Brush((int)(_clock * 16) % 2 == 0 ? 0xFFFF6A1Au : 0xFFFF9A2Au), null, c, 9, 9);
                dc.DrawEllipse(Brush(0xFFFFF0A0), null, c, 4, 4);
                break;
            default:
                double spin = s.Age * 20;
                var a = new Point(Math.Cos(spin) * 7, Math.Sin(spin) * 7);
                dc.DrawLine(Pen(0xFF14141C, 6), c - a, c + a);
                dc.DrawLine(Pen(0xFFC8CCD8, 3), c - a, c + a);
                break;
        }
    }

    private void DrawEffects(DrawingContext dc)
    {
        foreach (var fx in Engine.Effects)
        {
            var c = P(fx.Pos);
            double t = fx.Age / fx.Life;
            switch (fx.Kind)
            {
                case EffectKind.Splat:
                    DrawSplat(dc, c, fx, t);
                    break;
                case EffectKind.Blast:
                    double r = fx.Size * Unit * (0.4 + t * 0.8);
                    dc.DrawEllipse(Brush(Fade(0xFFFF6A1A, 1 - t)), null, c, r, r);
                    dc.DrawEllipse(Brush(Fade(0xFFFFE14A, 1 - t)), null, c, r * 0.7, r * 0.7);
                    dc.DrawEllipse(Brush(Fade(0xFFFFFFFF, 1 - t * 1.5)), null, c, r * 0.4, r * 0.4);
                    dc.DrawEllipse(null, Pen(Fade(0xFFFFFFFF, 1 - t), 3), c, r * 1.25, r * 1.25);
                    break;
                case EffectKind.Sparkle:
                    for (int i = 0; i < 4; i++)
                    {
                        double a = i * Math.PI / 2 + t * 2;
                        Star(dc, c + new Point(Math.Cos(a) * t * 16, Math.Sin(a) * t * 16), 5 * (1 - t) + 1, fx.Colour == 0xFFFFE14A ? 0xFFFFF4B0 : fx.Colour);
                    }
                    break;
                case EffectKind.Debris:
                    double spin = fx.Age * 9 + fx.Seed % 7;
                    double size = fx.Size * Unit;
                    using (dc.PushTransform(Matrix.CreateRotation(spin) * Matrix.CreateTranslation(c.X, c.Y)))
                    {
                        dc.FillRectangle(Brush(Fade(fx.Colour, 1.3 - t)), new Rect(-size / 2, -size / 3, size, size * 0.66));
                        dc.DrawRectangle(null, Pen(Fade(0xFF14141C, 1.3 - t), 1.5), new Rect(-size / 2, -size / 3, size, size * 0.66));
                    }
                    break;
                case EffectKind.Confetti:
                    uint[] colours = [0xFFFF3D3D, 0xFFFFE14A, 0xFF4FD7FF, 0xFF7CFF6A, 0xFFFF5AC8];
                    for (int i = 0; i < 6; i++)
                    {
                        double x = c.X + Math.Sin(fx.Age * 3 + i * 1.7 + fx.Seed) * 30 + i * 14 - 35;
                        double y = c.Y + fx.Age * (40 + i * 9);
                        dc.FillRectangle(Brush(Fade(colours[(i + fx.Seed % 5) % 5], 1.2 - t)), new Rect(x, y, 6, 3 + 3 * Math.Abs(Math.Sin(fx.Age * 6 + i))));
                    }
                    break;
                case EffectKind.Smoke:
                    dc.DrawEllipse(Brush(Fade(0x80606070, 1 - t)), null, c + new Point(0, -t * 30), 8 + t * 14, 8 + t * 14);
                    break;
                case EffectKind.Score:
                    Text(dc, fx.Text, c + new Point(2, -10 - fx.Age * 24 + 2), 30, Black, center: true);
                    Text(dc, fx.Text, c + new Point(0, -12 - fx.Age * 24), 30, Brush(fx.Colour), center: true);
                    break;
            }
        }
    }

    /// <summary>A cartoon splat: blobs of goo thrown out, and stars.</summary>
    private static void DrawSplat(DrawingContext dc, Point c, Effect fx, double t)
    {
        var rng = new Random(fx.Seed);
        double alpha = t < 0.6 ? 1 : (1 - t) / 0.4;
        double s = fx.Size;
        dc.DrawEllipse(Brush(Fade(fx.Colour, alpha * 0.85)), null, c, 13 * s * Math.Min(1, t * 6), 9 * s * Math.Min(1, t * 6));
        for (int i = 0; i < 7; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2, d = (8 + rng.NextDouble() * 18) * s * Math.Min(1, t * 4);
            double r = (2.5 + rng.NextDouble() * 4) * s;
            dc.DrawEllipse(Brush(Fade(fx.Colour, alpha)), null, c + new Point(Math.Cos(a) * d, Math.Sin(a) * d * 0.7), r, r);
        }
        if (t < 0.5)
            for (int i = 0; i < 3; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                Star(dc, c + new Point(Math.Cos(a) * 20 * s * t * 2, Math.Sin(a) * 20 * s * t * 2 - 6), 6 * (1 - t * 2), 0xFFFFFFFF);
            }
    }

    private static void Star(DrawingContext dc, Point c, double r, uint colour)
    {
        if (r <= 0.5) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4 - Math.PI / 2, d = i % 2 == 0 ? r : r * 0.35;
                var p = c + new Point(Math.Cos(a) * d, Math.Sin(a) * d);
                if (i == 0) ctx.BeginFigure(p, true); else ctx.LineTo(p);
            }
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(Brush(colour), null, geo);
    }

    // ----- the scores along the top ---------------------------------------------------------------

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF0C0C14), new Rect(0, 0, ScreenWidth, HudHeight));
        dc.FillRectangle(Brush(0xFF2A2A40), new Rect(0, HudHeight - 3, ScreenWidth, 3));
        bool inShow = g.State is not (GameState.Title or GameState.HighScores);
        for (int i = 0; i < 2; i++)
        {
            var p = g.Players[i];
            double x = i == 0 ? 16 : ScreenWidth - 16;
            bool right = i == 1;
            var colour = Brush(p.Colour);
            Text(dc, i == 0 ? "1UP" : "2UP", new Point(x, 14), 26, colour, right: right);
            if (inShow && p.Joined)
            {
                Text(dc, $"${p.Score:N0}", new Point(right ? x - 56 : x + 56, 16), 34, Yellow, right: right);
                // Lives.
                for (int l = 0; l < Math.Min(p.Lives, 6); l++)
                {
                    double lx = right ? x - 10 - l * 22 : x + 10 + l * 22;
                    dc.DrawEllipse(colour, Pen(0xFF14141C, 1.5), new Point(lx, 44), 8, 8);
                    dc.FillRectangle(Brush(0xFF4FD7FF), new Rect(lx - 5, 42, 10, 3));
                }
                if (p.Out) Text(dc, "OUT", new Point(right ? x - 10 : x + 10, 46), 26, Red, right: right);
                // What they're carrying, and for how long.
                double ix = right ? x - 170 : x + 170;
                var power = p.Weapon switch { Weapon.Spread => ItemKind.Spread, Weapon.Rockets => ItemKind.Rockets, Weapon.Grenades => ItemKind.Grenades, _ => (ItemKind?)null };
                foreach (var (kind, time, max) in new[] { (power, p.WeaponTime, GameEngine.WeaponSeconds), (p.ShieldTime > 0 ? ItemKind.Shield : (ItemKind?)null, p.ShieldTime, GameEngine.ShieldSeconds), (p.SpeedTime > 0 ? ItemKind.Speed : (ItemKind?)null, p.SpeedTime, GameEngine.SpeedSeconds) })
                {
                    if (kind is not { } k) continue;
                    DrawItem(dc, k, new Point(ix, 44), 0.6);
                    dc.FillRectangle(Brush(0xFF2A2A40), new Rect(ix + 14, 40, 40, 6));
                    dc.FillRectangle(Cyan, new Rect(ix + 14, 40, 40 * Math.Clamp(time / max, 0, 1), 6));
                    ix += right ? -64 : 64;
                }
            }
            else if (inShow && (int)(_clock * 2) % 2 == 0 && g.State == GameState.Playing && !p.Joined)
                Text(dc, "PRESS 2 TO JOIN", new Point(right ? x - 56 : x + 56, 22), 30, colour, right: right);
            else if (!inShow)
                Text(dc, i == 0 ? $"HI  ${g.HighScores.TopScore:N0}" : "INSERT COIN... OR JUST PRESS START", new Point(right ? x - 56 : x + 56, 22), 28, Dim, right: right);
        }
        if (!inShow) return;
        // The middle: where we are, the keys, and how many still to come.
        double cx = ScreenWidth / 2;
        Text(dc, g.Phase == Phase.Map || g.Phase == Phase.Tally ? $"CIRCUIT {g.CircuitIndex + 1}" : g.Room.Name, new Point(cx, 15), 26, White, center: true);
        for (int k = 0; k < Show.KeysNeeded; k++)
        {
            var at = new Point(cx - (Show.KeysNeeded - 1) * 11 + k * 22, 45);
            if (k < g.Keys) DrawKey(dc, at, 0.45);
            else dc.DrawEllipse(null, Pen(0xFF4A4A60, 2), at, 5, 5);
        }
        if (g.Keys > Show.KeysNeeded) Text(dc, $"+{g.Keys - Show.KeysNeeded}", new Point(cx + Show.KeysNeeded * 11 + 6, 45), 22, Yellow);
    }

    // ----- helpers --------------------------------------------------------------------------------

    private static void Banner(DrawingContext dc, string title, string? sub)
    {
        var c = P(Field.Centre);
        double w = Math.Max(560, title.Length * 40 + 80);
        var box = new Rect(c.X - w / 2, c.Y - 70, w, sub is null ? 104 : 136);
        dc.FillRectangle(Brush(0xD8000000), box);
        dc.DrawRectangle(null, Pen(0xFFFFE14A, 3), box.Deflate(4));
        Text(dc, title, new Point(c.X + 3, c.Y - 14), 76, Brush(0xFF8A1E1E), center: true);
        Text(dc, title, new Point(c.X, c.Y - 18), 76, Yellow, center: true);
        if (sub is not null) Text(dc, sub, new Point(c.X, c.Y + 38), 32, White, center: true);
    }

    /// <summary>A sprite with its top-left at a screen point, two screen pixels to its pixel unless told otherwise.</summary>
    private static void DrawSprite(DrawingContext dc, Sprites.Sprite s, Point at, bool flip, double px = 2, IReadOnlyDictionary<char, uint>? recolour = null, bool white = false)
    {
        foreach (var (row, col, len, ch) in s.Runs)
        {
            uint colour = white ? 0xFFFFFFFF : recolour is not null && recolour.TryGetValue(ch, out var rc) ? rc : Sprites.Colours.GetValueOrDefault(ch, 0xFFFF00FF);
            double x = flip ? at.X + (s.Width - col - len) * px : at.X + col * px;
            dc.FillRectangle(Brush(colour), new Rect(x, at.Y + row * px, len * px + 0.2, px + 0.2));
        }
    }

    private static void Text(DrawingContext dc, string text, Point at, double size, IBrush brush, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, brush);
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static double TextWidth(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, White).Width;
}
