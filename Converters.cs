using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ChatRoomReset.Pages;

public class BoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var b = value is bool vb && vb;
        if (parameter is string p && p == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
