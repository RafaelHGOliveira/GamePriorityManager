using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GPM.App;

/// exe path -> its embedded icon (spec "UI": icons from the exe). Extraction reads the
/// file, so it runs off the UI thread, and each path is extracted once per session.
public static class ExeIcons
{
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Task<ImageSource?> GetAsync(string? path) =>
        path is null
            ? Task.FromResult<ImageSource?>(null)
            : Cache.GetOrAdd(path, p => Task.Run(() => Extract(p)));

    private static ImageSource? Extract(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // frozen = usable from the UI thread
            return source;
        }
        catch (Exception)
        {
            return null; // corrupt/locked/inaccessible exe shouldn't crash the row's icon
        }
    }
}
