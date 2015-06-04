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
using System.Windows.Navigation;
using System.Windows.Shapes;
using RugpLib;
using Microsoft.Win32;

namespace RugpViewer {
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window {
    public MainWindow() {
      InitializeComponent();
    }

    private void CmdOpen_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
      e.CanExecute = (ocean == null);
    }

    private void CmdOpen_Executed(object sender, ExecutedRoutedEventArgs e) {
      var d = new OpenFileDialog();
      d.DefaultExt = "rio";
      d.CheckFileExists = true;
      d.InitialDirectory = "c:\\age\\alternative\\";
      d.Title = Properties.Resources.SelectRioFileSentence;
      d.Filter = "rio Files (*.rio)|*.rio";

      var result = d.ShowDialog();
      if (!result.GetValueOrDefault(false))
        return;

      ocean = new RugpOcean(d.FileName);
      rtv.Ocean = ocean;
      rtv.SelectedItemChanged += rtv_SelectedItemChanged;
      atv.SelectedItemChanged += atv_SelectedItemChanged;
    }

    void atv_SelectedItemChanged(RugpObject ro) {
      ov.ShowObject(ro);
    }

    void rtv_SelectedItemChanged(RugpObject ro) {
      ov.ShowObject(ro);
    }

    private void CmdFindAllImages_Executed(object sender, ExecutedRoutedEventArgs e) {
      if (ocean == null)
        return;

      var mbr = MessageBox.Show(Properties.Resources.WarningAnalysisMemoryUsage,
        Properties.Resources.AreYouSureTitle,
        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.Yes);
      if (mbr == MessageBoxResult.No)
        return;

      Progress.run((IProgressHandler h) => {
        h.Headline = Properties.Resources.FindingAllImagesEllipsis;
        findAllImagesAnalysis = FindAllImagesAnalysis.run(ocean, (double completion, string statusText) => {
          h.Completion = completion;
          h.Indeterminate = (completion < 0);
          h.StatusMessage = statusText;
        });
      }, this);

      atv.Analysis = findAllImagesAnalysis;
    }

    private void CmdFindAllImages_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
      e.CanExecute = (ocean != null && findAllImagesAnalysis == null);
    }

    RugpOcean ocean;
    FindAllImagesAnalysis findAllImagesAnalysis;
  }
}
