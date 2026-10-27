using System.Text;

namespace Square.DevTools;

/// <summary>
/// Process-wide tee for managed <see cref="System.Console"/> output. The first
/// subscriber installs forwarding writers over the current <c>Console.Out</c> and
/// <c>Console.Error</c>; every subscriber receives whole lines (or 8192-character
/// chunks of overlong lines) through its callback while the original output and the
/// original writer exceptions keep flowing unchanged. Restoring the original writers
/// is deferred until the last subscription is disposed, and only when the installed
/// writers are still the current ones, so an application that replaced
/// <c>Console.Out</c> meanwhile is never overwritten.
/// </summary>
internal static class DevToolsConsoleCapture
{
    private const int MaxLineUnits = 8192;

    private static readonly object Gate = new();
    private static readonly List<Action<bool, string>> Subscribers = [];
    private static Action<bool, string>[]? Snapshot;
    private static bool Installed;
    private static TextWriter? SavedOut;
    private static TextWriter? SavedError;
    private static TextWriter? InstalledOut;
    private static TextWriter? InstalledError;

    /// <summary>
    /// Starts forwarding managed console output to <paramref name="callback"/>.
    /// The boolean parameter is <see langword="true"/> for <c>Console.Error</c> lines
    /// and <see langword="false"/> for <c>Console.Out</c> lines. The callback runs
    /// synchronously while the console write completes and must not await network
    /// work or write to the captured <see cref="System.Console"/>.
    /// </summary>
    public static IDisposable Subscribe(Action<bool, string> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (Gate)
        {
            if (!Installed) Install();
            Subscribers.Add(callback);
            PublishSnapshot();
            return new TeeSubscription(callback);
        }
    }

    private static void Install()
    {
        var savedOut = System.Console.Out;
        var savedError = System.Console.Error;
        var outWriter = TextWriter.Synchronized(new ForwardingWriter(savedOut, new LineAggregator(stderr: false)));
        var errorWriter = TextWriter.Synchronized(new ForwardingWriter(savedError, new LineAggregator(stderr: true)));
        try
        {
            System.Console.SetOut(outWriter);
            System.Console.SetError(errorWriter);
        }
        catch
        {
            if (ReferenceEquals(System.Console.Out, outWriter)) System.Console.SetOut(savedOut);
            if (ReferenceEquals(System.Console.Error, errorWriter)) System.Console.SetError(savedError);
            throw;
        }
        SavedOut = savedOut;
        SavedError = savedError;
        InstalledOut = outWriter;
        InstalledError = errorWriter;
        Installed = true;
    }

    private static void Unsubscribe(Action<bool, string> callback)
    {
        lock (Gate)
        {
            Subscribers.Remove(callback);
            if (Subscribers.Count > 0 || !Installed)
            {
                PublishSnapshot();
                return;
            }
            var savedOut = SavedOut;
            var savedError = SavedError;
            if (ReferenceEquals(System.Console.Out, InstalledOut) && savedOut != null)
                System.Console.SetOut(savedOut);
            if (ReferenceEquals(System.Console.Error, InstalledError) && savedError != null)
                System.Console.SetError(savedError);
            Installed = false;
            SavedOut = SavedError = InstalledOut = InstalledError = null;
            PublishSnapshot();
        }
    }

    private static void PublishSnapshot() =>
        Volatile.Write(ref Snapshot, Subscribers.Count == 0 ? null : [.. Subscribers]);

    /// <summary>
    /// Delivers one captured line or chunk. Callback exceptions are transport
    /// failures of a single subscriber: they must never surface through the
    /// application's own <see cref="System.Console"/> call, so they are swallowed.
    /// </summary>
    private static void Emit(bool stderr, string text)
    {
        var targets = Volatile.Read(ref Snapshot);
        if (targets is null) return;
        for (var i = 0; i < targets.Length; i++)
        {
            try { targets[i](stderr, text); }
            catch { /* Capturing must never break the application's console output. */ }
        }
    }

    /// <summary>
    /// Aggregates forwarded console fragments into lines. LF, CRLF and CR each end
    /// exactly one line (a trailing CR stays pending until the next fragment decides
    /// whether it continues as CRLF), empty lines are forwarded, a flush emits the
    /// pending partial line, and buffering beyond 8192 UTF-16 units emits the real
    /// text in chunks that never split a surrogate pair. All capture failures are
    /// contained: the original output has already been produced before this class
    /// sees the text.
    /// </summary>
    private sealed class LineAggregator(bool stderr)
    {
        private readonly object Lock = new();
        private readonly char[] Buffer = new char[MaxLineUnits];
        private int Length;
        private bool PendingCr;
        private bool HasSegments;

        public void Append(char value)
        {
            try
            {
                lock (Lock) AppendCore([value]);
            }
            catch { /* Capturing must never throw into the application. */ }
        }

