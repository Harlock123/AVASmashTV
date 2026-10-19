using AVASmashTV.Models;

namespace AVASmashTV.Engine;

/// <summary>
/// The bosses. Each is a set of parts in layers: only the parts of the lowest layer still standing can be
/// hurt (shots pass the others with a clank of armour), so they come apart from the outside in.
/// </summary>
public sealed partial class GameEngine
{
    public const int PartPoints = 5000;
    public const int BossPoints = 50000;
    public const double BossDeathSeconds = 2.6;
    private double _clankSound, _bossHitSound;
    private int _lastHitter = -1;

    public static Vec BossHome(BossKind k) => k switch
    {
        BossKind.Tankenstein => new Vec(Field.Width / 2, 5.8),
        BossKind.Vipervision => new Vec(Field.Width / 2, 7.5),
        _ => new Vec(Field.Width / 2, 6.2),
    };

    private static Boss MakeBoss(BossKind kind, int players)
    {
        var b = new Boss { Kind = kind, Pos = new Vec(Field.Width / 2, -3.5), Mode = 4 };
        double hp = players > 1 ? 1.4 : 1;
        void Part(string name, double x, double y, double w, double h, int layer, int health) =>
            b.Parts.Add(new BossPart { Name = name, Offset = new Vec(x, y), Size = new Vec(w, h), Layer = layer, MaxHealth = (int)(health * hp), Health = (int)(health * hp), Reload = 1.5 + b.Parts.Count * 0.37 });
        switch (kind)
        {
            case BossKind.Tankenstein:
                Part("LEFT CANNON", -2.9, 0.1, 0.8, 0.9, 0, 22);
                Part("RIGHT CANNON", 2.9, 0.1, 0.8, 0.9, 0, 22);
                Part("LEFT POD", -1.6, -1.5, 0.7, 0.6, 0, 16);
                Part("RIGHT POD", 1.6, -1.5, 0.7, 0.6, 0, 16);
                Part("HEAD", 0, -2.1, 0.85, 0.8, 1, 34);
                Part("TORSO", 0, -0.3, 1.7, 1.3, 2, 50);
                Part("TREADS", 0, 2.0, 3.1, 0.9, 3, 60);
                break;
            case BossKind.Vipervision:
                Part("LEFT HOOD", -1.9, -0.4, 0.9, 1.5, 0, 26);
                Part("RIGHT HOOD", 1.9, -0.4, 0.9, 1.5, 0, 26);
                Part("LEFT FANG", -0.55, 1.25, 0.35, 0.5, 1, 14);
                Part("RIGHT FANG", 0.55, 1.25, 0.35, 0.5, 1, 14);
                Part("SCREEN", 0, -0.3, 1.15, 1.0, 2, 70);
                break;
            default:
                Part("TURRET 1", -3.6, -0.4, 0.75, 0.75, 0, 18);
                Part("TURRET 2", 3.6, -0.4, 0.75, 0.75, 0, 18);
                Part("TURRET 3", -2.6, 2.0, 0.75, 0.75, 0, 18);
                Part("TURRET 4", 2.6, 2.0, 0.75, 0.75, 0, 18);
                Part("THRONE", 0, 1.0, 2.2, 1.3, 1, 60);
                Part("MONTY", 0, -1.1, 1.0, 1.3, 2, 90);
                break;
        }
        return b;
    }

    /// <summary>Is p (a circle of radius r) over this part?</summary>
    private static bool Over(Boss b, BossPart part, Vec p, double r)
    {
        var d = p - (b.Pos + part.Offset);
        double ex = d.X / (part.Size.X + r), ey = d.Y / (part.Size.Y + r);
        return ex * ex + ey * ey <= 1;
    }

    /// <summary>A contestant's shot (at a point on its way) against the boss: true if it stopped there.</summary>
    private bool ShootBoss(Boss b, Shot s, Vec at)
    {
        // The cobra's coils are armour all the way along.
        foreach (var c in b.CoilPositions())
        {
            if ((c - at).Length > 0.75 + s.Radius) continue;
            s.Dead = true;
            s.Pos = at;
            Clank(at);
            if (s.Kind == ShotKind.Grenade) Explode(at, GrenadeBlast, s.Damage, s.Owner);
            return true;
        }
        int exposed = b.Exposed;
        bool armour = false;
        foreach (var part in b.Parts)
        {
            if (part.Gone || !Over(b, part, at, s.Radius)) continue;
            if (part.Layer != exposed) { armour = true; continue; }
            s.Dead = true;
            s.Pos = at;
            if (s.Kind == ShotKind.Grenade) Explode(at, GrenadeBlast, s.Damage, s.Owner);
            else HitPart(b, part, s.Damage, s.Owner);
            return true;
        }
        // Armour: a spark and a clank, and the shot carries on.
        if (armour && !s.Clanked)
        {
            s.Clanked = true;
            Clank(at);
        }
        return false;
    }

