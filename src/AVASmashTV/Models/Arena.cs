namespace AVASmashTV.Models;

/// <summary>A 2D point or vector in arena units (y grows downwards).</summary>
public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec Normal => Length < 1e-9 ? new Vec(0, 0) : this * (1 / Length);
    public double Angle => Math.Atan2(Y, X);
    public static Vec FromAngle(double a) => new(Math.Cos(a), Math.Sin(a));
    public Vec Rotate(double a) => new(X * Math.Cos(a) - Y * Math.Sin(a), X * Math.Sin(a) + Y * Math.Cos(a));
    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}

/// <summary>The arena: one room of the studio, open floor with a door in the middle of each wall.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    /// <summary>The floor, in units (a unit is 32 pixels on screen).</summary>
    public const double Width = 28;
    public const double Height = 19;
    /// <summary>How wide each door is.</summary>
    public const double DoorWidth = 3.2;

    public static Vec Centre => new(Width / 2, Height / 2);

    /// <summary>The middle of a door, on the wall's line.</summary>
    public static Vec Door(Side s) => s switch
    {
        Side.North => new(Width / 2, 0),
        Side.South => new(Width / 2, Height),
        Side.West => new(0, Height / 2),
        _ => new(Width, Height / 2),
    };

    /// <summary>Straight into the room from a door.</summary>
    public static Vec Inward(Side s) => s switch
    {
        Side.North => new(0, 1),
        Side.South => new(0, -1),
        Side.West => new(1, 0),
        _ => new(-1, 0),
    };

    /// <summary>Is p in line with the door's opening (along its wall)?</summary>
    public static bool InDoorway(Side s, Vec p) => s is Side.North or Side.South
        ? Math.Abs(p.X - Width / 2) < DoorWidth / 2
        : Math.Abs(p.Y - Height / 2) < DoorWidth / 2;
}

public enum Side { North, East, South, West }

public enum EnemyKind
{
    /// <summary>A club-swinging mutant in a jumpsuit: walks straight at you, in mobs.</summary>
    Grunt,
    /// <summary>A floating chrome ball that drifts after you and bounces off the walls. Three hits.</summary>
    Orb,
    /// <summary>A snake droid: each hit knocks a segment off its tail; the head goes last.</summary>
    Snake,
    /// <summary>A spinning blade droid that ricochets round the room at speed.</summary>
    Blade,
    /// <summary>A slow armoured tank whose turret follows you and lobs shells.</summary>
    Tank,
    /// <summary>A mine on the floor. Step on it and it's goodnight; shoot it and it takes the mob with it.</summary>
    Mine,
    /// <summary>A rolling bomb: when its fuse runs out, or it's shot, it bursts into shrapnel.</summary>
    Shrapnel,
}

/// <summary>Things that appear on the floor: prizes, keys and power-ups.</summary>
public enum ItemKind
{
    Cash, Toaster, Vcr, Tv, Gold, Car,
    /// <summary>A key to the Pleasure Dome.</summary>
    Key,
    /// <summary>Three shots at a time.</summary>
    Spread,
    /// <summary>Heavy rockets that burst on impact.</summary>
    Rockets,
    /// <summary>Grenades that blow a crowd apart.</summary>
    Grenades,
    /// <summary>A force field with spinning orbs that cut down anything they touch.</summary>
    Shield,
    /// <summary>Running shoes.</summary>
    Speed,
    ExtraLife,
}

public static class Items
{
    public static bool IsPrize(this ItemKind k) => k <= ItemKind.Car;
    public static bool IsPowerUp(this ItemKind k) => k >= ItemKind.Spread;

    /// <summary>What a prize is worth.</summary>
    public static int Value(this ItemKind k) => k switch
    {
        ItemKind.Cash => 1000,
        ItemKind.Toaster => 2000,
        ItemKind.Vcr => 4000,
        ItemKind.Tv => 6000,
        ItemKind.Gold => 10000,
        ItemKind.Car => 25000,
        ItemKind.Key => 5000,
        _ => 500,
    };

    public static string Title(this ItemKind k) => k switch
    {
        ItemKind.Cash => "CASH",
        ItemKind.Toaster => "TOASTERS",
        ItemKind.Vcr => "VCRS",
        ItemKind.Tv => "TV SETS",
        ItemKind.Gold => "GOLD BARS",
        ItemKind.Car => "NEW CARS",
        ItemKind.Key => "KEYS",
        ItemKind.Spread => "SPREAD GUN",
        ItemKind.Rockets => "ROCKETS",
        ItemKind.Grenades => "GRENADES",
        ItemKind.Shield => "FORCE FIELD",
        ItemKind.Speed => "SPEED SHOES",
        _ => "EXTRA LIFE",
    };
}

public enum BossKind
{
    /// <summary>A giant armoured mutant bolted to a tank: shoot his parts off one by one.</summary>
    Tankenstein,
    /// <summary>A giant cobra droid with a television for a face, its coils filling the room.</summary>
    Vipervision,
    /// <summary>The host himself, on his flying throne in the Pleasure Dome.</summary>
    Monty,
}

/// <summary>
/// One room of a circuit, on the circuit's map grid. Its links lead to the rooms next to it, through the
/// door on that side. The mix says what pours in: G grunts, O orbs, S snakes, B blades, T tanks,
/// X shrapnel bombs, each letter once for each share; M is a mine laid on the floor before the doors open.
/// </summary>
public sealed record RoomDef(string Name, int Col, int Row, string Mix, int[] Links, bool Key = false, BossKind? Boss = null)
{
    public bool IsDome => Boss == BossKind.Monty;
}

