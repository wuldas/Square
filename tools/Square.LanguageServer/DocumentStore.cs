using Square.Compiler.LanguageServices;

namespace Square.LanguageServer;

internal sealed class DocumentStore
{
    private readonly Dictionary<string, DocumentState> _documents = new(StringComparer.Ordinal);

    public void Open(string uri, int version, string text)
    {
        _documents[uri] = new DocumentState(uri, version, text);
    }

    public void Change(string uri, int version, string text)
    {
        if (_documents.TryGetValue(uri, out var existing))
            _documents[uri] = new DocumentState(uri, version, text);
        else
            Open(uri, version, text);
    }

    public void Close(string uri) => _documents.Remove(uri);

    public bool TryGet(string uri, out DocumentState? document) =>
        _documents.TryGetValue(uri, out document);

    public IEnumerable<DocumentState> All => _documents.Values;

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
