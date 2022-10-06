using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace BorderlessGaming.Next.Common.Converter;

class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        var invert = parameter?.ToString() == "true";
        var result = value == null;
        return invert ? !result : result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}