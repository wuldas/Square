using Square.UI;

[assembly: ElementExport("urn:square:codeeditor", "codeeditor", "CodeEditor", typeof(Square.Extensions.CodeEditor.CodeEditor))]

namespace Square.Extensions.CodeEditor;

/// <summary>
/// 注册 <see cref="CodeEditor"/> 控件标签与内置语言/主题。
/// 与 <c>Square.Extensions.ExtensionRegistration</c> 独立；引用本程序集后需显式调用。
/// </summary>
public static class CodeEditorRegistration
{
    private static bool _registered;

    /// <summary>
    /// 幂等注册：<c>CodeEditor</c> 元素标签，以及内置语言/主题贡献。
    /// </summary>
    public static void RegisterDefaults()
    {
        // Always ensure language/theme built-ins (idempotent).
        LanguageRegistry.EnsureBuiltIns();
        CodeEditorThemeRegistry.EnsureBuiltIns();

        if (_registered) return;
        _registered = true;
        ElementRegistry.Register("CodeEditor", static () => new CodeEditor());
    }
}