        public void Append(ReadOnlySpan<char> text)
        {
            try
            {
                lock (Lock) AppendCore(text);
            }
            catch { /* Capturing must never throw into the application. */ }
        }

        public void Append(StringBuilder text)
        {
            try
            {
                lock (Lock)
                {
                    foreach (var chunk in text.GetChunks()) AppendCore(chunk.Span);
                }
            }
            catch { /* Capturing must never throw into the application. */ }
        }


        public void Flush()
        {
            try
            {
                lock (Lock)
                {
                    if (Length == 0) return;
                    var take = Length - (char.IsHighSurrogate(Buffer[Length - 1]) ? 1 : 0);
                    if (take == 0) return;
                    Emit(new string(Buffer, 0, take));
                    HasSegments = true;
                    Length -= take;
                    if (Length > 0) Buffer[0] = Buffer[take];
                }
            }
            catch { /* Capturing must never throw into the application. */ }
        }

        private void AppendCore(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return;
            var index = 0;
            if (PendingCr)
            {
                // The pending CR already ended its line; only a following LF turns
                // it into a single CRLF ending.
                PendingCr = false;
                if (text.Length > 0 && text[0] == '\n') index = 1;
            }
            while (index < text.Length)
            {
                var scan = index;
                while (scan < text.Length && text[scan] is not ('\n' or '\r')) scan++;
                if (scan > index) AppendChunk(text.Slice(index, scan - index));
                if (scan == text.Length) return;
                var terminator = text[scan];
                if (terminator == '\r')
                {
                    if (scan + 1 < text.Length)
                        index = text[scan + 1] == '\n' ? scan + 2 : scan + 1;
                    else
                    {
                        index = scan + 1;
                        PendingCr = true;
                    }
                }
                else index = scan + 1;
                EndLineCore();
            }
        }

        private void AppendChunk(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                if (Length == MaxLineUnits) EmitChunk();
                var take = Math.Min(MaxLineUnits - Length, text.Length);
                text.Slice(0, take).CopyTo(Buffer.AsSpan(Length));
                Length += take;
                text = text.Slice(take);
                if (Length == MaxLineUnits) EmitChunk();
            }
        }

        private void EmitChunk()
        {
            var take = Length;
            if (char.IsHighSurrogate(Buffer[take - 1])) take--;
            Emit(new string(Buffer, 0, take));
            HasSegments = true;
            Length -= take;
            if (Length > 0) Buffer[0] = Buffer[take];
        }

        private void EndLineCore()
        {
            if (Length > 0 || !HasSegments) Emit(new string(Buffer, 0, Length));
            Length = 0;
            HasSegments = false;
        }

