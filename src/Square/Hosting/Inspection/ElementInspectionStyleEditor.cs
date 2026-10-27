using System.Text;
using Square.CSS.Ast;
using Square.CSS.Engine;
using Square.CSS.Properties;
using Square.UI;
using Square.UI.ElementApi;

namespace Square.Hosting;

/// <summary>
/// 运行时样式编辑器：把 <c>element.style</c> 与已匹配的 author 规则暴露为可编辑的虚拟资源。
/// 只修改运行中的应用；虚拟资源 URL 明确是运行时位置，不写回源文件，也不声称原文件位置。
/// 仅在 Dispatcher 线程使用，不做内部加锁。
/// </summary>
internal sealed class ElementInspectionStyleEditor(Element root)
{
    private const string InlinePrefix = "square-inline-";
    private const string RulePrefix = "square-rule-";
    private readonly Element _root = root;

    /// <summary>inline 虚拟资源：原始草稿保留检查器写入的注释与无效行，revision 变化即丢弃重建。</summary>
    private sealed class InlineResource(Element element)
    {
        public readonly Element Element = element;
        public string DraftText = "";
        public long Revision = -1;
        public string StyleSheetId => InlinePrefix + Element.DebugId;
    }

    /// <summary>author 规则组虚拟资源：原始声明文本保存在 engine 覆盖中，选择器组按 engine 顺序拼接。</summary>
    private sealed class RuleResource
    {
        public required string StyleSheetId { get; init; }
        public required CssEngine Engine { get; init; }
        /// <summary>该声明列表组的代表 AST 规则；覆盖以原始声明列表引用为键，任意成员等价。</summary>
        public required CssRule Rule { get; init; }
        public required CssCascadeOrigin Origin { get; init; }
        public required string SelectorGroup { get; set; }
    }

    internal static IReadOnlyList<ElementInspectionStyleDeclaration> CreateDeclarationSnapshots(IReadOnlyList<Declaration> declarations)
    {
        var result = new ElementInspectionStyleDeclaration[declarations.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var declaration = declarations[index];
            result[index] = new ElementInspectionStyleDeclaration(
                declaration.Property, declaration.Value, declaration.Important,
                ParsedOk: CssPropertyRegistry.IsValid(StyleAccessor.NormalizePropertyName(declaration.Property), declaration.Value));
        }
        return result;
    }

    private readonly Dictionary<int, InlineResource> _inlineResources = [];
    private readonly Dictionary<(CssEngine Engine, List<Declaration> Declarations), RuleResource> _ruleResourcesByKey = [];
    private readonly Dictionary<string, RuleResource> _ruleResourcesById = [];
    private int _nextRuleNumber = 1;

    /// <summary>读取元素样式的完整可编辑快照：computed、inline 与 matched 规则（UA 无可编辑 ID/range）。</summary>
    internal ElementInspectionStyleSnapshot BuildSnapshot(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var matched = CssStyleReconciler.GetMatchedRules(element);
        RefreshRuleResources(CssStyleReconciler.GetInspectionEngines(_root));

        var rules = new ElementInspectionStyleRule[matched.Count];
        for (var index = 0; index < matched.Count; index++)
        {
            var rule = matched[index];
            if (rule.Source is { } source && source.Origin == CssCascadeOrigin.Author &&
                _ruleResourcesByKey.TryGetValue((source.Engine, source.Rule.Declarations), out var resource))
            {
                var style = BuildRuleText(resource);
                rules[index] = new ElementInspectionStyleRule(
                    rule.Selector, style.Declarations, Origin: "regular", Style: style);
            }
            else
            {
                rules[index] = new ElementInspectionStyleRule(
                    rule.Selector, CreateDeclarationSnapshots(rule.Declarations),
                    Origin: rule.Source is { } ruleSource && ruleSource.Origin == CssCascadeOrigin.UserAgent
                        ? "user-agent"
                        : "regular",
                    Style: null);
            }
        }

        return new ElementInspectionStyleSnapshot(
            element.Style.GetAll(),
            element.Style.CssText,
            rules,
            InlineStyle: BuildInlineText(GetOrCreateInline(element)));
    }

