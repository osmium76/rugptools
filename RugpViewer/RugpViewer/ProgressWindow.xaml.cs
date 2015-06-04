using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RugpViewer {
  internal static class WndUtils {
    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;

    public static void DisableCloseButton(Window w) {
      var hwnd = new WindowInteropHelper(w).Handle;
      SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_SYSMENU);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
  }

  /// <summary>
  /// Interaction logic for ProgressWindow.xaml
  /// </summary>
  internal partial class ProgressWindow : Window, IProgressHandler {
    public ProgressWindow() {
      InitializeComponent();
      
    }

    public void DoneProgress() {
      _canClose = true;
      Close();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
      if (!_canClose)
        e.Cancel = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
      WndUtils.DisableCloseButton(this);
    }

    void _SetStatusLabel(string msg) {
      _RunOnDispatcherThread(statusLabel, new Action(() => statusLabel.Content = msg));
    }

    void _SetHeadline(string headline) {
      _RunOnDispatcherThread(headlineLabel, new Action(() => headlineLabel.Content = headline));
      _RunOnDispatcherThread(this, new Action(() => Title = headline));
    }

    void _SetCompletion(double completion) {
      _RunOnDispatcherThread(progressBar, new Action(() => progressBar.Value = completion));      
    }

    void _SetIndeterminate(bool indeterminate) {
      _RunOnDispatcherThread(progressBar, new Action(() => progressBar.IsIndeterminate = indeterminate));
    }

    void _RunOnDispatcherThread(DispatcherObject dObj, Action a) {
      if (_IsDispatcherThread())
        a();
      else
        dObj.Dispatcher.InvokeAsync(a);
    }

    static bool _IsDispatcherThread() {
      return Application.Current.Dispatcher.Thread == Thread.CurrentThread;
    }

    public double Completion {
      set { _SetCompletion(value); }
    }

    public bool Indeterminate {
      set { _SetIndeterminate(value); }
    }

    public string Headline {
      set { _SetHeadline(value); }
    }

    public string StatusMessage {
      set { _SetStatusLabel(value); }
    }

    bool _canClose = false;
  }

  public interface IProgressHandler {
    string Headline { set; }
    string StatusMessage { set; }
    double Completion { set; } // 0 <= x <= 1.0
    bool Indeterminate { set; }
  }

  public delegate void LongRunningTask(IProgressHandler handler);

  public static class Progress {
    public static void run(LongRunningTask t, Window parentWindow=null) {
      var w = new ProgressWindow();
      w.Owner = parentWindow;
      w.Loaded += (_, args) => {
        var bw = new BackgroundWorker();

        bw.DoWork += (s, workerArgs) => t(w);
        bw.RunWorkerCompleted += (s, workerArgs) => w.DoneProgress();
        bw.RunWorkerAsync();
      };

      w.ShowDialog();
    }
  }
}
