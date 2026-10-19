using AVASmashTV.Models;

namespace AVASmashTV.Engine;

/// <summary>What a contestant's gun fires.</summary>
public enum Weapon { Gun, Spread, Rockets, Grenades }

/// <summary>A contestant: player 1 in blue, player 2 in red.</summary>
public sealed class Player(int index)
{
    public int Index { get; } = index;
    /// <summary>In the show (joined, and not yet out of lives).</summary>
    public bool Joined { get; set; }
    public bool Out { get; set; }
    public bool Active => Joined && !Out;
    public Vec Pos { get; set; }
    /// <summary>Which way the body faces: the way it walks, or (while firing) the way it shoots.</summary>
    public Vec Facing { get; set; } = new(0, -1);
    /// <summary>The way the gun points, one of eight directions.</summary>
    public Vec Aim { get; set; } = new(0, -1);
    public int Lives { get; set; }
    public int Score { get; set; }
    /// <summary>What the prizes come to.</summary>
    public int Cash { get; set; }
    public int Kills { get; set; }
    public Dictionary<ItemKind, int> Prizes { get; } = [];
    public Weapon Weapon { get; set; }
    public double WeaponTime { get; set; }
    public double ShieldTime { get; set; }
    public double SpeedTime { get; set; }
    /// <summary>Just back in after a death: can't be hurt while it runs down.</summary>
    public double Invulnerable { get; set; }
    /// <summary>Hit, and lying there for a moment before coming back (or not).</summary>
    public bool Down { get; set; }
    public double DownTime { get; set; }
    public double Reload { get; set; }
    public double Walk { get; set; }
    public bool Firing { get; set; }

    public bool Alive => Active && !Down;
    public bool Safe => ShieldTime > 0 || Invulnerable > 0;
    public double Speed => SpeedTime > 0 ? GameEngine.FastSpeed : GameEngine.WalkSpeed;
    public const double Radius = 0.4;

    public static uint ColourOf(int index) => index == 0 ? 0xFF3A8AFFu : 0xFFFF4A3Au;
    public uint Colour => ColourOf(Index);
    public string Name => Index == 0 ? "PLAYER 1" : "PLAYER 2";
}

public sealed class Enemy
{
    public required EnemyKind Kind { get; init; }
    public Vec Pos { get; set; }
    public Vec Vel { get; set; }
    public int Health { get; set; }
    public double Age { get; set; }
    public double Reload { get; set; }
    /// <summary>Still coming in through a door: it walks straight in, through the wall's line.</summary>
    public double Entering { get; set; }
    /// <summary>Its own little offset, so a mob doesn't move as one.</summary>
    public double Quirk { get; init; }
    /// <summary>A snake's tail segments still on it.</summary>
    public int Segments { get; set; }
    /// <summary>Where a snake's head has been, newest first: its segments follow along it.</summary>
    public List<Vec> Trail { get; } = [];
    /// <summary>A tank's turret, an angle in radians.</summary>
    public double Turret { get; set; }
    /// <summary>A shrapnel bomb's time left.</summary>
    public double Fuse { get; set; }
    /// <summary>Just hit: drawn white for a moment.</summary>
    public double Flash { get; set; }
    public bool Dead { get; set; }

    public double Radius => Kind switch
    {
        EnemyKind.Tank => 0.85,
        EnemyKind.Orb => 0.5,
        EnemyKind.Blade => 0.5,
        EnemyKind.Mine => 0.4,
        EnemyKind.Shrapnel => 0.45,
        EnemyKind.Snake => 0.42,
        _ => 0.42,
    };

    /// <summary>Snake segments are this many steps of trail apart.</summary>
    public const int SegmentGap = 7;

    /// <summary>Where each of a snake's segments is, nearest the head first.</summary>
    public IEnumerable<Vec> SegmentPositions()
    {
        for (int i = 1; i <= Segments; i++)
        {
            int at = Math.Min(Trail.Count - 1, i * SegmentGap);
            yield return at >= 0 ? Trail[at] : Pos;
        }
    }
}

/// <summary>A piece of a boss, an ellipse about its middle. A layer can only be hurt once every layer below it is gone.</summary>
public sealed class BossPart
{
    public required string Name { get; init; }
    public required Vec Offset { get; init; }
    public required Vec Size { get; init; }
    public required int Layer { get; init; }
    public required int MaxHealth { get; init; }
    public int Health { get; set; }
    public bool Gone => Health <= 0;
    public double Reload { get; set; }
    public double Flash { get; set; }
}

