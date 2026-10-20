using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace InstaShare.Views
{
    /// <summary>
    /// true → Visible. Pass "Invert" as the parameter for the opposite.
    /// Also treats null and empty strings as false, so it doubles as a "has value" check.
    /// </summary>
    public sealed class VisibleWhenConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var visible = value switch
            {
                bool b => b,
                string s => s.Length > 0,
                int i => i > 0,
                null => false,
                _ => true,
            };
            if (parameter as string == "Invert")
                visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Lets a selector's SelectedIndex drive an enum property.
    /// </summary>
    public sealed class EnumIndexConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Enum e ? System.Convert.ToInt32(e) : -1;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int i && i >= 0 ? Enum.ToObject(targetType, i) : Binding.DoNothing;
    }

    /// <summary>
    /// Visible when an enum value's name equals the parameter, e.g. the current page.
    /// </summary>
    public sealed class VisibleWhenEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value?.ToString() == parameter as string ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    }

    /// <summary>
    /// Two-way "enum equals parameter" for radio buttons: checked when equal, and checking one selects it.
    /// </summary>
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString() == parameter as string;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
    }
}
