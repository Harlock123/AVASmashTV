using AVASmashTV.Engine;
using AVASmashTV.Models;

namespace AVASmashTV.Tests;

public sealed class GameplayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smashtv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    /// <summary>A room under way with nothing in it (no waves, no prizes) unless put there; player 1 in the middle.</summary>
    private (GameEngine G, Player P) Arena(int circuit = 0, int room = 1, int players = 1, int seed = 1)
    {
        var g = new GameEngine(seed, new HighScoreTable(_dir));
        g.StartGame(players);
        g.Calm();
        g.JumpTo(circuit, room);
        var p = g.Players[0];
        p.Pos = Field.Centre;
        return (g, p);
    }

    private static List<Sfx> Listen(GameEngine g)
    {
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        return heard;
    }

    private static void Steps(GameEngine g, int n, Controls c = default, Controls c2 = default)
    {
        for (int i = 0; i < n; i++) g.Tick(c, c2);
    }

    private static Vec V(double x, double y) => new(x, y);

    // ----- twin sticks ----------------------------------------------------------------------------

    [Fact]
    public void Moving_and_firing_are_independent()
    {
        var (g, p) = Arena();
        var start = p.Pos;
        Steps(g, 30, new Controls(1, 0, 0, -1));
        Assert.InRange(p.Pos.X - start.X, 2.8, 3.3);
        Assert.Equal(start.Y, p.Pos.Y, 6);
        Assert.Equal(V(0, -1), p.Aim);
        var shots = g.Shots.Where(s => !s.Hostile).ToList();
        Assert.True(shots.Count >= 4);
        Assert.All(shots, s => { Assert.Equal(0, s.Vel.X, 6); Assert.True(s.Vel.Y < 0); });

        // Walking down while shooting left.
        g.Shots.Clear();
        var mid = p.Pos;
        Steps(g, 20, new Controls(0, 1, -1, 0));
        Assert.True(p.Pos.Y > mid.Y + 1.5);
        Assert.Equal(mid.X, p.Pos.X, 6);
        Assert.All(g.Shots, s => { Assert.True(s.Vel.X < 0); Assert.Equal(0, s.Vel.Y, 6); });
        Assert.Equal(V(-1, 0), p.Facing);
    }

    [Fact]
    public void The_fire_stick_shoots_eight_ways()
    {
        foreach (var (fx, fy) in new[] { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) })
        {
            var (g, _) = Arena();
            g.Tick(new Controls(0, 0, fx, fy));
            var shot = Assert.Single(g.Shots);
            var want = V(fx, fy).Normal;
            Assert.Equal(want.X, shot.Vel.Normal.X, 6);
            Assert.Equal(want.Y, shot.Vel.Normal.Y, 6);
        }
    }

    [Fact]
    public void Point_blank_shots_hit_whatever_is_touching_you()
    {
        foreach (var (fx, fy) in new[] { (1, 0), (-1, -1), (0, 1) })
        {
            var (g, p) = Arena();
            p.Invulnerable = 999;
            var grunt = g.PutEnemy(EnemyKind.Grunt, p.Pos + V(fx, fy).Normal * 0.05);
            g.Tick(new Controls(0, 0, fx, fy));
            Assert.True(grunt.Dead, $"firing {fx},{fy}");
        }
    }

    [Fact]
    public void Fast_shots_do_not_pass_through_thin_things()
    {
        // A rocket's step is wider than a bullet-sized gap, but its whole path is checked.
        var (g, p) = Arena();
        var bomb = g.PutEnemy(EnemyKind.Mine, p.Pos + V(5, 0));
        for (int i = 0; i < 30 && !bomb.Dead; i++) g.Tick(new Controls(0, 0, 1, 0));
        Assert.True(bomb.Dead);
    }

    [Fact]
    public void Walking_alone_turns_the_gun_but_never_fires()
    {
        var (g, p) = Arena();
        Steps(g, 20, new Controls(1, 1));
        Assert.Empty(g.Shots);
        Assert.Equal(V(1, 1).Normal, p.Aim);
        Assert.False(p.Firing);
    }

    [Fact]
    public void The_walls_and_shut_doors_hold_everyone_in()
    {
        var (g, p) = Arena();
        Steps(g, 300, new Controls(-1, -1));
        Assert.Equal(V(Player.Radius, Player.Radius), p.Pos);
        // Even in line with a door: it's shut while they're still coming.
        p.Pos = V(Field.Width / 2, 3);
        Steps(g, 120, new Controls(0, -1));
        Assert.Equal(Player.Radius, p.Pos.Y, 6);
        Assert.Equal(Phase.Fighting, g.Phase);
        // Shots stop at the walls.
        Steps(g, 60, new Controls(0, 0, 0, -1));
        Assert.All(g.Shots, s => Assert.InRange(s.Pos.Y, 0, Field.Height));
    }

    // ----- the enemies ----------------------------------------------------------------------------

    [Fact]
    public void Grunts_mob_the_nearest_contestant_and_one_touch_costs_a_life()
    {
        var (g, p) = Arena();
        var heard = Listen(g);
        var grunt = g.PutEnemy(EnemyKind.Grunt, p.Pos + V(6, 0));
        int lives = p.Lives;
        for (int i = 0; i < 600 && !p.Down; i++) g.Tick(default);
        Assert.True(p.Down);
        Assert.Equal(lives - 1, p.Lives);
        Assert.Contains(Sfx.PlayerDie, heard);
        Assert.True((grunt.Pos - p.Pos).Length < 1.2);
        // Back up after a moment, blinking, and safe while it lasts.
        Steps(g, (int)(60 * GameEngine.DownSeconds) + 2);
        Assert.False(p.Down);
        Assert.True(p.Invulnerable > 0);
        grunt.Pos = p.Pos;
        Steps(g, 60);
        Assert.Equal(lives - 1, p.Lives);
        // ...and then it isn't.
        Steps(g, (int)(60 * GameEngine.InvulnerableSeconds));
        Assert.Equal(lives - 2, p.Lives);
    }

    [Fact]
    public void Orbs_bounce_off_the_walls_and_take_three_hits()
    {
        var (g, p) = Arena();
        p.Pos = V(Field.Width - 3, Field.Height - 3);
        var orb = g.PutEnemy(EnemyKind.Orb, V(0.8, 5));
        orb.Vel = V(-3, 0);
        Steps(g, 12);
        Assert.True(orb.Vel.X > 0, $"bounced: {orb.Vel}");
        Assert.InRange(orb.Pos.X, orb.Radius, Field.Width);

        (g, p) = Arena();
        var heard = Listen(g);
        orb = g.PutEnemy(EnemyKind.Orb, p.Pos + V(6, 0));
        Assert.Equal(3, orb.Health);
        for (int i = 0; i < 120 && !orb.Dead; i++) { orb.Pos = p.Pos + V(6, 0); orb.Vel = V(0, 0.1); g.Tick(new Controls(0, 0, 1, 0)); }
        Assert.True(orb.Dead);
        Assert.Equal(GameEngine.Points(EnemyKind.Orb), p.Score);
        Assert.True(heard.Count(s => s == Sfx.Hit) >= 2);
    }

    [Fact]
    public void Snakes_lose_a_segment_a_hit_and_the_head_goes_last()
    {
        var (g, p) = Arena();
        var snake = g.PutEnemy(EnemyKind.Snake, p.Pos + V(7, 0));
        int segments = snake.Segments;
        Assert.True(segments >= GameEngine.SnakeSegments);
        int last = segments;
        for (int i = 0; i < 600 && !snake.Dead; i++)
        {
            snake.Pos = p.Pos + V(7, 0);
            snake.Vel = V(0, 1);
            g.Tick(new Controls(0, 0, 1, 0));
            Assert.True(snake.Segments <= last);
            Assert.True(!snake.Dead || last == 0, "the head only goes once the tail is gone");
            last = snake.Segments;
        }
        Assert.True(snake.Dead);
        Assert.Equal(segments * 50 + GameEngine.Points(EnemyKind.Snake), p.Score);
    }

    [Fact]
    public void A_snake_slithers_with_its_segments_following()
    {
        var (g, p) = Arena();
        p.Invulnerable = 999;
        var snake = g.PutEnemy(EnemyKind.Snake, V(4, 4));
        snake.Vel = V(1, 0);
        Steps(g, 120);
        var segs = snake.SegmentPositions().ToList();
        Assert.Equal(snake.Segments, segs.Count);
        // Each segment a step behind the one before it.
        Assert.True((segs[0] - snake.Pos).Length > 0.3);
        for (int i = 1; i < segs.Count; i++) Assert.InRange((segs[i] - segs[i - 1]).Length, 0.3, 1.2);
    }

    [Fact]
    public void Blades_ricochet_round_the_room()
    {
        var (g, p) = Arena();
        p.Pos = V(2, Field.Height - 2);
        p.Invulnerable = 999;
        var blade = g.PutEnemy(EnemyKind.Blade, Field.Centre);
        blade.Vel = V(1, 1).Normal;
        int flipsX = 0, flipsY = 0;
        var was = blade.Vel;
        for (int i = 0; i < 360; i++)
        {
            g.Tick(default);
            p.Invulnerable = 999;
            if (Math.Sign(blade.Vel.X) != Math.Sign(was.X)) flipsX++;
            if (Math.Sign(blade.Vel.Y) != Math.Sign(was.Y)) flipsY++;
            was = blade.Vel;
            Assert.InRange(blade.Pos.X, blade.Radius - 1e-9, Field.Width - blade.Radius + 1e-9);
            Assert.InRange(blade.Pos.Y, blade.Radius - 1e-9, Field.Height - blade.Radius + 1e-9);
        }
        Assert.True(flipsX >= 1 && flipsY >= 1, $"{flipsX} {flipsY}");
        Assert.InRange(blade.Vel.Length, 5, 10);
    }

    [Fact]
    public void Tanks_swing_their_turrets_round_shell_you_and_take_a_beating()
    {
        var (g, p) = Arena();
        p.Invulnerable = 999;
        var tank = g.PutEnemy(EnemyKind.Tank, p.Pos + V(6, 0));
        tank.Reload = 0;
        tank.Turret = 0;
        for (int i = 0; i < 300 && !g.Shots.Any(s => s.Hostile); i++) g.Tick(default);
        var shell = Assert.Single(g.Shots, s => s.Hostile);
        Assert.Equal(ShotKind.Shell, shell.Kind);
        Assert.True(shell.Vel.X < 0, "aimed back at the contestant");

        (g, p) = Arena();
        tank = g.PutEnemy(EnemyKind.Tank, p.Pos + V(6, 0));
        tank.Reload = 999;
        Assert.Equal(12, tank.Health);
        int ticks = 0;
        for (; ticks < 600 && !tank.Dead; ticks++) { tank.Pos = p.Pos + V(6, 0); tank.Reload = 999; g.Tick(new Controls(0, 0, 1, 0)); }
        Assert.True(tank.Dead);
        Assert.True(ticks > 60, "it takes a dozen hits");
        Assert.Equal(GameEngine.Points(EnemyKind.Tank), p.Score);
    }

    [Fact]
    public void Mines_go_off_underfoot_and_take_the_mob_with_them()
    {
        // Step on one.
        var (g, p) = Arena();
        var heard = Listen(g);
        var mine = g.PutEnemy(EnemyKind.Mine, p.Pos + V(2, 0));
        Steps(g, 40, new Controls(1, 0));
        Assert.DoesNotContain(mine, g.Enemies);
        Assert.True(p.Down);
        Assert.Contains(Sfx.Explosion, heard);

        // Shoot one with a mob round it.
        (g, p) = Arena();
        mine = g.PutEnemy(EnemyKind.Mine, p.Pos + V(6, 0));
        var mob = new[] { V(6.6, 0.9), V(6.6, -0.9), V(7.4, 0) }.Select(o => (g.PutEnemy(EnemyKind.Grunt, p.Pos + o), o)).ToList();
        for (int i = 0; i < 60 && !mine.Dead; i++)
        {
            foreach (var (e, o) in mob) if (!e.Dead) e.Pos = p.Pos + o;
            g.Tick(new Controls(0, 0, 1, 0));
        }
        Assert.True(mine.Dead);
        Assert.All(mob, m => Assert.True(m.Item1.Dead));
        Assert.Equal(GameEngine.Points(EnemyKind.Mine) + 3 * GameEngine.Points(EnemyKind.Grunt), p.Score);

        // A grunt that walks onto one.
        (g, p) = Arena();
        p.Pos = V(5, Field.Height / 2);
        mine = g.PutEnemy(EnemyKind.Mine, V(10, Field.Height / 2));
        var grunt = g.PutEnemy(EnemyKind.Grunt, V(13, Field.Height / 2));
        Steps(g, 120);
        Assert.True(mine.Dead && grunt.Dead);
        Assert.Equal(0, p.Score);
        Assert.False(p.Down);
    }

    [Fact]
    public void Shrapnel_bombs_burst_when_shot_or_when_the_fuse_runs_out()
    {
        var (g, p) = Arena();
        p.Invulnerable = 999;
        var bomb = g.PutEnemy(EnemyKind.Shrapnel, p.Pos + V(6, 0));
        bomb.Fuse = 99;
        for (int i = 0; i < 120 && !bomb.Dead; i++) { bomb.Pos = p.Pos + V(6, 0); g.Tick(new Controls(0, 0, 1, 0)); }
        Assert.True(bomb.Dead);
        Assert.Equal(GameEngine.ShrapnelPieces, g.Shots.Count(s => s.Hostile && s.Kind == ShotKind.Shrapnel));
        Assert.Equal(GameEngine.Points(EnemyKind.Shrapnel), p.Score);

        (g, p) = Arena();
        bomb = g.PutEnemy(EnemyKind.Shrapnel, p.Pos + V(8, 4));
        bomb.Fuse = 0.1;
        Steps(g, 10);
        Assert.True(bomb.Dead);
        Assert.Equal(GameEngine.ShrapnelPieces, g.Shots.Count(s => s.Hostile));
        Assert.Equal(0, p.Score);
        // The pieces fly every way; one coming at you is a life.
        g.Shots.Clear();
        g.Shots.Add(new Shot { Kind = ShotKind.Shrapnel, Pos = p.Pos + V(2, 0), Vel = V(-7.5, 0), Hostile = true });
        Steps(g, 30);
        Assert.True(p.Down);
    }

    // ----- power-ups ------------------------------------------------------------------------------

    [Fact]
    public void The_spread_gun_fires_three_at_once_and_runs_out()
    {
        var (g, p) = Arena();
        var heard = Listen(g);
        g.PutItem(ItemKind.Spread, p.Pos);
        g.Tick(default);
        Assert.Equal(Weapon.Spread, p.Weapon);
        Assert.Contains(Sfx.PowerUp, heard);
        g.Tick(new Controls(0, 0, 0, -1));
        Assert.Equal(3, g.Shots.Count);
        Assert.Equal(3, g.Shots.Select(s => Math.Round(s.Vel.Angle, 3)).Distinct().Count());
        Steps(g, (int)(60 * GameEngine.WeaponSeconds) + 2);
        Assert.Equal(Weapon.Gun, p.Weapon);
        g.Shots.Clear();
        p.Reload = 0;
        g.Tick(new Controls(0, 0, 0, -1));
        Assert.Single(g.Shots);
    }

    [Fact]
    public void Rockets_hit_harder()
    {
        var (g, p) = Arena();
        var heard = Listen(g);
        g.PutItem(ItemKind.Rockets, p.Pos);
        g.Tick(default);
        Assert.Equal(Weapon.Rockets, p.Weapon);
        var tank = g.PutEnemy(EnemyKind.Tank, p.Pos + V(6, 0));
        tank.Reload = 999;
        for (int i = 0; i < 120 && tank.Health == 12; i++) { tank.Pos = p.Pos + V(6, 0); g.Tick(new Controls(0, 0, 1, 0)); }
        Assert.True(tank.Health <= 12 - 4, $"one rocket took it to {tank.Health}");
        Assert.Contains(Sfx.Rocket, heard);
    }

    [Fact]
    public void A_grenade_blows_a_crowd_apart()
    {
        var (g, p) = Arena();
        var heard = Listen(g);
        g.PutItem(ItemKind.Grenades, p.Pos);
        g.Tick(default);
        Assert.Equal(Weapon.Grenades, p.Weapon);
        var crowd = new List<(Enemy E, Vec At)>();
        for (int i = 0; i < 6; i++)
        {
            var at = p.Pos + V(5.4 + (i % 2) * 0.8, -1 + i / 2 * 1.0);
            crowd.Add((g.PutEnemy(EnemyKind.Grunt, at), at));
        }
        g.Tick(new Controls(0, 0, 1, 0));
        for (int i = 0; i < 50; i++)
        {
            foreach (var (e, at) in crowd) if (!e.Dead) e.Pos = at;
            g.Tick(default);
        }
        Assert.True(crowd.Count(c => c.E.Dead) >= 5, $"{crowd.Count(c => c.E.Dead)} went down");
        Assert.Contains(Sfx.Grenade, heard);
        Assert.Contains(Sfx.Explosion, heard);
    }

    [Fact]
    public void The_force_field_stops_shots_and_cuts_down_the_mob_until_it_runs_out()
    {
        var (g, p) = Arena();
        g.PutItem(ItemKind.Shield, p.Pos);
        g.Tick(default);
        Assert.True(p.ShieldTime > 0);
        g.Shots.Add(new Shot { Kind = ShotKind.Shell, Pos = p.Pos + V(4, 0), Vel = V(-6, 0), Hostile = true });
        var mob = Enumerable.Range(0, 3).Select(i => g.PutEnemy(EnemyKind.Grunt, p.Pos + Vec.FromAngle(i * 2.1) * 3)).ToList();
        Steps(g, 120);
        Assert.DoesNotContain(g.Shots, s => s.Hostile);
        Assert.All(mob, m => Assert.True(m.Dead));
        Assert.False(p.Down);
        Assert.Equal(GameEngine.StartingLives, p.Lives);
        Assert.Equal(3, p.Kills);
        Steps(g, (int)(60 * GameEngine.ShieldSeconds));
        Assert.True(p.ShieldTime <= 0);
        g.PutEnemy(EnemyKind.Grunt, p.Pos + V(1, 0));
        Steps(g, 30);
        Assert.True(p.Down);
    }

    [Fact]
    public void Speed_shoes_and_an_extra_life()
    {
        var (g, p) = Arena();
        var heard = Listen(g);
        var start = p.Pos;
        Steps(g, 30, new Controls(1, 0));
        double slow = p.Pos.X - start.X;
        g.PutItem(ItemKind.Speed, p.Pos);
        g.Tick(default);
        Assert.True(p.SpeedTime > 0);
        start = p.Pos;
        Steps(g, 30, new Controls(-1, 0));
        Assert.True(start.X - p.Pos.X > slow * 1.3, $"{start.X - p.Pos.X} against {slow}");

        int lives = p.Lives;
        g.PutItem(ItemKind.ExtraLife, p.Pos);
        g.Tick(default);
        Assert.Equal(lives + 1, p.Lives);
        Assert.Contains(Sfx.OneUp, heard);
        Assert.Equal("AN EXTRA LIFE! YOU'RE GONNA NEED IT!", g.Message);
    }

    [Fact]
    public void Dying_loses_the_power_ups()
    {
        var (g, p) = Arena();
        g.PutItem(ItemKind.Rockets, p.Pos);
        g.PutItem(ItemKind.Speed, p.Pos);
        g.Tick(default);
        Assert.Equal(Weapon.Rockets, p.Weapon);
        g.PutEnemy(EnemyKind.Grunt, p.Pos + V(0.6, 0));
        Steps(g, 10);
        Assert.True(p.Down);
        Assert.Equal(Weapon.Gun, p.Weapon);
        Assert.True(p.SpeedTime <= 0);
    }

    // ----- prizes ---------------------------------------------------------------------------------

    [Fact]
    public void Prizes_add_up_with_a_ding_and_are_counted_for_the_tally()
    {
        var (g, p) = Arena();
        var heard = Listen(g);
        var prizes = new[] { ItemKind.Cash, ItemKind.Toaster, ItemKind.Vcr, ItemKind.Tv, ItemKind.Gold, ItemKind.Car, ItemKind.Cash };
        foreach (var k in prizes)
        {
            g.PutItem(k, p.Pos);
            g.Tick(default);
        }
        Assert.Equal(prizes.Sum(k => k.Value()), p.Cash);
        Assert.Equal(p.Cash, p.Score);
        Assert.Equal(2, p.Prizes[ItemKind.Cash]);
        Assert.Equal(1, p.Prizes[ItemKind.Car]);
        Assert.Equal(prizes.Length, heard.Count(s => s == Sfx.Prize));
        Assert.True(ItemKind.Car.Value() > ItemKind.Gold.Value() && ItemKind.Gold.Value() > ItemKind.Toaster.Value());
    }

    [Fact]
    public void A_car_gets_the_host_excited()
    {
        var (g, p) = Arena();
        g.PutItem(ItemKind.Car, p.Pos);
        g.Tick(default);
        Assert.Equal("A BRAND NEW CAR!", g.Message);
    }

    [Fact]
    public void Prizes_and_power_ups_vanish_if_left_but_keys_wait()
    {
        var (g, _) = Arena();
        g.PutItem(ItemKind.Cash, V(3, 3));
        g.PutItem(ItemKind.Shield, V(25, 3));
        g.PutItem(ItemKind.Key, V(3, 16));
        Steps(g, (int)(60 * GameEngine.ItemSeconds) + 2);
        var left = Assert.Single(g.Floor);
        Assert.Equal(ItemKind.Key, left.Kind);
    }

    [Fact]
    public void Prizes_and_power_ups_turn_up_on_the_floor_as_the_room_goes_on()
    {
        var g = new GameEngine(4, new HighScoreTable(_dir));
        g.StartGame(1);
        g.Go();
        var p = g.Players[0];
        var seen = new HashSet<ItemKind>();
        for (int i = 0; i < 60 * 30 && g.Phase == Phase.Fighting; i++)
        {
            p.Invulnerable = 999;
            p.Pos = V(1, 1);
            g.Tick(default);
            foreach (var it in g.Floor) seen.Add(it.Kind);
        }
        Assert.Contains(seen, k => k.IsPrize());
        Assert.Contains(seen, k => k.IsPowerUp());
    }

    // ----- the waves and the doors ----------------------------------------------------------------

    [Fact]
    public void Waves_pour_in_through_doors_that_bang_open()
    {
        var g = new GameEngine(3, new HighScoreTable(_dir));
        var heard = Listen(g);
        g.StartGame(1);
        g.Go();
        g.Players[0].Invulnerable = 999;
        int toCome = g.ToCome;
        Assert.True(toCome > 0);
        for (int i = 0; i < 120 && g.Enemies.Count == 0; i++) g.Tick(default);
        Assert.NotEmpty(g.Enemies);
        Assert.Contains(Sfx.Door, heard);
        Assert.Contains(g.DoorOpen, d => d > 0);
        Assert.True(g.ToCome < toCome);
        // They start in the doorway, outside the room, and walk in.
        var first = g.Enemies[0];
        Assert.True(first.Entering > 0);
        var side = Enum.GetValues<Side>().Single(s => Field.InDoorway(s, first.Pos) && (Field.Door(s) - first.Pos).Length < 3);
        Assert.True(g.DoorOpen[(int)side] > 0);
        for (int i = 0; i < 120 && first.Entering > 0; i++) { g.Tick(default); g.Players[0].Invulnerable = 999; }
        Assert.Equal(0, first.Entering);
        Assert.InRange(first.Pos.X, 0, Field.Width);
        Assert.InRange(first.Pos.Y, 0, Field.Height);
        // The doors slam shut again behind them.
        for (int i = 0; i < 600 && g.DoorOpen.Any(d => d > 0); i++) { g.Tick(default); g.Players[0].Invulnerable = 999; g.Enemies.Clear(); g.SetWave(1, 1); }
        Assert.All(g.DoorOpen, d => Assert.Equal(0, d));
    }

    [Fact]
    public void The_room_clears_when_the_last_wave_is_down_and_its_exits_open()
    {
        var g = new GameEngine(3, new HighScoreTable(_dir));
        var heard = Listen(g);
        g.StartGame(1);
        g.Go();
        for (int i = 0; i < 60 * 120 && g.Phase == Phase.Fighting; i++)
        {
            g.Players[0].Invulnerable = 999;
            g.Tick(default);
            // Gone as soon as they're in.
            g.Enemies.RemoveAll(e => e.Entering == 0);
        }
        Assert.Equal(Phase.Cleared, g.Phase);
        Assert.Equal(0, g.ToCome);
        Assert.Equal(1, g.RoomsCleared);
        Assert.Contains(Sfx.Crowd, heard);
        Assert.NotNull(g.Message);
        // Bonus prizes in the middle, and the doors to the next rooms on the map.
        Assert.True(g.Floor.Count(i => i.Kind.IsPrize()) >= 4);
        Assert.Equal(1, g.ExitTo[(int)Side.East]);
        Assert.Equal(2, g.ExitTo[(int)Side.North]);
        Assert.Equal(-1, g.ExitTo[(int)Side.South]);
        Assert.Equal(-1, g.ExitTo[(int)Side.West]);
    }

    [Fact]
    public void Out_through_an_exit_the_map_shows_the_route_and_the_next_room_follows()
    {
        var (g, p) = Arena(0, 0);
        g.ClearRoom();
        // A shut door holds.
        p.Pos = V(Field.Width / 2, Field.Height - 2);
        Steps(g, 60, new Controls(0, 1));
        Assert.Equal(Phase.Cleared, g.Phase);
        // The east door leads on.
        p.Pos = V(Field.Width - 3, Field.Height / 2);
        for (int i = 0; i < 120 && g.Phase == Phase.Cleared; i++) g.Tick(new Controls(1, 0));
        Assert.Equal(Phase.Map, g.Phase);
        Assert.Equal(1, g.RoomIndex);
        Assert.Equal([0, 1], g.Route);
        Assert.Equal(Side.West, g.CameIn);
        Assert.True(p.Pos.X < 4, "in through the west door");
        Steps(g, (int)(60 * GameEngine.MapSeconds) + 2);
        Assert.Equal(Phase.Ready, g.Phase);
        Steps(g, (int)(60 * GameEngine.ReadySeconds) + 2);
        Assert.Equal(Phase.Fighting, g.Phase);
        Assert.Equal("CASH AND CARRY", g.Room.Name);
    }

    [Fact]
    public void Every_circuit_branches_and_every_route_reaches_its_boss()
    {
        int best = 0, worst = 0;
        foreach (var c in Show.Circuits)
        {
            Assert.Equal(c.Rooms.Length, c.Rooms.Select(r => (r.Col, r.Row)).Distinct().Count());
            Assert.All(c.Rooms, r => { Assert.InRange(r.Col, 0, c.Cols - 1); Assert.InRange(r.Row, 0, c.Rows - 1); });
            // Each link is through a door to the room beside it.
            for (int i = 0; i < c.Rooms.Length; i++)
                foreach (int next in c.Rooms[i].Links) c.Towards(i, next);
            var routes = c.Routes().ToList();
            Assert.True(routes.Count >= 4, $"circuit {c.Number} has {routes.Count} routes");
            Assert.All(routes, r => Assert.Equal(c.BossRoom, r[^1]));
            Assert.All(routes, r => Assert.InRange(r.Length, 5, 8));
            Assert.Single(c.Rooms, r => r.Boss is not null);
            Assert.Contains(c.Rooms[0].Links.Length, new[] { 2 });
            // The way you go decides how many keys you find.
            var keys = routes.Select(r => r.Count(i => c.Rooms[i].Key)).ToList();
            Assert.True(keys.Max() > keys.Min());
            best += keys.Max();
            worst += keys.Min();
        }
        Assert.True(best >= Show.KeysNeeded, "the Pleasure Dome can be reached");
        Assert.True(worst < Show.KeysNeeded, "but not by any old route");
        Assert.True(Show.Circuits[^1].Rooms[Show.Circuits[^1].BossRoom].IsDome);
    }

    // ----- the bosses -----------------------------------------------------------------------------

    /// <summary>A shot of a given strength right on a part of the boss.</summary>
    private static void Hit(GameEngine g, Boss b, BossPart part, int damage)
    {
        g.Shots.Add(new Shot { Kind = ShotKind.Bullet, Pos = b.Pos + part.Offset, Vel = V(0, 0.001), Owner = 0, Damage = damage });
        g.Tick(default);
        g.Shots.Clear();
        g.Players[0].Invulnerable = 999;
    }

    [Theory]
    [InlineData(BossKind.Tankenstein)]
    [InlineData(BossKind.Vipervision)]
    [InlineData(BossKind.Monty)]
    public void Bosses_come_apart_from_the_outside_in(BossKind kind)
    {
        int circuit = (int)kind;
        var (g, p) = Arena(circuit, Show.Circuits[circuit].BossRoom);
        p.Pos = V(3, Field.Height - 2);
        p.Invulnerable = 999;
        var heard = Listen(g);
        var b = g.PutBoss(kind);
        int layers = b.Parts.Max(x => x.Layer) + 1;
        Assert.True(layers >= 3);
        int score = 0;
        for (int layer = 0; layer < layers; layer++)
        {
            Assert.Equal(layer, b.Exposed);
            // The armour underneath shrugs shots off...
            foreach (var inner in b.Parts.Where(x => x.Layer > layer))
            {
                int before = inner.Health;
                Hit(g, b, inner, 1000);
                Assert.Equal(before, inner.Health);
            }
            if (layer < layers - 1) Assert.Contains(Sfx.Clank, heard);
            // ...while the outside comes off, piece by piece.
            foreach (var outer in b.Parts.Where(x => x.Layer == layer).ToList())
            {
                Hit(g, b, outer, outer.Health - 1);
                Assert.False(outer.Gone);
                Hit(g, b, outer, 1);
                Assert.True(outer.Gone, outer.Name);
                score += GameEngine.PartPoints;
            }
        }
        Assert.True(b.Dead);
        Assert.Equal(score + GameEngine.BossPoints, p.Score);
        Assert.Contains(Sfx.BossPart, heard);
        Assert.Contains(Sfx.BossHit, heard);
        Steps(g, (int)(60 * GameEngine.BossDeathSeconds) + 5);
        Assert.Null(g.Boss);
        Assert.Equal(Phase.Cleared, g.Phase);
        if (kind == BossKind.Monty) Assert.True(g.Won);
        else Assert.Equal(GameEngine.NextCircuit, g.ExitTo[(int)Side.North]);
    }

    [Fact]
    public void Tankenstein_loses_his_guns_then_his_head_then_his_body_then_his_tank()
    {
        var (g, p) = Arena(0, Show.Circuits[0].BossRoom);
        p.Invulnerable = 999;
        var b = g.PutBoss(BossKind.Tankenstein);
        Assert.Equal(["LEFT CANNON", "RIGHT CANNON", "LEFT POD", "RIGHT POD"], b.Parts.Where(x => x.Layer == 0).Select(x => x.Name));
        Assert.Equal("HEAD", Assert.Single(b.Parts, x => x.Layer == 1).Name);
        Assert.Equal("TORSO", Assert.Single(b.Parts, x => x.Layer == 2).Name);
        Assert.Equal("TREADS", Assert.Single(b.Parts, x => x.Layer == 3).Name);
        // His guns fire while he has them.
        for (int i = 0; i < 60 * 3; i++) { g.Tick(default); p.Invulnerable = 999; }
        Assert.Contains(g.Shots, s => s.Hostile);
        foreach (var gun in b.Parts.Where(x => x.Layer == 0)) Hit(g, b, gun, 999);
        Assert.Equal(1, b.Exposed);
        Assert.Equal("HIS GUNS ARE GONE! GO FOR THE HEAD!", g.Message);
        g.Shots.Clear();
        Steps(g, 60);
        Assert.DoesNotContain(g.Shots, s => s.Hostile && s.Kind == ShotKind.Fireball && (s.Pos - b.Pos).Length > 1);
    }

    [Fact]
    public void The_cobra_s_coils_are_armour_and_deadly_to_touch()
    {
        var (g, p) = Arena(1, Show.Circuits[1].BossRoom);
        p.Pos = V(2, Field.Height - 2);
        p.Invulnerable = 999;
        var b = g.PutBoss(BossKind.Vipervision);
        for (int i = 0; i < 60 * 4; i++) { g.Tick(default); p.Invulnerable = 999; }
        var coils = b.CoilPositions().ToList();
        Assert.True(coils.Count >= 8, $"{coils.Count} coils");
        var coil = coils[^1];
        var heard = Listen(g);
        g.Shots.Add(new Shot { Kind = ShotKind.Bullet, Pos = coil, Vel = V(0, 0.001), Owner = 0 });
        int health = b.Health;
        g.Tick(default);
        Assert.DoesNotContain(g.Shots, s => !s.Hostile);
        Assert.Equal(health, b.Health);
        Assert.Contains(Sfx.Clank, heard);
        p.Invulnerable = 0;
        p.Pos = b.CoilPositions().Last();
        g.Tick(default);
        Assert.True(p.Down);
    }

    // ----- keys and the Pleasure Dome -------------------------------------------------------------

    [Fact]
    public void Keys_turn_up_in_key_rooms_and_count_for_the_team()
    {
        var (g, p) = Arena(0, 2);
        Assert.True(g.Room.Key);
        g.Uncalm();
        g.SetWave(999);
        var heard = Listen(g);
        Item? key = null;
        for (int i = 0; i < 60 * 12 && key is null; i++)
        {
            p.Invulnerable = 999;
            g.Enemies.Clear();
            g.Tick(default);
            key = g.Floor.FirstOrDefault(x => x.Kind == ItemKind.Key);
        }
        Assert.NotNull(key);
        Assert.True(g.RoomTime >= GameEngine.KeyAfter);
        p.Pos = key.Pos;
        g.Tick(default);
        Assert.Equal(1, g.Keys);
        Assert.Contains(Sfx.Key, heard);
        Assert.Contains("PLEASURE DOME", g.Message);
        // Only one to a room.
        for (int i = 0; i < 60 * 10; i++) { p.Invulnerable = 999; g.Enemies.Clear(); g.Tick(default); }
        Assert.DoesNotContain(g.Floor, x => x.Kind == ItemKind.Key);
    }

    [Fact]
    public void With_the_keys_the_Pleasure_Dome_opens()
    {
        var (g, p) = Arena(2, 9);
        g.SetKeys(Show.KeysNeeded);
        g.ClearRoom();
        Assert.Equal(11, g.ExitTo[(int)Side.North]);
        p.Pos = V(Field.Width / 2, 2);
        for (int i = 0; i < 120 && g.Phase == Phase.Cleared; i++) g.Tick(new Controls(0, -1));
        Assert.Equal(Phase.Map, g.Phase);
        Assert.True(g.Room.IsDome);
        Assert.Equal("THE PLEASURE DOME", g.Room.Name);
        g.Uncalm();
        Steps(g, (int)(60 * (GameEngine.MapSeconds + GameEngine.ReadySeconds)) + 4);
        Assert.Equal(Phase.Fighting, g.Phase);
        Assert.Equal(BossKind.Monty, g.Boss?.Kind);
        Assert.Equal("WELCOME TO MY PLEASURE DOME! NOW... DIE!", g.Message);
    }

    [Fact]
    public void Without_the_keys_the_dome_stays_locked_and_the_show_ends()
    {
        var (g, _) = Arena(2, 10);
        g.SetKeys(Show.KeysNeeded - 1);
        g.ClearRoom();
        Assert.All(g.ExitTo, x => Assert.Equal(-1, x));
        Assert.Contains("NO PLEASURE DOME", g.Message);
        Steps(g, 60 * 6);
        Assert.Equal(Phase.Tally, g.Phase);
        Assert.Equal("THE PLEASURE DOME STAYED LOCKED", g.Ending);
        Assert.False(g.Won);
        Steps(g, (int)(60 * GameEngine.TallySeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Beating_the_host_wins_the_show()
    {
        var (g, p) = Arena(2, Show.Circuits[2].BossRoom);
        var heard = Listen(g);
        var b = g.PutBoss(BossKind.Monty);
        while (b.Parts.Any(x => !x.Gone))
            foreach (var part in b.Parts.Where(x => !x.Gone && x.Layer == b.Exposed).ToList()) Hit(g, b, part, 999);
        Steps(g, (int)(60 * GameEngine.BossDeathSeconds) + 5);
        Assert.True(g.Won);
        Steps(g, 60 * 7);
        Assert.Equal(Phase.Tally, g.Phase);
        Assert.Equal("THE SHOW IS WON", g.Ending);
        Assert.Contains(Sfx.Applause, heard);
        Steps(g, (int)(60 * GameEngine.TallySeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.True(p.Score > GameEngine.BossPoints);
    }

    [Fact]
    public void After_a_boss_the_next_circuit_starts_on_its_map()
    {
        var (g, p) = Arena(0, Show.Circuits[0].BossRoom);
        g.ClearRoom();
        p.Pos = V(Field.Width / 2, 2);
        for (int i = 0; i < 120 && g.Phase == Phase.Cleared; i++) g.Tick(new Controls(0, -1));
        Assert.Equal(Phase.Map, g.Phase);
        Assert.Equal(1, g.CircuitIndex);
        Assert.Equal(0, g.RoomIndex);
        Assert.Equal([0], g.Route);
        Assert.Equal("CIRCUIT 2: THE BACK LOT!", g.Message);
        Assert.True(g.Difficulty > 4, "harder further in");
    }

    // ----- two players, lives and the end ---------------------------------------------------------

    [Fact]
    public void Two_contestants_play_at_once_each_on_their_own_sticks()
    {
        var (g, a) = Arena(players: 2);
        var b = g.Players[1];
        Assert.True(a.Joined && b.Joined);
        a.Pos = V(8, Field.Height / 2);
        b.Pos = V(20, Field.Height / 2);
        Steps(g, 30, new Controls(1, 0, 0, -1), new Controls(-1, 0, 0, 1));
        Assert.True(a.Pos.X > 10.5 && b.Pos.X < 17.5);
        Assert.All(g.Shots.Where(s => s.Owner == 0), s => Assert.True(s.Vel.Y < 0));
        Assert.All(g.Shots.Where(s => s.Owner == 1), s => Assert.True(s.Vel.Y > 0));
        Assert.Contains(g.Shots, s => s.Owner == 0);
        Assert.Contains(g.Shots, s => s.Owner == 1);
        // Each scores their own.
        g.Shots.Clear();
        var grunt = g.PutEnemy(EnemyKind.Grunt, b.Pos + V(0, 5));
        for (int i = 0; i < 60 && !grunt.Dead; i++) { grunt.Pos = b.Pos + V(0, 5); g.Tick(default, new Controls(0, 0, 0, 1)); }
        Assert.True(grunt.Dead);
        Assert.Equal(GameEngine.Points(EnemyKind.Grunt), b.Score);
        Assert.Equal(0, a.Score);
        Assert.Equal(1, b.Kills);
    }

    [Fact]
    public void Player_two_can_join_a_show_under_way()
    {
        var (g, _) = Arena();
        var heard = Listen(g);
        var b = g.Players[1];
        Assert.False(b.Joined);
        g.Tick(default, default, false, true);
        Assert.True(b.Joined);
        Assert.Equal(GameEngine.StartingLives, b.Lives);
        Assert.Contains(Sfx.OneUp, heard);
        Assert.Contains("PLAYER 2", g.Message);
        // Joining twice changes nothing.
        b.Lives = 1;
        g.Tick(default, default, false, false);
        g.Tick(default, default, false, true);
        Assert.Equal(1, b.Lives);
    }

    [Fact]
    public void The_show_goes_on_until_both_are_out()
    {
        var (g, a) = Arena(players: 2);
        var b = g.Players[1];
        a.Lives = 1;
        g.PutEnemy(EnemyKind.Grunt, a.Pos);
        Steps(g, (int)(60 * GameEngine.DownSeconds) + 10);
        Assert.True(a.Out);
        Assert.Equal(Phase.Fighting, g.Phase);
        Assert.Equal(GameState.Playing, g.State);
        g.Enemies.Clear();
        b.Lives = 1;
        g.PutEnemy(EnemyKind.Grunt, b.Pos);
        Steps(g, (int)(60 * GameEngine.DownSeconds) + 10);
        Assert.True(b.Out);
        Assert.Equal(Phase.Dying, g.Phase);
    }

    [Fact]
    public void Out_of_lives_the_tally_then_game_over_and_initials_for_the_table()
    {
        var (g, p) = Arena(players: 2);
        var heard = Listen(g);
        p.Score = 300000;
        g.Players[1].Score = 250000;
        foreach (var x in g.Players) x.Lives = 1;
        foreach (var x in g.Players) g.PutEnemy(EnemyKind.Grunt, x.Pos);
        for (int i = 0; i < 600 && g.Phase != Phase.Dying; i++) g.Tick(default);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOW'S OVER, FOLKS!", g.Message);
        Assert.Contains(Sfx.SayShowsOver, heard);
        Steps(g, (int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(Phase.Tally, g.Phase);
        Assert.Equal("ALL OUT OF LIVES", g.Ending);
        Steps(g, (int)(60 * GameEngine.TallySeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Contains(Sfx.GameOver, heard);
        g.Idle((int)(60 * GameEngine.GameOverSeconds) + 2);
        // The better score enters first.
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(0, g.EntryPlayer);
        foreach (var ch in "ONE") g.Initials!.Type(ch);
        g.Idle(2);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(1, g.EntryPlayer);
        foreach (var ch in "TWO") g.Initials!.Type(ch);
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Contains(g.HighScores.Entries, e => e.Initials == "ONE" && e.Score == 300000);
        Assert.Contains(g.HighScores.Entries, e => e.Initials == "TWO" && e.Score == 250000);
        Assert.True(File.Exists(Path.Combine(_dir, "highscores.txt")));
        g.Idle((int)(60 * (GameEngine.HighScoreSeconds + 1)));
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void A_score_too_low_for_the_table_goes_straight_to_it()
    {
        var (g, p) = Arena();
        p.Lives = 1;
        g.PutEnemy(EnemyKind.Grunt, p.Pos);
        for (int i = 0; i < 60 * 30 && g.State == GameState.Playing; i++) g.Tick(default);
        g.Idle((int)(60 * GameEngine.GameOverSeconds) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(-1, g.NewEntryIndex);
    }

    // ----- the host -------------------------------------------------------------------------------

    [Fact]
    public void The_host_welcomes_you_and_has_something_to_say_about_everything()
    {
        var g = new GameEngine(9, new HighScoreTable(_dir));
        var heard = Listen(g);
        g.StartGame(1);
        Assert.StartsWith("WELCOME TO AVA SMASH TV!", g.Message);
        Assert.Contains(Sfx.SayBigBucks, heard);
        Assert.Contains(Sfx.StartTune, heard);
        Assert.Equal(Phase.Map, g.Phase);
        Steps(g, (int)(60 * GameEngine.MapSeconds) + 2);
        Assert.Equal(Phase.Ready, g.Phase);
        Assert.NotNull(g.Message);
        Assert.Contains(Sfx.Music, heard);
        // Every line of the host's has its voice, now and then.
        var voices = new HashSet<Sfx>();
        for (int seed = 0; seed < 40; seed++)
        {
            var (a, p) = Arena(seed: seed);
            var said = Listen(a);
            a.ClearRoom();
            foreach (var s in said.Where(s => s is >= Sfx.SayBigBucks and <= Sfx.SayShowsOver)) voices.Add(s);
            p.Lives = 3;
        }
        Assert.Contains(Sfx.SayLoveIt, voices);
        Assert.Contains(Sfx.SayWhatAHaul, voices);
        Assert.Contains(Sfx.SayRatings, voices);
    }

    [Fact]
    public void Losing_a_life_gets_a_quip()
    {
        var (g, p) = Arena();
        g.PutEnemy(EnemyKind.Grunt, p.Pos);
        g.Tick(default);
        Assert.True(p.Down);
        Assert.NotNull(g.Message);
        Assert.DoesNotContain("OUT OF THE SHOW", g.Message);
        p.Lives = 0;
        Steps(g, (int)(60 * GameEngine.DownSeconds) + 2);
        Assert.True(p.Out);
    }

    // ----- the demonstration, and the rest --------------------------------------------------------

    [Fact]
    public void The_computer_contestant_clears_rooms_and_takes_the_exits()
    {
        var g = new GameEngine(1, new HighScoreTable(_dir));
        g.StartGame(1);
        var pilot = new AttractPilot(1);
        var p = g.Players[0];
        int deaths = 0;
        bool wasDown = false;
        for (int i = 0; i < 60 * 60 * 8 && g.RoomsCleared < 3 && g.State == GameState.Playing; i++)
        {
            g.Tick(pilot.Decide(g, 0));
            if (p.Down && !wasDown) deaths++;
            wasDown = p.Down;
            if (p.Lives < 2) p.Lives = 3;
        }
        Assert.True(g.RoomsCleared >= 3, $"cleared {g.RoomsCleared}, in {g.Room.Name}, {deaths} deaths, doing {pilot.Doing}");
        Assert.True(g.Route.Count >= 3);
        Assert.True(p.Score > 0 && p.Kills > 20);
    }

    [Fact]
    public void The_same_seed_plays_the_same_show()
    {
        string Run()
        {
            var g = new GameEngine(21, new HighScoreTable(_dir));
            g.StartGame(2);
            var pilots = new[] { new AttractPilot(5), new AttractPilot(6) };
            for (int i = 0; i < 60 * 60; i++) g.Tick(pilots[0].Decide(g, 0), pilots[1].Decide(g, 1));
            return $"{g.CircuitIndex} {g.RoomIndex} {g.Phase} {g.Players[0].Score} {g.Players[1].Score} {g.Players[0].Pos} {g.Enemies.Count} {g.Floor.Count}";
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void The_title_plays_its_tune_and_then_the_demonstration_with_sound()
    {
        var g = new GameEngine(5, new HighScoreTable(_dir));
        var heard = Listen(g);
        g.Idle(1);
        Assert.Equal([Sfx.TitleTune], heard);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + 10)));
        Assert.Equal(GameState.Attract, g.State);
        Assert.All(g.Players, p => Assert.True(p.Joined));
        Assert.Contains(Sfx.Music, heard);
        Assert.Contains(Sfx.Shoot, heard);
        for (int i = 0; i < 60 * GameEngine.AttractSeconds && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(Sfx.MusicStop, heard.Last(s => s is Sfx.Music or Sfx.MusicStop));
        g.Idle((int)(60 * (GameEngine.HighScoreSeconds + 1)));
        Assert.Equal(GameState.Title, g.State);
        // A start from the demonstration begins a real show.
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + 2)));
        Assert.Equal(GameState.Attract, g.State);
        g.Tick(default, default, false, true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Players[1].Joined);
        Assert.Equal(0, g.CircuitIndex);
    }

    [Fact]
    public void Pausing_holds_everything()
    {
        var (g, p) = Arena();
        var grunt = g.PutEnemy(EnemyKind.Grunt, p.Pos + V(6, 0));
        var at = p.Pos;
        var gruntAt = grunt.Pos;
        g.Pause();
        Assert.Equal(GameState.Paused, g.State);
        Steps(g, 90, new Controls(1, 0, 1, 0));
        Assert.Equal(at, p.Pos);
        Assert.Equal(gruntAt, grunt.Pos);
        Assert.Empty(g.Shots);
        g.Resume();
        Steps(g, 10, new Controls(-1, 0));
        Assert.True(p.Pos.X < at.X);
    }

    [Fact]
    public void Every_sound_and_sprite_is_there()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        Assert.All(Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop).Select(s => s.ToString()), f => Assert.True(File.Exists(Path.Combine(folder, f + ".wav")), f));
        Assert.Equal(Enum.GetValues<Sfx>().Length - 1, Directory.GetFiles(folder, "*.wav").Length);
        Assert.All(Sprites.All.SelectMany(s => s.Rows).SelectMany(r => r).Where(ch => ch != '.'), ch => Assert.True(Sprites.Colours.ContainsKey(ch), $"colour {ch}"));
        Assert.All(Sprites.All, s => Assert.All(s.Rows, r => Assert.Equal(s.Width, r.Length)));
    }
}
