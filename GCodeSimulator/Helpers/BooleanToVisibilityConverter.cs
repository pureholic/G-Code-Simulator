using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GCodeSimulator.Helpers
{
    /// <summary>
    /// Boolean 값을 Visibility 열거형으로 변환하는 컨버터
    /// XAML 바인딩에서 bool 값으로 UI 요소의 표시/숨김을 제어
    /// </summary>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BooleanToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Boolean 값을 Visibility로 변환
        /// </summary>
        /// <param name="value">변환할 Boolean 값</param>
        /// <param name="targetType">대상 타입</param>
        /// <param name="parameter">변환 매개변수</param>
        /// <param name="culture">문화권 정보</param>
        /// <returns>true이면 Visible, false이면 Collapsed</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool && (bool)value) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Visibility 값을 Boolean으로 역변환
        /// </summary>
        /// <param name="value">변환할 Visibility 값</param>
        /// <param name="targetType">대상 타입</param>
        /// <param name="parameter">변환 매개변수</param>
        /// <param name="culture">문화권 정보</param>
        /// <returns>Visible이면 true, 그렇지 않으면 false</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Visible;
        }
    }
}
