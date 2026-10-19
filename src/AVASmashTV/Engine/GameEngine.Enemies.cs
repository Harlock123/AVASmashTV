using AVASmashTV.Models;

namespace AVASmashTV.Engine;

public sealed partial class GameEngine
{
    public const int SnakeSegments = 7;
    public const int ShrapnelPieces = 8;

    public static int Points(EnemyKind k) => k switch
    {
        EnemyKind.Grunt => 100,
        EnemyKind.Orb => 300,
        EnemyKind.Snake => 500,
        EnemyKind.Blade => 400,
        EnemyKind.Tank => 1000,
        EnemyKind.Mine => 50,
        _ => 250,
    };

    public static int Toughness(EnemyKind k) => k switch
    {
        EnemyKind.Orb => 3,
        EnemyKind.Blade => 3,
        EnemyKind.Tank => 12,
        EnemyKind.Shrapnel => 2,
        _ => 1,
    };

    /// <summary>The goo they leave behind (it's only ketchup).</summary>
    public static uint SplatColour(EnemyKind k) => k switch
    {
        EnemyKind.Grunt => 0xFF8AE83A,
        EnemyKind.Orb => 0xFFC8D0E0,
        EnemyKind.Snake => 0xFFFFB02A,
        EnemyKind.Blade => 0xFFE0E4F0,
        EnemyKind.Tank => 0xFFFF8A1E,
        _ => 0xFFFFE14A,
    };

    private Enemy AddEnemy(EnemyKind kind, Vec at)
    {
        var e = new Enemy { Kind = kind, Pos = at, Health = Toughness(kind), Quirk = _rng.NextDouble() * Math.PI * 2, Reload = 1 + _rng.NextDouble() * 1.5 };
        switch (kind)
        {
            case EnemyKind.Snake:
                e.Segments = SnakeSegments + Difficulty / 4;
                break;
            case EnemyKind.Shrapnel:
                e.Fuse = 4 + _rng.NextDouble() * 2;
                break;
            case EnemyKind.Blade:
                e.Vel = Vec.FromAngle(Math.PI / 4 + _rng.Next(4) * Math.PI / 2);
                break;
        }
        Enemies.Add(e);
        return e;
    }

    /// <summary>The nearest contestant still standing, if any.</summary>
    private Player? Nearest(Vec from) =>
        Players.Where(p => p.Alive).OrderBy(p => (p.Pos - from).Length).FirstOrDefault();

    private void MoveEnemies(double dt)
    {
        double pace = Pace;
        for (int i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (e.Dead) continue;
            e.Age += dt;
            if (e.Flash > 0) e.Flash -= dt;
            var target = Nearest(e.Pos);
            if (e.Entering > 0)
            {
                // Through the door and in, before anything else.
                e.Pos += e.Vel.Normal * (Math.Max(2.5, Speed(e.Kind) * pace) * dt);
                if (e.Kind == EnemyKind.Snake) Trail(e);
                double r = e.Radius;
                if (e.Pos.X > r + 0.4 && e.Pos.Y > r + 0.4 && e.Pos.X < Field.Width - r - 0.4 && e.Pos.Y < Field.Height - r - 0.4)
                {
                    e.Entering = 0;
                    if (e.Kind == EnemyKind.Blade) e.Vel = new Vec(Math.Abs(e.Vel.X) < 0.1 ? (_rng.Next(2) * 2 - 1) : e.Vel.X, Math.Abs(e.Vel.Y) < 0.1 ? (_rng.Next(2) * 2 - 1) : e.Vel.Y).Normal;
                }
                continue;
            }
            switch (e.Kind)
            {
                case EnemyKind.Grunt: Grunt(e, target, pace, dt); break;
                case EnemyKind.Orb: Orb(e, target, pace, dt); break;
                case EnemyKind.Snake: Snake(e, target, pace, dt); break;
                case EnemyKind.Blade: Bounce(e, e.Vel.Normal * (6.5 * Math.Min(1.4, pace)), dt); break;
                case EnemyKind.Tank: Tank(e, target, pace, dt); break;
                case EnemyKind.Shrapnel: Bomb(e, target, pace, dt); break;
            }
            if (e.Dead) continue;
            Contact(e);
        }
        Jostle();
        Enemies.RemoveAll(e => e.Dead);
    }

