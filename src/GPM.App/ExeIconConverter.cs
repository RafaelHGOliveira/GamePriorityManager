using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GPM.App;

/// exe path -> its embedded icon as an ImageSource (spec "UI": icons from the exe).
public sealed class ExeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        if (icon is null) return null;
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
