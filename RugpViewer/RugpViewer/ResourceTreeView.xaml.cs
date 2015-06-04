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
  /// Interaction logic for ResourceTreeView.xaml
  /// </summary>
  
  public delegate void SelectedRugpObjectChangedEventHandler(RugpObject ro);

  public partial class ResourceTreeView : UserControl {
    public ResourceTreeView() {
      InitializeComponent();
    }
    public void InitView() {
      treeView.Items.Add(Ocean.Project);
      treeView.SelectedItemChanged += treeView_SelectedItemChanged;
    }

    void treeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) {
      if (SelectedItemChanged == null)
        return;
      var ro = (RugpObject)e.NewValue;
      SelectedItemChanged.Invoke(ro);
    }

    public event SelectedRugpObjectChangedEventHandler SelectedItemChanged;

    RugpOcean _ocean;
    public RugpOcean Ocean {
      get { return _ocean; }
      set { _ocean = value; InitView(); }
    }
  }
}
