using System;
using System.Text;
using System.Windows;

namespace GCodeSimulator
{
    /// <summary>
    /// WPF 애플리케이션의 메인 클래스
    /// 애플리케이션의 시작점 및 전역 리소스 관리를 담당
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now}] Unhandled Exception:");
            sb.AppendLine(e.Exception.ToString());

            var inner = e.Exception.InnerException;
            while (inner != null)
            {
                sb.AppendLine("\nInner Exception:");
                sb.AppendLine(inner.ToString());
                inner = inner.InnerException;
            }
            
            try
            {
                System.IO.File.WriteAllText("error.log", sb.ToString());
            }
            catch { /* Ignore logging errors */ }

            MessageBox.Show($"오류가 발생했습니다. 상세 내용은 error.log를 확인하세요.\n\n{e.Exception.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            
            e.Handled = true; 
        }
    }
}
