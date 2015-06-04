using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RugpLib;

namespace RugpTool {
  class Program {
    static int Main(string[] args) {
      if (args.Length < 1) {
        Console.WriteLine("Usage: [rio filename]");
        return 1;
      }

      string fn = args[0];
      var ocean = new RugpOcean(fn);
      PrintObject(ocean.Project);

      return 0;
    }

    static void PrintWithIndent(string s, int indent=0) {
      var lines = s.Split('\n');
      foreach (string l in lines) {
        Console.Write(new String(' ', indent * 2));
        Console.WriteLine(l);
      }
    }

    static void PrintObject(RugpObject o, int indent=0) {
      PrintWithIndent(o.ToString(), indent);
      foreach (RugpObject c in o.Children) {
        if (c == null) {
          PrintWithIndent("(null child reference)", indent);
          continue;
        }

        PrintObject(c, indent + 1);
      }

      foreach (RugpObject c in o.VirtualChildren) {
        if (c == null) {
          PrintWithIndent("(null virtual child reference)", indent);
          continue;
        }

        PrintObject(c, indent + 1);
      }
    }
  }
}