public sealed class Boss
{
    public required BossKind Kind { get; init; }
    public Vec Pos { get; set; }
    public double Age { get; set; }
    public List<BossPart> Parts { get; } = [];
    /// <summary>The cobra's coils: where its head has been, newest first.</summary>
    public List<Vec> Trail { get; } = [];
    /// <summary>What it's doing: 0 moving about, 1 winding up, 2 charging, 3 going back.</summary>
    public int Mode { get; set; }
    public double ModeTime { get; set; }
    public Vec Target { get; set; }
    public double Spawn { get; set; }
    /// <summary>Beaten: the last part is off, and it's going up in smoke.</summary>
    public bool Dead { get; set; }
    public double DeadTime { get; set; }

    public string Name => Kind switch
    {
        BossKind.Tankenstein => "TANKENSTEIN",
        BossKind.Vipervision => "VIPERVISION",
        _ => "MONTY MAYHEM",
    };

    /// <summary>The lowest layer still standing: only its parts can be hurt.</summary>
    public int Exposed => Parts.Where(p => !p.Gone).Select(p => p.Layer).DefaultIfEmpty(int.MaxValue).Min();
    public BossPart? Part(string name) => Parts.FirstOrDefault(p => p.Name == name);
    public int Health => Parts.Sum(p => Math.Max(0, p.Health));
    public int MaxHealth => Parts.Sum(p => p.MaxHealth);

    /// <summary>The cobra's body segments are this many steps of trail apart.</summary>
    public const int CoilGap = 9;
    public const int Coils = 16;

    /// <summary>Where the coils are: as far along the trail as it reaches, leaving out the neck tucked under the head.</summary>
    public IEnumerable<Vec> CoilPositions()
    {
        if (Kind != BossKind.Vipervision) yield break;
        for (int i = 1; i <= Coils && i * CoilGap < Trail.Count; i++)
        {
            var c = Trail[i * CoilGap];
            if ((c - Pos).Length >= NeckLength) yield return c;
        }
    }

    /// <summary>Coils nearer the head than this are under it, out of the way.</summary>
    public const double NeckLength = 1.6;
}

public sealed class Item
{
    public required ItemKind Kind { get; init; }
    public Vec Pos { get; set; }
    /// <summary>Seconds left on the floor (a key stays until the room is left).</summary>
    public double Life { get; set; } = GameEngine.ItemSeconds;
    public double Age { get; set; }
    public bool Taken { get; set; }
}

public enum ShotKind { Bullet, Rocket, Grenade, Shell, Fireball, Shrapnel }

/// <summary>A shot: a contestant's, or the other side's (a tank's shell, a boss's fireball, a bomb's shrapnel).</summary>
public sealed class Shot
{
    public required ShotKind Kind { get; init; }
    public Vec Pos { get; set; }
    public Vec Vel { get; set; }
    public bool Hostile { get; init; }
    public int Damage { get; init; } = 1;
    /// <summary>Whose it is (for the score), or -1.</summary>
    public int Owner { get; init; } = -1;
    public double Age { get; set; }
    /// <summary>Has glanced off a boss's armour already (one clank a shot).</summary>
    public bool Clanked { get; set; }
    /// <summary>How long it flies (a grenade bursts when it runs out).</summary>
    public double Life { get; init; } = 3;
    public bool Dead { get; set; }
    public double Radius => Kind switch { ShotKind.Rocket => 0.25, ShotKind.Grenade => 0.25, ShotKind.Shell => 0.3, ShotKind.Fireball => 0.32, ShotKind.Shrapnel => 0.18, _ => 0.15 };
}

public enum EffectKind { Score, Splat, Blast, Sparkle, Debris, Confetti, Smoke }

public sealed class Effect
{
    public required EffectKind Kind { get; init; }
    public Vec Pos { get; set; }
    public Vec Vel { get; init; }
    public double Age { get; set; }
    public double Life { get; init; } = 1;
    public double Size { get; init; } = 1;
    public uint Colour { get; init; } = 0xFFFFE14A;
    public string Text { get; init; } = "";
    /// <summary>Its own seed, for the shape of a splat or the spin of debris.</summary>
    public int Seed { get; init; }
    public bool Done => Age >= Life;
}

/// <summary>
/// One contestant's controls for one step, twin-stick: the move stick and the fire stick, each axis −1, 0 or 1,
/// independently (walk one way, shoot another). Firing is holding the fire stick in any direction.
/// </summary>
public record struct Controls(int MoveX, int MoveY, int FireX = 0, int FireY = 0)
{
    public readonly bool Firing => FireX != 0 || FireY != 0;
    public readonly bool Moving => MoveX != 0 || MoveY != 0;
}
