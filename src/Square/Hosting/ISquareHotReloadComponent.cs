using System.ComponentModel;

namespace Square.Hosting;

/// <summary>Runtime contract implemented by generated Square components in Debug builds.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISquareHotReloadComponent
{
    /// <summary>Whether the generated template or component style changed.</summary>
    bool HasHotReloadChanges { get; }

    /// <summary>Rebuilds the generated subtree while preserving the component instance.</summary>
    void RebuildAfterHotReload();
}
