using System;

namespace Square.Rendering.Yoga.Debug
{
    internal static class AssertFatal
    {
        private static void FatalWithMessage(string message)
        {
            throw new InvalidOperationException(message);
        }

        public static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                YogaLog.Log(LogLevel.Fatal, message);
                FatalWithMessage(message);
            }
        }

        public static void AssertWithNode(Node node, bool condition, string message)
        {
            if (!condition)
            {
                YogaLog.Log(node, LogLevel.Fatal, message);
                FatalWithMessage(message);
            }
        }

        public static void AssertWithConfig(Config config, bool condition, string message)
        {
            if (!condition)
            {
                YogaLog.Log(config, LogLevel.Fatal, message);
                FatalWithMessage(message);
            }
        }
    }
}

