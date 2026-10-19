namespace AVASmashTV.Models;

/// <summary>
/// The game's pixel art, this game's own drawings, painted from ellipses and boxes into a pixel grid. Each
/// letter is a colour (see <see cref="Colours"/>); '.' is clear; 'p' and 'q' are the contestant's own colour,
/// light and dark. The rest of the cast (orbs, blades, tanks, the bosses, the prizes) is drawn as shapes by
/// the view.
/// </summary>
public static class Sprites
{
    public sealed record Sprite(string[] Rows)
    {
        public int Width => Rows[0].Length;
        public int Height => Rows.Length;

        /// <summary>The pixels as runs of one colour along each row, to draw as few boxes as can be.</summary>
        public IReadOnlyList<(int Row, int Col, int Length, char Colour)> Runs => _runs ??= MakeRuns();
        private List<(int, int, int, char)>? _runs;

        private List<(int, int, int, char)> MakeRuns()
        {
            var runs = new List<(int, int, int, char)>();
            for (int r = 0; r < Height; r++)
                for (int c = 0; c < Width;)
                {
                    char ch = Rows[r][c];
                    int len = 1;
                    while (c + len < Width && Rows[r][c + len] == ch) len++;
                    if (ch != '.') runs.Add((r, c, len, ch));
                    c += len;
                }
            return runs;
        }
    }

    /// <summary>A pixel grid to paint shapes into.</summary>
    private sealed class Canvas(int width, int height)
    {
        private readonly char[,] _px = Fill(width, height);

        private static char[,] Fill(int w, int h)
        {
            var g = new char[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++) g[x, y] = '.';
            return g;
        }

        public Canvas Ellipse(double cx, double cy, double rx, double ry, char c)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                    if (dx * dx + dy * dy <= 1) _px[x, y] = c;
                }
            return this;
        }

        public Canvas Box(int x0, int y0, int x1, int y1, char c)
        {
            for (int x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
                for (int y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++) _px[x, y] = c;
            return this;
        }

        public Canvas Dot(int x, int y, char c) => Box(x, y, x, y, c);

        public Sprite Done()
        {
            var rows = new string[height];
            for (int y = 0; y < height; y++)
            {
                var chars = new char[width];
                for (int x = 0; x < width; x++) chars[x] = _px[x, y];
                rows[y] = new string(chars);
            }
            return new Sprite(rows);
        }
    }

    public static readonly IReadOnlyDictionary<char, uint> Colours = new Dictionary<char, uint>
    {
        ['w'] = 0xFFF4F4F8, // white
        ['k'] = 0xFF14141C, // black
        ['s'] = 0xFFFFC898, // skin
        ['m'] = 0xFFA8ACB8, // steel
        ['d'] = 0xFF5C6070, // dark grey
        ['r'] = 0xFFE83A3A, // red
        ['R'] = 0xFF8A1E1E, // dark red
        ['b'] = 0xFF3A7AE8, // blue
        ['B'] = 0xFF1A3A8A, // dark blue
        ['y'] = 0xFFFFE14A, // yellow
        ['Y'] = 0xFFB08A10, // dark yellow
        ['g'] = 0xFF7CD83A, // mutant green
        ['G'] = 0xFF3A7A1E, // dark green
        ['n'] = 0xFF9A6A30, // brown
        ['N'] = 0xFF5A3A18, // dark brown
        ['o'] = 0xFFFF8A1E, // orange
        ['O'] = 0xFFA84E0A, // dark orange
        ['c'] = 0xFF4FD7FF, // cyan
        ['p'] = 0xFF3A8AFF, // the contestant's colour (redrawn for player 2)
        ['q'] = 0xFF1A4AA8, // and its shade
    };

    /// <summary>Player 2's colours in place of 'p' and 'q'.</summary>
    public static readonly IReadOnlyDictionary<char, uint> Player2 = new Dictionary<char, uint> { ['p'] = 0xFFFF4A3A, ['q'] = 0xFF9A1E14 };

    // ----- the contestants: helmet, armoured vest, combat boots; two steps of a walk -------------------------

    private static Canvas Contestant() => new Canvas(16, 16)
        .Ellipse(8, 9.5, 4.6, 4.2, 'q').Ellipse(8, 9, 3.6, 3.4, 'p')
        .Box(5, 8, 10, 8, 'q').Box(7, 9, 8, 11, 'y')
        .Ellipse(8, 4.2, 3.2, 3.2, 'q').Ellipse(8, 3.8, 2.8, 2.6, 'p').Box(5, 4, 10, 5, 'k').Box(6, 4, 9, 4, 'c')
        .Box(2, 8, 3, 11, 's').Box(12, 8, 13, 11, 's').Box(2, 7, 3, 7, 'q').Box(12, 7, 13, 7, 'q');

    public static readonly Sprite ContestantA = Contestant().Box(5, 13, 6, 15, 'd').Box(9, 13, 10, 14, 'd').Box(5, 15, 6, 15, 'k').Box(9, 14, 10, 14, 'k').Done();
    public static readonly Sprite ContestantB = Contestant().Box(5, 13, 6, 14, 'd').Box(9, 13, 10, 15, 'd').Box(5, 14, 6, 14, 'k').Box(9, 15, 10, 15, 'k').Done();

    // ----- the grunts: green, bald, in orange overalls, with a club ----------------------------------------

    private static Canvas Grunt() => new Canvas(16, 16)
        .Ellipse(8, 10, 4.8, 4.4, 'O').Ellipse(8, 9.6, 3.8, 3.4, 'o').Box(6, 7, 6, 12, 'O').Box(9, 7, 9, 12, 'O')
        .Ellipse(8, 4.4, 3.4, 3.2, 'G').Ellipse(8, 4, 3, 2.8, 'g').Box(6, 3, 7, 3, 'k').Box(9, 3, 10, 3, 'k').Dot(6, 3, 'y').Dot(10, 3, 'y')
        .Box(6, 6, 10, 6, 'G').Dot(7, 6, 'w').Dot(9, 6, 'w')
        .Box(2, 8, 3, 11, 'g');

    public static readonly Sprite GruntA = Grunt().Box(12, 8, 13, 11, 'g').Box(13, 1, 14, 9, 'n').Box(12, 0, 15, 2, 'N')
        .Box(5, 13, 6, 15, 'G').Box(9, 13, 10, 14, 'G').Done();
    public static readonly Sprite GruntB = Grunt().Box(12, 8, 13, 11, 'g').Box(13, 3, 14, 10, 'n').Box(12, 2, 15, 4, 'N')
        .Box(5, 13, 6, 14, 'G').Box(9, 13, 10, 15, 'G').Done();

    public static IEnumerable<Sprite> All => [ContestantA, ContestantB, GruntA, GruntB];
}
