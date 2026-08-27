using Square.Compiler.Parser;
using Square.Compiler.ParserCore;
using Square.Compiler.Template.Compatibility;

namespace Square.Compiler.LanguageServices;

internal static class SqxCoreParserFacade
{
    public static Square.Compiler.Parser.SqxDocument Parse(
        string source,
        string sourcePath,
        bool strictTemplate,
        bool tolerant = false)
    {
        var core = SqxCoreParser.Parse(source, sourcePath, new SqxCoreParserOptions
        {
            StrictTemplate = strictTemplate,
            CaseSensitiveSectionNames = true,
            Tolerant = tolerant
        });

        return ConvertDocument(core);
    }

    private static Square.Compiler.Parser.SqxDocument ConvertDocument(CoreDocument core)
    {
        var document = new Square.Compiler.Parser.SqxDocument
        {
            Syntax = core.Syntax,
            SourcePath = core.SourcePath,
            Name = core.Script != null && core.Script.ComponentName != null
                ? core.Script.ComponentName
                : core.FileName,
            Template = new Square.Compiler.Parser.SqxTemplate
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
