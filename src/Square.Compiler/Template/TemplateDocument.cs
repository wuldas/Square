using Square.Compiler.Parser;
using Square.Compiler.Syntax;

namespace Square.Compiler.Template;

/// <summary>语言无关的模板 IR 文档入口；SQX 与 SQV 前端都降低到该模型。</summary>
internal sealed class TemplateDocument
{
    public string SourcePath = "";
    public ComponentDocumentSyntax Syntax;
    public string Name = "";
    public List<SqxNode> Roots = new();
    public string ScriptCode;
    public string ScriptLang;
    public string StyleCode;
    public string Namespace;
    public string Access = "public";

    public static TemplateDocument From(SqxDocument document) => new()
    {
        SourcePath = document.SourcePath,
        Syntax = document.Syntax,
        Name = document.Name,
        Roots = document.Template.Roots,
        ScriptCode = document.ScriptCode,
        ScriptLang = document.ScriptLang,
        StyleCode = document.StyleCode,
        Namespace = document.Namespace,
        Access = document.Access
    };
}
