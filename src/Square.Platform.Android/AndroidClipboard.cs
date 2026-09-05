using Android.Content;

namespace Square.Platform.Android;

/// <summary>Android 系统剪贴板桥接。</summary>
public static class AndroidClipboard
{
    /// <summary>读取主剪贴板的 Unicode 文本。</summary>
    public static string GetText(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var manager = context.GetSystemService(Context.ClipboardService) as global::Android.Content.ClipboardManager;
        var clip = manager?.PrimaryClip;
        if (clip == null || clip.ItemCount == 0) return "";
        return clip.GetItemAt(0)?.CoerceToText(context)?.ToString() ?? "";
    }

    /// <summary>写入主剪贴板的 Unicode 文本。</summary>
    public static void SetText(Context context, string text)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(text);
        var manager = context.GetSystemService(Context.ClipboardService) as global::Android.Content.ClipboardManager;
        if (manager != null) manager.PrimaryClip = ClipData.NewPlainText("Square", text);
    }
}