    /// <summary>枚举当前树全部虚拟资源：author 规则组在前，含 inline 声明的元素在后；顺带清理解绑/重建的旧资源。</summary>
    internal IReadOnlyList<ElementInspectionStyleText> CaptureStyleSheets()
    {
        var engines = CssStyleReconciler.GetInspectionEngines(_root);
        RefreshRuleResources(engines);

        var results = new List<ElementInspectionStyleText>();
        foreach (var engine in engines)
            foreach (var resource in _ruleResourcesById.Values)
                if (ReferenceEquals(resource.Engine, engine) && resource.Origin == CssCascadeOrigin.Author)
                    results.Add(BuildRuleText(resource));

        CaptureInlineSheets(results);
        return results;
    }

    /// <summary>按 ID 查询单个虚拟资源；解绑/重建或未知的 ID 一律不存在。</summary>
    internal ElementInspectionStyleText InspectStyleSheet(string styleSheetId)
    {
        if (!string.IsNullOrEmpty(styleSheetId) &&
            styleSheetId.StartsWith(InlinePrefix, StringComparison.Ordinal) &&
            int.TryParse(styleSheetId[InlinePrefix.Length..], out var debugId))
        {
            if (FindElementByDebugId(_root, debugId) is { } element)
                return BuildInlineText(GetOrCreateInline(element));
            _inlineResources.Remove(debugId);
            throw new KeyNotFoundException("Style sheet not found.");
        }

        RefreshRuleResources(CssStyleReconciler.GetInspectionEngines(_root));
        if (_ruleResourcesById.TryGetValue(styleSheetId, out var resource))
        {
            if (resource.Origin != CssCascadeOrigin.Author)
                throw new InvalidOperationException("Style sheet is read-only.");
            return BuildRuleText(resource);
        }

        throw new KeyNotFoundException("Style sheet not found.");
    }

    /// <summary>
    /// 原子应用整批样式编辑：先解析全部 live 资源并按顺序暂存每段替换（范围校验 + 解析 + 赋值准备），
    /// 全部成功后才按资源逐个提交；任何未知/失效/只读资源或错误范围都不产生任何修改。
    /// </summary>
    internal IReadOnlyList<ElementInspectionStyleText> ApplyEdits(IReadOnlyList<ElementInspectionStyleTextEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0) return [];

        RefreshRuleResources(CssStyleReconciler.GetInspectionEngines(_root));

