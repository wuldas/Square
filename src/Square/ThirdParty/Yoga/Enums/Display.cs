namespace Square.Rendering.Yoga
{
    internal enum Display
    {
        Flex = 0,
        None = 1,
        Contents = 2,
        Grid = 3,
    }

    internal static partial class YogaEnums
    {
    }

    internal static class DisplayExtensions
    {
        public static string ToString(this Display display)
        {
            return display switch
            {
                Display.Flex => "flex",
                Display.None => "none",
                Display.Contents => "contents",
                Display.Grid => "grid",
                _ => "unknown"
            };
        }
    }
}

