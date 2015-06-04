using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Globalization;

namespace RugpViewer {
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application {
    public App() {
      //Thread.CurrentThread.CurrentUICulture = new CultureInfo("ja-JP");
    }

    protected override void OnStartup(StartupEventArgs e) {
      base.OnStartup(e);
    }
  }
}
