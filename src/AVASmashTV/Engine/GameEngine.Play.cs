using AVASmashTV.Models;

namespace AVASmashTV.Engine;

public sealed partial class GameEngine
{
    public const double MapSeconds = 3.2;
    public const double ReadySeconds = 2.0;
    public const double DyingSeconds = 3;
    public const double TallySeconds = 10;
    public const double DownSeconds = 1.6;
    public const double InvulnerableSeconds = 2.5;
    public const double WalkSpeed = 6.2;
    public const double FastSpeed = 8.8;
    public const double ItemSeconds = 10;
    public const double WeaponSeconds = 12;
    public const double ShieldSeconds = 8;
    public const double SpeedSeconds = 15;
    /// <summary>How near the force field's spinning orbs cut.</summary>
    public const double ShieldReach = 1.55;
    public const double PrizeEvery = 3.2;
    public const double PowerEvery = 7.5;
    /// <summary>When a room's key turns up.</summary>
    public const double KeyAfter = 8;
    public const int MaxEnemies = 110;
    public const double GrenadeBlast = 2.3;
    public const double MineBlast = 1.9;

    /// <summary>Which circuit (0 to 2) and which of its rooms.</summary>
    public int CircuitIndex { get; private set; }
    public int RoomIndex { get; private set; }
    public Circuit Circuit => Show.Circuits[CircuitIndex];
    public RoomDef Room => Circuit.Rooms[RoomIndex];
    /// <summary>The rooms of this circuit so far, the one under way last.</summary>
    public List<int> Route { get; } = [];
    /// <summary>The team's keys to the Pleasure Dome.</summary>
    public int Keys { get; private set; }
    public int RoomsCleared { get; private set; }
    /// <summary>Time in this room since its doors opened.</summary>
    public double RoomTime { get; private set; }
    /// <summary>How hard this room is: the further into the show, the more of them, and the faster.</summary>
    public int Difficulty { get; private set; }

    public List<Enemy> Enemies { get; } = [];
    public List<Shot> Shots { get; } = [];
    public List<Item> Floor { get; } = [];
    public Boss? Boss { get; private set; }

    /// <summary>Each door's opening, seconds left (they slam shut after a wave is through).</summary>
    public double[] DoorOpen { get; } = new double[4];
    /// <summary>Where each door leads once the room is clear: a room of this circuit, <see cref="NextCircuit"/>, or -1 for shut.</summary>
    public int[] ExitTo { get; } = [-1, -1, -1, -1];
    public const int NextCircuit = -2;
    /// <summary>The room just left by this door (players come in through it).</summary>
    public Side? CameIn { get; private set; }

    /// <summary>How many still to come through the doors.</summary>
    public int ToCome => Math.Max(0, _budget - _spawned);

    private int _budget, _spawned;
    private double _spawnClock, _prizeClock, _powerClock, _endClock, _hitSound, _shootSound, _splatSound;
    private bool _keyDropped, _calm, _ending;

    public double Pace => 1 + 0.03 * Difficulty;

    // ----- rooms ----------------------------------------------------------------------------------

    private void EnterRoom(int circuit, int room, Side? cameIn = null)
    {
        CircuitIndex = circuit;
        RoomIndex = room;
        CameIn = cameIn;
        Difficulty = circuit * 5 + Depth(Circuit, room);
        Enemies.Clear();
        Shots.Clear();
        Floor.Clear();
        Boss = null;
        Array.Fill(DoorOpen, 0);
        Array.Fill(ExitTo, -1);
        RoomTime = 0;
        _budget = Room.Boss is null ? Math.Min(120, 26 + 7 * Difficulty) : 0;
        if (Room.Mix == "G" && Room.Boss is null) _budget = 22;
        _spawned = 0;
        _spawnClock = 0.4;
        _prizeClock = PrizeEvery * 0.6;
        _powerClock = PowerEvery * 0.5;
        _keyDropped = false;
        _ending = false;
        // In through the door they took; everyone gets a fresh start in the new room.
        int n = 0;
        foreach (var p in Players.Where(p => p.Active))
        {
            p.Pos = cameIn is { } s ? Field.Door(s) + Field.Inward(s) * 2.2 + Field.Inward(s).Rotate(Math.PI / 2) * (n++ == 0 ? -1.2 : 1.2) : StartSpot(p.Index);
            p.Down = false;
            if (p.Lives <= 0) p.Out = true;
        }
    }