    public static double Speed(EnemyKind k) => k switch
    {
        EnemyKind.Grunt => 3.1,
        EnemyKind.Orb => 3.4,
        EnemyKind.Snake => 4.4,
        EnemyKind.Blade => 6.5,
        EnemyKind.Tank => 1.3,
        EnemyKind.Shrapnel => 2.0,
        _ => 0,
    };

    /// <summary>Kept inside the walls.</summary>
    private static Vec Inside(Vec p, double r) => new(Math.Clamp(p.X, r, Field.Width - r), Math.Clamp(p.Y, r, Field.Height - r));

    /// <summary>Straight at the nearest contestant, weaving a little so the mob spreads out.</summary>
    private static void Grunt(Enemy e, Player? target, double pace, double dt)
    {
        if (target is null) return;
        var to = target.Pos - e.Pos;
        var side = to.Normal.Rotate(Math.PI / 2) * (Math.Sin(e.Age * 1.7 + e.Quirk) * Math.Min(1.5, to.Length * 0.25));
        var dir = (to + side).Normal;
        e.Vel = dir;
        e.Pos = Inside(e.Pos + dir * (Math.Min(4.8, Speed(EnemyKind.Grunt) * pace) * dt), e.Radius);
    }

    /// <summary>Drifts after the nearest contestant, with a wobble, and bounces off the walls.</summary>
    private static void Orb(Enemy e, Player? target, double pace, double dt)
    {
        var v = e.Vel;
        if (target is not null) v += ((target.Pos - e.Pos).Normal * 4.5 + Vec.FromAngle(e.Age * 2 + e.Quirk) * 2.5) * dt;
        double max = Speed(EnemyKind.Orb) * Math.Min(1.5, pace);
        if (v.Length > max) v = v.Normal * max;
        Bounce(e, v, dt);
    }

    /// <summary>On in a straight line, off the walls like a ball off a cushion.</summary>
    private static void Bounce(Enemy e, Vec v, double dt)
    {
        var p = e.Pos + v * dt;
        double r = e.Radius;
        if (p.X < r || p.X > Field.Width - r) v = v with { X = -v.X };
        if (p.Y < r || p.Y > Field.Height - r) v = v with { Y = -v.Y };
        e.Vel = v;
        e.Pos = Inside(p, r);
    }

    /// <summary>Slithers after the nearest contestant, its segments following where its head has been.</summary>
    private static void Snake(Enemy e, Player? target, double pace, double dt)
    {
        double heading = e.Vel.Length > 0 ? e.Vel.Angle : Math.PI / 2;
        if (target is not null)
        {
            double want = (target.Pos - e.Pos).Angle + Math.Sin(e.Age * 5 + e.Quirk) * 0.8;
            double diff = Math.IEEERemainder(want - heading, Math.PI * 2);
            heading += Math.Clamp(diff, -2.6 * dt, 2.6 * dt);
        }
        var v = Vec.FromAngle(heading) * (Speed(EnemyKind.Snake) * Math.Min(1.4, pace));
        Bounce(e, v, dt);
        Trail(e);
    }

    private static void Trail(Enemy e)
    {
        e.Trail.Insert(0, e.Pos);
        int keep = (e.Segments + 1) * Enemy.SegmentGap + 1;
        if (e.Trail.Count > keep) e.Trail.RemoveRange(keep, e.Trail.Count - keep);
    }

