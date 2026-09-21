using Square.Compiler.Syntax;

namespace Square.Compiler.ParserCore
{
    internal sealed class CoreScript
    {
        public string Language = "csharp";
        public string Code = "";
        public string Namespace;
        public string ComponentName;
        public string Access = "public";
        public int Line;
        public int Column;
    }

    internal sealed class CoreStyle
    {
        public string Css = "";
        public int Line;
        public int Column;
    }

    internal sealed class CoreDocument
    {
        public ComponentDocumentSyntax Syntax;
        public string FileName = "";
        public string SourcePath = "";
        public CoreScript Script;
        public CoreStyle Style;
    }
}
