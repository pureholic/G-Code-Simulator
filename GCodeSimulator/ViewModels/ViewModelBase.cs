using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GCodeSimulator.ViewModels
{
    /// <summary>
    /// MVVM 패턴의 기본 ViewModel 클래스
    /// INotifyPropertyChanged를 구현하여 데이터 바인딩을 지원
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        /// <summary>
        /// 속성 값이 변경될 때 발생하는 이벤트
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 속성 변경 이벤트를 발생시킴
        /// </summary>
        /// <param name="propertyName">변경된 속성의 이름 (자동으로 설정됨)</param>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 속성 값을 설정하고 변경되었을 때 PropertyChanged 이벤트를 발생시킴
        /// </summary>
        /// <typeparam name="T">속성의 타입</typeparam>
        /// <param name="field">속성을 저장하는 필드의 참조</param>
        /// <param name="value">설정할 새 값</param>
        /// <param name="propertyName">속성의 이름 (자동으로 설정됨)</param>
        /// <returns>값이 변경되었으면 true, 그렇지 않으면 false</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