    /// <summary>Keeps its distance, its turret swinging round, and lobs a shell when it's lined up.</summary>
    private void Tank(Enemy e, Player? target, double pace, double dt)
    {
        if (target is null) return;
        var to = target.Pos - e.Pos;
        double dist = to.Length;
        var dir = dist > 6.5 ? to.Normal : dist < 4.5 ? to.Normal * -1 : to.Normal.Rotate(Math.PI / 2) * Math.Sign(Math.Sin(e.Quirk + e.Age * 0.2));
        e.Vel = dir;
        e.Pos = Inside(e.Pos + dir * (Speed(EnemyKind.Tank) * pace * dt), e.Radius);
        double diff = Math.IEEERemainder(to.Angle - e.Turret, Math.PI * 2);
        e.Turret += Math.Clamp(diff, -1.6 * dt, 1.6 * dt);
        e.Reload -= dt;
        if (e.Reload <= 0 && Math.Abs(diff) < 0.25)
        {
            e.Reload = Math.Max(1.4, 2.8 / pace);
            var aim = Vec.FromAngle(e.Turret);
            Shots.Add(new Shot { Kind = ShotKind.Shell, Pos = e.Pos + aim * 1.0, Vel = aim * 6.5, Hostile = true, Life = 5 });
            Play(Sfx.Grenade);
        }
    }

    /// <summary>Rolls after the nearest contestant until its fuse runs out.</summary>
    private void Bomb(Enemy e, Player? target, double pace, double dt)
    {
        if (target is not null)
        {
            var dir = (target.Pos - e.Pos).Normal;
            e.Vel = dir;
            e.Pos = Inside(e.Pos + dir * (Speed(EnemyKind.Shrapnel) * pace * dt), e.Radius);
        }
        e.Fuse -= dt;
        if (e.Fuse <= 0) Shrapnel(e, -1);
    }

    /// <summary>What happens when it touches a contestant (or a mine).</summary>
    private void Contact(Enemy e)
    {
        foreach (var p in Players)
        {
            if (!p.Alive) continue;
            double d = (p.Pos - e.Pos).Length;
            // The force field's orbs cut down anything that comes close.
            if (p.ShieldTime > 0 && e.Kind != EnemyKind.Mine && d < ShieldReach + e.Radius && (int)(e.Age * 60) % 4 == 0)
            {
                Damage(e, 1, p.Index);
                if (e.Dead) return;
            }
            if (!Touches(e, p.Pos, Player.Radius - 0.08)) continue;
            switch (e.Kind)
            {
                case EnemyKind.Mine:
                    MineGoesUp(e, -1);
                    KillPlayer(p);
                    return;
                case EnemyKind.Shrapnel:
                    Shrapnel(e, -1);
                    KillPlayer(p);
                    return;
                default:
                    KillPlayer(p);
                    break;
            }
        }
        // Anything that walks sets off a mine (fliers go over).
        if (e.Kind is EnemyKind.Grunt or EnemyKind.Tank or EnemyKind.Snake or EnemyKind.Shrapnel)
        {
            var mine = Enemies.FirstOrDefault(m => m.Kind == EnemyKind.Mine && !m.Dead && (m.Pos - e.Pos).Length < m.Radius + e.Radius - 0.1);
            if (mine is not null) MineGoesUp(mine, -1);
        }
    }

    /// <summary>The mob doesn't pile up in one spot.</summary>
    private void Jostle()
    {
        for (int i = 0; i < Enemies.Count; i++)
        {
            var a = Enemies[i];
            if (a.Dead || a.Entering > 0 || a.Kind is EnemyKind.Mine or EnemyKind.Blade) continue;
            for (int j = i + 1; j < Enemies.Count; j++)
            {
                var b = Enemies[j];
                if (b.Dead || b.Entering > 0 || b.Kind is EnemyKind.Mine or EnemyKind.Blade) continue;
                var d = b.Pos - a.Pos;
                double len = d.Length, min = (a.Radius + b.Radius) * 0.9;
                if (len >= min || len < 1e-6) continue;
                var push = d.Normal * ((min - len) * 0.5);
                a.Pos = Inside(a.Pos - push, a.Radius);
                b.Pos = Inside(b.Pos + push, b.Radius);
            }
        }
    }

