namespace Java.Lang
{
    public class Object : IDisposable
    {
        public void Dispose() { }
    }
}

namespace Android.OS
{
    internal static class SystemClock
    {
        internal static long Now { get; set; }
        internal static long UptimeMillis() => Now;
    }
}

namespace Android.Views
{
    // A deterministic native boundary: the production scheduler is linked unchanged.
    public sealed class Choreographer
    {
        public interface IFrameCallback
        {
            void DoFrame(long frameTimeNanos);
        }

        internal static Choreographer Instance { get; set; } = new();
        internal IFrameCallback? Pending { get; private set; }
        internal long Deadline { get; private set; }
        internal int Posts { get; private set; }
        internal int Removes { get; private set; }

        internal void PostFrameCallback(IFrameCallback callback) => PostFrameCallbackDelayed(callback, 0);
        internal void PostFrameCallbackDelayed(IFrameCallback callback, long delayMillis)
        {
            if (Pending != null) throw new InvalidOperationException("More than one native callback pending.");
            Pending = callback;
            Deadline = Android.OS.SystemClock.Now + delayMillis;
            Posts++;
        }

        internal void RemoveFrameCallback(IFrameCallback callback)
        {
            if (ReferenceEquals(Pending, callback)) Pending = null;
            Removes++;
        }

        internal void Dispatch()
        {
            if (Pending == null || Android.OS.SystemClock.Now < Deadline) return;
            var callback = Pending;
            Pending = null;
            callback.DoFrame(0);
        }
    }
}
