using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RugpLib {
  public class Analysis : RugpObject {
    public Analysis(IOcean c) : base(c) { }
  }

  // 0.0 <= completion <= 1.0; -1 = indeterminate
  public delegate void ProgressCallback(double completion, string statusMessage);

  public class LoadErrorInfo {
    public ExtentLoadException Exception { get; set; }
  }

  public class ObjectLocationIdentity :Tuple<ulong,string> {
    public static readonly ObjectLocationIdentity None = new ObjectLocationIdentity();

    ObjectLocationIdentity() : base(0, null) { }
    public ObjectLocationIdentity(ulong offset, string type) : base(offset, type) { }

    public bool IsNull() { return Type == null; }

    public ulong Offset { get { return Item1; } }
    public string Type { get { return Item2; } }
  }

  public class FindAllImagesAnalysis : Analysis {
    FindAllImagesAnalysis(IOcean c) : base(c) { }

    void _Enqueue(RugpObject ro) {
      if (ro is NullReference)
        return;

      var repr = RugpObject.GetLocationIdentity(ro);
      if (!repr.IsNull() && _alreadySeen.Contains(repr))
        return;

      _todo.Enqueue(ro);

      if (!repr.IsNull())
        _alreadySeen.Add(repr);
    }

    void _HandleCr6Ti(Cr6Ti ro) {
      //_images.Add(ro);
      AddChild(ro);
      ++_imagesFound;
    }

    void _HandleSelf(RugpObject ro) {
      if (ro is Cr6Ti) {
        _HandleCr6Ti((Cr6Ti)ro);
        return;
      }
    }

    void _Handle(RugpObject ro) {
      _HandleSelf(ro);

      foreach (RugpObject c in ro.Children)
        _Enqueue(c);

      try {
        foreach (RugpObject c in ro.VirtualChildren)
          _Enqueue(c);
      } catch (ExtentLoadException ex) {
        var lei = new LoadErrorInfo();
        lei.Exception = ex;
        _errors.Add(lei);
      }
    }

    void _Run() {
      _pcb(0.0, "Finding all images...");
      _Enqueue(Ocean.Project);

      while (_todo.Count > 0) {
        var ro = _todo.Dequeue();

        //if ((_numProcessed % 100) == 0)
          _pcb((double)_numProcessed/(double)(_numProcessed+_todo.Count+1),
            String.Format("Processed {0} of {1} ({2} errors)\n{3}", _numProcessed, (_numProcessed + _todo.Count+1), _errors.Count, ro.ToString().Split('\n')[0]));

        _Handle(ro);

        //if (_imagesFound > 100) break;

        ++_numProcessed;
      }

      _pcb(1.0, "Done");
      _todo = null;
      _alreadySeen = null;
      System.GC.Collect();
    }

    public static FindAllImagesAnalysis run(IOcean ocean, ProgressCallback pcb) {
      var a = new FindAllImagesAnalysis(ocean);

      if (pcb == null)
        a._pcb = (x, y) => { };
      else
        a._pcb = pcb;

      a._Run();
      return a;
    }

    public List<LoadErrorInfo> Errors { get { return _errors; } }

    uint _numProcessed = 0;
    uint _imagesFound = 0;
    ProgressCallback _pcb;
    Queue<RugpObject> _todo = new Queue<RugpObject>();
    HashSet<ObjectLocationIdentity> _alreadySeen = new HashSet<ObjectLocationIdentity>();
    List<LoadErrorInfo> _errors = new List<LoadErrorInfo>();
  }
}