    /// <summary>A hit: a snake loses a segment; anything else loses health, and goes down at none.</summary>
    private void Damage(Enemy e, int damage, int owner)
    {
        if (e.Dead) return;
        e.Flash = 0.08;
        switch (e.Kind)
        {
            case EnemyKind.Mine:
                MineGoesUp(e, owner);
                return;
            case EnemyKind.Snake when e.Segments > 0:
                var tail = e.SegmentPositions().Last();
                e.Segments--;
                AddScore(owner, 50);
                Effects.Add(new Effect { Kind = EffectKind.Splat, Pos = tail, Life = 0.6, Size = 0.6, Colour = SplatColour(e.Kind), Seed = _rng.Next() });
                HitSound();
                return;
        }
        e.Health -= damage;
        if (e.Health > 0) { HitSound(); return; }
        if (e.Kind == EnemyKind.Shrapnel) { Shrapnel(e, owner); return; }
        Kill(e, owner);
    }

    private void HitSound()
    {
        if (_hitSound > 0) return;
        _hitSound = 0.06;
        Play(Sfx.Hit);
    }

    private void Kill(Enemy e, int owner)
    {
        if (e.Dead) return;
        e.Dead = true;
        AddScore(owner, Points(e.Kind));
        if (owner >= 0) Players[owner].Kills++;
        Effects.Add(new Effect { Kind = EffectKind.Splat, Pos = e.Pos, Life = 0.9, Size = e.Kind == EnemyKind.Tank ? 1.6 : 1, Colour = SplatColour(e.Kind), Seed = _rng.Next() });
        if (e.Kind is EnemyKind.Tank or EnemyKind.Blade or EnemyKind.Orb)
        {
            Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = e.Pos, Life = 0.5, Size = e.Radius * 1.6 });
            if (owner >= 0 && e.Kind == EnemyKind.Tank) Popup(e.Pos, Points(e.Kind).ToString());
            Play(Sfx.Explosion);
        }
        else if (_splatSound <= 0)
        {
            _splatSound = 0.05;
            Play(Sfx.Splat);
        }
    }

    /// <summary>A mine goes up, and takes anything near with it.</summary>
    private void MineGoesUp(Enemy mine, int owner)
    {
        if (mine.Dead) return;
        mine.Dead = true;
        AddScore(owner, Points(EnemyKind.Mine));
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = mine.Pos, Life = 0.6, Size = MineBlast });
        Play(Sfx.Explosion);
        foreach (var e in Enemies.ToList())
            if (!e.Dead && e.Kind != EnemyKind.Mine && (e.Pos - mine.Pos).Length < MineBlast + e.Radius)
            {
                if (e.Kind == EnemyKind.Shrapnel) Shrapnel(e, owner);
                else if (e.Kind == EnemyKind.Snake) { e.Segments = 0; Kill(e, owner); }
                else Kill(e, owner);
            }
        // Mines next to it go up too.
        foreach (var m in Enemies.Where(m => m.Kind == EnemyKind.Mine && !m.Dead && (m.Pos - mine.Pos).Length < MineBlast).ToList()) MineGoesUp(m, owner);
    }

    /// <summary>A shrapnel bomb bursts: pieces fly out every way, and they hurt whoever they hit.</summary>
    private void Shrapnel(Enemy e, int owner)
    {
        if (e.Dead) return;
        e.Dead = true;
        AddScore(owner, Points(EnemyKind.Shrapnel));
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = e.Pos, Life = 0.45, Size = 1.1 });
        Play(Sfx.Explosion);
        for (int i = 0; i < ShrapnelPieces; i++)
        {
            var dir = Vec.FromAngle(i * Math.PI * 2 / ShrapnelPieces + e.Quirk);
            Shots.Add(new Shot { Kind = ShotKind.Shrapnel, Pos = e.Pos + dir * 0.4, Vel = dir * 7.5, Hostile = true, Life = 1.3 });
        }
    }
}