    /// <summary>How many rooms from the circuit's first.</summary>
    private static int Depth(Circuit c, int room)
    {
        var depth = new int[c.Rooms.Length];
        Array.Fill(depth, -1);
        depth[0] = 0;
        var q = new Queue<int>([0]);
        while (q.Count > 0)
        {
            int at = q.Dequeue();
            foreach (var next in c.Rooms[at].Links.Where(l => depth[l] < 0))
            {
                depth[next] = depth[at] + 1;
                q.Enqueue(next);
            }
        }
        return Math.Max(0, depth[room]);
    }

    private void Simulate(double dt, Controls c1, Controls c2)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Map:
                if (PhaseTime < MapSeconds) return;
                SetPhase(Phase.Ready);
                var line = Room.Boss switch
                {
                    BossKind.Tankenstein => "MEET TANKENSTEIN! HE'S HAD A VERY BAD DAY!",
                    BossKind.Vipervision => "IT'S VIPERVISION! DO NOT ADJUST YOUR SET!",
                    BossKind.Monty => "WELCOME TO MY PLEASURE DOME! NOW... DIE!",
                    _ => Pick(RoomLines),
                };
                Say(line, 3, VoiceOf(line));
                Music(true);
                return;
            case Phase.Dying:
                if (PhaseTime >= DyingSeconds) StartTally("ALL OUT OF LIVES");
                return;
            case Phase.Tally:
                if (PhaseTime >= TallySeconds) GameOver();
                return;
        }

        if (_hitSound > 0) _hitSound -= dt;
        if (_shootSound > 0) _shootSound -= dt;
        if (_splatSound > 0) _splatSound -= dt;
        for (int i = 0; i < 4; i++) if (DoorOpen[i] > 0) DoorOpen[i] = Math.Max(0, DoorOpen[i] - dt);

        if (Phase == Phase.Ready && PhaseTime >= ReadySeconds) OpenRoom();
        if (Phase == Phase.Fighting) RoomTime += dt;

        MovePlayers(dt, c1, c2);
        if (Phase is not (Phase.Fighting or Phase.Cleared or Phase.Ready)) return;
        MoveShots(dt);
        if (Phase == Phase.Fighting && !_calm) RunWaves(dt);
        MoveEnemies(dt);
        if (Boss is not null) MoveBoss(Boss, dt);
        UpdateItems(dt);

        if (Phase == Phase.Fighting && Boss is null && Room.Boss is null && _spawned >= _budget && !Enemies.Any(e => e.Kind != EnemyKind.Mine))
            RoomClear();
        if (Phase == Phase.Cleared && _ending)
        {
            _endClock -= dt;
            if (_endClock <= 0) StartTally(Won ? "THE SHOW IS WON" : "THE PLEASURE DOME STAYED LOCKED");
        }
        if (Phase is Phase.Fighting or Phase.Cleared && Players.Any(p => p.Joined) && Players.Where(p => p.Joined).All(p => p.Out))
        {
            Music(false);
            Say("SHOW'S OVER, FOLKS!", 4, Sfx.SayShowsOver);
            SetPhase(Phase.Dying);
        }
    }

    /// <summary>The doors open: the wave begins (or the boss rolls in).</summary>
    private void OpenRoom()
    {
        SetPhase(Phase.Fighting);
        Music(true);
        if (_calm) return;
        // Mines laid on the floor, away from the contestants and the doors.
        int mines = Room.Mix.Count(ch => ch == 'M') * (2 + Difficulty / 3);
        for (int i = 0; i < mines; i++)
        {
            for (int t = 0; t < 30; t++)
            {
                var at = new Vec(2 + _rng.NextDouble() * (Field.Width - 4), 2 + _rng.NextDouble() * (Field.Height - 4));
                if (Players.Any(p => p.Active && (p.Pos - at).Length < 4) || Enemies.Any(e => (e.Pos - at).Length < 1.2)) continue;
                if (Enum.GetValues<Side>().Any(s => (Field.Door(s) - at).Length < 4)) continue;
                AddEnemy(EnemyKind.Mine, at);
                break;
            }
        }
        if (Room.Boss is { } kind) Boss = MakeBoss(kind, Players.Count(p => p.Active));
    }

    private void RoomClear()
    {
        SetPhase(Phase.Cleared);
        RoomsCleared++;
        foreach (var e in Enemies) Effects.Add(new Effect { Kind = EffectKind.Sparkle, Pos = e.Pos, Life = 0.5 });
        Enemies.Clear();
        Shots.RemoveAll(s => s.Hostile);
        Play(Sfx.Crowd);
        var line = Pick(ClearLines);
        Say(line, 3, VoiceOf(line));
        // Bonus prizes, and confetti.
        for (int i = 0; i < 4; i++) Drop(PrizeKind(), Field.Centre + Vec.FromAngle(i * Math.PI / 2 + 0.6) * 3.2);
        for (int i = 0; i < 14; i++)
            Effects.Add(new Effect { Kind = EffectKind.Confetti, Pos = new Vec(_rng.NextDouble() * Field.Width, _rng.NextDouble() * 4), Vel = new Vec(_rng.NextDouble() - 0.5, 2 + _rng.NextDouble() * 2), Life = 3, Seed = _rng.Next() });

        if (Room.Boss is not null)
        {
            if (Room.IsDome)
            {
                Won = true;
                _ending = true;
                _endClock = 6;
                Say("YOU... YOU BEAT THE SHOW! TAKE THE PRIZES! TAKE THEM ALL!", 6, Sfx.SayLoveIt);
                Play(Sfx.Applause);
                return;
            }
            ExitTo[(int)Side.North] = NextCircuit;
            DoorOpen[(int)Side.North] = 1;
            Play(Sfx.Door);
            return;
        }
        foreach (int next in Room.Links)
        {
            if (Circuit.Rooms[next].IsDome && Keys < Show.KeysNeeded)
            {
                _ending = true;
                _endClock = 5;
                Say($"ONLY {Keys} KEYS? NO KEYS, NO PLEASURE DOME! IT TAKES {Show.KeysNeeded}!", 5);
                continue;
            }
            var side = Circuit.Towards(RoomIndex, next);
            ExitTo[(int)side] = next;
            DoorOpen[(int)side] = 1;
        }
        if (ExitTo.Any(x => x != -1)) Play(Sfx.Door);
    }

    /// <summary>Out through an exit door: the map, then the room it leads to.</summary>
    private void LeaveRoom(Side side)
    {
        int to = ExitTo[(int)side];
        Side opposite = (Side)(((int)side + 2) % 4);
        Play(Sfx.Door);
        if (to == NextCircuit)
        {
            EnterRoom(CircuitIndex + 1, 0);
            Route.Clear();
            Route.Add(0);
            Say($"CIRCUIT {CircuitIndex + 1}: {Circuit.Name}!", 3);
        }
        else
        {
            EnterRoom(CircuitIndex, to, opposite);
            Route.Add(to);
            if (Room.IsDome) Say("YOU HAVE THE KEYS! THE PLEASURE DOME IS OPEN!", 3);
        }
        SetPhase(Phase.Map);
    }

    private void StartTally(string why)
    {
        Ending = why;
        Music(false);
        Play(Sfx.Applause);
        if (Won) Play(Sfx.Crowd);
        SetPhase(Phase.Tally);
    }

    // ----- the waves ------------------------------------------------------------------------------

    private void RunWaves(double dt)
    {
        _spawnClock -= dt;
        if (Room.Boss is not null || _spawned >= _budget || _spawnClock > 0) return;
        _spawnClock = Math.Max(0.65, 1.8 - 0.07 * Difficulty) * (0.7 + _rng.NextDouble() * 0.6);
        var mix = Room.Mix.Where(ch => ch != 'M').ToArray();
        var kind = Show.KindOf(mix[_rng.Next(mix.Length)]);
        int count = kind switch
        {
            EnemyKind.Grunt => 3 + _rng.Next(4) + Difficulty / 3,
            EnemyKind.Orb => 2 + _rng.Next(2),
            EnemyKind.Blade => 1 + _rng.Next(2),
            EnemyKind.Shrapnel => 1 + _rng.Next(2),
            _ => 1,
        };
        count = Math.Min(count, Math.Min(_budget - _spawned, MaxEnemies - Enemies.Count));
        if (count <= 0) return;
        Burst(kind, (Side)_rng.Next(4), count);
        _spawned += count;
    }

    /// <summary>A door bangs open and a line of them files in through it.</summary>
    private void Burst(EnemyKind kind, Side side, int count)
    {
        if (DoorOpen[(int)side] <= 0) Play(Sfx.Door);
        DoorOpen[(int)side] = Math.Max(DoorOpen[(int)side], 1.2 + count * 0.15);
        var inward = Field.Inward(side);
        var across = inward.Rotate(Math.PI / 2);
        for (int i = 0; i < count; i++)
        {
            double lateral = (i % 3 - 1) * 0.9 + (_rng.NextDouble() - 0.5) * 0.3;
            var at = Field.Door(side) - inward * (0.6 + i / 3 * 0.9) + across * lateral;
            var e = AddEnemy(kind, at);
            e.Entering = 1;
            e.Vel = inward;
        }
    }

    // ----- the contestants ------------------------------------------------------------------------

    private void MovePlayers(double dt, Controls c1, Controls c2)
    {
        for (int i = 0; i < Players.Length; i++)
        {
            var p = Players[i];
            if (!p.Active) continue;
            if (MovePlayer(p, i == 0 ? c1 : c2, dt)) return;
        }
    }

    /// <summary>One contestant's step; true if they've walked out of the room.</summary>
    private bool MovePlayer(Player p, Controls c, double dt)
    {
        if (p.Down)
        {
            p.DownTime -= dt;
            if (p.DownTime > 0) return false;
            p.Down = false;
            if (p.Lives <= 0)
            {
                p.Out = true;
                return false;
            }
            p.Invulnerable = InvulnerableSeconds;
            return false;
        }
        if (p.Invulnerable > 0) p.Invulnerable -= dt;
        if (p.ShieldTime > 0) p.ShieldTime -= dt;
        if (p.SpeedTime > 0) p.SpeedTime -= dt;
        if (p.WeaponTime > 0)
        {
            p.WeaponTime -= dt;
            if (p.WeaponTime <= 0) p.Weapon = Weapon.Gun;
        }

        // The move stick: eight ways.
        var move = new Vec(Math.Sign(c.MoveX), Math.Sign(c.MoveY)).Normal;
        if (move.Length > 0)
        {
            var to = p.Pos + move * (p.Speed * dt);
            p.Pos = Confine(to);
            p.Walk += dt;
            if (Phase == Phase.Cleared)
                foreach (var s in Enum.GetValues<Side>())
                {
                    if (ExitTo[(int)s] == -1 || !Field.InDoorway(s, p.Pos) || Beyond(s, p.Pos) < 0.25) continue;
                    LeaveRoom(s);
                    return true;
                }
        }

        // The fire stick, on its own: the gun points (and the body turns) the way it's held.
        p.Firing = c.Firing;
        if (c.Firing)
        {
            p.Aim = new Vec(Math.Sign(c.FireX), Math.Sign(c.FireY)).Normal;
            p.Facing = p.Aim;
        }
        else if (move.Length > 0) p.Facing = p.Aim = move;
        p.Reload -= dt;
        if (c.Firing && p.Reload <= 0 && Phase != Phase.Map) Fire(p);

        // Whatever's underfoot.
        foreach (var item in Floor)
            if (!item.Taken && (item.Pos - p.Pos).Length < 0.85) Take(p, item);
        Floor.RemoveAll(i => i.Taken);
        return false;
    }

    /// <summary>How far past its wall a point is, in a door's direction.</summary>
    private static double Beyond(Side s, Vec p) => s switch
    {
        Side.North => -p.Y,
        Side.South => p.Y - Field.Height,
        Side.West => -p.X,
        _ => p.X - Field.Width,
    };

    /// <summary>Kept inside the walls, except out through an open exit door.</summary>
    private Vec Confine(Vec p)
    {
        double r = Player.Radius;
        foreach (var s in Enum.GetValues<Side>())
        {
            // Into an open exit's doorway (only right at its wall), and out through it.
            if (Phase != Phase.Cleared || ExitTo[(int)s] == -1 || !Field.InDoorway(s, p) || Beyond(s, p) <= -r) continue;
            return s is Side.North or Side.South
                ? new Vec(Math.Clamp(p.X, Field.Width / 2 - Field.DoorWidth / 2 + r, Field.Width / 2 + Field.DoorWidth / 2 - r), Math.Clamp(p.Y, -1, Field.Height + 1))
                : new Vec(Math.Clamp(p.X, -1, Field.Width + 1), Math.Clamp(p.Y, Field.Height / 2 - Field.DoorWidth / 2 + r, Field.Height / 2 + Field.DoorWidth / 2 - r));
        }
        return new Vec(Math.Clamp(p.X, r, Field.Width - r), Math.Clamp(p.Y, r, Field.Height - r));
    }

    private void Fire(Player p)
    {
        // Out of the gun (the first step sweeps from the middle of the contestant, so nothing touching is missed).
        var muzzle = p.Pos + p.Aim * 0.2;
        switch (p.Weapon)
        {
            case Weapon.Spread:
                p.Reload = 0.11;
                foreach (double a in new[] { -0.24, 0, 0.24 })
                    Shots.Add(new Shot { Kind = ShotKind.Bullet, Pos = muzzle, Vel = p.Aim.Rotate(a) * 19, Owner = p.Index });
                Gunfire(Sfx.Spread);
                break;
            case Weapon.Rockets:
                p.Reload = 0.24;
                Shots.Add(new Shot { Kind = ShotKind.Rocket, Pos = muzzle, Vel = p.Aim * 15, Damage = 4, Owner = p.Index });
                Play(Sfx.Rocket);
                break;
            case Weapon.Grenades:
                p.Reload = 0.42;
                Shots.Add(new Shot { Kind = ShotKind.Grenade, Pos = muzzle, Vel = p.Aim * 11, Damage = 6, Owner = p.Index, Life = 0.5 });
                Play(Sfx.Grenade);
                break;
            default:
                p.Reload = 0.09;
                Shots.Add(new Shot { Kind = ShotKind.Bullet, Pos = muzzle, Vel = p.Aim * 20, Owner = p.Index });
                Gunfire(Sfx.Shoot);
                break;
        }
    }

    /// <summary>The machine gun's rattle, at most every other shot.</summary>
    private void Gunfire(Sfx s)
    {
        if (_shootSound > 0) return;
        _shootSound = 0.15;
        Play(s);
    }

    /// <summary>Hit: down for a moment, a life gone, the power-ups lost.</summary>
    private void KillPlayer(Player p)
    {
        if (!p.Alive || p.Safe) return;
        p.Down = true;
        p.DownTime = DownSeconds;
        p.Lives--;
        p.Weapon = Weapon.Gun;
        p.WeaponTime = p.SpeedTime = 0;
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = p.Pos, Life = 0.7, Size = 1.4, Colour = p.Colour });
        Effects.Add(new Effect { Kind = EffectKind.Splat, Pos = p.Pos, Life = DownSeconds, Size = 1.1, Colour = p.Colour, Seed = _rng.Next() });
        Play(Sfx.PlayerDie);
        Say(p.Lives > 0 ? Pick(DeathLines) : $"{p.Name} IS OUT OF THE SHOW!", 3);
        // Whatever was about to hit them is gone with them.
        Shots.RemoveAll(s => s.Hostile && (s.Pos - p.Pos).Length < 3);
    }

    // ----- shots ----------------------------------------------------------------------------------

    private void MoveShots(double dt)
    {
        for (int i = 0; i < Shots.Count; i++)
        {
            var s = Shots[i];
            if (s.Dead) continue;
            s.Age += dt;
            var from = s.Pos;
            s.Pos += s.Vel * dt;
            bool outside = s.Pos.X < 0 || s.Pos.Y < 0 || s.Pos.X > Field.Width || s.Pos.Y > Field.Height;
            if (outside || s.Age > s.Life)
            {
                s.Dead = true;
                if (s.Kind == ShotKind.Grenade) Explode(s.Pos, GrenadeBlast, s.Damage, s.Owner);
                continue;
            }
            if (s.Hostile)
            {
                foreach (var p in Players)
                {
                    if (!p.Alive) continue;
                    double d = (p.Pos - s.Pos).Length;
                    if (p.ShieldTime > 0 && d < ShieldReach + s.Radius)
                    {
                        s.Dead = true;
                        Effects.Add(new Effect { Kind = EffectKind.Sparkle, Pos = s.Pos, Life = 0.25 });
                        break;
                    }
                    if (d < Player.Radius + s.Radius - 0.05)
                    {
                        s.Dead = true;
                        KillPlayer(p);
                        break;
                    }
                }
                continue;
            }
            if (Boss is { Dead: false } boss && (ShootBoss(boss, s, (from + s.Pos) * 0.5) || ShootBoss(boss, s, s.Pos))) continue;
            foreach (var e in Enemies)
            {
                if (e.Dead || !Touches(e, from, s.Pos, s.Radius)) continue;
                s.Dead = true;
                if (s.Kind == ShotKind.Grenade) Explode(s.Pos, GrenadeBlast, s.Damage, s.Owner);
                else
                {
                    Damage(e, s.Damage, s.Owner);
                    if (s.Kind == ShotKind.Rocket) Explode(s.Pos, 1.2, 2, s.Owner, small: true);
                }
                break;
            }
        }
        Shots.RemoveAll(s => s.Dead);
        Enemies.RemoveAll(e => e.Dead);
    }

    /// <summary>A circle at p touches the enemy (or, for a snake, any of its segments).</summary>
    private static bool Touches(Enemy e, Vec p, double r) => Touches(e, p, p, r);

    /// <summary>A circle swept from a to b touches the enemy (or, for a snake, any of its segments).</summary>
    private static bool Touches(Enemy e, Vec a, Vec b, double r)
    {
        if (Gap(e.Pos, a, b) < e.Radius + r) return true;
        return e.Kind == EnemyKind.Snake && e.SegmentPositions().Any(q => Gap(q, a, b) < 0.36 + r);
    }

    /// <summary>How near the line from a to b comes to p.</summary>
    private static double Gap(Vec p, Vec a, Vec b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    /// <summary>A blast: everything of theirs near it hurt (the contestants' own blasts never hurt them).</summary>
    private void Explode(Vec at, double radius, int damage, int owner, bool small = false)
    {
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = at, Life = small ? 0.35 : 0.6, Size = radius });
        if (!small) Play(Sfx.Explosion);
        foreach (var e in Enemies.ToList())
            if (!e.Dead && (e.Pos - at).Length < radius + e.Radius)
                for (int i = 0; i < (e.Kind == EnemyKind.Snake ? damage : 1) && !e.Dead; i++) Damage(e, e.Kind == EnemyKind.Snake ? 1 : damage, owner);
        if (Boss is { Dead: false } b)
            foreach (var part in b.Parts.Where(p => !p.Gone && p.Layer == b.Exposed && (b.Pos + p.Offset - at).Length < radius + Math.Max(p.Size.X, p.Size.Y)).ToList())
                HitPart(b, part, damage, owner);
    }

    // ----- prizes and power-ups -------------------------------------------------------------------

    private ItemKind PrizeKind()
    {
        int c = CircuitIndex;
        (ItemKind, int)[] weights = [(ItemKind.Cash, 30), (ItemKind.Toaster, 18), (ItemKind.Vcr, 14), (ItemKind.Tv, 12), (ItemKind.Gold, 7 + c * 3), (ItemKind.Car, 2 + c * 2)];
        return Weighted(weights);
    }

    private ItemKind PowerKind() =>
        Weighted([(ItemKind.Spread, 24), (ItemKind.Rockets, 18), (ItemKind.Grenades, 15), (ItemKind.Shield, 14), (ItemKind.Speed, 16), (ItemKind.ExtraLife, 5)]);

    private ItemKind Weighted((ItemKind Kind, int Weight)[] table)
    {
        int roll = _rng.Next(table.Sum(t => t.Weight));
        foreach (var (kind, weight) in table)
        {
            if (roll < weight) return kind;
            roll -= weight;
        }
        return table[0].Kind;
    }

    private Vec FreeSpot()
    {
        for (int t = 0; ; t++)
        {
            var at = new Vec(2.5 + _rng.NextDouble() * (Field.Width - 5), 2.5 + _rng.NextDouble() * (Field.Height - 5));
            if (t > 20 || (!Players.Any(p => p.Active && (p.Pos - at).Length < 2) && !Floor.Any(i => (i.Pos - at).Length < 1.5))) return at;
        }
    }

    private Item Drop(ItemKind kind, Vec at)
    {
        var item = new Item { Kind = kind, Pos = at, Life = kind == ItemKind.Key ? double.PositiveInfinity : ItemSeconds };
        Floor.Add(item);
        Effects.Add(new Effect { Kind = EffectKind.Sparkle, Pos = at, Life = 0.5 });
        return item;
    }

    private void UpdateItems(double dt)
    {
        foreach (var i in Floor)
        {
            i.Age += dt;
            i.Life -= dt;
        }
        Floor.RemoveAll(i => i.Life <= 0);
        if (Phase != Phase.Fighting || _calm) return;
        _prizeClock -= dt;
        if (_prizeClock <= 0)
        {
            _prizeClock = PrizeEvery * (0.7 + _rng.NextDouble() * 0.6);
            Drop(PrizeKind(), FreeSpot());
        }
        _powerClock -= dt;
        if (_powerClock <= 0)
        {
            _powerClock = PowerEvery * (0.7 + _rng.NextDouble() * 0.6);
            Drop(PowerKind(), FreeSpot());
        }
        if (Room.Key && !_keyDropped && RoomTime >= KeyAfter)
        {
            _keyDropped = true;
            Drop(ItemKind.Key, FreeSpot());
        }
    }

    private void Take(Player p, Item item)
    {
        item.Taken = true;
        var k = item.Kind;
        if (k.IsPrize())
        {
            AddScore(p.Index, k.Value(), cash: true);
            p.Prizes[k] = p.Prizes.GetValueOrDefault(k) + 1;
            Popup(item.Pos, $"${k.Value():N0}", 0xFF7CFF6A);
            Play(Sfx.Prize);
            if (PrizeLine(k) is { } line && (k == ItemKind.Car || _rng.Next(3) == 0)) Say(line, 2.5);
            return;
        }
        if (k == ItemKind.Key)
        {
            Keys++;
            AddScore(p.Index, k.Value());
            p.Prizes[k] = p.Prizes.GetValueOrDefault(k) + 1;
            Popup(item.Pos, $"KEY {Keys}", 0xFFFFE14A, 1.5);
            Play(Sfx.Key);
            Say(Keys >= Show.KeysNeeded ? $"THAT'S {Keys} KEYS! THE PLEASURE DOME AWAITS!" : $"A KEY TO THE PLEASURE DOME! {Keys} OF {Show.KeysNeeded}!", 3);
            return;
        }
        AddScore(p.Index, k.Value());
        switch (k)
        {
            case ItemKind.Spread: p.Weapon = Weapon.Spread; p.WeaponTime = WeaponSeconds; break;
            case ItemKind.Rockets: p.Weapon = Weapon.Rockets; p.WeaponTime = WeaponSeconds; break;
            case ItemKind.Grenades: p.Weapon = Weapon.Grenades; p.WeaponTime = WeaponSeconds; break;
            case ItemKind.Shield: p.ShieldTime = ShieldSeconds; break;
            case ItemKind.Speed: p.SpeedTime = SpeedSeconds; break;
            default:
                p.Lives++;
                Play(Sfx.OneUp);
                break;
        }
        Popup(item.Pos, k.Title(), 0xFF4FD7FF, 1.2);
        Play(Sfx.PowerUp);
        Say(PowerLine(k), 2.5);
    }

    // ----- for the tests and screenshots ----------------------------------------------------------

    internal void Idle(int steps)
    {
        for (int i = 0; i < steps; i++) Tick(default);
    }

    /// <summary>Straight to the fighting, the doors open.</summary>
    internal void Go()
    {
        if (Phase is Phase.Map) SetPhase(Phase.Ready);
        if (Phase is Phase.Ready) OpenRoom();
    }

    internal void JumpTo(int circuit, int room)
    {
        EnterRoom(circuit, room);
        Route.Clear();
        Route.Add(room);
        SetPhase(Phase.Map);
        Go();
    }

    /// <summary>No waves, prizes or power-ups, unless put there.</summary>
    internal void Calm()
    {
        _calm = true;
        Enemies.Clear();
        Floor.Clear();
        Shots.Clear();
        Boss = null;
        _budget = _spawned = 0;
    }

    /// <summary>The waves still to come.</summary>
    internal void SetWave(int budget, int spawned = 0)
    {
        _budget = budget;
        _spawned = spawned;
    }

    internal void Uncalm() => _calm = false;

    internal void SetKeys(int keys) => Keys = keys;

    internal Enemy PutEnemy(EnemyKind kind, Vec at) => AddEnemy(kind, at);

    internal Item PutItem(ItemKind kind, Vec at) => Drop(kind, at);

    internal Boss PutBoss(BossKind kind)
    {
        Boss = MakeBoss(kind, Players.Count(p => p.Active));
        Boss.Mode = 0;
        Boss.Pos = BossHome(kind);
        return Boss;
    }

    internal void ClearRoom() => RoomClear();

    internal void SpawnBurst(EnemyKind kind, Side side, int count) => Burst(kind, side, count);
}
