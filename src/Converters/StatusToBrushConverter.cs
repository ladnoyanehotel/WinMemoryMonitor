using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WinMemoryCleaner
{
    /// <summary>
    /// Maps the system status level to the hardcoded status color:
    /// green = normal, yellow = warning, red = critical, blue = operation/information.
    /// </summary>
    public class StatusToBrushConverter : IValueConverter
    {
        private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(46, 160, 67));
        private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(214, 158, 46));
        private static readonly Brush CriticalBrush = new SolidColorBrush(Color.FromRgb(211, 47, 47));
        private static readonly Brush OptimizingBrush = new SolidColorBrush(Color.FromRgb(0, 174, 247));

        static StatusToBrushConverter()
        {
            NormalBrush.Freeze();
            WarningBrush.Freeze();
            CriticalBrush.Freeze();
            OptimizingBrush.Freeze();
        }

        /// <summary>
        /// Converts a status level to a brush
        /// </summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Enums.Status.Level)
            {
                switch ((Enums.Status.Level)value)
                {
                    case Enums.Status.Level.Critical:
                        return CriticalBrush;

                    case Enums.Status.Level.Warning:
                        return WarningBrush;

                    case Enums.Status.Level.Optimizing:
                        return OptimizingBrush;
                }
            }

            return NormalBrush;
        }

        /// <summary>
        /// Not supported
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
