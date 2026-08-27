using Square.Compiler.ParserCore;
using Square.Compiler.Template.Compatibility;

namespace Square.Compiler.Parser
{
    internal static class SqxParser
    {
        public static SqxDocument Parse(string source, string fileName)
        {
            try
            {
                var core = SqxCoreParser.Parse(source, fileName, new SqxCoreParserOptions
                {
                    StrictTemplate = true,
                    CaseSensitiveSectionNames = true
                });
                var document = ConvertDocument(core);
                SqxValidator.Validate(document.Template.Roots);
                return document;
            }
            catch (CoreParseException exception)
            {
                throw new SqxParseException(exception.Message, exception.Position, length: exception.Length);
            }
        }

        private static SqxDocument ConvertDocument(CoreDocument core)
        {
            var document = new SqxDocument
            {
                Syntax = core.Syntax,
                SourcePath = core.SourcePath,
                Name = core.Script != null && core.Script.ComponentName != null
                    ? core.Script.ComponentName
                    : core.FileName,
                Template = new SqxTemplate
                {
                    Roots = TemplateIrCompatibilityAdapter.ToSqxNodes(
                        core.Syntax.Template.Ir,
                        core.Syntax.SourceText,
                        core.Syntax.Dialect,
                        core.Syntax.Template.ContentRange.Offset)
                }
            };
            if (core.Script != null)
            {
                document.Namespace = core.Script.Namespace;
                document.Access = core.Script.Access;
            }
            return document;
        }


    }

    internal sealed class SqxParseException : Exception
    {
        public int Position { get; }
        public int Length { get; }
        public string DiagnosticId { get; }

        public SqxParseException(
            string message,
            int position,
            string diagnosticId = null,
            int length = 0) : base(message)
        {
            Position = position;
            Length = length;
            DiagnosticId = diagnosticId;
        }
    }
}
