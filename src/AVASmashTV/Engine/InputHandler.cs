using Avalonia.Input;

namespace AVASmashTV.Engine;

/// <summary>
/// Keyboard and gamepads boiled down to each contestant's twin sticks. Player 1 walks with W A S D and fires
/// with the arrow keys; player 2 walks with I J K L and fires with the number pad (8 2 4 6, and 7 9 1 3 for
/// the diagonals). Each gamepad is a contestant's too (the first player 1's, the second player 2's): the left
/// stick or D-pad walks, the right stick fires, and the face buttons fire four ways.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager[] _pads = [new(0), new(1)];

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);

    public void Update()
    {
        foreach (var pad in _pads) pad.Update();
    }

    /// <summary>The last-pressed of the keys held, as −1 or +1 (0 for neither).</summary>
    private int Axis(Key[] minus, Key[] plus)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (minus.Contains(_held[i])) return -1;
            if (plus.Contains(_held[i])) return 1;
        }
        return 0;
    }

    private static readonly Key[][] P1Move = [[Key.A], [Key.D], [Key.W], [Key.S]];
    private static readonly Key[][] P1Fire = [[Key.Left], [Key.Right], [Key.Up], [Key.Down]];
    private static readonly Key[][] P2Move = [[Key.J], [Key.L], [Key.I], [Key.K]];
    private static readonly Key[][] P2Fire =
    [
        [Key.NumPad4, Key.NumPad7, Key.NumPad1], [Key.NumPad6, Key.NumPad9, Key.NumPad3],
        [Key.NumPad8, Key.NumPad7, Key.NumPad9], [Key.NumPad2, Key.NumPad1, Key.NumPad3],
    ];

    /// <summary>A stick's eight ways from a gamepad's analogue position.</summary>
    private static (int, int) Stick(float x, float y)
    {
        if (x * x + y * y < 0.25f) return (0, 0);
        double a = Math.Atan2(y, x);
        int octant = (int)Math.Round(a / (Math.PI / 4));
        return ((int)Math.Round(Math.Cos(octant * Math.PI / 4)), (int)Math.Round(Math.Sin(octant * Math.PI / 4)));
    }

    /// <summary>Contestant 0's or 1's sticks. Down the screen is +1, as in the engine.</summary>
    public Controls For(int player)
    {
        var (move, fire) = player == 0 ? (P1Move, P1Fire) : (P2Move, P2Fire);
        var pad = _pads[player];
        int mx = Axis(move[0], move[1]), my = Axis(move[2], move[3]);
        int fx = Axis(fire[0], fire[1]), fy = Axis(fire[2], fire[3]);
        // A number-pad diagonal is both of its axes at once.
        if (player == 1)
        {
            if (Has(Key.NumPad7)) (fx, fy) = (-1, -1);
            else if (Has(Key.NumPad9)) (fx, fy) = (1, -1);
            else if (Has(Key.NumPad1)) (fx, fy) = (-1, 1);
            else if (Has(Key.NumPad3)) (fx, fy) = (1, 1);
        }
        if (mx == 0 && my == 0)
        {
            (mx, my) = Stick(pad.LeftStickX, pad.LeftStickY);
            if (mx == 0 && my == 0) (mx, my) = (pad.DPadLeft ? -1 : pad.DPadRight ? 1 : 0, pad.DPadUp ? -1 : pad.DPadDown ? 1 : 0);
        }
        if (fx == 0 && fy == 0)
        {
            (fx, fy) = Stick(pad.RightStickX, pad.RightStickY);
            if (fx == 0 && fy == 0) (fx, fy) = ((pad.ButtonB ? 1 : 0) - (pad.ButtonX ? 1 : 0), (pad.ButtonA ? 1 : 0) - (pad.ButtonY ? 1 : 0));
        }
        return new Controls(mx, my, fx, fy);
    }

    public Controls Player1 => For(0);
    public Controls Player2 => For(1);

    /// <summary>Player 1's start: Enter, Space or 1, or the first pad's Start.</summary>
    public bool Start1 => Has(Key.Enter) || Has(Key.Space) || Has(Key.D1) || _pads[0].ButtonStart;
    /// <summary>Player 2's start (or join): 2, or the second pad's Start.</summary>
    public bool Start2 => Has(Key.D2) || _pads[1].ButtonStart;

    private ControllerManager Pad => _pads[0];
    public bool PadUp => Pad.DPadUp || Pad.LeftStickY < -0.5f;
    public bool PadDown => Pad.DPadDown || Pad.LeftStickY > 0.5f;
    public bool PadLeft => Pad.DPadLeft || Pad.LeftStickX < -0.5f;
    public bool PadRight => Pad.DPadRight || Pad.LeftStickX > 0.5f;
    public bool PadAccept => Pad.ButtonA || Pad.ButtonX || Pad.ButtonB;
    public bool PadDone => Pad.ButtonStart;
    public bool PadPause => Pad.ButtonStart;
    public bool PadBack => Pad.ButtonBack;

    public void Dispose()
    {
        foreach (var pad in _pads) pad.Dispose();
    }
}