/// <summary>A circuit: rooms on a map grid, with branching routes from the first to the boss's.</summary>
public sealed record Circuit(int Number, string Name, int Cols, int Rows, RoomDef[] Rooms)
{
    public RoomDef Start => Rooms[0];
    public int BossRoom => Array.FindIndex(Rooms, r => r.Boss is not null);

    /// <summary>Which wall of room a has the door to room b (they're next to each other on the grid).</summary>
    public Side Towards(int a, int b)
    {
        var (ra, rb) = (Rooms[a], Rooms[b]);
        return (rb.Col - ra.Col, rb.Row - ra.Row) switch
        {
            (1, 0) => Side.East,
            (-1, 0) => Side.West,
            (0, -1) => Side.North,
            (0, 1) => Side.South,
            _ => throw new InvalidOperationException($"{ra.Name} and {rb.Name} aren't next to each other"),
        };
    }

    /// <summary>Every route from the first room to the last.</summary>
    public IEnumerable<int[]> Routes()
    {
        IEnumerable<int[]> From(int at)
        {
            if (Rooms[at].Links.Length == 0) { yield return [at]; yield break; }
            foreach (var next in Rooms[at].Links)
                foreach (var rest in From(next)) yield return [at, .. rest];
        }
        return From(0);
    }
}

/// <summary>The show: three circuits, and the keys it takes to get into the Pleasure Dome at the end of the third.</summary>
public static class Show
{
    public const int KeysNeeded = 6;

    public static readonly Circuit[] Circuits =
    [
        new(1, "THE STUDIO", 4, 3,
        [
            new("OPENING NUMBERS", 0, 2, "G", [1, 2]),
            new("CASH AND CARRY", 1, 2, "GGGGO", [3, 4]),
            new("CLUB MEMBERS ONLY", 0, 1, "GGGGGM", [4, 5], Key: true),
            new("HAVE A BALL", 2, 2, "GGOO", [6, 7]),
            new("TOASTER TOWN", 1, 1, "GGGX", [7], Key: true),
            new("MIND THE MINES", 0, 0, "GGGOMMM", [8]),
            new("BLADE RUNNERS-UP", 3, 2, "GGGBO", [9]),
            new("TANKS A LOT", 2, 1, "GGGT", [9, 10]),
            new("SHRAPNEL SURPRISE", 1, 0, "GGXO", [10]),
            new("SMASH OR CASH", 3, 1, "GGGOBT", [11], Key: true),
            new("BONUS BONANZA", 2, 0, "GGGGOX", [11]),
            new("MEET TANKENSTEIN", 3, 0, "G", [], Boss: BossKind.Tankenstein),
        ]),
        new(2, "THE BACK LOT", 4, 3,
        [
            new("BACK LOT BLITZ", 0, 0, "GGGS", [1, 2]),
            new("SNAKES ALIVE", 1, 0, "GGSS", [3, 4], Key: true),
            new("ORB-ITAL VELOCITY", 0, 1, "GOOO", [4, 5]),
            new("BUZZ SAW BOULEVARD", 2, 0, "GGBB", [6, 7]),
            new("VCR VALHALLA", 1, 1, "GGTXM", [7], Key: true),
            new("TANK TOP", 0, 2, "GGGTT", [8]),
            new("DOUBLE OR NOTHING", 3, 0, "GGOSB", [9]),
            new("SHRAPNEL CITY", 2, 1, "GGXXO", [9, 10], Key: true),
            new("MINE ALL MINE", 1, 2, "GGGSMMMM", [10]),
            new("COMMERCIAL BREAK", 3, 1, "GGOBTS", [11]),
            new("PRIME TIME CRIME", 2, 2, "GGGBX", [11]),
            new("VIPERVISION", 3, 2, "G", [], Boss: BossKind.Vipervision),
        ]),
        new(3, "THE NETWORK", 4, 3,
        [
            new("THE BIG TIME", 3, 2, "GGGOT", [1, 2]),
            new("RATINGS WAR", 2, 2, "GGBST", [3, 4], Key: true),
            new("CAR CRAZY", 3, 1, "GGOOX", [4, 5]),
            new("BLADES OF GLORY", 1, 2, "GBBO", [6, 7]),
            new("MINEFIELD OF DREAMS", 2, 1, "GGTXMMM", [7], Key: true),
            new("GOLD RUSH", 3, 0, "GGOSX", [8]),
            new("SNAKE EYES", 0, 2, "GSSB", [9]),
            new("SUDDEN DEATH", 1, 1, "GGOBTX", [9, 10], Key: true),
            new("SWEEPS WEEK", 2, 0, "GGGTTO", [10]),
            new("THE LAST COMMERCIAL", 0, 1, "GGSBXO", [11]),
            new("STANDING OVATION", 1, 0, "GGGGBXT", [11]),
            new("THE PLEASURE DOME", 0, 0, "G", [], Boss: BossKind.Monty),
        ]),
    ];

    public static EnemyKind KindOf(char c) => c switch
    {
        'O' => EnemyKind.Orb,
        'S' => EnemyKind.Snake,
        'B' => EnemyKind.Blade,
        'T' => EnemyKind.Tank,
        'X' => EnemyKind.Shrapnel,
        'M' => EnemyKind.Mine,
        _ => EnemyKind.Grunt,
    };
}
