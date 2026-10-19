using AVASmashTV.Models;

namespace AVASmashTV.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>Where the show is up to.</summary>
public enum Phase
{
    /// <summary>The circuit's map, the route so far and the room ahead.</summary>
    Map,
    /// <summary>The room's name, before the doors open.</summary>
    Ready,
    /// <summary>The doors are open and they're pouring in.</summary>
    Fighting,
    /// <summary>The room's clear: pick up what's left and take an exit.</summary>
    Cleared,
    /// <summary>Everyone's out of lives.</summary>
    Dying,
    /// <summary>The final tally of prizes.</summary>
    Tally,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Shoot, Spread, Rocket, Grenade, Splat, Explosion, Hit, Clank, Prize, PowerUp, Key, OneUp, Door, Crowd, Applause, PlayerDie, BossHit, BossPart,
    /// <summary>The host's voice.</summary>
    SayBigBucks, SayLoveIt, SayGoodLuck, SayWhatAHaul, SayRatings, SayShowsOver,
    /// <summary>The show's music, looping.</summary>
    Music, MusicStop,
    TitleTune, StartTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and both contestants'
/// controls; it runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays
/// the same game.
/// </summary>
public sealed partial class GameEngine
{
    public const int StartingLives = 3;
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    public const double GameOverSeconds = 3;

    private readonly Random _rng;
    private double _accumulator;
    private bool _start1Prev, _start2Prev;
    private bool _titleTunePending = true;
    private bool _music;
    private readonly Queue<int> _entries = [];

