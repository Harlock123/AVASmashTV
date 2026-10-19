using Avalonia;
using Avalonia.Media;
using AVASmashTV.Engine;
using AVASmashTV.Models;

namespace AVASmashTV.Views;

/// <summary>The cast drawn as shapes: the prizes and power-ups, the robots and the tanks, the bosses and the host.</summary>
public sealed partial class GameCanvas
{
    private static DrawingContext.PushedState At(DrawingContext dc, Point c, double scale = 1, double turn = 0) =>
        dc.PushTransform(Matrix.CreateRotation(turn) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(c.X, c.Y));

    private static Geometry Poly(params Point[] pts)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.BeginFigure(pts[0], true);
        foreach (var p in pts.Skip(1)) ctx.LineTo(p);
        ctx.EndFigure(true);
        return geo;
    }

    private static void Box(DrawingContext dc, uint fill, double x, double y, double w, double h, double radius = 0, uint? edge = null, double edgeWidth = 1.5)
    {
        var r = new Rect(x, y, w, h);
        if (radius > 0) dc.DrawRectangle(Brush(fill), edge is { } e ? Pen(e, edgeWidth) : null, r, radius, radius);
        else dc.DrawRectangle(Brush(fill), edge is { } e2 ? Pen(e2, edgeWidth) : null, r);
    }

    private static void Disc(DrawingContext dc, uint fill, double x, double y, double rx, double? ry = null, uint? edge = null, double edgeWidth = 1.5) =>
        dc.DrawEllipse(Brush(fill), edge is { } e ? Pen(e, edgeWidth) : null, new Point(x, y), rx, ry ?? rx);

    // ----- prizes and power-ups -------------------------------------------------------------------

    /// <summary>A prize or a power-up, about a unit across at scale 1, centred on c.</summary>
    private static void DrawItem(DrawingContext dc, ItemKind kind, Point c, double scale)
    {
        if (kind == ItemKind.Key) { DrawKey(dc, c, scale); return; }
        using var _ = At(dc, c, scale);
        const uint ink = 0xFF14141C;
        switch (kind)
        {
            case ItemKind.Cash:
                Box(dc, 0xFF2A7A2A, -13, -11, 26, 13, 2, ink);
                Box(dc, 0xFF3AA83A, -14, -6, 28, 14, 2, ink);
                Box(dc, 0xFF7CD86A, -10, -3, 20, 8, 1);
                Text(dc, "$", new Point(0, 1), 20, Brush(0xFF1E5A1E), center: true);
                break;
            case ItemKind.Toaster:
                Box(dc, 0xFFD89A4A, -9, -14, 6, 8, 1, ink);
                Box(dc, 0xFFD89A4A, 3, -12, 6, 8, 1, ink);
                Box(dc, 0xFFC8CCD8, -14, -7, 28, 17, 5, ink);
                Box(dc, 0xFFE8ECF4, -11, -5, 22, 3, 1);
                Box(dc, ink, -9, -7, 6, 2);
                Box(dc, ink, 3, -7, 6, 2);
                Box(dc, 0xFF5A5E6E, 13, -2, 4, 7, 1, ink);
                Box(dc, 0xFF5A5E6E, -12, 8, 24, 3);
                break;
            case ItemKind.Vcr:
                Box(dc, 0xFF2A2A34, -16, -7, 32, 14, 2, 0xFF5A5A6E);
                Box(dc, ink, -12, -3, 14, 3);
                Box(dc, 0xFF0A1A0A, 4, -4, 10, 6, 1);
                Text(dc, "12:00", new Point(9, -1), 9, Green, center: true);
                Disc(dc, 0xFF8A8EA0, -10, 4, 1.5);
                Disc(dc, 0xFF8A8EA0, -5, 4, 1.5);
                break;
            case ItemKind.Tv:
                dc.DrawLine(Pen(0xFF8A8EA0, 1.5), new Point(-1, -11), new Point(-8, -19));
                dc.DrawLine(Pen(0xFF8A8EA0, 1.5), new Point(1, -11), new Point(8, -19));
                Box(dc, 0xFF9A6A30, -15, -11, 30, 23, 4, ink);
                Box(dc, 0xFF3AB8E8, -12, -8, 20, 16, 4, ink);
                Box(dc, 0xFFC8F0FF, -10, -6, 5, 3, 1);
                Disc(dc, 0xFFE8C88A, 12, -4, 2);
                Disc(dc, 0xFFE8C88A, 12, 3, 2);
                break;
            case ItemKind.Gold:
                foreach (var (x, y) in new[] { (-8.0, 4.0), (8.0, 4.0), (0.0, -6.0) })
                {
                    dc.DrawGeometry(Brush(0xFFFFC830), Pen(0xFF9A7010, 1.5), Poly(new Point(x - 9, y + 6), new Point(x + 9, y + 6), new Point(x + 6, y - 4), new Point(x - 6, y - 4)));
                    Box(dc, 0xFFFFF0A0, x - 5, y - 3, 10, 2);
                }
                break;
            case ItemKind.Car:
                dc.DrawGeometry(Brush(0xFFE83A3A), Pen(ink, 1.5), Poly(new Point(-16, 6), new Point(-16, -1), new Point(-9, -3), new Point(-5, -10), new Point(6, -10), new Point(10, -3), new Point(16, -1), new Point(16, 6)));
                dc.DrawGeometry(Brush(0xFF9AE0FF), null, Poly(new Point(-7, -3), new Point(-4, -8), new Point(0, -8), new Point(0, -3)));
                dc.DrawGeometry(Brush(0xFF9AE0FF), null, Poly(new Point(2, -3), new Point(2, -8), new Point(5, -8), new Point(8, -3)));
                Box(dc, 0xFFE8E8F0, -16, 3, 32, 2);
                Disc(dc, ink, -9, 6, 4.5);
                Disc(dc, ink, 9, 6, 4.5);
                Disc(dc, 0xFFA8ACB8, -9, 6, 2);
                Disc(dc, 0xFFA8ACB8, 9, 6, 2);
                break;
            default:
                DrawBadge(dc, kind);
                break;
        }
    }

    private static void DrawBadge(DrawingContext dc, ItemKind kind)
    {
        const uint ink = 0xFF14141C;
        uint bg = kind switch
        {
            ItemKind.Spread => 0xFFFF8A1E,
            ItemKind.Rockets => 0xFFD83030,
            ItemKind.Grenades => 0xFF5AB83A,
            ItemKind.Shield => 0xFF1A8AD8,
            ItemKind.Speed => 0xFFE0B820,
            _ => 0xFFD83AB0,
        };
        Box(dc, 0xFFFFFFFF, -15, -15, 30, 30, 7);
        Box(dc, bg, -13, -13, 26, 26, 6);
        switch (kind)
        {
            case ItemKind.Spread:
                foreach (var (x, y) in new[] { (-9.0, -8.0), (0.0, -10.0), (9.0, -8.0) })
                {
                    dc.DrawLine(Pen(0xFFFFF4B0, 2.5), new Point(0, 9), new Point(x, y));
                    Disc(dc, 0xFFFFFFFF, x, y, 2.5);
                }
                break;
            case ItemKind.Rockets:
                using (dc.PushTransform(Matrix.CreateRotation(-Math.PI / 4)))
                {
                    Box(dc, 0xFFF0F0F4, -4, -6, 8, 14, 1, ink);
                    dc.DrawGeometry(Brush(0xFFFFE14A), Pen(ink, 1), Poly(new Point(-4, -6), new Point(0, -13), new Point(4, -6)));
                    dc.DrawGeometry(Brush(ink), null, Poly(new Point(-4, 4), new Point(-8, 10), new Point(-4, 8)));
                    dc.DrawGeometry(Brush(ink), null, Poly(new Point(4, 4), new Point(8, 10), new Point(4, 8)));
                    Disc(dc, 0xFFFFE14A, 0, 11, 2.5, 3);
                }
                break;
            case ItemKind.Grenades:
                Disc(dc, 0xFF2A4A1A, 0, 3, 8, 9, ink);
                dc.DrawLine(Pen(0xFF1A2A10, 1.5), new Point(-7, 1), new Point(7, 1));
                dc.DrawLine(Pen(0xFF1A2A10, 1.5), new Point(-7, 6), new Point(7, 6));
                Box(dc, 0xFFA8ACB8, -3, -9, 6, 4, 1, ink);
                dc.DrawLine(Pen(0xFFA8ACB8, 2), new Point(3, -8), new Point(8, -3));
                dc.DrawEllipse(null, Pen(0xFFFFE14A, 1.5), new Point(-6, -9), 3, 3);
                break;
            case ItemKind.Shield:
                dc.DrawEllipse(null, Pen(0xFFFFFFFF, 2.5), new Point(0, 0), 8, 8);
                for (int i = 0; i < 3; i++)
                {
                    double a = i * Math.PI * 2 / 3 - Math.PI / 2;
                    Disc(dc, 0xFFFFFFFF, Math.Cos(a) * 8, Math.Sin(a) * 8, 3.2, edge: 0xFF4FD7FF);
                }
                break;
            case ItemKind.Speed:
                dc.DrawGeometry(Brush(0xFFFFFFFF), Pen(ink, 1.5), Poly(new Point(-9, 7), new Point(-9, -2), new Point(-3, -2), new Point(-1, 2), new Point(9, 4), new Point(10, 8), new Point(-9, 8)));
                dc.DrawGeometry(Brush(0xFFFFF4B0), Pen(ink, 1), Poly(new Point(-9, -2), new Point(-12, -10), new Point(-6, -7), new Point(-4, -11), new Point(-3, -2)));
                break;
            default:
                Text(dc, "1UP", new Point(0, 0), 20, White, center: true);
                break;
        }
    }

    /// <summary>A key to the Pleasure Dome.</summary>
    private static void DrawKey(DrawingContext dc, Point c, double scale)
    {
        using var _ = At(dc, c, scale);
        dc.DrawEllipse(Brush(0x60FFE14A), null, new Point(0, 0), 18, 18);
        dc.DrawEllipse(null, Pen(0xFF9A7010, 7), new Point(-7, 0), 7, 7);
        dc.DrawEllipse(null, Pen(0xFFFFD040, 4), new Point(-7, 0), 7, 7);
        Box(dc, 0xFFFFD040, -1, -2.5, 16, 5, 1, 0xFF9A7010);
        Box(dc, 0xFFFFD040, 9, 2, 3, 6, 0, 0xFF9A7010);
        Box(dc, 0xFFFFD040, 4, 2, 3, 4, 0, 0xFF9A7010);
    }

    // ----- the enemies ----------------------------------------------------------------------------

    private void DrawEnemy(DrawingContext dc, Enemy e)
    {
        var c = P(e.Pos);
        bool flash = e.Flash > 0;
        const uint ink = 0xFF14141C;
        if (e.Kind is not (EnemyKind.Mine or EnemyKind.Blade)) dc.DrawEllipse(Brush(0x50000000), null, c + new Point(0, e.Radius * Unit * 0.9), e.Radius * Unit * 0.9, 5);
        switch (e.Kind)
        {
            case EnemyKind.Grunt:
                DrawSprite(dc, (int)(e.Age * 7 + e.Quirk) % 2 == 0 ? Sprites.GruntA : Sprites.GruntB, c - new Point(20, 24), e.Vel.X < -0.1, 2.5, null, flash);
                break;
            case EnemyKind.Orb:
            {
                double r = e.Radius * Unit;
                Disc(dc, flash ? 0xFFFFFFFF : 0xFF4A5060, c.X, c.Y, r, edge: ink, edgeWidth: 2);
                Disc(dc, flash ? 0xFFFFFFFF : 0xFF9AA0B0, c.X - 1, c.Y - 1, r * 0.8);
                Disc(dc, 0xFFE8ECF4, c.X - r * 0.3, c.Y - r * 0.35, r * 0.3);
                dc.DrawLine(Pen(0xFF2A2E3A, 2), c + new Point(-r, 0), c + new Point(r, 0));
                var look = e.Vel.Normal;
                Disc(dc, 0xFFFF3D3D, c.X + look.X * r * 0.45, c.Y + look.Y * r * 0.3 + 2, 4, 3, 0xFF5A0A0A);
                break;
            }
            case EnemyKind.Snake:
            {
                var segs = e.SegmentPositions().ToList();
                for (int i = segs.Count - 1; i >= 0; i--)
                {
                    var s = P(segs[i]);
                    double r = 11 - i * 0.25;
                    Disc(dc, flash ? 0xFFFFFFFF : i % 2 == 0 ? 0xFFFFB02Au : 0xFF2A2A1Au, s.X, s.Y, r, edge: ink, edgeWidth: 2);
                    if (i % 2 == 0) Disc(dc, 0xFFFFE08A, s.X - 2, s.Y - 3, r * 0.35);
                }
                var h = e.Vel.Normal;
                var side = new Point(-h.Y, h.X);
                if ((int)(e.Age * 6) % 3 == 0)
                {
                    var tip = c + new Point(h.X * 24, h.Y * 24);
                    dc.DrawLine(Pen(0xFFFF3D3D, 2), c + new Point(h.X * 12, h.Y * 12), tip);
                    dc.DrawLine(Pen(0xFFFF3D3D, 2), tip, tip + new Point(h.X * 4 + side.X * 4, h.Y * 4 + side.Y * 4));
                    dc.DrawLine(Pen(0xFFFF3D3D, 2), tip, tip + new Point(h.X * 4 - side.X * 4, h.Y * 4 - side.Y * 4));
                }
                Disc(dc, flash ? 0xFFFFFFFF : 0xFFFF8A1E, c.X, c.Y, 14, 13, ink, 2);
                foreach (int k in new[] { -1, 1 })
                {
                    var eye = c + new Point(h.X * 5 + side.X * 6 * k, h.Y * 5 + side.Y * 6 * k);
                    Disc(dc, 0xFFFFFFFF, eye.X, eye.Y, 4);
                    Disc(dc, 0xFFD81A1A, eye.X + h.X * 1.5, eye.Y + h.Y * 1.5, 2);
                }
                break;
            }
            case EnemyKind.Blade:
            {
                dc.DrawEllipse(Brush(0x40000000), null, c + new Point(0, 14), 14, 4);
                using (At(dc, c, 1, e.Age * 16))
                {
                    for (int i = 0; i < 3; i++)
                        using (dc.PushTransform(Matrix.CreateRotation(i * Math.PI * 2 / 3)))
                            dc.DrawGeometry(Brush(flash ? 0xFFFFFFFF : 0xFFD8DCE8), Pen(0xFF5A5E6E, 1.5), Poly(new Point(0, -4), new Point(17, -10), new Point(12, 2), new Point(0, 4)));
                    Disc(dc, 0xFFE83A3A, 0, 0, 6, edge: ink, edgeWidth: 2);
                    Disc(dc, 0xFFFFE14A, 0, 0, 2);
                }
                break;
            }
            case EnemyKind.Tank:
            {
                double heading = e.Vel.Length > 0.01 ? e.Vel.Angle : 0;
                double roll = e.Age * 30 % 8;
                using (At(dc, c, 1, heading))
                {
                    Box(dc, 0xFF2A2A24, -26, -22, 52, 10, 3, ink);
                    Box(dc, 0xFF2A2A24, -26, 12, 52, 10, 3, ink);
                    for (double x = -24 + roll; x < 24; x += 8)
                    {
                        Box(dc, 0xFF4A4A40, x, -21, 3, 8);
                        Box(dc, 0xFF4A4A40, x, 13, 3, 8);
                    }
                    Box(dc, flash ? 0xFFFFFFFF : 0xFF5A6A3A, -22, -13, 44, 26, 4, ink, 2);
                    Box(dc, 0xFF4A5A2E, -18, -9, 8, 18, 2);
                }
                using (At(dc, c, 1, e.Turret))
                {
                    Box(dc, 0xFF3A4424, 4, -4, 26, 8, 2, ink);
                    Disc(dc, flash ? 0xFFFFFFFF : 0xFF7A8A4A, 0, 0, 12, edge: ink, edgeWidth: 2);
                    Disc(dc, 0xFFFFE14A, -3, -3, 3);
                }
                break;
            }
            case EnemyKind.Mine:
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    dc.DrawLine(Pen(0xFF5A5E6E, 3), c, c + new Point(Math.Cos(a) * 13, Math.Sin(a) * 13));
                }
                Disc(dc, 0xFF2A2A30, c.X, c.Y, 10, edge: 0xFF6A6E7E, edgeWidth: 2);
                bool lit = (int)(e.Age * 3 + e.Quirk) % 2 == 0;
                if (lit) Disc(dc, 0x60FF3D3D, c.X, c.Y, 9);
                Disc(dc, lit ? 0xFFFF3D3Du : 0xFF5A1A1Au, c.X, c.Y, 4);
                break;
            }
            case EnemyKind.Shrapnel:
            {
                bool hot = e.Fuse < 1.5 && (int)(e.Fuse * 10) % 2 == 0;
                using (At(dc, c, 1, e.Age * 3))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3;
                        dc.DrawGeometry(Brush(0xFF6A6E7E), null, Poly(new Point(Math.Cos(a - 0.3) * 11, Math.Sin(a - 0.3) * 11), new Point(Math.Cos(a) * 18, Math.Sin(a) * 18), new Point(Math.Cos(a + 0.3) * 11, Math.Sin(a + 0.3) * 11)));
                    }
                    Disc(dc, flash || hot ? 0xFFFF3D3D : 0xFF1A1A22, 0, 0, 13, edge: 0xFF5A5E6E, edgeWidth: 2);
                    Disc(dc, 0xFF5A5E70, -4, -4, 3.5);
                }
                dc.DrawLine(Pen(0xFF9A6A30, 2.5), c + new Point(0, -12), c + new Point(5, -19));
                Star(dc, c + new Point(6, -21), 5 + Math.Sin(e.Age * 30) * 2, 0xFFFFE14A);
                break;
            }
        }
    }

    // ----- the bosses -----------------------------------------------------------------------------

    private void DrawBoss(DrawingContext dc, Boss b)
    {
        if (b.Dead && (int)(b.DeadTime * 10) % 2 == 0 && b.DeadTime > 1.5) return;
        switch (b.Kind)
        {
            case BossKind.Tankenstein: DrawTankenstein(dc, b); break;
            case BossKind.Vipervision: DrawViper(dc, b); break;
            default: DrawMonty(dc, b); break;
        }
    }

    /// <summary>A part's centre and half-size, in pixels from the boss's middle.</summary>
    private static (double X, double Y, double W, double H) Px(BossPart p) => (p.Offset.X * Unit, p.Offset.Y * Unit, p.Size.X * Unit, p.Size.Y * Unit);

    private static void FlashOver(DrawingContext dc, BossPart p)
    {
        if (p.Flash <= 0) return;
        var (x, y, w, h) = Px(p);
        dc.DrawEllipse(Brush(0xA0FFFFFF), null, new Point(x, y), w, h);
    }

    /// <summary>Sparks and smoke where a part used to be.</summary>
    private void Stump(DrawingContext dc, double x, double y, double r)
    {
        Disc(dc, 0xFF2A2A30, x, y, r, r * 0.8, 0xFF14141C, 2);
        Disc(dc, 0xFF5A1A0A, x, y, r * 0.5);
        for (int i = 0; i < 3; i++)
        {
            double a = _clock * 9 + i * 2.1;
            Star(dc, new Point(x + Math.Cos(a) * r * 0.8, y + Math.Sin(a * 1.3) * r * 0.6), 4 + (int)(_clock * 20 + i) % 3 * 2, i == 0 ? 0xFFFFE14A : 0xFFFFFFFF);
        }
        double puff = (_clock * 0.8 + x * 0.01) % 1;
        Disc(dc, Fade(0x9060606E, 1 - puff), x + puff * 6, y - puff * 30, 6 + puff * 10);
    }

    private void DrawTankenstein(DrawingContext dc, Boss b)
    {
        const uint ink = 0xFF14141C;
        double shake = b.Mode == 1 ? Math.Sin(_clock * 80) * 2 : 0;
        var c = P(b.Pos) + new Point(shake, 0);
        dc.DrawEllipse(Brush(0x60000000), null, c + new Point(0, 92), 110, 18);
        using var _ = At(dc, c);
        var treads = b.Part("TREADS")!;
        // The tank base.
        if (!treads.Gone)
        {
            double roll = (b.Pos.X * 12) % 12;
            Box(dc, 0xFF22222A, -100, 38, 200, 54, 14, ink, 3);
            for (double x = -96 + (roll + 12) % 12; x < 94; x += 12) Box(dc, 0xFF3A3A46, x, 40, 5, 50);
            for (int i = 0; i < 6; i++) Disc(dc, 0xFF6A6A78, -75 + i * 30, 65, 11, edge: ink, edgeWidth: 2);
            for (int i = 0; i < 6; i++) Disc(dc, 0xFF3A3A44, -75 + i * 30, 65, 4);
            Box(dc, 0xFF5A6A3A, -88, 18, 176, 26, 6, ink, 2.5);
            for (int i = 0; i < 9; i++) Box(dc, i % 2 == 0 ? 0xFFFFE14Au : ink, -60 + i * 13.3, 34, 13.3, 7);
            for (int i = 0; i < 6; i++) Disc(dc, 0xFF8A9A6A, -78 + i * 31, 24, 2.5);
            FlashOver(dc, treads);
        }
        var torso = b.Part("TORSO")!;
        var head = b.Part("HEAD")!;
        // The arms: from the shoulders out to the cannons.
        foreach (var (name, sx) in new[] { ("LEFT CANNON", -1), ("RIGHT CANNON", 1) })
        {
            var gun = b.Part(name)!;
            if (torso.Gone) continue;
            if (gun.Gone) continue;
            var (gx, gy, _, _) = Px(gun);
            // A great bulging arm out to the elbow, and a three-barrelled gun for a forearm.
            var shoulder = new Point(sx * 42, -32);
            var elbow = new Point(gx, gy - 22);
            dc.DrawLine(Pen(ink, 30), shoulder, elbow);
            dc.DrawLine(Pen(0xFFE0906A, 25), shoulder, elbow);
            Disc(dc, 0xFFC87050, (shoulder.X + elbow.X) / 2, (shoulder.Y + elbow.Y) / 2 - 3, 13, 9);
            Disc(dc, 0xFF8A8FA0, shoulder.X + sx * 4, shoulder.Y - 4, 17, 13, ink, 2.5);
            for (int k = -1; k <= 1; k++) Box(dc, 0xFF3A3E4C, gx + k * 9 - 4, gy + 6, 8, 26, 2, ink);
            for (int k = -1; k <= 1; k++) Disc(dc, (int)(_clock * 12 + k) % 3 == 0 ? 0xFFFF8A1Eu : ink, gx + k * 9, gy + 31, 3.5);
            Box(dc, 0xFF6A6E7E, gx - 20, gy - 26, 40, 36, 8, ink, 2.5);
            Box(dc, 0xFF9AA0B0, gx - 15, gy - 22, 8, 28, 3);
            Box(dc, 0xFFFFE14A, gx - 20, gy - 2, 40, 6);
            Disc(dc, 0xFF8A8FA0, elbow.X, elbow.Y, 10, edge: ink, edgeWidth: 2);
            FlashOver(dc, gun);
        }
        // The body.
        if (!torso.Gone)
        {
            var (tx, ty, tw, th) = Px(torso);
            Disc(dc, 0xFFE0906A, tx, ty, tw, th, ink, 3);
            Disc(dc, 0xFFC87050, tx - 22, ty + 4, 18, 14);
            Disc(dc, 0xFFC87050, tx + 22, ty + 4, 18, 14);
            dc.DrawGeometry(Brush(0xFF8A8FA0), Pen(ink, 2.5), Poly(new Point(tx - 44, ty - 26), new Point(tx + 44, ty - 26), new Point(tx + 34, ty + 18), new Point(tx, ty + 30), new Point(tx - 34, ty + 18)));
            dc.DrawGeometry(Brush(0xFFB0B4C4), null, Poly(new Point(tx - 38, ty - 22), new Point(tx - 4, ty - 22), new Point(tx - 4, ty + 20), new Point(tx - 30, ty + 14)));
            bool open = b.Exposed >= 2;
            Disc(dc, open ? ((int)(_clock * 8) % 2 == 0 ? 0xFFFF3D3Du : 0xFFFFE14Au) : 0xFF5A1A1Au, tx, ty - 2, 11, edge: ink, edgeWidth: 2.5);
            foreach (var (rx, ry) in new[] { (-36.0, -20.0), (36.0, -20.0), (-26.0, 12.0), (26.0, 12.0) }) Disc(dc, 0xFF5A5E6E, tx + rx, ty + ry, 2.5);
            Box(dc, 0xFF6A4A2A, tx - 46, ty + 26, 92, 10, 3, ink);
            Box(dc, 0xFFFFD040, tx - 8, ty + 26, 16, 10, 2, ink);
            FlashOver(dc, torso);
        }
        else Stump(dc, 0, 10, 30);
        // Where an arm was shot off: a sparking shoulder.
        foreach (var (name, sx) in new[] { ("LEFT CANNON", -1), ("RIGHT CANNON", 1) })
            if (!torso.Gone && b.Part(name)!.Gone) Stump(dc, sx * 52, -14, 13);
        foreach (var (name, sx) in new[] { ("LEFT POD", -1), ("RIGHT POD", 1) })
        {
            var pod = b.Part(name)!;
            if (torso.Gone) continue;
            var (px, py, pw, ph) = Px(pod);
            if (pod.Gone) { Stump(dc, px, py + 4, 10); continue; }
            Box(dc, 0xFF5A6A3A, px - pw, py - ph, pw * 2, ph * 2, 5, ink, 2.5);
            for (int i = 0; i < 4; i++)
            {
                double hx = px - pw + 8 + (i % 2) * (pw * 2 - 16), hy = py - ph + 8 + (i / 2) * (ph * 2 - 16);
                Disc(dc, ink, hx, hy, 5);
                Disc(dc, 0xFFFF3D3D, hx, hy, 2.5);
            }
            FlashOver(dc, pod);
        }
        // The head.
        if (torso.Gone) return;
        if (head.Gone) { Stump(dc, 0, -50, 14); return; }
        var (hx0, hy0, hw, hh) = Px(head);
        Box(dc, 0xFFC87050, hx0 - 14, hy0 + 10, 28, 20, 4, ink);
        Disc(dc, 0xFFE0906A, hx0, hy0 + 2, hw, hh, ink, 3);
        dc.DrawGeometry(Brush(0xFF8A8FA0), Pen(ink, 2.5), Poly(new Point(hx0 - hw - 2, hy0 + 2), new Point(hx0 - hw + 4, hy0 - hh + 2), new Point(hx0 + hw - 4, hy0 - hh + 2), new Point(hx0 + hw + 2, hy0 + 2)));
        dc.DrawGeometry(Brush(0xFFE83A3A), Pen(ink, 1.5), Poly(new Point(hx0 - 6, hy0 - hh + 2), new Point(hx0, hy0 - hh - 16), new Point(hx0 + 6, hy0 - hh + 2)));
        Box(dc, ink, hx0 - hw + 4, hy0 - 4, hw * 2 - 8, 9, 3);
        bool blink = (int)(_clock * 3) % 5 == 0;
        Box(dc, blink ? 0xFFFFFFFFu : 0xFFFF3D3Du, hx0 - 15, hy0 - 2, 10, 5, 2);
        Box(dc, blink ? 0xFFFFFFFFu : 0xFFFF3D3Du, hx0 + 5, hy0 - 2, 10, 5, 2);
        Box(dc, ink, hx0 - 13, hy0 + 12, 26, 9, 3);
        for (int i = 0; i < 5; i++) Box(dc, 0xFFF4F4F8, hx0 - 12 + i * 5.2, hy0 + 12, 3.5, 4);
        FlashOver(dc, head);
    }

    private void DrawViper(DrawingContext dc, Boss b)
    {
        const uint ink = 0xFF14141C;
        var coils = b.CoilPositions().ToList();
        // The coils, tail first.
        for (int i = coils.Count - 1; i >= 0; i--)
        {
            var s = P(coils[i]);
            double r = 24 - i * 0.7;
            dc.DrawEllipse(Brush(0x50000000), null, s + new Point(0, r * 0.8), r, 6);
            Disc(dc, 0xFF2E7A3A, s.X, s.Y, r, edge: ink, edgeWidth: 2.5);
            dc.DrawGeometry(Brush(0xFFE8C83A), null, Poly(s + new Point(0, -r * 0.55), s + new Point(r * 0.45, 0), s + new Point(0, r * 0.55), s + new Point(-r * 0.45, 0)));
            Disc(dc, 0xFF5AB85A, s.X - r * 0.35, s.Y - r * 0.4, r * 0.25);
        }
        double rear = b.Mode == 1 ? Math.Sin(_clock * 60) * 3 - 6 : 0;
        var c = P(b.Pos) + new Point(0, rear);
        if (coils.Count > 0) dc.DrawLine(Pen(0xFF2E7A3A, 30), c + new Point(0, 30), P(coils[0]));
        using var _ = At(dc, c);
        var screen = b.Part("SCREEN")!;
        // The hood, spread wide.
        foreach (var (name, sx) in new[] { ("LEFT HOOD", -1), ("RIGHT HOOD", 1) })
        {
            var hood = b.Part(name)!;
            var (x, y, w, h) = Px(hood);
            if (hood.Gone)
            {
                dc.DrawGeometry(Brush(0xFF1E4A24), Pen(ink, 2), Poly(new Point(sx * 30, -30), new Point(x, y - 10), new Point(x - sx * 6, y + 6), new Point(x + sx * 4, y + 16), new Point(sx * 30, 26)));
                continue;
            }
            dc.DrawGeometry(Brush(0xFF3A9A48), Pen(ink, 3), Poly(new Point(sx * 20, -50), new Point(x - sx * 4, y - h - 8), new Point(x + sx * w * 0.9, y - h * 0.6), new Point(x + sx * (w + 6), y + h * 0.2), new Point(x + sx * w * 0.6, y + h), new Point(sx * 22, 40)));
            dc.DrawGeometry(Brush(0xFF2A7A36), null, Poly(new Point(sx * 26, -38), new Point(x, y - h + 4), new Point(x + sx * w * 0.6, y), new Point(x + sx * w * 0.3, y + h - 6), new Point(sx * 26, 30)));
            Disc(dc, 0xFFE8C83A, x + sx * 4, y - 10, 13, 17, ink, 2);
            Disc(dc, 0xFFC83A2A, x + sx * 4, y - 10, 7, 10);
            Disc(dc, ink, x + sx * 4, y - 10, 3, 6);
            Disc(dc, 0xFFE8C83A, x + sx * 10, y + 24, 8, 6, ink, 1.5);
            FlashOver(dc, hood);
        }
        // The fangs.
        foreach (var (name, sx) in new[] { ("LEFT FANG", -1), ("RIGHT FANG", 1) })
        {
            var fang = b.Part(name)!;
            var (x, y, w, h) = Px(fang);
            if (fang.Gone) { Box(dc, 0xFF8A8A7A, x - 4, y - h, 8, 6, 2, ink); continue; }
            dc.DrawGeometry(Brush(0xFFF4F4E8), Pen(ink, 2), Poly(new Point(x - w, y - h), new Point(x + w, y - h), new Point(x + sx * 2, y + h)));
            if ((int)(_clock * 2 + sx) % 3 == 0) Disc(dc, 0xFF8AE83A, x + sx * 2, y + h + 4, 3, 4);
            FlashOver(dc, fang);
        }
        // The head: a television, its screen an eye.
        var (scx, scy, sw, sh) = Px(screen);
        dc.DrawLine(Pen(0xFF8A8FA0, 3), new Point(scx - 8, scy - sh), new Point(scx - 26, scy - sh - 26));
        dc.DrawLine(Pen(0xFF8A8FA0, 3), new Point(scx + 8, scy - sh), new Point(scx + 26, scy - sh - 26));
        Disc(dc, 0xFFFF3D3D, scx - 26, scy - sh - 26, 4);
        Disc(dc, 0xFFFF3D3D, scx + 26, scy - sh - 26, 4);
        Box(dc, 0xFF3A3A44, scx - sw - 6, scy - sh - 6, sw * 2 + 12, sh * 2 + 18, 10, ink, 3);
        bool staticNow = screen.Flash > 0 || b.Mode == 1;
        Box(dc, staticNow ? 0xFFC8CCD8 : 0xFF0A1A2A, scx - sw, scy - sh, sw * 2, sh * 2, 8);
        if (staticNow)
        {
            var rng = new Random((int)(_clock * 30));
            for (int i = 0; i < 40; i++) Box(dc, rng.Next(2) == 0 ? ink : 0xFFFFFFFF, scx - sw + rng.NextDouble() * (sw * 2 - 4), scy - sh + rng.NextDouble() * (sh * 2 - 3), 4, 3);
        }
        else
        {
            var target = Engine.Alive.OrderBy(p => (p.Pos - b.Pos).Length).FirstOrDefault();
            var look = target is null ? new Vec(0, 1) : (target.Pos - b.Pos).Normal;
            Disc(dc, 0xFFF4F4E8, scx, scy, sw * 0.7, sh * 0.6);
            Disc(dc, b.Exposed >= 2 ? 0xFFFF3D3Du : 0xFFE8C83Au, scx + look.X * 8, scy + look.Y * 5, sh * 0.42);
            Disc(dc, ink, scx + look.X * 10, scy + look.Y * 6, sh * 0.2, sh * 0.36);
            for (double y = scy - sh + 2; y < scy + sh; y += 4) Box(dc, 0x30000000, scx - sw, y, sw * 2, 1.5);
        }
        Disc(dc, 0xFFA8ACB8, scx + sw - 6, scy + sh + 6, 3);
        Disc(dc, 0xFFA8ACB8, scx + sw - 16, scy + sh + 6, 3);
        FlashOver(dc, screen);
    }

    private void DrawMonty(DrawingContext dc, Boss b)
    {
        const uint ink = 0xFF14141C;
        var c = P(b.Pos);
        dc.DrawEllipse(Brush(0x50000000), null, c + new Point(0, 120), 90, 16);
        var throne = b.Part("THRONE")!;
        var monty = b.Part("MONTY")!;
        var target = Engine.Alive.OrderBy(p => (p.Pos - b.Pos).Length).FirstOrDefault();
        using (At(dc, c))
        {
            // The jets under the throne.
            double flicker = (int)(_clock * 20) % 2 * 6;
            foreach (int x in new[] { -40, 40 })
            {
                dc.DrawGeometry(Brush(0xFFFF8A1E), null, Poly(new Point(x - 12, 70), new Point(x + 12, 70), new Point(x, 104 + flicker)));
                dc.DrawGeometry(Brush(0xFFFFF0A0), null, Poly(new Point(x - 6, 70), new Point(x + 6, 70), new Point(x, 88 + flicker / 2)));
            }
            if (!throne.Gone)
            {
                var (tx, ty, tw, th) = Px(throne);
                // The high back, behind him.
                Box(dc, 0xFFD8A830, -52, -96, 104, 120, 30, ink, 3);
                Box(dc, 0xFFB01E3A, -40, -84, 80, 100, 24);
                Disc(dc, 0xFFFFE14A, 0, -100, 12, edge: ink, edgeWidth: 2);
                Star(dc, new Point(0, -100), 8, 0xFFFFFFFF);
                Box(dc, 0xFFD8A830, tx - tw, ty - th + 10, tw * 2, th * 2 - 10, 14, ink, 3);
                Box(dc, 0xFFB01E3A, tx - tw + 10, ty - th + 14, tw * 2 - 20, 18, 8);
                for (int i = 0; i < 5; i++) Disc(dc, (int)(_clock * 6 + i) % 5 == 0 ? 0xFFFFFFFFu : 0xFFFF5AC8u, tx - tw + 20 + i * (tw * 2 - 40) / 4, ty + th - 12, 4);
                FlashOver(dc, throne);
            }
            else Box(dc, 0xFF5A5E6E, -30, 40, 60, 30, 8, ink, 2);
            if (!monty.Gone)
            {
                var (mx, my, mw, mh) = Px(monty);
                // The tuxedo.
                Disc(dc, 0xFF1A1A24, mx, my + 18, mw, mh * 0.7, ink, 2.5);
                dc.DrawGeometry(Brush(0xFFF4F4F8), null, Poly(new Point(mx - 10, my - 4), new Point(mx + 10, my - 4), new Point(mx, my + 34)));
                dc.DrawGeometry(Brush(0xFFE83A3A), null, Poly(new Point(mx - 12, my - 2), new Point(mx, my + 3), new Point(mx - 12, my + 8)));
                dc.DrawGeometry(Brush(0xFFE83A3A), null, Poly(new Point(mx + 12, my - 2), new Point(mx, my + 3), new Point(mx + 12, my + 8)));
                // An arm up, waving the microphone.
                double wave = Math.Sin(_clock * 5) * 6;
                dc.DrawLine(Pen(0xFF1A1A24, 10), new Point(mx + 26, my + 10), new Point(mx + 40, my - 14 + wave));
                Disc(dc, 0xFFFFC898, mx + 40, my - 16 + wave, 6);
                Box(dc, 0xFF3A3A44, mx + 38, my - 34 + wave, 5, 16, 2);
                Disc(dc, 0xFF8A8FA0, mx + 40, my - 36 + wave, 5);
                DrawHostHead(dc, new Point(mx, my - 26), 1, monty.Flash > 0);
            }
        }
        // The turrets swing round after you.
        foreach (var t in b.Parts.Where(p => p.Name.StartsWith("TURRET")))
        {
            var at = b.Pos + t.Offset;
            var tc = P(at);
            if (t.Gone) { Stump(dc, tc.X, tc.Y, 12); continue; }
            double aim = target is null ? Math.PI / 2 : (target.Pos - at).Angle;
            using (At(dc, tc, 1, aim)) Box(dc, 0xFF3A3A44, 4, -5, 24, 10, 3, ink);
            Disc(dc, 0xFFD8A830, tc.X, tc.Y, 18, edge: ink, edgeWidth: 2.5);
            Disc(dc, 0xFFFFE89A, tc.X - 5, tc.Y - 6, 5);
            Disc(dc, (int)(_clock * 4) % 2 == 0 ? 0xFFFF3D3Du : 0xFF8A1E1Eu, tc.X, tc.Y + 6, 3);
            if (t.Flash > 0) Disc(dc, 0xA0FFFFFF, tc.X, tc.Y, 18);
        }
    }

    /// <summary>The host: a slick pompadour, a tan, and far too many teeth.</summary>
    private static void DrawHostHead(DrawingContext dc, Point c, double scale, bool flash = false)
    {
        const uint ink = 0xFF14141C;
        using var _ = At(dc, c, scale);
        Disc(dc, 0xFFE8A878, -24, 2, 6, 9, ink);
        Disc(dc, 0xFFE8A878, 24, 2, 6, 9, ink);
        Disc(dc, flash ? 0xFFFFFFFF : 0xFFF0B888, 0, 0, 24, 28, ink, 2.5);
        // The hair.
        dc.DrawGeometry(Brush(0xFF2A1A12), Pen(ink, 2), Poly(new Point(-25, -4), new Point(-26, -22), new Point(-12, -36), new Point(10, -40), new Point(30, -32), new Point(22, -26), new Point(25, -10), new Point(16, -18), new Point(-6, -20), new Point(-18, -14)));
        Disc(dc, 0xFF5A3A2A, 6, -32, 8, 3);
        // The eyebrows, the eyes, the grin.
        dc.DrawLine(Pen(0xFF2A1A12, 3), new Point(-15, -9), new Point(-5, -11));
        dc.DrawLine(Pen(0xFF2A1A12, 3), new Point(5, -11), new Point(15, -9));
        Disc(dc, 0xFFFFFFFF, -10, -3, 5, 4);
        Disc(dc, 0xFFFFFFFF, 10, -3, 5, 4);
        Disc(dc, 0xFF2A5AB8, -9, -3, 2.5);
        Disc(dc, 0xFF2A5AB8, 11, -3, 2.5);
        dc.DrawLine(Pen(0xFFC8805A, 2), new Point(0, -2), new Point(-3, 7));
        dc.DrawGeometry(Brush(0xFF8A1E1E), Pen(ink, 2), Poly(new Point(-15, 11), new Point(15, 11), new Point(10, 21), new Point(0, 24), new Point(-10, 21)));
        Box(dc, 0xFFFFFFFF, -13, 11, 26, 6, 2);
        for (int i = 1; i < 6; i++) dc.DrawLine(Pen(0xFFC8C8D0, 1), new Point(-13 + i * 4.4, 11), new Point(-13 + i * 4.4, 17));
        Star(dc, new Point(9, 13), 5, 0xFFFFFFFF);
    }

    /// <summary>The boss's name and what's left of it, along the bottom of the arena.</summary>
    private void DrawBossBar(DrawingContext dc, Boss b)
    {
        double w = 420, x = FloorRect.X + FloorRect.Width / 2 - w / 2, y = FloorRect.Bottom - 30;
        Box(dc, 0xC0000000, x - 120, y - 6, w + 132, 26, 4);
        Text(dc, b.Name, new Point(x - 10, y + 7), 26, Red, right: true);
        Box(dc, 0xFF3A1A1A, x, y, w, 14, 3);
        double done = 0;
        foreach (var layer in b.Parts.GroupBy(p => p.Layer).OrderBy(gp => gp.Key))
        {
            double share = layer.Sum(p => p.MaxHealth) / (double)b.MaxHealth;
            double left = layer.Sum(p => Math.Max(0, p.Health)) / (double)layer.Sum(p => p.MaxHealth);
            Box(dc, layer.Key == b.Exposed ? 0xFFFFE14Au : 0xFFE83A3Au, x + done * w, y, share * w * left, 14);
            done += share;
            dc.FillRectangle(Black, new Rect(x + done * w - 1, y, 2, 14));
        }
    }
}
