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

namespace RugpViewer {
  /// <summary>
  /// Interaction logic for AnalysisTreeView.xaml
  /// </summary>
  public partial class AnalysisTreeView : UserControl {
    public AnalysisTreeView() {
      InitializeComponent();
    }

    public void InitView() {
      treeView.Items.Add(Analysis);
      treeView.SelectedItemChanged += treeView_SelectedItemChanged;
    }

    void treeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
      if (SelectedItemChanged == null)
        return;

      var ro = (RugpObject)e.NewValue;
      SelectedItemChanged.Invoke(ro);
    }

    public event SelectedRugpObjectChangedEventHandler SelectedItemChanged;

    Analysis _analysis;
    public Analysis Analysis {
      get { return _analysis; }
      set { _analysis = value; InitView(); }
    }
  }
}
