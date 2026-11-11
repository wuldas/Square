namespace Square.Rendering.Yoga
{
    internal enum Gutter : byte
    {
        Column = 0,
        Row = 1,
        All = 2
    }

    internal static class GutterExtensions
    {
        public const int OrdinalCount = 3;

        public static Gutter ScopedEnum(int unscoped)
        {
            return (Gutter)unscoped;
        }

        public static int UnscopedEnum(Gutter scoped)
        {
            return (int)scoped;
        }

        public static string ToString(Gutter e)
        {
            return e switch
            {
                Gutter.Column => "GutterColumn",
                Gutter.Row => "GutterRow",
                Gutter.All => "GutterAll",
                _ => throw new ArgumentOutOfRangeException(nameof(e))
            };
        }
    }
}

