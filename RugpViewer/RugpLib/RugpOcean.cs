using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace RugpLib {
  public enum RugpVersion : uint {
    Unknown,
    // Muv-Luv (CD):                       2003-02-28  (         )  rUGP

    // Muv-Luv (DVD):                      2004-04-30  (1.10.1073)  rUGP 5.60.10
    v56010 = 0x053C0A,

    // Muv-Luv (All Ages):                 2006-09-22  (         )  rUGP 5.72.09
    // Muv-Luv (XB360):                    2011-10-27  (         )  rUGP
    // Muv-Luv (Win7):                     2012-06-29  (         )  rUGP 6.10.09D
    // Muv-Luv (PS3):                      2012-10-25  (         )  rUGP
    //
    // Muv-Luv Alternative (CD):           2006-03-03  (         )  rUGP
    
    // Muv-Luv Alternative (DVD):          2006-04-24  (         )  rUGP 5.70.18
    v57018 = 0x054612,

    // Muv-Luv Alternative (All Ages):     2006-09-22  (         )  rUGP
    // Muv-Luv Alternative (XB360):        2011-10-27  (         )  rUGP
    // Muv-Luv Alternative (Win7):         2012-06-29  (         )  rUGP 6.10.09D
    // Muv-Luv Alternative (PS3):          2012-10-25  (         )  rUGP
    //
    // Muv-Luv Altered Fable:              2007-08-31
    // Muv-Luv Alternative Chronicles 01:  2010-??-??  (         )  rUGP 5.91.04
    // Muv-Luv Alternative Chronicles 02:  2011-??-??  (         )  rUGP
    // Muv-Luv Alternative Chronicles 03:  2012-03-30  (         )  rUGP
    // Muv-Luv Alternative Chronicles 04:  2013-09-27  (         )  rUGP
  }

  public class TypeNotAvailable : RugpObject {
    public TypeNotAvailable(IMultiFileCursor o, string type) :base(o) {
      _type = type;
    }

    public override string ToString() {
      return String.Format("TypeNotAvailable({0})", _type);
    }

    string _type;
  }

  public class NullReference : RugpObject {
    public NullReference(IMultiFileCursor o) : base(o) { }
    public override string ToString() {
      return "NullReference";
    }
  }

  public class ExtentLoadException : Exception {
    public ExtentLoadException(Exception innerException)
      : base(String.Format("An exception occurred while loading an extent: {0}", innerException.ToString()), innerException) { }
  }

  public class RugpOcean :IOcean {
    public RugpOcean(MultiFile mf) {
      this.mf = mf;
      load();
    }
    public RugpOcean(string filename) {
      mf = new MultiFile(filename);
      load();
    }
    void load() {
      resetCache();
      var c = new MultiFileCursor(mf);
      c.Ocean = this;
      c.Seek(2 * 0x28D1B);
      project = (CrelicUnitedGameProject)CrelicUnitedGameProject.fromOcean(c);
      project.DoneReading(c);
    }

    void resetCache() {
      cache.Clear();
      for (int i = 0; i < 5; ++i)
        cache.Add(new ClassID());
    }

    public void TrimCache(uint n) {
      while (cache.Count > n)
        cache.RemoveAt(cache.Count - 1);
    }

    public void PadCache(uint n) {
      for (int i=0;i<n;++i)
        cache.Add(new ClassID());
    }

    public void DupCache() {
      cache.Add(cache[cache.Count - 1]);
    }

    public void AddToCache(ClassID c) {
      cache.Add(c);
    }

    public RugpObject LoadObjectFromExtent(Extent e, ClassID clsname) {
      try {
        var ident = new ObjectLocationIdentity(e.Offset, clsname.Name);

        WeakReference<RugpObject> r;
        if (extentObjects.TryGetValue(ident, out r)) {
          RugpObject rr;
          if (r.TryGetTarget(out rr))
            return rr;
        }

        var c = new MultiFileCursor(mf);
        c.Ocean = this;
        c.Seek((long)e.Offset);

        if (c.Stream.SubBufferInfo == null) {
          c.Ocean.TrimCache(0);
          c.Ocean.PadCache(5);
        }

        RugpObject rr2 = _LoadObjectAtCursorByName(c, clsname);
        if (rr2 == null) {
          if (clsname == null)
            return null;
          else
            rr2 = new TypeNotAvailable(c, clsname.Name);
        }

        extentObjects[ident] = new WeakReference<RugpObject>(rr2);
        return rr2;
      } catch(Exception ex) {
        throw new ExtentLoadException(ex);
      }
    }

    public RugpObject LoadObjectAtCursorByName(IMultiFileCursor c, ClassID clsname) {
      return _RequireObject(_LoadObjectAtCursorByName(c, clsname), clsname);
    }

    public RugpObject LoadObjectAtCursorByType(IMultiFileCursor c, Type t) {
      return _RequireObject(_LoadObjectAtCursorByType(c, t), t);
    }

    public RugpObject LoadObjectAtCursorByType<T>(IMultiFileCursor c) {
      return LoadObjectAtCursorByType(c, typeof(T));
    }

    static RugpObject _RequireObject(RugpObject obj, object x) {
      if (obj == null)
        throw new Exception(String.Format("type not supported, but required: {0}", x));

      return obj;
    }

    RugpObject _LoadObjectAtCursorByName(IMultiFileCursor c, ClassID clsname) {
      if (clsname == null)
        return null;

      string name = clsname.Name;
      if (name.StartsWith("&-"))
        name = name.Substring(2);

      var asm = Assembly.GetAssembly(typeof(RugpObject));
      Type t = asm.GetType("RugpLib."+name);
      if (t == null) {
        Console.WriteLine(String.Format("Warning: Type not supported: {0}", name));
        return null; // return new TypeNotAvailable(c, name);
        //if (name == "CObjectOcean" || name == "CStdb")
        //  return null;
        //throw new Exception(string.Format("Type not supported: {0}", name));
        //return null;
      }

      return _LoadObjectAtCursorByType(c, t);
    }

    RugpObject _LoadObjectAtCursorByType(IMultiFileCursor c, Type t) {
      MethodInfo mi = t.GetMethod("fromOcean", BindingFlags.Static|BindingFlags.Public);
      if (mi == null)
        throw new Exception(String.Format("Type has no fromOcean method: {0}", t));

      var obj = (RugpObject)mi.Invoke(null, new object[] { c });
      if (obj == null)
        throw new Exception(string.Format("Failed to instantiate {0}", t));

      obj.DoneReading(c);

      return obj;
    }

    public byte[] GetRawDataAtExtent(Extent e) {
      var c = new MultiFileCursor(mf);
      c.Ocean = this;
      c.Seek((long)e.Offset);
      return c.Read(e.Length);
    }

    CrelicUnitedGameProject project;
    List<ClassID> cache = new List<ClassID>();
    public List<ClassID> ClassIDCache { get { return cache; } set { cache = value; } }
    public CrelicUnitedGameProject Project { get { return project; } }
    Dictionary<ObjectLocationIdentity, WeakReference<RugpObject>> extentObjects = new Dictionary<ObjectLocationIdentity, WeakReference<RugpObject>>();
    MultiFile mf;
  }
}