    private void Clank(Vec at)
    {
        Effects.Add(new Effect { Kind = EffectKind.Sparkle, Pos = at, Life = 0.2, Colour = 0xFFFFFFFF });
        if (_clankSound > 0) return;
        _clankSound = 0.12;
        Play(Sfx.Clank);
    }

    private void HitPart(Boss b, BossPart part, int damage, int owner)
    {
        if (part.Gone || b.Dead) return;
        part.Health -= damage;
        part.Flash = 0.07;
        _lastHitter = owner;
        if (_bossHitSound <= 0) { _bossHitSound = 0.09; Play(Sfx.BossHit); }
        if (!part.Gone) return;
        // Off it comes.
        var at = b.Pos + part.Offset;
        AddScore(owner, PartPoints);
        Popup(at, PartPoints.ToString(), 0xFFFFE14A, 1.4);
        Play(Sfx.BossPart);
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = at, Life = 0.8, Size = Math.Max(part.Size.X, part.Size.Y) * 1.6 });
        for (int i = 0; i < 7; i++)
            Effects.Add(new Effect { Kind = EffectKind.Debris, Pos = at, Vel = Vec.FromAngle(_rng.NextDouble() * Math.PI * 2) * (3 + _rng.NextDouble() * 5), Life = 1.1, Size = 0.3 + _rng.NextDouble() * 0.4, Colour = 0xFF8A8FA0, Seed = _rng.Next() });
        if (b.Parts.All(p => p.Gone))
        {
            b.Dead = true;
            b.DeadTime = 0;
            AddScore(owner, BossPoints);
            Popup(b.Pos, BossPoints.ToString(), 0xFFFFE14A, 2.5);
            Play(Sfx.Crowd);
            Say($"{b.Name} IS HISTORY! I LOVE IT!", 4, Sfx.SayLoveIt);
            Shots.RemoveAll(s => s.Hostile);
        }
        else if (part.Layer != b.Exposed) Say(b.Kind switch
        {
            BossKind.Tankenstein => b.Exposed switch { 1 => "HIS GUNS ARE GONE! GO FOR THE HEAD!", 2 => "OFF WITH HIS HEAD! NOW THE BODY!", _ => "NOTHING LEFT BUT THE TANK!" },
            BossKind.Vipervision => b.Exposed == 1 ? "THE HOOD'S DOWN! PULL ITS FANGS!" : "NO FANGS! SMASH THE SCREEN!",
            _ => b.Exposed == 1 ? "MY TURRETS! MY BEAUTIFUL TURRETS!" : "NOT THE THRONE! ANYTHING BUT THE THRONE!",
        }, 3);
    }

    private void MoveBoss(Boss b, double dt)
    {
        b.Age += dt;
        b.ModeTime += dt;
        if (_clankSound > 0) _clankSound -= dt;
        if (_bossHitSound > 0) _bossHitSound -= dt;
        foreach (var p in b.Parts) if (p.Flash > 0) p.Flash -= dt;
        if (b.Dead)
        {
            // Going up in a string of explosions.
            b.DeadTime += dt;
            if ((int)(b.DeadTime * 8) != (int)((b.DeadTime - dt) * 8))
            {
                var at = b.Pos + new Vec(_rng.NextDouble() * 6 - 3, _rng.NextDouble() * 5 - 2.5);
                Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = at, Life = 0.6, Size = 1 + _rng.NextDouble() });
                Play(Sfx.Explosion);
            }
            if (b.DeadTime >= BossDeathSeconds)
            {
                foreach (var e in Enemies) Kill(e, _lastHitter);
                Enemies.Clear();
                Boss = null;
                RoomClear();
            }
            return;
        }
        var target = Nearest(b.Pos);
        var aimAt = target?.Pos ?? new Vec(Field.Width / 2, Field.Height - 2);
        switch (b.Kind)
        {
            case BossKind.Tankenstein: Tankenstein(b, aimAt, dt); break;
            case BossKind.Vipervision: Vipervision(b, aimAt, dt); break;
            default: Monty(b, aimAt, dt); break;
        }
        // Reinforcements through the doors now and then.
        b.Spawn -= dt;
        if (b.Spawn <= 0 && b.Mode != 4)
        {
            b.Spawn = b.Kind == BossKind.Monty ? 5 : 6.5;
            if (Enemies.Count < 30) Burst(EnemyKind.Grunt, b.Kind == BossKind.Monty ? (Side)_rng.Next(4) : _rng.Next(2) == 0 ? Side.East : Side.West, 3);
        }
        // Touching any of it is the end.
        foreach (var p in Players)
        {
            if (!p.Alive || p.Safe) continue;
            if (b.Parts.Any(part => !part.Gone && Over(b, part, p.Pos, Player.Radius - 0.1)) || b.CoilPositions().Any(c => (c - p.Pos).Length < 0.75 + Player.Radius - 0.1))
                KillPlayer(p);
        }
    }

    /// <summary>Moves toward a point at up to a speed; true when there.</summary>
    private static bool Toward(Boss b, Vec to, double speed, double dt)
    {
        var d = to - b.Pos;
        if (d.Length <= speed * dt) { b.Pos = to; return true; }
        b.Pos += d.Normal * (speed * dt);
        return false;
    }

    private bool Ready(BossPart? part, double dt, double every)
    {
        if (part is null || part.Gone) return false;
        part.Reload -= dt;
        if (part.Reload > 0) return false;
        part.Reload = every / Math.Min(1.5, Pace);
        return true;
    }

    private void Fan(Vec from, Vec at, int count, double spread, double speed, ShotKind kind = ShotKind.Fireball)
    {
        var dir = (at - from).Normal;
        for (int i = 0; i < count; i++)
        {
            double a = count == 1 ? 0 : -spread / 2 + spread * i / (count - 1);
            Shots.Add(new Shot { Kind = kind, Pos = from, Vel = dir.Rotate(a) * speed, Hostile = true, Life = 6 });
        }
    }

    private void Ring(Vec from, int count, double speed, double turn)
    {
        for (int i = 0; i < count; i++)
            Shots.Add(new Shot { Kind = ShotKind.Fireball, Pos = from, Vel = Vec.FromAngle(turn + i * Math.PI * 2 / count) * speed, Hostile = true, Life = 6 });
    }

    /// <summary>Rolls along after you, guns blazing; every so often it charges right down the room.</summary>
    private void Tankenstein(Boss b, Vec aim, double dt)
    {
        var home = BossHome(b.Kind);
        bool tankOnly = b.Exposed == 3;
        switch (b.Mode)
        {
            case 4:
                if (Toward(b, home, 2.5, dt)) { b.Mode = 0; b.ModeTime = 0; }
                return;
            case 0:
                Toward(b, new Vec(Math.Clamp(aim.X, 4, Field.Width - 4), home.Y), tankOnly ? 2.8 : 1.6, dt);
                if (b.ModeTime > 8) { b.Mode = 1; b.ModeTime = 0; }
                break;
            case 1:
                if (b.ModeTime > 0.8) { b.Mode = 2; b.ModeTime = 0; b.Target = new Vec(b.Pos.X, Field.Height - 3.2); Play(Sfx.Door); }
                break;
            case 2:
                if (Toward(b, b.Target, 6.5, dt)) { b.Mode = 3; b.ModeTime = 0; }
                break;
            case 3:
                if (Toward(b, home with { X = b.Pos.X }, 2.6, dt)) { b.Mode = 0; b.ModeTime = 0; }
                break;
        }
        foreach (var name in new[] { "LEFT CANNON", "RIGHT CANNON" })
        {
            var part = b.Part(name)!;
            if (Ready(part, dt, 1.7)) Fan(b.Pos + part.Offset + new Vec(0, 0.8), aim, 3, 0.5, 7);
        }
        foreach (var name in new[] { "LEFT POD", "RIGHT POD" })
        {
            var part = b.Part(name)!;
            if (Ready(part, dt, 2.4)) Fan(b.Pos + part.Offset, aim, 1, 0, 5);
        }
        if (b.Exposed >= 1 && Ready(b.Part("HEAD"), dt, 0.9)) Fan(b.Pos + b.Part("HEAD")!.Offset, aim, 1, 0, 9, ShotKind.Shell);
        if (b.Exposed >= 2 && Ready(b.Part("TORSO"), dt, 2.6)) Ring(b.Pos + b.Part("TORSO")!.Offset, 12, 5, b.Age);
        if (tankOnly && Ready(b.Part("TREADS"), dt, 1.6)) Fan(b.Pos + new Vec(0, 2.6), aim, 5, 1.0, 6.5, ShotKind.Shell);
    }

    /// <summary>Where the cobra sways to: round and about the upper room, below the host's microphone.</summary>
    private static Vec Sway(double t) => new(Field.Width / 2 + 9.5 * Math.Sin(0.33 * t), 7.6 + 3.4 * Math.Sin(0.53 * t + 1));

    /// <summary>Sways round the room, its coils trailing after it, and strikes at whoever's nearest.</summary>
    private void Vipervision(Boss b, Vec aim, double dt)
    {
        double t = b.Age;
        switch (b.Mode)
        {
            case 4:
                if (Toward(b, BossHome(b.Kind), 4, dt)) { b.Mode = 0; b.ModeTime = 0; }
                break;
            case 0:
                Toward(b, Sway(t), 4.2, dt);
                if (b.ModeTime > 7) { b.Mode = 1; b.ModeTime = 0; }
                break;
            case 1:
                if (b.ModeTime > 0.9) { b.Mode = 2; b.ModeTime = 0; b.Target = aim; }
                break;
            case 2:
                var to = new Vec(Math.Clamp(b.Target.X, 3, Field.Width - 3), Math.Clamp(b.Target.Y, 3, Field.Height - 3));
                if (Toward(b, to, 10, dt) || b.ModeTime > 0.7) { b.Mode = 3; b.ModeTime = 0; }
                break;
            case 3:
                if (Toward(b, Sway(t), 5, dt) || b.ModeTime > 3) { b.Mode = 0; b.ModeTime = 0; }
                break;
        }
        b.Trail.Insert(0, b.Pos);
        int keep = (Boss.Coils + 1) * Boss.CoilGap + 1;
        if (b.Trail.Count > keep) b.Trail.RemoveRange(keep, b.Trail.Count - keep);
        if (b.Mode is 1 or 2) return;
        foreach (var name in new[] { "LEFT HOOD", "RIGHT HOOD" })
        {
            var part = b.Part(name)!;
            if (Ready(part, dt, 2.2)) Fan(b.Pos + part.Offset, aim, 5, 1.0, 5.5);
        }
        if (b.Exposed >= 1)
            foreach (var name in new[] { "LEFT FANG", "RIGHT FANG" })
            {
                var part = b.Part(name)!;
                if (Ready(part, dt, 1.3)) Fan(b.Pos + part.Offset, aim, 3, 0.2, 8, ShotKind.Shell);
            }
        if (b.Exposed >= 2 && Ready(b.Part("SCREEN"), dt, 2.4))
        {
            Ring(b.Pos, 14, 5, b.Age);
            if (Enemies.Count(e => e.Kind == EnemyKind.Snake) < 3) AddEnemy(EnemyKind.Snake, b.Pos + new Vec(0, 2));
        }
    }

    /// <summary>The host on his flying throne: drifts to and fro, swoops, and throws shrapnel bombs.</summary>
    private void Monty(Boss b, Vec aim, double dt)
    {
        var home = BossHome(b.Kind);
        switch (b.Mode)
        {
            case 4:
                if (Toward(b, home, 3, dt)) { b.Mode = 0; b.ModeTime = 0; }
                return;
            case 0:
                Toward(b, new Vec(Field.Width / 2 + 8 * Math.Sin(0.4 * b.Age), home.Y + 0.6 * Math.Sin(1.3 * b.Age)), 3.5, dt);
                if (b.ModeTime > 9) { b.Mode = 2; b.ModeTime = 0; b.Target = new Vec(Math.Clamp(aim.X, 5, Field.Width - 5), Field.Height - 5); }
                break;
            case 2:
                if (Toward(b, b.Target, 5.5, dt)) { b.Mode = 3; b.ModeTime = 0; }
                break;
            default:
                if (Toward(b, home, 3, dt)) { b.Mode = 0; b.ModeTime = 0; }
                break;
        }
        for (int i = 1; i <= 4; i++)
        {
            var part = b.Part($"TURRET {i}")!;
            if (Ready(part, dt, 1.5)) Fan(b.Pos + part.Offset, aim, 1, 0, 8, ShotKind.Shell);
        }
        if (b.Exposed >= 1 && Ready(b.Part("THRONE"), dt, 2.6))
        {
            var box = AddEnemy(EnemyKind.Shrapnel, b.Pos + new Vec(0, 2.6));
            box.Fuse = 2.5;
            Ring(b.Pos + new Vec(0, 1), 10, 4.5, b.Age);
        }
        if (b.Exposed >= 2 && Ready(b.Part("MONTY"), dt, 1.8)) Fan(b.Pos + new Vec(0, -0.5), aim, 7, 1.4, 6);
    }
}
