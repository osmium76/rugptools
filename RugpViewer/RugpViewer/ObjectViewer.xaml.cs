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
using System.Reflection;
using System.IO;
using RugpLib;
using Microsoft.Win32;

namespace RugpViewer {
  /// <summary>
  /// Interaction logic for ObjectViewer.xaml
  /// </summary>
  public partial class ObjectViewer : UserControl {
    public ObjectViewer() {
      InitializeComponent();
    }

    public void ShowObject(RugpObject ro) {
      curObj = ro;
      tb.Text = ReflectString(ro);
      string s = "";
      if (ro.SubBufferInfo != null) {
        s = String.Format("[From sub-buffer: {0}]\n", ro.SubBufferInfo.ToString());
      }

      s += String.Format("  [0x{0:X}:]\n", ro.SelfExtent.Offset) + Hex.Encode(RawData(ro));
      s += "\n\n" + ro.GetExtraHexDump();

      tbhex.Document = new FlowDocument();
      tbhex.Document.Blocks.Add(new Paragraph(new Run(s)));

      var img = ro.GetImage();
      if (img != null) {
        image.Source = BitmapSource.Create((int)img.Width, (int)img.Height,
          96, 96,
          PixelFormats.Bgra32, null, img.Buffer, (int)img.Stride);
        imageTab.Visibility = System.Windows.Visibility.Visible;
        tabControl.SelectedItem = imageTab;

        CommandManager.InvalidateRequerySuggested();
      } else {
        if (tabControl.SelectedItem == imageTab)
          tabControl.SelectedItem = infoTab;

        image.Source = null;
        imageTab.Visibility = System.Windows.Visibility.Hidden;
      }
    }

    byte[] RawData(RugpObject ro) {
      return ro.Ocean.GetRawDataAtExtent(ro.SelfExtent);
    }

    string ReflectString(RugpObject ro) {
      if (ro == null)
        return "(null)";

      string s = ro.ToString() + "\n\n";
      Type t = ro.GetType();
      PropertyInfo[] props = t.GetProperties();
      foreach (var p in props) {
        RugpAttribute attr = p.GetCustomAttribute<RugpAttribute>(true);
        if (attr != null && attr.Priority < 0)
          continue;

        if (p.GetGetMethod() == null)
          continue;
        object o = p.GetValue(ro, null);
        s += string.Format("{0}: {1}\n", p.Name, (o != null) ? o.ToString() : "(null)");
      }
      return s;
    }

    private void SaveImage_Executed(object sender, ExecutedRoutedEventArgs e) {
      if (image.Source == null)
        return;

      var d = new SaveFileDialog();
      d.DefaultExt = "png";
      d.InitialDirectory = "C:\\age\\alternative\\";
      d.Title = "Save Image...";
      d.Filter = "PNG Files (*.png)|*.png";
      d.FileName = String.Format("{0,8:X8}.png", curObj.SelfExtent.Offset);

      var result = d.ShowDialog();
      if (!result.GetValueOrDefault(false))
        return;

      var enc = new PngBitmapEncoder();
      enc.Frames.Add(BitmapFrame.Create((BitmapSource)image.Source));

      try {
        using (var stream = File.Create(d.FileName))
          enc.Save(stream);
      } catch (Exception) {
        MessageBox.Show("Failed to write image.", "Couldn't save image", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void SaveImage_CanExecute(object sender, CanExecuteRoutedEventArgs e) {
      e.CanExecute = (image.Source != null);
    }

    RugpObject curObj;
  }
}
