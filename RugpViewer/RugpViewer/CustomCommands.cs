using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace RugpViewer {
  public static class CustomCommands {
    public static readonly RoutedUICommand SaveImage = new RoutedUICommand(Properties.Resources.SaveImageButtonLabel, "SaveImage", typeof(CustomCommands),
      new InputGestureCollection() {
        new KeyGesture(Key.S, ModifierKeys.Control)
      }
    );

    public static readonly RoutedUICommand FindAllImages = new RoutedUICommand(Properties.Resources.FindAllImagesButtonLabel, "FindAllImages", typeof(CustomCommands),
      new InputGestureCollection() {

      }
    );
  }
}
