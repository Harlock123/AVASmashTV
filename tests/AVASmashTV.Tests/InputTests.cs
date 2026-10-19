using Avalonia.Input;
using AVASmashTV.Engine;
using AVASmashTV.Models;

namespace AVASmashTV.Tests;

/// <summary>The keys, through the InputHandler, as the game reads them.</summary>
public sealed class InputTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smashtv-keys-" + Guid.NewGuid().ToString("N"));
    private readonly InputHandler _input = new();

    public void Dispose()
    {
        _input.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void Down(params Key[] keys)
    {
        foreach (var k in keys) _input.OnKeyDown(k);
    }

    private void Up(params Key[] keys)
    {
        foreach (var k in keys) _input.OnKeyUp(k);
    }

    [Fact]
    public void Player_one_walks_with_WASD_and_fires_with_the_arrows()
    {
        Down(Key.W);
        Assert.Equal(new Controls(0, -1), _input.Player1);
        Down(Key.D);
        Assert.Equal(new Controls(1, -1), _input.Player1);
        Down(Key.Left);
        Assert.Equal(new Controls(1, -1, -1, 0), _input.Player1);
        Down(Key.Down);
        Assert.Equal(new Controls(1, -1, -1, 1), _input.Player1);
        Up(Key.W, Key.D);
        Assert.Equal(new Controls(0, 0, -1, 1), _input.Player1);
        // None of it is player 2's.
        Assert.Equal(default, _input.Player2);
    }

    [Fact]
    public void Player_two_walks_with_IJKL_and_fires_with_the_number_pad()
    {
        Down(Key.I, Key.L);
        Assert.Equal(new Controls(1, -1), _input.Player2);
        Down(Key.NumPad2);
        Assert.Equal(new Controls(1, -1, 0, 1), _input.Player2);
        Up(Key.NumPad2);
        Down(Key.NumPad7);
        Assert.Equal(new Controls(1, -1, -1, -1), _input.Player2);
        Up(Key.NumPad7);
        Down(Key.NumPad3);
        Assert.Equal(new Controls(1, -1, 1, 1), _input.Player2);
        Assert.Equal(default, _input.Player1);
    }

    [Fact]
    public void The_last_of_two_opposite_keys_wins()
    {
        Down(Key.A, Key.D);
        Assert.Equal(1, _input.Player1.MoveX);
        Down(Key.A);
        Assert.Equal(-1, _input.Player1.MoveX);
        Up(Key.A);
        Assert.Equal(1, _input.Player1.MoveX);
    }

    [Fact]
    public void Up_on_the_keys_is_up_the_screen_in_the_game()
    {
        var g = new GameEngine(1, new HighScoreTable(_dir));
        g.StartGame(1);
        g.Calm();
        g.JumpTo(0, 1);
        var p = g.Players[0];
        p.Pos = Field.Centre;
        Down(Key.W, Key.Up);
        for (int i = 0; i < 20; i++) g.Tick(_input.Player1, _input.Player2, _input.Start1, _input.Start2);
        Assert.True(p.Pos.Y < Field.Centre.Y - 1, "W walks up the screen");
        Assert.All(g.Shots, s => Assert.True(s.Vel.Y < 0, "the up arrow shoots up it"));
        Up(Key.W, Key.Up);
        Down(Key.S, Key.Right);
        var at = p.Pos;
        for (int i = 0; i < 20; i++) g.Tick(_input.Player1, _input.Player2, _input.Start1, _input.Start2);
        Assert.True(p.Pos.Y > at.Y + 1);
        Assert.Contains(g.Shots, s => s.Vel.X > 0 && s.Vel.Y == 0);
    }

    [Fact]
    public void One_and_two_start_a_show_for_one_or_two_and_two_joins_one_under_way()
    {
        var g = new GameEngine(1, new HighScoreTable(_dir));
        g.Idle(2);
        Down(Key.D2);
        g.Tick(_input.Player1, _input.Player2, _input.Start1, _input.Start2);
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Players[0].Joined && g.Players[1].Joined);
        Up(Key.D2);

        g = new GameEngine(1, new HighScoreTable(_dir));
        g.Idle(2);
        Down(Key.Enter);
        g.Tick(_input.Player1, _input.Player2, _input.Start1, _input.Start2);
        Assert.Equal(GameState.Playing, g.State);
        Assert.False(g.Players[1].Joined);
        Up(Key.Enter);
        g.Tick(_input.Player1, _input.Player2, _input.Start1, _input.Start2);
        Down(Key.D2);
        g.Tick(_input.Player1, _input.Player2, _input.Start1, _input.Start2);
        Assert.True(g.Players[1].Joined);
        Assert.True(_input.Start2);
        Assert.False(_input.Start1);
    }

    [Fact]
    public void A_key_let_go_under_another_name_is_let_go()
    {
        _input.OnKeyDown(Key.W, PhysicalKey.W);
        Assert.Equal(-1, _input.Player1.MoveY);
        // Its release reported as some other key: the physical key still lets it go.
        _input.OnKeyUp(Key.None, PhysicalKey.W);
        Assert.Equal(0, _input.Player1.MoveY);
    }
}