        // Resolve the entire live batch, including private read-only UA resources.
        var resolved = new object[edits.Count];
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            ArgumentNullException.ThrowIfNull(edit);
            resolved[index] = ResolveResource(edit.StyleSheetId);
        }

        // 阶段二：按数组顺序对暂存文本应用各段替换；同一资源的后续编辑基于前一段结果。
        var stagedBlocks = new Dictionary<object, (string Block, IReadOnlyList<CssDeclarationTextEntry> Entries)>();
        var results = new ElementInspectionStyleText[edits.Count];
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            var resource = resolved[index];
            var block = stagedBlocks.TryGetValue(resource, out var pending) ? pending.Block : CurrentBlock(resource);
            var blockOffset = resource is RuleResource ruleResource ? ruleResource.SelectorGroup.Length + 3 : 0;
            var text = resource is RuleResource ruleText
                ? ruleText.SelectorGroup + " {\n" + block + "\n}"
                : block;

            var start = GetOffset(text, edit.Range);
            var end = GetOffset(text, edit.Range.EndLine, edit.Range.EndColumn);
            var fullBlock = start == blockOffset && end == blockOffset + block.Length;
            if (start > end || start < blockOffset || end > blockOffset + block.Length ||
                (!fullBlock && (!CssDeclarationText.IsEditBoundary(text, start) || !CssDeclarationText.IsEditBoundary(text, end))))
                throw new ArgumentException("Invalid style edit range.");

            var localStart = start - blockOffset;
            var localEnd = end - blockOffset;
            var newBlock = block[..localStart] + edit.Text + block[localEnd..];
            var entries = CssDeclarationText.Parse(newBlock);
            stagedBlocks[resource] = (newBlock, entries);
            results[index] = resource is InlineResource inline
                ? BuildInlineText(inline, newBlock, entries)
                : BuildRuleText((RuleResource)resource, newBlock, entries);
        }

        var preparedRules = new Dictionary<RuleResource, IReadOnlyList<Declaration>>();
        foreach (var (resource, (_, entries)) in stagedBlocks)
        {
            if (resource is not RuleResource rule) continue;
            var declarations = new List<Declaration>(entries.Count);
            foreach (var entry in entries)
                if (entry.ParsedOk && !entry.Disabled)
                    declarations.Add(new Declaration(entry.Property, entry.Value, entry.Important));
            preparedRules.Add(rule, declarations);
        }
        cancellationToken.ThrowIfCancellationRequested();

        // 阶段三：提交；编辑后的样式不随断开回滚，覆盖仅随 engine 重建消失。
        foreach (var (resource, (block, entries)) in stagedBlocks)
        {
            if (resource is InlineResource inline)
            {
                inline.Element.Style.SetInspectionDeclarations(entries);
                inline.DraftText = block;
                inline.Revision = inline.Element.Style.InlineStyleRevision;
            }
            else
            {
                var ruleResource = (RuleResource)resource;
                if (!ruleResource.Engine.SetInspectionDeclarations(ruleResource.Rule, block, preparedRules[ruleResource]))
                    throw new KeyNotFoundException("Style sheet not found.");
            }
        }

        return results;
    }

    private object ResolveResource(string? styleSheetId)
    {
        if (!string.IsNullOrEmpty(styleSheetId) &&
            styleSheetId.StartsWith(InlinePrefix, StringComparison.Ordinal) &&
            int.TryParse(styleSheetId[InlinePrefix.Length..], out var debugId))
        {
            if (FindElementByDebugId(_root, debugId) is { } element)
                return GetOrCreateInline(element);
            _inlineResources.Remove(debugId);
            throw new KeyNotFoundException("Style sheet not found.");
        }

        if (!string.IsNullOrEmpty(styleSheetId) && _ruleResourcesById.TryGetValue(styleSheetId, out var resource))
        {
            if (resource.Origin != CssCascadeOrigin.Author)
                throw new InvalidOperationException("Style sheet is read-only.");
            return resource;
        }

        throw new KeyNotFoundException("Style sheet not found.");
    }

    private InlineResource GetOrCreateInline(Element element) =>
        _inlineResources.TryGetValue(element.DebugId, out var resource)
            ? resource
            : _inlineResources[element.DebugId] = new InlineResource(element);

    /// <summary>Catalog loaded groups; UA identities stay private and can only reject writes.</summary>
    private void RefreshRuleResources(IReadOnlyList<CssEngine> engines)
    {
        if (_ruleResourcesByKey.Count > 0)
        {
            HashSet<CssEngine>? currentEngines = null;
            List<(CssEngine, List<Declaration>)>? staleKeys = null;
            foreach (var key in _ruleResourcesByKey.Keys)
            {
                currentEngines ??= new HashSet<CssEngine>(engines, ReferenceEqualityComparer.Instance);
                if (!currentEngines.Contains(key.Engine)) (staleKeys ??= []).Add(key);
            }

            if (staleKeys != null)
                foreach (var key in staleKeys)
                    RemoveRuleResource(key);
        }

        foreach (var engine in engines)
        {
            var rules = engine.GetInspectionRules();
            var order = new List<(CssEngine, List<Declaration>)>();
            var groups = new Dictionary<(CssEngine, List<Declaration>), (List<string> Selectors, CssRule Rule, CssCascadeOrigin Origin)>();
            foreach (var rule in rules)
            {
                if (rule.Source is not { } source) continue;
                var key = (engine, source.Rule.Declarations);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = ([], source.Rule, source.Origin);
                    groups[key] = group;
                    order.Add(key);
                }

                group.Selectors.Add(rule.Selector);
                if (source.Origin == CssCascadeOrigin.Author && group.Origin != CssCascadeOrigin.Author)
                    groups[key] = (group.Selectors, source.Rule, source.Origin);
            }

            if (_ruleResourcesByKey.Count > 0)
            {
                List<(CssEngine, List<Declaration>)>? engineStale = null;
                foreach (var key in _ruleResourcesByKey.Keys)
                    if (ReferenceEquals(key.Engine, engine) && !groups.ContainsKey(key))
                        (engineStale ??= []).Add(key);
                if (engineStale != null)
                    foreach (var key in engineStale)
                        RemoveRuleResource(key);
            }

            foreach (var key in order)
            {
                var (selectors, representative, origin) = groups[key];
                var selectorGroup = string.Join(", ", selectors);
                if (_ruleResourcesByKey.TryGetValue(key, out var resource))
                {
                    if (resource.Origin == origin)
                    {
                        resource.SelectorGroup = selectorGroup;
                        continue;
                    }
                    RemoveRuleResource(key);
                }

                resource = new RuleResource
                {
                    StyleSheetId = RulePrefix + _nextRuleNumber++,
                    Engine = engine,
                    Rule = representative,
                    Origin = origin,
                    SelectorGroup = selectorGroup,
                };
                _ruleResourcesByKey[key] = resource;
                _ruleResourcesById[resource.StyleSheetId] = resource;
            }
        }
    }

    private void RemoveRuleResource((CssEngine Engine, List<Declaration> Declarations) key)
    {
        if (_ruleResourcesByKey.Remove(key, out var resource))
            _ruleResourcesById.Remove(resource.StyleSheetId);
    }

    private void CaptureInlineSheets(List<ElementInspectionStyleText> results)
    {
        var seen = new HashSet<int>();
        CollectInlineSheets(_root, results, seen);
        if (_inlineResources.Count == 0) return;
        List<int>? stale = null;
        foreach (var debugId in _inlineResources.Keys)
            if (!seen.Contains(debugId))
                (stale ??= []).Add(debugId);
        if (stale != null)
            foreach (var debugId in stale)
                _inlineResources.Remove(debugId);
    }

    private void CollectInlineSheets(Element element, List<ElementInspectionStyleText> results, HashSet<int> seen)
    {
        seen.Add(element.DebugId);
        if (element.Style.Length > 0 || _inlineResources.ContainsKey(element.DebugId))
        {
            results.Add(BuildInlineText(GetOrCreateInline(element)));
        }

        foreach (var child in element.Children)
            CollectInlineSheets(child, results, seen);
    }

    /// <summary>inline 当前声明块：revision 变化（应用显式替换，含同值替换）即丢弃草稿并按 authored 声明重建。</summary>
    private string CurrentBlock(InlineResource inline)
    {
        var revision = inline.Element.Style.InlineStyleRevision;
        if (revision != inline.Revision)
        {
            inline.DraftText = inline.Element.Style.CssText;
            inline.Revision = revision;
        }

        return inline.DraftText;
    }

    private string CurrentBlock(object resource) => resource switch
    {
        InlineResource inline => CurrentBlock(inline),
        RuleResource rule => CurrentBlock(rule),
        _ => throw new InvalidOperationException("Unreachable resource kind."),
    };

    private static string CurrentBlock(RuleResource resource) =>
        resource.Engine.GetInspectionDeclarationText(resource.Rule) ?? SerializeDeclarations(resource.Rule.Declarations);

    private ElementInspectionStyleText BuildInlineText(InlineResource inline)
    {
        var text = CurrentBlock(inline);
        return BuildInlineText(inline, text, CssDeclarationText.Parse(text));
    }

    private static ElementInspectionStyleText BuildInlineText(InlineResource inline, string text,
        IReadOnlyList<CssDeclarationTextEntry> entries) => new(
        inline.StyleSheetId, text, CreateRange(text, 0, text.Length),
        IsInline: true, OwnerDebugId: inline.Element.DebugId, Origin: "regular",
        Declarations: CreateDeclarations(text, entries, blockOffset: 0));

    private static ElementInspectionStyleText BuildRuleText(RuleResource resource)
    {
        var block = CurrentBlock(resource);
        return BuildRuleText(resource, block, CssDeclarationText.Parse(block));
    }

    private static ElementInspectionStyleText BuildRuleText(RuleResource resource, string block,
        IReadOnlyList<CssDeclarationTextEntry> entries)
    {
        var text = resource.SelectorGroup + " {\n" + block + "\n}";
        var blockOffset = resource.SelectorGroup.Length + 3;
        return new ElementInspectionStyleText(
            resource.StyleSheetId,
            text,
            CreateRange(text, blockOffset, blockOffset + block.Length),
            IsInline: false,
            OwnerDebugId: null,
            Origin: resource.Origin == CssCascadeOrigin.UserAgent ? "user-agent" : "regular",
            Declarations: CreateDeclarations(text, entries, blockOffset));
    }

    /// <summary>声明行快照：Text/Range/Disabled/ParsedOk 保留原始行内容，坐标为完整虚拟资源坐标。</summary>
    private static ElementInspectionStyleDeclaration[] CreateDeclarations(
        string text, IReadOnlyList<CssDeclarationTextEntry> entries, int blockOffset)
    {
        var result = new ElementInspectionStyleDeclaration[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            result[index] = new ElementInspectionStyleDeclaration(
                entry.Property,
                entry.Value,
                entry.Important,
                Range: CreateRange(text, entry.StartOffset + blockOffset, entry.EndOffset + blockOffset),
                Text: entry.Text,
                Disabled: entry.Disabled,
                ParsedOk: entry.ParsedOk);
        }

        return result;
    }

    /// <summary>按 AST 声明序列化原始声明块文本，每条声明独占一行。</summary>
    private static string SerializeDeclarations(IReadOnlyList<Declaration> declarations)
    {
        var builder = new StringBuilder();
        foreach (var declaration in declarations)
        {
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(declaration.Property).Append(": ").Append(declaration.Value);
            if (declaration.Important) builder.Append(" !important");
            builder.Append(';');
        }

        return builder.ToString();
    }

    private static ElementInspectionStyleRange CreateRange(string text, int startOffset, int endOffset)
    {
        var (startLine, startColumn) = GetLineColumn(text, startOffset);
        var (endLine, endColumn) = GetLineColumn(text, endOffset);
        return new ElementInspectionStyleRange(startLine, startColumn, endLine, endColumn);
    }

    /// <summary>行/列按 UTF-16 code units 计，CRLF 算一次换行，end exclusive。</summary>
    private static (int Line, int Column) GetLineColumn(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }

        return (line, offset - lineStart);
    }

    private static int GetOffset(string text, int line, int column)
    {
        if (line < 0 || column < 0) throw CreateRangeError();
        var offset = 0;
        for (var remaining = line; remaining > 0; remaining--)
        {
            var newline = text.IndexOf('\n', offset);
            if (newline < 0) throw CreateRangeError();
            offset = newline + 1;
        }

        if (column > text.Length - offset) throw CreateRangeError();
        return offset + column;
    }

    private static int GetOffset(string text, ElementInspectionStyleRange range) =>
        GetOffset(text, range.StartLine, range.StartColumn);

    private static ArgumentException CreateRangeError() => new("Invalid style edit range.");

    private static Element? FindElementByDebugId(Element element, int debugId)
    {
        if (element.DebugId == debugId) return element;
        foreach (var child in element.Children)
        {
            var found = FindElementByDebugId(child, debugId);
            if (found != null) return found;
        }

        return null;
    }
}
