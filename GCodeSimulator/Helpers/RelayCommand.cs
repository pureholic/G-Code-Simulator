using System;
using System.Windows.Input;

namespace GCodeSimulator.Helpers
{
    /// <summary>
    /// ICommand 인터페이스를 구현한 릴레이 커맨드 클래스
    /// MVVM 패턴에서 뷰의 동작을 ViewModel의 메서드에 바인딩
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute; // 실행할 동작
        private readonly Func<object?, bool>? _canExecute; // 실행 가능 여부를 판단하는 조건

        /// <summary>
        /// 명령의 실행 가능 여부가 변경되었음을 알리는 이벤트
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// RelayCommand 생성자
        /// </summary>
        /// <param name="execute">명령이 실행될 때 호출할 동작</param>
        /// <param name="canExecute">명령 실행 가능 여부를 판단하는 함수 (선택사항)</param>
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// 명령을 실행할 수 있는지 판단
        /// </summary>
        /// <param name="parameter">명령 매개변수</param>
        /// <returns>실행 가능하면 true, 그렇지 않으면 false</returns>
        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// 명령을 실행
        /// </summary>
        /// <param name="parameter">명령 매개변수</param>
        public void Execute(object? parameter)
        {
            _execute(parameter);
        }
    }
}
