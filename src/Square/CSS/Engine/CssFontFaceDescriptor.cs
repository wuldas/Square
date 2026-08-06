using Square.Graphics;

namespace Square.CSS.Engine;

/// <summary>从 CSS <c>@font-face</c> 规则解析出的字体描述符。</summary>
public sealed record CssFontFaceDescriptor(
    string Family,
    string Source,
    FontWeight Weight,
    FontStyle Style,
    bool IsLocal);
