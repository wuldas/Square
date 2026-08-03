using Square.UI;

namespace Square.Native;

/// <summary>从 Square Element Tree 创建原生 UI 语义快照。</summary>
public static class NativeUiTreeBuilder
{
    /// <summary>构建元素及其当前可见子树的只读快照。</summary>
    public static NativeUiNode Snapshot(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return SnapshotCore(root);
    }

    private static NativeUiNode SnapshotCore(Element element) => new()
    {
        Kind = element.TagName,
        SourceElement = element,
        Bounds = element.Geometry,
        Style = element.Style.GetAll(),
        Classes = element.ClassList.GetAll().OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
        Children = element.Children
            .Where(static child => child.IsVisible)
            .Select(SnapshotCore)
            .ToArray()
    };
}