    public GameEngine(int seed = 1999, HighScoreTable? table = null)
    {
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        Players = [new Player(0), new Player(1)];
        EnterRoom(0, 0);
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Map;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    /// <summary>Whose initials are being entered.</summary>
    public int EntryPlayer { get; private set; } = -1;
    public int NewEntryIndex { get; private set; } = -1;
    /// <summary>The computer contestants of the demonstration, one for each player.</summary>
    public AttractPilot?[] Pilots { get; } = new AttractPilot?[2];
    public bool Silent { get; set; }

    public Player[] Players { get; }
    public IEnumerable<Player> Active => Players.Where(p => p.Active);
    public IEnumerable<Player> Alive => Players.Where(p => p.Alive);
    public int TopScore => Players.Where(p => p.Joined).Select(p => p.Score).DefaultIfEmpty(0).Max();

    public List<Effect> Effects { get; } = [];

    /// <summary>What the host has to say, and for how much longer.</summary>
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    /// <summary>The Pleasure Dome's host beaten: the show is won.</summary>
    public bool Won { get; private set; }
    /// <summary>Why the show ended, for the tally.</summary>
    public string? Ending { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    /// <summary>The host speaks (in a speech bubble), maybe out loud too.</summary>
    private void Say(string text, double seconds = 3, Sfx? voice = null)
    {
        Message = text;
        MessageTime = seconds;
        if (voice is { } v) Play(v);
    }

    private string Pick(string[] lines) => lines[_rng.Next(lines.Length)];

    public void Update(double dt, Controls p1, Controls p2 = default, bool start1 = false, bool start2 = false)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(p1, p2, start1, start2);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls p1, Controls p2 = default, bool start1 = false, bool start2 = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool one = start1 && !_start1Prev, two = start2 && !_start2Prev;
        _start1Prev = start1;
        _start2Prev = start2;
        foreach (var e in Effects) { e.Age += dt; e.Pos += e.Vel * dt; }
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                if (one || two) StartGame(two ? 2 : 1);
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilots[0]?.Decide(this, 0) ?? default, Pilots[1]?.Decide(this, 1) ?? default);
                if (one || two) { StartGame(two ? 2 : 1); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                if (one) Join(0);
                if (two) Join(1);
                Simulate(dt, p1, p2);
                break;
            case GameState.GameOver:
                if (StateTime > GameOverSeconds)
                {
                    _entries.Clear();
                    foreach (var p in Players.Where(p => p.Joined).OrderByDescending(p => p.Score)) _entries.Enqueue(p.Index);
                    NextEntry();
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    var p = Players[EntryPlayer];
                    NewEntryIndex = HighScores.Insert(done.Initials, p.Score, RoomsCleared);
                    HighScores.Save();
                    NextEntry();
                }
                break;
            case GameState.HighScores:
                if (one || two) StartGame(two ? 2 : 1);
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>The next contestant whose score makes the table enters initials; then the table.</summary>
    private void NextEntry()
    {
        while (_entries.Count > 0)
        {
            int i = _entries.Dequeue();
            if (!HighScores.Qualifies(Players[i].Score)) continue;
            State = GameState.EnterInitials;
            StateTime = 0;
            EntryPlayer = i;
            Initials = new InitialsEntry(HighScores.LastInitials);
            return;
        }
        Initials = null;
        EntryPlayer = -1;
        ShowHighScores(NewEntryIndex);
    }

    public void StartGame(int players)
    {
        Pilots[0] = Pilots[1] = null;
        Begin(GameState.Playing, players, 0, 0);
        Phase = Phase.Map;
        Say("WELCOME TO AVA SMASH TV! BIG BUCKS! BIG PRIZES! I'M IN HEAVEN!", 5, Sfx.SayBigBucks);
        Play(Sfx.StartTune);
    }

    private void StartAttract()
    {
        Pilots[0] = new AttractPilot(_rng.Next());
        Pilots[1] = new AttractPilot(_rng.Next());
        int circuit = _rng.Next(Show.Circuits.Length);
        var rooms = Show.Circuits[circuit].Rooms;
        int room = _rng.Next(rooms.Length - 1);
        Begin(GameState.Attract, 2, circuit, room);
        SetPhase(Phase.Ready);
        Say(Pick(RoomLines), 3);
    }

    private void Begin(GameState state, int players, int circuit, int room)
    {
        State = state;
        StateTime = 0;
        NewEntryIndex = -1;
        Effects.Clear();
        Message = null;
        Won = false;
        Ending = null;
        Keys = 0;
        RoomsCleared = 0;
        foreach (var p in Players)
        {
            p.Joined = false;
            p.Out = false;
            p.Score = p.Cash = p.Kills = 0;
            p.Prizes.Clear();
        }
        for (int i = 0; i < players; i++) Join(i, quiet: true);
        EnterRoom(circuit, room);
        Route.Clear();
        Route.Add(room);
        SetPhase(Phase.Map);
    }

    /// <summary>A contestant steps into the show, with a full set of lives (once per show).</summary>
    public void Join(int index, bool quiet = false)
    {
        var p = Players[index];
        if (p.Joined) return;
        p.Joined = true;
        p.Out = false;
        p.Lives = StartingLives;
        Arm(p);
        p.Pos = StartSpot(index);
        p.Invulnerable = quiet ? 0 : 2;
        if (quiet) return;
        Play(Sfx.OneUp);
        Say($"{p.Name} HAS ENTERED THE ARENA! THE CROWD GOES WILD!", 3);
    }

    private static void Arm(Player p)
    {
        p.Weapon = Weapon.Gun;
        p.WeaponTime = p.ShieldTime = p.SpeedTime = 0;
        p.Down = false;
        p.Invulnerable = 0;
        p.Facing = p.Aim = new Vec(0, -1);
    }

    private static Vec StartSpot(int index) => new(Field.Width / 2 + (index == 0 ? -2 : 2), Field.Height - 3);

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        Pilots[0] = Pilots[1] = null;
        _titleTunePending = true;
        Music(false);
        Effects.Clear();
        Message = null;
        foreach (var p in Players) p.Joined = false;
        EnterRoom(0, 0);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Pilots[0] = Pilots[1] = null;
        Music(false);
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase is Phase.Ready or Phase.Fighting or Phase.Cleared) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    /// <summary>Points for a contestant (the kills), or cash too (the prizes).</summary>
    internal void AddScore(int player, int points, bool cash = false)
    {
        if (player < 0 || player >= Players.Length) return;
        var p = Players[player];
        p.Score += points;
        if (cash) p.Cash += points;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void Popup(Vec at, string text, uint colour = 0xFFFFE14A, double life = 1) =>
        Effects.Add(new Effect { Kind = EffectKind.Score, Pos = at, Life = life, Text = text, Colour = colour });

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    // ----- what the host says ---------------------------------------------------------------------

    private static readonly string[] RoomLines =
    [
        "GOOD LUCK! YOU'LL NEED IT!",
        "LET'S MEET TONIGHT'S VICTIMS... ER, GUESTS!",
        "THE AUDIENCE IS HUNGRY FOR ACTION!",
        "DON'T TOUCH THAT DIAL, FOLKS!",
        "MORE GUYS WITH CLUBS! THE KIDS LOVE IT!",
        "SHOOT FOR THE PRIZES, KIDS!",
        "IT'S SHOWTIME!",
    ];

    private static readonly string[] ClearLines =
    [
        "I LOVE IT!",
        "WHAT A HAUL!",
        "RATINGS ARE THROUGH THE ROOF!",
        "THE CROWD GOES WILD!",
        "NOW THAT'S GOOD TELEVISION!",
        "SOMEBODY GET A MOP!",
    ];

    private static readonly string[] DeathLines =
    [
        "OOH! THAT'S GOTTA STING!",
        "DON'T WORRY, WE HAVE GREAT INSURANCE!",
        "CLEAN-UP IN THE ARENA, PLEASE!",
        "AND HE'S DOWN! THE CROWD LOVES IT!",
        "THAT'LL LEAVE A MARK!",
    ];

    private static Sfx? VoiceOf(string line) => line switch
    {
        "I LOVE IT!" => Sfx.SayLoveIt,
        "WHAT A HAUL!" => Sfx.SayWhatAHaul,
        "RATINGS ARE THROUGH THE ROOF!" => Sfx.SayRatings,
        "GOOD LUCK! YOU'LL NEED IT!" => Sfx.SayGoodLuck,
        _ => null,
    };

    private static string PowerLine(ItemKind k) => k switch
    {
        ItemKind.Spread => "SPREAD GUN! SHARE THE LOVE!",
        ItemKind.Rockets => "ROCKETS! NOW WE'RE TALKING!",
        ItemKind.Grenades => "GRENADES! STAND BACK, FOLKS!",
        ItemKind.Shield => "FORCE FIELD! NOTHING CAN TOUCH YOU!",
        ItemKind.Speed => "SPEED SHOES! LOOK AT THOSE FEET GO!",
        _ => "AN EXTRA LIFE! YOU'RE GONNA NEED IT!",
    };

    private static string? PrizeLine(ItemKind k) => k switch
    {
        ItemKind.Car => "A BRAND NEW CAR!",
        ItemKind.Toaster => "A TOASTER! EVERYBODY NEEDS A TOASTER!",
        ItemKind.Vcr => "A VCR! NOW YOU CAN TAPE YOURSELF WINNING!",
        ItemKind.Gold => "SOLID GOLD, BABY!",
        ItemKind.Key => "A KEY TO THE PLEASURE DOME!",
        _ => null,
    };
}