        private void Emit(string text) => DevToolsConsoleCapture.Emit(stderr, text);
    }

    /// <summary>
    /// Forwards every write to the original writer first and captures the same text
    /// afterwards. Originals are never disposed; typed and formatted overloads preserve
    /// the original dispatch and exceptions. Capture-only formatting failures stay isolated.
    /// </summary>
    private sealed class ForwardingWriter(TextWriter original, LineAggregator aggregator) : TextWriter
    {
        public override Encoding Encoding => original.Encoding;
        public override IFormatProvider FormatProvider => original.FormatProvider;
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string NewLine { get => original.NewLine; set => original.NewLine = value; }
        public override void Write(bool value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString());
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(bool value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString());
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(int value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(int value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(uint value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(uint value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(long value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(long value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(ulong value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(ulong value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(float value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(float value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(double value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(double value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(decimal value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(decimal value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value.ToString(FormatProvider));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(object? value)
        {
            original.Write(value);
            try
            {
                aggregator.Append(value?.ToString());
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(object? value)
        {
            original.WriteLine(value);
            try
            {
                aggregator.Append(value?.ToString());
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }


        public override void Write(string format, object? arg0)
        {
            original.Write(format, arg0);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg0));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(string format, object? arg0)
        {
            original.WriteLine(format, arg0);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg0));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(string format, object? arg0, object? arg1)
        {
            original.Write(format, arg0, arg1);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg0, arg1));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(string format, object? arg0, object? arg1)
        {
            original.WriteLine(format, arg0, arg1);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg0, arg1));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(string format, object? arg0, object? arg1, object? arg2)
        {
            original.Write(format, arg0, arg1, arg2);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg0, arg1, arg2));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(string format, object? arg0, object? arg1, object? arg2)
        {
            original.WriteLine(format, arg0, arg1, arg2);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg0, arg1, arg2));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(string format, params object?[] arg)
        {
            original.Write(format, arg);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(string format, params object?[] arg)
        {
            original.WriteLine(format, arg);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(string format, params ReadOnlySpan<object?> arg)
        {
            original.Write(format, arg);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg));
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void WriteLine(string format, params ReadOnlySpan<object?> arg)
        {
            original.WriteLine(format, arg);
            try
            {
                aggregator.Append(string.Format(FormatProvider, format, arg));
                aggregator.Append(original.NewLine);
            }
            catch { /* Capture formatting cannot alter the original write. */ }
        }

        public override void Write(char value)
        {
            original.Write(value);
            aggregator.Append(value);
        }

        public override void Write(char[]? buffer)
        {
            original.Write(buffer);
            if (buffer != null) aggregator.Append(buffer);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            original.Write(buffer, index, count);
            aggregator.Append(buffer.AsSpan(index, count));
        }

        public override void Write(string? value)
        {
            original.Write(value);
            if (value != null) aggregator.Append(value);
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            original.Write(buffer);
            aggregator.Append(buffer);
        }

        public override void Write(StringBuilder? value)
        {
            original.Write(value);
            if (value != null) aggregator.Append(value);
        }

        public override void WriteLine()
        {
            original.WriteLine();
            aggregator.Append(original.NewLine);
        }

        public override void WriteLine(char value)
        {
            original.WriteLine(value);
            aggregator.Append(value);
            aggregator.Append(original.NewLine);
        }

        public override void WriteLine(char[]? buffer)
        {
            original.WriteLine(buffer);
            if (buffer != null) aggregator.Append(buffer);
            aggregator.Append(original.NewLine);
        }

        public override void WriteLine(char[] buffer, int index, int count)
        {
            original.WriteLine(buffer, index, count);
            aggregator.Append(buffer.AsSpan(index, count));
            aggregator.Append(original.NewLine);
        }

        public override void WriteLine(string? value)
        {
            original.WriteLine(value);
            if (value != null) aggregator.Append(value);
            aggregator.Append(original.NewLine);
        }

        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            original.WriteLine(buffer);
            aggregator.Append(buffer);
            aggregator.Append(original.NewLine);
        }

        public override void WriteLine(StringBuilder? value)
        {
            original.WriteLine(value);
            if (value != null) aggregator.Append(value);
            aggregator.Append(original.NewLine);
        }

        public override void Flush()
        {
            original.Flush();
            aggregator.Flush();
        }

        public override async Task WriteAsync(char value)
        {
            await original.WriteAsync(value).ConfigureAwait(false);
            aggregator.Append(value);
        }

        public override async Task WriteAsync(string? value)
        {
            await original.WriteAsync(value).ConfigureAwait(false);
            if (value != null) aggregator.Append(value);
        }

        public override async Task WriteAsync(char[] buffer, int index, int count)
        {
            await original.WriteAsync(buffer, index, count).ConfigureAwait(false);
            aggregator.Append(buffer.AsSpan(index, count));
        }

        public override async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            await original.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            aggregator.Append(buffer.Span);
        }

        public override async Task WriteAsync(StringBuilder? value, CancellationToken cancellationToken = default)
        {
            await original.WriteAsync(value, cancellationToken).ConfigureAwait(false);
            if (value != null) aggregator.Append(value);
        }

        public override async Task WriteLineAsync(char value)
        {
            await original.WriteLineAsync(value).ConfigureAwait(false);
            aggregator.Append(value);
            aggregator.Append(original.NewLine);
        }

        public override async Task WriteLineAsync()
        {
            await original.WriteLineAsync().ConfigureAwait(false);
            aggregator.Append(original.NewLine);
        }

        public override async Task WriteLineAsync(string? value)
        {
            await original.WriteLineAsync(value).ConfigureAwait(false);
            if (value != null) aggregator.Append(value);
            aggregator.Append(original.NewLine);
        }

        public override async Task WriteLineAsync(char[] buffer, int index, int count)
        {
            await original.WriteLineAsync(buffer, index, count).ConfigureAwait(false);
            aggregator.Append(buffer.AsSpan(index, count));
            aggregator.Append(original.NewLine);
        }

        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            await original.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);
            aggregator.Append(buffer.Span);
            aggregator.Append(original.NewLine);
        }

        public override async Task WriteLineAsync(StringBuilder? value, CancellationToken cancellationToken = default)
        {
            await original.WriteLineAsync(value, cancellationToken).ConfigureAwait(false);
            if (value != null) aggregator.Append(value);
            aggregator.Append(original.NewLine);
        }

        public override async Task FlushAsync()
        {
            await original.FlushAsync().ConfigureAwait(false);
            aggregator.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await original.FlushAsync(cancellationToken).ConfigureAwait(false);
            aggregator.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) aggregator.Flush();
            // The original writer keeps working after the forwarding writer is
            // disposed; disposing it is the application's own decision.
            base.Dispose(disposing);
        }
    }

    private sealed class TeeSubscription(Action<bool, string> callback) : IDisposable
    {
        private Action<bool, string>? _callback = callback;

        public void Dispose()
        {
            lock (Gate)
            {
                var callback = _callback;
                if (callback is null) return;
                _callback = null;
                Unsubscribe(callback);
            }
        }
    }
}
