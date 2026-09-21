using Square.Compiler.LanguageServices;

namespace Square.LanguageServer;

internal sealed class DocumentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DocumentState> _documents = new(StringComparer.Ordinal);
    private long _revision;

    public long Revision
    {
        get { lock (_gate) return _revision; }
    }

    public bool Open(string uri, int version, string text)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(uri, out var existing) && version < existing.Version) return false;
            _documents[uri] = new DocumentState(uri, version, text);
            _revision++;
            return true;
        }
    }

    public bool Change(string uri, int version, string text)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(uri, out var existing) && version <= existing.Version) return false;
            _documents[uri] = new DocumentState(uri, version, text);
            _revision++;
            return true;
        }
    }

    public void Close(string uri)
    {
        lock (_gate)
        {
            if (_documents.Remove(uri)) _revision++;
        }
    }

    public bool TryGet(string uri, out DocumentState? document)
    {
        lock (_gate) return _documents.TryGetValue(uri, out document);
    }

    public IReadOnlyList<DocumentState> All
    {
        get { lock (_gate) return _documents.Values.ToArray(); }
    }

    internal sealed class DocumentState
    {
        public DocumentState(string uri, int version, string text)
        {
            Uri = uri;
            Version = version;
            Text = text;
        }

        public string Uri { get; }
        public int Version { get; }
        public string Text { get; }
    }
}
