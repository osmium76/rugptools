using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RugpLib {
  public class RugpAttribute : Attribute {
    public RugpAttribute(int priority = 0) {
      Priority = priority;
    }

    public int Priority { get; set; }
  }

  public interface IAdditionalChildren<T> {
    IEnumerator<T> GetAdditionalChildren();
  }

  public class AdditionalChildrenEnumerable<T> :IEnumerable<T> where T :IAdditionalChildren<T> {
    public AdditionalChildrenEnumerable(T obj) {
      _obj = obj;
    }

    public IEnumerator<T> GetEnumerator() {
      return _obj.GetAdditionalChildren();
    }

    IEnumerator IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }

    IAdditionalChildren<T> _obj;
  }

  public class RugpObject :IAdditionalChildren<RugpObject> {
    public RugpObject(IMultiFileCursor c) {
      Children = new List<RugpObject>();
      Ocean = c.Ocean;
      SelfExtent = new Extent(c.Position, 0);
      SourceStream = c.Stream;
    }

    // This is used by analyses.
    public RugpObject(IOcean c) {
      Children = new List<RugpObject>();
      Ocean = c;
    }

    public void DoneReading(IMultiFileCursor c) {
      SelfExtent = new Extent(SelfExtent.Offset, c.Position - SelfExtent.Offset);
    }

    public void AddChild(RugpObject child) {
      Children.Add(child);
    }

    public virtual IEnumerator<RugpObject> GetAdditionalChildren() {
      return Enumerable.Empty<RugpObject>().GetEnumerator();
    }

    [Rugp(-1)]
    public List<RugpObject> Children { get; set; }

    [Rugp(-1)]
    public IEnumerable<RugpObject> VirtualChildren {
      get {
        return Children.Concat(new AdditionalChildrenEnumerable<RugpObject>(this));
      }
    }

    [Rugp(-1)]
    public IOcean Ocean { get; set; }

    public virtual string GetTreeViewLabel() {
      return String.Format("({0})", ToString().Split('\n')[0]);
    }

    [Rugp(-1)]
    public virtual string TreeViewLabel { get { return GetTreeViewLabel(); } }

    public Extent SelfExtent { get; set; }
    
    [Rugp(-1)]
    public IMultiFileStream SourceStream { get; set; }

    public SubBufferInfo SubBufferInfo { get { return SourceStream == null ? null : SourceStream.SubBufferInfo; } }

    public virtual string GetExtraHexDump() { return ""; }
    public virtual ImageInfo GetImage() { return null; }

    public static ObjectLocationIdentity GetLocationIdentity(RugpObject ro) {
      if (ro == null || ro.SubBufferInfo != null)
        return ObjectLocationIdentity.None;

      return new ObjectLocationIdentity(ro.SelfExtent.Offset, ro.GetType().Name);
    }
  }

  public class ImageInfo {
    public uint Width { get; set; }
    public uint Height { get; set; }
    public byte[] Buffer { get; set; } // in BGRA32
    public uint Stride { get; set; }
  }

  public class CrelicUnitedGameProject : RugpObject {
    public CrelicUnitedGameProject(IMultiFileCursor c) :base(c) {

    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      // -,-,-,-,-
      c.Ocean.TrimCache(2);
      // -,-
      var o = new CrelicUnitedGameProject(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadWord();
      o.Unk3 = c.ReadWord();
      o.ClassID = c.ReadClassID();
      // -,-,rUGP
      c.Ocean.ClassIDCache.Add(o.ClassID);
      // -,-,rUGP,rUGP

      var numProjectItems = c.ReadWord();
      for (int i=0; i<numProjectItems; ++i)
        o.AddChild(c.ReadObject<ProjectItem>()); //ProjectItem.fromOcean(c));

      // The encrypted buffer.
      var mfb = c.CreateCryptedSubBuffer(0x7E6B8CE2);
      o.CryptedPart = mfb.SubBufferInfo;
      var ebc = new MultiFileCursor(mfb);
      ebc.Ocean = c.Ocean;
      o.Unk4 = ebc.ReadDword();
      for (int i = 0; i < 9; ++i)
        o.AddChild(ebc.ReadObjRef()); //ObjRef.fromOcean(ebc));

      return o;
    }

    public override string GetExtraHexDump() {
      return String.Format("Includes crypted part:\n\n{0}\n{1}", CryptedPart, Hex.Encode(CryptedPart.Buffer));
    }

    public uint Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public ushort Unk3 { get; set; }
    public uint Unk4 { get; set; }
    public ClassID ClassID { get; set; }
    public SubBufferInfo CryptedPart { get; set; }
  }

  public class ProjectItem : RugpObject {
    public ProjectItem(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var pi = new ProjectItem(c);
      var flags = c.ReadWord();
      ushort fMode = (ushort)(flags & 0x0007);
      bool fNoChildren = (flags & 0x0200) != 0;
      bool fOneByte = (flags & 0x8000) != 0;
      if (fMode == 0) {
        if (fOneByte)
          pi.Unk1 = c.ReadByte();
        else
          pi.Unk1 = c.ReadWord();
        pi.ClassID = c.ReadClassID();
        pi.Extent = c.ReadExtent();
        if (!fNoChildren) {
          var numChildren = c.ReadWord();
          for (int i = 0; i < numChildren; ++i)
            pi.AddChild(fromOcean(c));
        }
      } else if (fMode == 1) {
        pi.Unk1 = c.ReadDword();
        var w = c.ReadWord();
        if (w == 0x2D6B) {
          pi.Schema = c.ReadWord();
          pi.Name = c.ReadString2B();
        } else if (w == 0x1E57)
          pi.ClassID = c.ReadClassID();
        else
          throw new Exception(String.Format("Unknown w: {0}", w));
        pi.Extent = c.ReadExtent();
        var numChildren = c.ReadWord();
        for (int i = 0; i < numChildren; ++i)
          pi.AddChild(fromOcean(c));
      }
      return pi;
    }

    public override string ToString() {
      return String.Format("PI: {0}\n  {1}", (Name != null ? "\"" + Name + "\"" : ClassID.ToString()), Extent.ToString());
    }
    public override IEnumerator<RugpObject> GetAdditionalChildren() {
      if (ExObject == null) {
        ExObject = Ocean.LoadObjectFromExtent(Extent, ClassID);
      }

      if (ExObject == null) {
        return Enumerable.Empty<RugpObject>().GetEnumerator();
      }

      return Enumerable.Repeat(ExObject, 1).GetEnumerator();
    }

    public uint Unk1 { get; set; }
    public ushort Schema { get; set; }
    public string Name { get; set; }
    public ClassID ClassID { get; set; }
    public Extent Extent { get; set; }

    [Rugp(-1)]
    public RugpObject ExObject { get; set; }
    public ushort Flags { get; set; }
  }

  public class ProjectItemList : RugpObject {
    public ProjectItemList(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var pil = new ProjectItemList(c);
      var numItems = c.ReadWord();
      for (int i = 0; i < numItems; ++i)
        pil.AddChild(c.ReadObject<ProjectItem>()); //ProjectItem.fromOcean(c));
      return pil;
    }
  }

  public class CProcessOcean : RugpObject {
    public CProcessOcean(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CProcessOcean(c);

      o.Unk1 = c.ReadDword();
      if (o.Unk1 != 33)
        throw new Exception("CProcessOcean");

      for (int i = 0; i < 5;++i)
        o.AddChild(c.ReadObjRef());

      return o;
    }
    public uint Unk1 { get; set; }
  }

  public class CBoxOcean : RugpObject {
    public CBoxOcean(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CBoxOcean(c);
      for (int i = 0; i < 15; ++i)
        o.AddChild(c.ReadObjRef());
      o._Subload(c);
      o.AddChild(c.ReadObjRef());
      o._Subload(c);
      return o;
    }

    protected void _Subload(IMultiFileCursor c) {
      for (; ; ) {
        byte b = c.ReadByte();
        if (b == 0)
          break;
        AddChild(c.ReadObjRef());
        if ((b & 1) != 0)
          _Subload(c);
      }
    }
  }

  public class CCharBox : RugpObject {
    public CCharBox(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CCharBox(c);
      o.Unk1 = c.ReadWord();
      o.Unk2 = c.ReadWord();
      o.Unk3 = c.ReadDword();
      o.Unk4 = c.ReadByte();
      o.Unk5 = new UInt64[5];
      for (int i = 0; i < 5; ++i)
        o.Unk5[i] = c.ReadQword();
      o.Str = c.ReadString();
      o.Unk6 = c.ReadWord();
      if (o.Unk6 != 0)
        throw new Exception("CCharBox: Unk6 must not be zero");
      o.Unk7 = c.ReadWord();
      o.Unk8 = c.ReadWord();
      o.Unk9 = c.ReadDword();
      o.Unk10 = c.ReadDword();
      o.Unk11 = c.ReadDword();
      o.Unk12 = c.ReadDword();
      o.Unk13 = c.ReadQword();
      o.Unk14 = c.ReadWord();
      o.Unk15 = c.ReadWord();
      o.AddChild(c.ReadObjRef());
      o.Unk16 = c.ReadByte();
      o.AddChild(c.ReadObjRef());
      o.Unk17 = c.ReadDword();
      o.Unk18 = c.ReadDword();
      return o;
    }
    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public uint Unk3 { get; set; }
    public byte Unk4 { get; set; }
    public UInt64[] Unk5 { get; set; }
    public string Str { get; set; }
    public ushort Unk6 { get; set; }
    public ushort Unk7 { get; set; }
    public ushort Unk8 { get; set; }
    public uint Unk9 { get; set; }
    public uint Unk10 { get; set; }
    public uint Unk11 { get; set; }
    public uint Unk12 { get; set; }
    public UInt64 Unk13 { get; set; }
    public ushort Unk14 { get; set; }
    public ushort Unk15 { get; set; }
    public byte Unk16 { get; set; }
    public uint Unk17 { get; set; }
    public uint Unk18 { get; set; }
  }

  public class CBg2d : RugpObject {
    public CBg2d(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CBg2d(c);
      c.Ocean.AddToCache(new ClassID("CRip008",0));
      c.Ocean.AddToCache(new ClassID("Cr6Ti",0));

      o.Unk1 = c.ReadWord();
      o.Unk2 = c.ReadWord();
      o.Unk3 = c.ReadWord();
      o.Unk4 = c.ReadWord();
      o.Unk5 = c.ReadWord();
      o.Unk6 = c.ReadWord();
      o.Unk7 = c.ReadDword();
      o.Unk8 = c.ReadWord();
      o.Unk9 = c.Read(12);

      var f = c.ReadDword();
      if ((f & 2) != 0)
        c.Read(3*2);
      if ((f & 8) != 0)
        c.Read(1*2);
      if ((f & 4) != 0)
        c.Read(12);

      var n = c.ReadWord();
      for (ushort i=0; i<n; ++i) {
        c.Read(12);
        o.AddChild(c.ReadObjRef());
      }

      var x = c.ReadByte();
      if (x == 0)
        o.AddChild(c.ReadObjRef());

      return o;
    }

    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public ushort Unk3 { get; set; }
    public ushort Unk4 { get; set; }
    public ushort Unk5 { get; set; }
    public ushort Unk6 { get; set; }
    public uint Unk7 { get; set; }
    public ushort Unk8 { get; set; }
    public byte[] Unk9 { get; set; }
  }

  public class Cr6Ti : RugpObject {
    public Cr6Ti(IMultiFileCursor c) : base(c) { }

    void _Set(uint x, uint y, byte r, byte g, byte b, byte a) {
      if (x >= Width)
        return;

      _sustainR[x] = r;
      _sustainG[x] = g;
      _sustainB[x] = b;
      _sustainA[x] = a;
      _dataR[x+y*Width] = r;
      _dataG[x+y*Width] = g;
      _dataB[x+y*Width] = b;
      _dataA[x + y * Width] = a;
    }

    void _InitBuffers() {
      _sustainR = new byte[Width];
      _sustainG = new byte[Width];
      _sustainB = new byte[Width];
      _sustainA = new byte[Width];
      _dataR = new byte[Width*Height];
      _dataG = new byte[Width*Height];
      _dataB = new byte[Width*Height];
      _dataA = new byte[Width*Height];
    }

    void _DiscardSustain() {
      _sustainR = null;
      _sustainG = null;
      _sustainB = null;
      _sustainA = null;
    }

    static byte clampu(int x) {
      if (x < 0)
        return 0;
      if (x > 255)
        return 255;
      return (byte)x;
    }

    static sbyte clampi(int x) {
      if (x < -128)
        return -128;
      if (x > 127)
        return 127;
      return (sbyte)x;
    }

    void _LoadOpaque1(IMultiFileCursor c) {
      _InitBuffers();

      for (uint y=0; y<Height; ++y) {
        int count = -1;
        int r = 0;
        int g = 0;
        int b = 0;
        int dr = 0;
        int dg = 0;
        int db = 0;
        int x = 0;

        while (x < Width) {
          if (count == -1)
            count = (int)c.ReadPackedUnsigned();

          if (c.ReadPackedBit()) {
            r = _sustainR[x];
            g = _sustainG[x];
            b = _sustainB[x];
            dr = 0;
            dg = 0;
            db = 0;
          } else {
            int t = clampu(g + dg*2);
            t = t - g;
            dg = clampi(c.ReadPackedSigned() + (int)t/2);
            db = dg;
            dr = dg;
            r = clampu(r + dr*2);
            g = clampu(g + dg*2);
            b = clampu(b + db*2);
            r = clampu(r + c.ReadPackedSigned()*2);
            b = clampu(r + c.ReadPackedSigned()*2);
          }

          _Set((uint)x,(uint)y, (byte)r,(byte)g,(byte)b, 255);
          ++x;
          --count;

          if (count == -1 && x < Width) {
            var n = c.ReadPackedUnsigned() + 1;
            for (int i=0; i<n; ++i) {
              _Set((uint)x,(uint)y, (byte)r, (byte)g, (byte)b, 255);
              ++x;
            }

            dr = 0;
            dg = 0;
            db = 0;
          }
        }
      }

      _DiscardSustain();
    }

    void _LoadOpaque2(IMultiFileCursor c) {
      _InitBuffers();

      // depth is 0x??BBGGRR
      int rdepth = (int)(Depth >>  8) & 0xFF;
      int gdepth = (int)(Depth >> 16) & 0xFF;
      int bdepth = (int)(Depth >> 24) & 0xFF;
      for (int y=0;y<Height;++y) {
        int count = -1;
        int r = 0;
        int g = 0;
        int b = 0;
        int x = 0;

        while (x < Width) {
          if (count < 0)
            count = (int)c.ReadPackedUnsigned();

          if (c.ReadPackedBit()) {
            r = _sustainR[x];
            g = _sustainG[x];
            b = _sustainB[x];
          } else {
            int d = c.ReadPackedSigned() << (int)(8 - gdepth);
            r = clampu(r+d);
            g = clampu(g+d);
            b = clampu(b+d);

            r = (byte)(clampu(r + (c.ReadPackedSigned() << (int)(8 - rdepth))) & (0x100 - (1<<(int)(8 - rdepth))));
            g = (byte)(g & (byte)(0x100 - (1<<(int)(8 - gdepth))));
            b = (byte)(clampu(b + (c.ReadPackedSigned() << (int)(8 - bdepth))) & (0x100 - (1<<(int)(8 - bdepth))));
          }

          _Set((uint)x, (uint)y, (byte)r, (byte)g, (byte)b, 255);
          ++x;
          --count;

          if (count < 0 && x < Width) {
            uint n = c.ReadPackedUnsigned() + 1;
            for (uint i=0; i<n; ++i) {
              _Set((uint)x, (uint)y, (byte)r, (byte)g, (byte)b, 255);
              ++x;
            }
          }
        }
      }

      _DiscardSustain();
    }
   
    void _LoadTransparent(IMultiFileCursor c) {
      _InitBuffers();

      for (int y=0; y<Height; ++y) {
        int x = 0;
        int ta = 0;
        int tg = 0;
        int dg = 0;
        int r = 0;
        int g = 0;
        int b = 0;
        int samea = 0;
        int samef = 0;
        bool f = true;
        int a = 0;

        while (x < Width) {
          --samea;
          if (samea < 0) {
            int q = c.ReadPackedSigned();
            ta += q;
            if (ta == 0) {
              uint n = c.ReadPackedUnsigned();
              for (uint i=0; i<=n; ++i) {
                _Set((uint)x,(uint)y, 0, 0, 0, 0);
                ++x;
              }

              continue;
            }

            if (ta == 32)
              samea = (int)c.ReadPackedUnsigned();

            a = clampu(ta*8);
          }

          --samef;
          if (samef < 0) {
            f = !f;
            samef = (int)c.ReadPackedUnsigned();
            dg = 0;
          }

          if (f) {
            if (samea < samef) {
              _Set((uint)x,(uint)y, (byte)r, (byte)g, (byte)b, (byte)a);
              ++x;
            } else {
              if (samef > 0)
                samea -= samef;

              for (uint i=0; i<=samef; ++i) {
                _Set((uint)x,(uint)y, (byte)r, (byte)g, (byte)b, (byte)a);
                ++x;
              }

              samef = 0;
            }
          } else {
            if (c.ReadPackedBit()) {
              if (x < Width) {
                r = _sustainR[x];
                g = _sustainG[x];
                b = _sustainB[x];
                _Set((uint)x,(uint)y,(byte)r,(byte)g,(byte)b,(byte)a);
              }

              ++x;
              dg = 0;
            } else {
              // ACTUAL PIXEL
              tg = clampu(g + dg*2);
              tg = tg - g;
              if (tg < -128)
                tg += 256;
              if (tg > 127)
                tg -= 256;

              dg = clampi(tg/2 + c.ReadPackedSigned());

              r = clampu(r + dg*2 + c.ReadPackedSigned()*2);
              g = clampu(g + dg*2);
              b = clampu(b + dg*2 + c.ReadPackedSigned()*2);

              r = (byte)(r & 0xFE);
              g = (byte)(g & 0xFE);
              b = (byte)(b & 0xFE);

              // PIXEL READOUT
              _Set((uint)x, (uint)y, (byte)r, (byte)g, (byte)b, (byte)a);
              ++x;
            }
          }
        }
      }

      _DiscardSustain();
    }

    void _Interleave() {
      _interleaved = new byte[Width*Height*4];
      for (ushort y = 0; y < Height; ++y)
        for (ushort x = 0; x < Width; ++x) {
          _interleaved[y * Width*4 + x*4 + 0] = _dataB[y * Width + x];
          _interleaved[y * Width*4 + x*4 + 1] = _dataG[y * Width + x];
          _interleaved[y * Width*4 + x*4 + 2] = _dataR[y * Width + x];
          _interleaved[y * Width * 4 + x * 4 + 3] = _dataA[y * Width + x];// (byte)(127 + _dataA[y * Width + x] / 2);
        }
    }

    public static Cr6Ti fromExtent(IMultiFileCursor c) {
      var o = new Cr6Ti(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadWord();
      o.Unk3 = c.ReadWord();
      o.Unk4 = c.ReadDword();
      o.Width = c.ReadWord();
      o.Height = c.ReadWord();
      if (o.Width > 4096 || o.Height > 4096)
        throw new Exception(String.Format("insane image dimensions: {0}x{1}", o.Width, o.Height));

      o.Flags  = c.ReadDword();
      o.Depth  = c.ReadDword();
      o.Unk5   = c.ReadByte();
      o.Unk6   = c.ReadWord();
      o.AddChild(c.ReadObjRef());
      o.DataSize = c.ReadDword(); // data size
      o.Unk7   = c.ReadDword();
      o.Unk8   = c.ReadDword();

      var fl = o.Flags & 0xFF;
      if (fl == 2) {
        if ((o.Depth & 0x04) != 0)
          o._LoadOpaque1(c);
        else
          o._LoadOpaque2(c);
      } else if (fl == 3)
        o._LoadTransparent(c);
      else
        throw new Exception("unknown flags in Cr6Ti");

      o._Interleave();

      return o;
    }

    public override ImageInfo GetImage() {
      var img = new ImageInfo();
      img.Width = Width;
      img.Height = Height;
      img.Stride = Width * 4U;
      img.Buffer = _interleaved;
      return img;
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var cc = (IMultiFileCursor)c.Clone();
      cc.Ocean = c.Ocean;
      var o = fromExtent(cc);
      c.Read(o.DataSize+42);
      return o;
    }

    public uint Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public ushort Unk3 { get; set; }
    public uint Unk4 { get; set; }
    public ushort Width { get; set; }
    public ushort Height { get; set; }
    public uint Flags { get; set; }
    public uint Depth { get; set; }
    public byte Unk5 { get; set; }
    public ushort Unk6 { get; set; }
    public uint DataSize { get; set; }
    public uint Unk7 { get; set; }
    public uint Unk8 { get; set; }

    byte[] _dataR;
    byte[] _dataG;
    byte[] _dataB;
    byte[] _dataA;
    byte[] _sustainR;
    byte[] _sustainG;
    byte[] _sustainB;
    byte[] _sustainA;
    byte[] _interleaved;
  }

  public class CRsa : RugpObject {
    public CRsa(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CRsa(c);

      o.Unk1 = c.ReadWord();
      o.Unk2 = c.ReadWord();
      o.Unk3 = c.ReadByte();
      o.Unk4 = c.ReadDword();

      var mfb = c.CreateCryptedSubBuffer(0x7E6B8CE2);
      o.Buffer = mfb.SubBufferInfo.Buffer;

      var ebc = new MultiFileCursor(mfb);
      ebc.Ocean = c.Ocean;

      var origCache = c.Ocean.ClassIDCache;
      c.Ocean.ClassIDCache = new List<ClassID>();
      c.Ocean.PadCache(2);

      try {
        o.BufUnk1 = ebc.ReadWord();
        o.BufUnk2 = ebc.ReadWord();
        o.BufUnk3 = ebc.ReadDword();
        o.BufUnk4 = ebc.ReadDword();
        o.BufUnk5 = ebc.ReadDword();
        var numItems = ebc.ReadDword();
        o.BufUnk6 = ebc.ReadDword();

        for (uint i = 0; i < numItems; ++i) {
          ClassID cid = ebc.ReadClassID();
          if (cid == null)
            continue;

          if (!_supportedNames.Contains(cid.Name))
            throw new Exception(string.Format("Unknown RSA class name: {0}", cid));

          var obj = c.Ocean.LoadObjectAtCursorByName(ebc, cid);
          o.AddChild(obj);
        }
      } finally {
        c.Ocean.ClassIDCache = origCache;
      }

      return o;
    }

    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public byte Unk3 { get; set; }
    public uint Unk4 { get; set; }

    public byte[] Buffer { get; set; }
    public ushort BufUnk1 { get; set; }
    public ushort BufUnk2 { get; set; }
    public uint BufUnk3 { get; set; }
    public uint BufUnk4 { get; set; }
    public uint BufUnk5 { get; set; }
    public uint BufUnk6 { get; set; }

    static HashSet<string> _supportedNames = new HashSet<string>();

    static CRsa() {
      _supportedNames.Add("CVmSound");
      _supportedNames.Add("CVmFlagOp");
      _supportedNames.Add("CVmGenericMsg");
      _supportedNames.Add("CVmCall");
      _supportedNames.Add("CVmSync");
      _supportedNames.Add("CVmLabel");
      _supportedNames.Add("CVmJump");
      _supportedNames.Add("CVmRet");
      _supportedNames.Add("CVmBlt");
      _supportedNames.Add("CVmMsg");
      _supportedNames.Add("CVmImage");
    }
  }

  public class CPostureMoment : RugpObject {
    public CPostureMoment(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CPostureMoment(c);

      o.Unk1 = c.ReadByte();
      o.Unk2 = c.ReadByte();
      o.Unk3 = c.ReadByte();
      for (int i=0; i<5; ++i)
        o.AddChild(c.ReadObjRef());

      o.Unk4 = c.Read(12);
      o.Unk5 = c.ReadDword();
      o.Unk6 = c.Read(6*4);
      o.Unk7 = c.ReadWord();
      o.Unk8 = c.ReadWord();
      o.Unk9 = c.Read(3*4);

      return o;
    }

    public byte Unk1 { get; set; }
    public byte Unk2 { get; set; }
    public byte Unk3 { get; set; }
    public byte[] Unk4 { get; set; }
    public uint Unk5 { get; set; }
    public byte[] Unk6 { get; set; }
    public ushort Unk7 { get; set; }
    public ushort Unk8 { get; set; }
    public byte[] Unk9 { get; set; }
  }

  public class CTWFGaugeBox : RugpObject {
    public CTWFGaugeBox(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CTWFGaugeBox(c);
      c.Read(2 * 2 + 1 * 4 + 1 * 1 + 5 * 8);
      c.ReadString();
      var N = c.ReadWord();
      if (N != 0)
        throw new Exception("TWFGaugeBox");

      c.Read(2 * 2 + 4 * 4 + 1 * 8 + 2 * 2);
      o.AddChild(c.ReadObjRef());

      c.ReadByte();

      o.AddChild(c.ReadObjRef());
      c.Read(2 * 4 + 1 * 2);

      for (int i = 0; i < 5; ++i)
        o.AddChild(c.ReadObjRef());

      return o;
    }
  }

  public class CTWFRemainderBox : RugpObject {
    public CTWFRemainderBox(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CTWFRemainderBox(c);
      c.Read(2 * 2 + 1 * 4 + 1 * 1 + 5 * 8);
      c.ReadString();
      var N = c.ReadWord();
      if (N != 0)
        throw new Exception("TWFRemainderBox");

      c.Read(2 * 2 + 4 * 4 + 1 * 8 + 2 * 2);
      o.AddChild(c.ReadObjRef());

      c.ReadByte();

      o.AddChild(c.ReadObjRef());
      c.Read(2*4 + 1*2);

      o.AddChild(c.ReadObjRef());
      c.ReadDword();

      return o;
    }
  }

  public class CPmEffect : RugpObject {
    public CPmEffect(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CPmEffect(c);
      c.ReadWord();
      o.AddChild(c.ReadObjRef());
      c.ReadString();
      c.ReadWord();
      return o;
    }
  }

  public class CPmBgm : RugpObject {
    public CPmBgm(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CPmBgm(c);
      c.ReadWord();
      o.AddChild(c.ReadObjRef());
      c.ReadString();
      c.ReadWord();
      return o;
    }
  }

  public class CMN_Time_Bezier : RugpObject {
    public CMN_Time_Bezier(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CMN_Time_Bezier(c);
      var N = c.ReadWord();

      for (ushort i = 0; i < N; ++i)
        c.Read(0x18);

      return o;
    }
  }

  public class CMN_Time_2G : RugpObject {
    public CMN_Time_2G(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CMN_Time_2G(c);
      c.ReadDword();
      return o;
    }
  }

  public class CRbx : RugpObject {
    public CRbx(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CRbx(c);
      c.ReadDword();
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());

      var N = c.ReadWord();
      for (ushort i = 0; i < N; ++i) {
        ClassID cid = c.ReadClassID();
        if (cid != null) {
          c.Ocean.AddToCache(cid);
          o.AddChild(c.Ocean.LoadObjectAtCursorByName(c, cid));
        }
      }

      c.Read(1 * 4 + 2 * 2 + 1 * 4);
      var x = c.ReadDword();
      if (x != 0)
        throw new Exception("CRbx");

      return o;
    }
  }

  public class CImgSel : RugpObject {
    public CImgSel(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CImgSel(c);

      c.Read(3 * 4);

      for (int i = 0; i < 4;++i)
        o.AddChild(c.ReadObjRef());

      c.ReadString();
      c.Read(1 * 2 + 1 * 8);

      return o;
    }
  }

  public class CNormalCamera : RugpObject {
    public CNormalCamera(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CNormalCamera(c);
      c.Read(0x24);
      return o;
    }
  }

  public class CAcsPos : RugpObject {
    public CAcsPos(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CAcsPos(c);
      c.Read(6 * 4 + 2 * 2 + 1 * 4);
      return o;
    }
  }

  public class CMoveAcsOngen : RugpObject {
    public CMoveAcsOngen(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CMoveAcsOngen(c);

      o.Unk1 = c.Read(12);
      for (;;) {
        byte a = c.ReadByte();
        if (a == 0x70)
          break;

        byte b = c.ReadByte();
        c.ReadDword();
        if ((b & 1) != 0)
          c.Read(12);
        else {
          c.ReadDword();
          c.ReadDword();
        }

        ushort n = c.ReadWord();
        for (ushort i=0; i<n; ++i)
          c.Read(12);

        if ((b & 2) != 0) {
          var cl = c.ReadArchiveClassID();
          if (cl != null) {
            c.Ocean.AddToCache(cl);
            var obj = c.Ocean.LoadObjectAtCursorByName(c, cl);
            o.AddChild(obj);
          }
        }
      }

      c.ReadDword();
      c.ReadDword();
      c.ReadDword();
      c.ReadByte();
      c.ReadWord();

      return o;
    }

    public byte[] Unk1 { get; set; }
  }

  public class CFlashLayerEffect : RugpObject {
    public CFlashLayerEffect(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFlashLayerEffect(c);

      o.AddChild(c.ReadObjRef());
      o.Unk = c.Read(4*8+2*2+4*7+2*2+4*7);

      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class CFadeNormalBase : RugpObject {
    public CFadeNormalBase(IMultiFileCursor c) : base(c) { }

    protected void _Read(IMultiFileCursor c) {
      var N = c.ReadWord();
      Unk1 = c.ReadWord();

      for (int i = 0; i < 3; ++i)
        AddChild(c.ReadObjRef());

      Unk2 = c.ReadQword();
      Unk3 = c.ReadQword();
      Unk4 = c.ReadQword();
      Unk5 = c.Read((ulong)(N * 4));
    }

    public ushort Unk1 { get; set; }
    public ulong Unk2 { get; set; }
    public ulong Unk3 { get; set; }
    public ulong Unk4 { get; set; }
    public byte[] Unk5 { get; set; }
  }

  public class CFadeNormal :CFadeNormalBase {
    public CFadeNormal(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeNormal(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeSdtRatio : CFadeNormalBase {
    public CFadeSdtRatio(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeSdtRatio(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeRSideCarten : CFadeNormalBase {
    public CFadeRSideCarten(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeRSideCarten(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeMozaik : CFadeNormalBase {
    public CFadeMozaik(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeMozaik(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeMulHorz : CFadeNormalBase {
    public CFadeMulHorz(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeMulHorz(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeInvert : CFadeNormalBase {
    public CFadeInvert(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeInvert(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeShock : CFadeNormalBase {
    public CFadeShock(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeShock(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeXsRatioBase : RugpObject {
    public CFadeXsRatioBase(IMultiFileCursor c) : base(c) { }

    protected void _Read(IMultiFileCursor c) {
      //var o = new CFadeXsRatio(c);
      var N = c.ReadWord();
      Unk1 = c.ReadWord();

      for (ushort i=0; i<3; ++i)
        AddChild(c.ReadObjRef());

      Unk2 = c.ReadQword();
      Unk3 = c.ReadQword();
      Unk4 = c.ReadQword();
      Unk5 = c.Read((ulong)(N*4));
      Unk6 = c.ReadByte();
      Unk7 = c.ReadByte();
      Unk8 = c.ReadWord();
      Unk9 = c.ReadWord();
    }

    public ushort Unk1 { get; set; }
    public ulong Unk2 { get; set; }
    public ulong Unk3 { get; set; }
    public ulong Unk4 { get; set; }
    public byte[] Unk5 { get; set; }
    public byte Unk6 { get; set; }
    public byte Unk7 { get; set; }
    public ushort Unk8 { get; set; }
    public ushort Unk9 { get; set; }
  }

  public class CFadeXsRatio : CFadeXsRatioBase {
    public CFadeXsRatio(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsRatio(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeXsMergeBlack : CFadeXsRatioBase {
    public CFadeXsMergeBlack(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsMergeBlack(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeStretchAntiBase : CFadeNormalBase {
    public CFadeStretchAntiBase(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);

      c.Read(2 * 8 + 1);
    }
  }

  public class CFadeStretchAnti : CFadeStretchAntiBase {
    public CFadeStretchAnti(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeStretchAnti(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeOverStretchAnti : CFadeStretchAntiBase {
    public CFadeOverStretchAnti(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);

      c.Read(2 * 4);
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeOverStretchAnti(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeXsOverStretchAnti : CFadeXsRatioBase {
    public CFadeXsOverStretchAnti(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);

      c.Read(2 * 12);
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsOverStretchAnti(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeQubeStretchAnti : CFadeStretchAntiBase {
    public CFadeQubeStretchAnti(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);

      for (int i = 0; i < 13; ++i)
        c.ReadWord();

      c.ReadByte();
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeQubeStretchAnti(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeXsRasterNoize : CFadeXsRatioBase {
    public CFadeXsRasterNoize(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);

      c.Read(4*2 + 3*1 + 1*4 + 1*2);
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsRasterNoize(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeCarten : CFadeNormalBase {
    public CFadeCarten(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      c.ReadWord();
      c.ReadWord();
      c.ReadByte();
      c.ReadByte();
      base._Read(c);
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeCarten(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeXsSqrRaster_HRasterV_VRasterHBase : CFadeXsRatioBase {
    public CFadeXsSqrRaster_HRasterV_VRasterHBase(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);
      c.Read(4 * 2 + 3 * 1);
    }
  }

  public class CFadeXsSqrRaster_HRasterV_VRasterH :CFadeXsSqrRaster_HRasterV_VRasterHBase {
    public CFadeXsSqrRaster_HRasterV_VRasterH(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsSqrRaster_HRasterV_VRasterH(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeXsSrcRotate : CFadeXsRatioBase {
    public CFadeXsSrcRotate(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);
      c.Read(2 * 4 + 4 * 2 + 2 * 1);
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsSrcRotate(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeXsCircleRaster : CFadeXsSqrRaster_HRasterV_VRasterHBase {
    public CFadeXsCircleRaster(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);
      c.ReadDword();
    }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsCircleRaster(c);
      o._Read(c);
      return o;
    }
  }
  
  public class CFadeXsHRasterHOffset : CFadeXsSqrRaster_HRasterV_VRasterHBase {
    public CFadeXsHRasterHOffset(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeXsHRasterHOffset(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeMergeBlack : RugpObject {
    public CFadeMergeBlack(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeMergeBlack(c);
      o.N    = c.ReadWord();
      o.Unk1 = c.ReadWord();
      for (int i=0; i<3; ++i)
        o.AddChild(c.ReadObjRef());

      o.Unk2 = c.ReadQword();
      o.Unk3 = c.ReadQword();
      o.Unk4 = c.ReadQword();
      o.Unk5 = c.Read(0x44);

      return o;
    }

    public ushort N { get; set; }
    public ushort Unk1 { get; set; }
    public ulong Unk2 { get; set; }
    public ulong Unk3 { get; set; }
    public ulong Unk4 { get; set; }
    public byte[] Unk5 { get; set; }
  }

  public class CFadeMergeWhiteBase : CFadeNormalBase {
    public CFadeMergeWhiteBase(IMultiFileCursor c) : base(c) { }

    protected new void _Read(IMultiFileCursor c) {
      base._Read(c);
      Unk6 = c.ReadDword();
    }

    public uint Unk6 { get; set; }
  }

  public class CFadeMergeWhite : CFadeMergeWhiteBase {
    public CFadeMergeWhite(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeMergeWhite(c);
      o._Read(c);
      return o;
    }
  }

  public class CFadeMergeColor : CFadeMergeWhiteBase {
    public CFadeMergeColor(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CFadeMergeColor(c);
      o._Read(c);
      return o;
    }
  }

  public class RSAsub : RugpObject {
    public RSAsub(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new RSAsub(c);

      o.D = c.ReadDword();
      if ((o.D & 0x03) == 0) {
        o.AddChild(c.ReadObjRef());
      } else if ((o.D & 0x03) == 1) {
        o.Name = c.ReadString();
      } else if ((o.D & 0x03) == 2) {
        // nothing
      } else
        throw new Exception("Unexpected value of D & 0x03");

      return o;
    }

    public uint D { get; set; }
    public string Name { get; set; }
  }

  public class CVmSound : RugpObject {
    public CVmSound(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmSound(c);

      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      o.Unk3 = c.ReadByte();
      o.F = c.ReadByte();
      o.Unk4 = c.ReadWord();
      o.Unk5 = c.ReadWord();
      o.Unk6 = c.ReadWord();

      if ((o.F & 0x04) != 0) {
        if ((o.F & 0x08) != 0) {
          o.AddChild(c.ReadObjRef());
        } else {
          o.Unk7 = c.ReadWord();
          o.Unk8 = c.ReadWord();
          o.Unk9 = c.ReadWord();
          o.Unk10 = c.ReadWord();
          if ((o.F & 0x10) == 0) {
            o.Unk11 = c.ReadWord();
            o.Unk12 = c.ReadWord();
            o.Unk13 = c.ReadWord();
          }
        }
      }

      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public byte Unk3 { get; set; }
    public byte F { get; set; }
    public ushort Unk4 { get; set; }
    public ushort Unk5 { get; set; }
    public ushort Unk6 { get; set; }
    public ushort Unk7 { get; set; }
    public ushort Unk8 { get; set; }
    public ushort Unk9 { get; set; }
    public ushort Unk10 { get; set; }
    public ushort Unk11 { get; set; }
    public ushort Unk12 { get; set; }
    public ushort Unk13 { get; set; }
  }

  public class CVmFlagOp : RugpObject {
    public CVmFlagOp(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmFlagOp(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.AddChild(c.ReadObject<RSAsub>());
      o.AddChild(c.ReadObject<RSAsub>());
      o.AddChild(c.ReadObjRef());
      o.Unk3 = c.ReadWord();
      o.Unk4 = c.ReadByte();
      o.Unk5 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public ushort Unk3 { get; set; }
    public byte Unk4 { get; set; }
    public uint Unk5 { get; set; }
  }

  public class CVmGenericMsg : RugpObject {
    public CVmGenericMsg(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmGenericMsg(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.AddChild(c.ReadObject<RSAsub>());
      o.RawClass = c.ReadRawClassID();
      c.Ocean.DupCache();
      if (o.RawClass == null || !o.RawClass.Name.StartsWith("&-"))
        throw new Exception(String.Format("Invalid CVmGenericMsg: {0}", o.RawClass));
      var om = c.Ocean.LoadObjectAtCursorByName(c, o.RawClass);
      o.AddChild(om);
      o.Unk3 = c.ReadWord();

      var N = c.ReadWord();
      for (ushort i = 0; i < N; ++i)
        o.AddChild(c.ReadObject<VmGenericMsgPart>());

        return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
    public ClassID RawClass { get; set; }
  }

  public class VmGenericMsgPart : RugpObject {
    public VmGenericMsgPart(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new VmGenericMsgPart(c);
      var w = c.ReadWord();
      if (w == 0x1257)
        o.ClassID = c.ReadClassID();
      else if (w == 0x369E) {
        o.ClassID = c.ReadRawClassID();
        o.ClassIDIsRaw = true;
      } else if (w == 0x2D6B || w == 0x2F1A) {
        o.Unk1 = c.ReadWord();
        var N = c.ReadWord();
        o.Unk2 = c.Read(N);
      } else
        throw new Exception(String.Format("Unknown w: {0}", w));

      o.X = c.ReadWord();
      if (o.X == 0xFFFF) {
        var L = c.ReadWord();
        o.Name = Encoding.GetEncoding("shift_jis").GetString(c.Read(L));
        c.Ocean.AddToCache(new ClassID(o.Name, 0));
      }

      o.AddChild(c.ReadObject<RSAsub>());

      return o;
    }

    public ClassID ClassID { get; set; }
    public bool ClassIDIsRaw { get; set; }
    public ushort Unk1 { get; set; }
    public byte[] Unk2 { get; set; }
    public ushort X { get; set; }
    public string Name { get; set; }
  }

  public class _OM : RugpObject {
    public _OM(IMultiFileCursor c) : base(c) { }
  }

  public class OM_Null : _OM {
    public OM_Null(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      return new OM_Null(c);
    }
  }

  public class OM_Visible : _OM {
    public OM_Visible(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      return new OM_Null(c);
    }
  }

  public class OM_VmmFinishByUser : _OM {
    public OM_VmmFinishByUser(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_VmmFinishByUser(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_VramAgesImageIn : _OM {
    public OM_VramAgesImageIn(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_VramAgesImageIn(c);
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.Unk3 = c.ReadDword();
      o.Unk4 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      o.Unk5 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
    public uint Unk4 { get; set; }
    public uint Unk5 { get; set; }
  }

  public class OM_UpdateAllView : _OM {
    public OM_UpdateAllView(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      return new OM_UpdateAllView(c);
    }
  }

  public class OM_AccessGetValue : _OM {
    public OM_AccessGetValue(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AccessGetValue(c);

      o.Unk1 = c.ReadString();
      o.Unk2 = c.ReadString();
      o.AddChild(c.ReadObjRef());

      return o;
    }

    public string Unk1 { get; set; }
    public string Unk2 { get; set; }
  }

  public class OM_AccessWrite : _OM {
    public OM_AccessWrite(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AccessWrite(c);

      o.Unk1 = c.ReadString();
      o.Unk2 = c.ReadString();

      return o;
    }

    public string Unk1 { get; set; }
    public string Unk2 { get; set; }
  }

  public class OM_AddPostureMomentum : _OM {
    public OM_AddPostureMomentum(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AddPostureMomentum(c);

      o.AddChild(c.ReadObjRef());

      return o;
    }
  }

  public class OM_AddSelecter : _OM {
    public OM_AddSelecter(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AddSelecter(c);

      o.Unk1 = c.ReadString();
      o.AddChild(c.ReadObjRef());

      return o;
    }

    public string Unk1 { get; set; }
  }

  public class OM_AgesRecalc : _OM {
    public OM_AgesRecalc(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AgesRecalc(c);

      o.Unk1 = c.ReadDword();

      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_AllDisableSelecter : _OM {
    public OM_AllDisableSelecter(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AllDisableSelecter(c);

      return o;
    }
  }

  public class OM_AskUserSelectEx : _OM {
    public OM_AskUserSelectEx(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AskUserSelectEx(c);
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_BodyScroll : _OM {
    public OM_BodyScroll(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_BodyScroll(c);
      o.Unk = c.Read(5*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_BoxScroll : _OM {
    public OM_BoxScroll(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_BoxScroll(c);
      o.Unk = c.Read(5*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_ChangeAllBoxVisibility : _OM {
    public OM_ChangeAllBoxVisibility(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_ChangeAllBoxVisibility(c);
      o.Unk1 = c.ReadByte();
      o.Unk2 = c.ReadWord();
      return o;
    }

    public byte Unk1 { get; set; }
    public ushort Unk2 { get; set; }
  }

  public class OM_ChangeBoxStyleOthers : _OM {
    public OM_ChangeBoxStyleOthers(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_ChangeBoxStyleOthers(c);
      o.Unk = c.Read(2*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_ChangeSelecterStatus : _OM {
    public OM_ChangeSelecterStatus(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_ChangeSelecterStatus(c);
      o.Unk1 = c.ReadString();
      o.Unk2 = c.ReadWord();
      o.Unk3 = c.ReadWord();
      return o;
    }

    public string Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public ushort Unk3 { get; set; }
  }

  public class OM_CopyImage : _OM {
    public OM_CopyImage(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_CopyImage(c);
      o.Unk = c.Read(8*4 + 2*2);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_Disable : _OM {
    public OM_Disable(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_Disable(c);

      return o;
    }
  }

  public class OM_Enable : _OM {
    public OM_Enable(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_Enable(c);

      return o;
    }
  }

  public class OM_EmbedAlpherChannel : _OM {
    public OM_EmbedAlpherChannel(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_EmbedAlpherChannel(c);
      o.AddChild(c.ReadObjRef());
      o.Unk = c.Read(2*2 + 2*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_EnableSelecter : _OM {
    public OM_EnableSelecter(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_EnableSelecter(c);
      o.Unk1 = c.ReadString();
      return o;
    }

    public string Unk1 { get; set; }
  }

  public class OM_AllEnableSelecter : _OM {
    public OM_AllEnableSelecter(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_AllEnableSelecter(c);

      return o;
    }
  }

  public class OM_GetUserSelectHistory : _OM {
    public OM_GetUserSelectHistory(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_GetUserSelectHistory(c);
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.ReadByte();
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte Unk1 { get; set; }
  }

  public class OM_HumanIn : _OM {
    public OM_HumanIn(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_HumanIn(c);
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.Unk3 = c.ReadDword();
      o.Unk4 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      o.Unk5 = c.ReadDword();
      o.Unk6 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
    public uint Unk4 { get; set; }
    public uint Unk5 { get; set; }
    public uint Unk6 { get; set; }
  }

  public class OM_HumanOut : _OM {
    public OM_HumanOut(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_HumanOut(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.Unk3 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
  }

  public class OM_ImageASyncFadeinout : _OM {
    public OM_ImageASyncFadeinout(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_ImageASyncFadeinout(c);
      o.AddChild(c.ReadObjRef());
      o.Unk = c.Read(5*4);
      o.AddChild(c.ReadObjRef()); 
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_StartFLE : _OM {
    public OM_StartFLE(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_StartFLE(c);
      o.AddChild(c.ReadObjRef()); 
      o.Unk = c.Read(8*4+2*2+7*4+2*2+7*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_InitUserSelectHistory : _OM {
    public OM_InitUserSelectHistory(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_InitUserSelectHistory(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_InputEnable : _OM {
    public OM_InputEnable(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_InputEnable(c);

      return o;
    }
  }

  public class OM_Invisible : _OM {
    public OM_Invisible(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_Invisible(c);

      return o;
    }
  }

  public class OM_LoadImageArea : _OM {
    public OM_LoadImageArea(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_LoadImageArea(c);
      o.Unk1 = c.ReadWord();
      o.Unk2 = c.ReadWord();
      o.Unk3 = c.ReadByte();
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public byte Unk3 { get; set; }
  }

  public class OM_OffAutoUpdate : _OM {
    public OM_OffAutoUpdate(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_OffAutoUpdate(c);

      return o;
    }
  }

  public class OM_ParseImageBuild : _OM {
    public OM_ParseImageBuild(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_ParseImageBuild(c);
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.ReadByte();
      return o;
    }

    public byte Unk1 { get; set; }
  }

  public class OM_ParseScreenTrigger : _OM {
    public OM_ParseScreenTrigger(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_ParseScreenTrigger(c);
      o.Unk = c.Read(3*4);
      for (int i=0; i<3; ++i)
        o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_ParseScreenTrigger_AllHumanOut : _OM {
    public OM_ParseScreenTrigger_AllHumanOut(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_ParseScreenTrigger_AllHumanOut(c);

      return o;
    }
  }

  public class OM_PlayDeviceStreamVideo : _OM {
    public OM_PlayDeviceStreamVideo(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_PlayDeviceStreamVideo(c);
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.Unk3 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
  }

  public class OM_PlayMedia : _OM {
    public OM_PlayMedia(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_PlayMedia(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
  }

  public class OM_SetTimeInterval : _OM {
    public OM_SetTimeInterval(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetTimeInterval(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_PlayTextStaffrollVideo : _OM {
    public OM_PlayTextStaffrollVideo(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_PlayTextStaffrollVideo(c);
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      o.Unk = c.Read(2*2 + 7*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_RemoveOffAutoUpdate : _OM {
    public OM_RemoveOffAutoUpdate(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_RemoveOffAutoUpdate(c);

      return o;
    }
  }

  public class OM_RemoveSprite : _OM {
    public OM_RemoveSprite(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_RemoveSprite(c);

      return o;
    }
  }

  public class OM_rUGPSerifSetting : _OM {
    public OM_rUGPSerifSetting(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_rUGPSerifSetting(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadString();
      return o;
    }

    public uint Unk1 { get; set; }
    public string Unk2 { get; set; }
  }

  public class OM_SetAcsRoomRefRatio : _OM {
    public OM_SetAcsRoomRefRatio(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetAcsRoomRefRatio(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_SetAirDepth : _OM {
    public OM_SetAirDepth(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetAirDepth(c);
      o.Unk = c.Read(4+2*2+2*4+2+4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetBaseAltitude : _OM {
    public OM_SetBaseAltitude(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetBaseAltitude(c);
      o.Unk = c.Read(3*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetBaseCompassPoint : _OM {
    public OM_SetBaseCompassPoint(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetBaseCompassPoint(c);
      o.Unk = c.Read(3*2+2*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetBBInfoForQueue : _OM {
    public OM_SetBBInfoForQueue(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetBBInfoForQueue(c);
      o.Unk = c.Read(6*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetBodySize : _OM {
    public OM_SetBodySize(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetBodySize(c);
      o.Unk = c.Read(2*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetBoxPos : _OM {
    public OM_SetBoxPos(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetBoxPos(c);
      o.Unk = c.Read(4*2);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetBreathEffect : _OM {
    public OM_SetBreathEffect(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetBreathEffect(c);
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.Read(3*4+3*2+5*4+1*2);
      o.AddChild(c.ReadObjRef());
      o.Unk2 = c.Read(1*2+2*4);
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte[] Unk1 { get; set; }
    public byte[] Unk2 { get; set; }
  }

  public class OM_SetCacheExtra : _OM {
    public OM_SetCacheExtra(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetCacheExtra(c);
      o.Unk = c.Read(5*2);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetCharBreath : _OM {
    public OM_SetCharBreath(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetCharBreath(c);
      o.Unk = c.Read(4*2);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetColorData : _OM {
    public OM_SetColorData(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetColorData(c);
      o.Unk = c.Read(2*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetDataCPostureMoment : _OM {
    public OM_SetDataCPostureMoment(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetDataCPostureMoment(c);
      o.Unk = c.Read(3*2+4*4+2*2);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetDrawRatioAsParseWnd : _OM {
    public OM_SetDrawRatioAsParseWnd(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetDrawRatioAsParseWnd(c);
      o.Unk1 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      o.Unk2 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
  }

  public class OM_SetExclusiveCamera : _OM {
    public OM_SetExclusiveCamera(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetExclusiveCamera(c);
      o.Unk1 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_SetFitImage : _OM {
    public OM_SetFitImage(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetFitImage(c);

      return o;
    }
  }

  public class OM_SetMsgOutputSpeed : _OM {
    public OM_SetMsgOutputSpeed(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetMsgOutputSpeed(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_SetMsgTextLayout : _OM {
    public OM_SetMsgTextLayout(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetMsgTextLayout(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_SetRmtInfoForQueue : _OM {
    public OM_SetRmtInfoForQueue(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetRmtInfoForQueue(c);
      o.Unk1 = c.Read(4*4);
      o.Unk2 = c.ReadString();
      return o;
    }

    public byte[] Unk1 { get; set; }
    public string Unk2 { get; set; }
  }

  public class OM_SetRmtInfoScaling : _OM {
    public OM_SetRmtInfoScaling(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetRmtInfoScaling(c);
      o.Unk = c.Read(3*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetShading : _OM {
    public OM_SetShading(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetShading(c);
      o.Unk = c.Read(4+2*2+4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetShootingEnviroment : _OM {
    public OM_SetShootingEnviroment(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetShootingEnviroment(c);
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.Read(9*4+2*2+1*4);
      o.AddChild(c.ReadObjRef());
      o.Unk2 = c.ReadDword();
      return o;
    }

    public byte[] Unk1 { get; set; }
    public uint Unk2 { get; set; }
  }

  public class OM_SetShootingParam : _OM {
    public OM_SetShootingParam(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetShootingParam(c);
      o.Unk1 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      o.Unk2 = c.ReadString();
      o.Unk3 = c.ReadString();
      o.Unk4 = c.Read(6*4+3*1);
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      o.Unk5 = c.Read(2*2+6*4+2*2+1*4);
      return o;
    }

    public uint Unk1 { get; set; }
    public string Unk2 { get; set; }
    public string Unk3 { get; set; }
    public byte[] Unk4 { get; set; }
    public byte[] Unk5 { get; set; }
  }

  public class OM_SetSprite : _OM {
    public OM_SetSprite(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetSprite(c);

      return o;
    }
  }

  public class OM_SetStyleOthers : _OM {
    public OM_SetStyleOthers(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetStyleOthers(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_SetUucNavigateString : _OM {
    public OM_SetUucNavigateString(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetUucNavigateString(c);
      o.Unk1 = c.ReadString();
      return o;
    }

    public string Unk1 { get; set; }
  }

  public class OM_SetUucSaveSwitch : _OM {
    public OM_SetUucSaveSwitch(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetUucSaveSwitch(c);
      o.Unk1 = c.ReadByte();
      return o;
    }

    public byte Unk1 { get; set; }
  }

  public class OM_SetVarData : _OM {
    public OM_SetVarData(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetVarData(c);
      o.Unk1 = c.ReadDword();
      o.AddChild(c.ReadObject<RSAsub>());
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_SetWeather : _OM {
    public OM_SetWeather(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetWeather(c);
      o.Unk = c.Read(4*4+3*2+2*4);
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetWeatherRainSnow : _OM {
    public OM_SetWeatherRainSnow(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetWeatherRainSnow(c);
      o.AddChild(c.ReadObjRef());
      o.Unk1 = c.Read(2*2+5*4);
      o.AddChild(c.ReadObjRef());
      o.Unk2 = c.Read(2*2+2*4);
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte[] Unk1 { get; set; }
    public byte[] Unk2 { get; set; }
  }

  public class OM_SetWorldCamera : _OM {
    public OM_SetWorldCamera(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetWorldCamera(c);
      o.Unk = c.Read(9*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetWorldCameraAsScreen : _OM {
    public OM_SetWorldCameraAsScreen(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetWorldCameraAsScreen(c);
      o.Unk = c.Read(6*4);
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_SetWorldPlayerHeight : _OM {
    public OM_SetWorldPlayerHeight(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetWorldPlayerHeight(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_SetWorldSXY : _OM {
    public OM_SetWorldSXY(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_SetWorldSXY(c);
      o.Unk1 = c.ReadWord();
      o.Unk2 = c.ReadWord();
      return o;
    }

    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
  }

  public class OM_StartLayerEffect : _OM {
    public OM_StartLayerEffect(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_StartLayerEffect(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_StopAutoAnim : _OM {
    public OM_StopAutoAnim(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_StopAutoAnim(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_StopLayerEffect : _OM {
    public OM_StopLayerEffect(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_StopLayerEffect(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_StopXsFade : _OM {
    public OM_StopXsFade(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_StopXsFade(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_StoreFrameImage : _OM {
    public OM_StoreFrameImage(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_StoreFrameImage(c);
      o.AddChild(c.ReadObjRef());
      return o;
    }
  }

  public class OM_TryBrowseAndLoadUuc : _OM {
    public OM_TryBrowseAndLoadUuc(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TryBrowseAndLoadUuc(c);
      o.Unk1 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
  }

  public class OM_TWF_ChangeArmCount : _OM {
    public OM_TWF_ChangeArmCount(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TWF_ChangeArmCount(c);
      o.Unk1 = c.Read(2*2);
      o.Unk2 = c.ReadString();
      o.Unk3 = c.Read(2*4);
      return o;
    }

    public byte[] Unk1 { get; set; }
    public string Unk2 { get; set; }
    public byte[] Unk3 { get; set; }
  }

  public class OM_TWF_ExclusiveCommand : _OM {
    public OM_TWF_ExclusiveCommand(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TWF_ExclusiveCommand(c);
      o.Unk = c.Read(2*4);
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_TWF_ExclusiveCommand2 : _OM {
    public OM_TWF_ExclusiveCommand2(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TWF_ExclusiveCommand2(c);
      o.Unk = c.Read(3*4+2*2+3*4+11*2+3*4+2*2+3*4);
      o.AddChild(c.ReadObjRef());
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte[] Unk { get; set; }
  }

  public class OM_TWFGauge_AddPostureMomentum : _OM {
    public OM_TWFGauge_AddPostureMomentum(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TWFGauge_AddPostureMomentum(c);
      o.AddChild(c.ReadObjRef());
      return o;
    }
  }

  public class OM_TWF_SelectArm : _OM {
    public OM_TWF_SelectArm(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TWF_SelectArm(c);
      o.Unk1 = c.Read(2*2);
      o.Unk2 = c.ReadString();
      o.Unk3 = c.ReadDword();
      return o;
    }

    public byte[] Unk1 { get; set; }
    public string Unk2 { get; set; }
    public uint Unk3 { get; set; }
  }

  public class OM_TWF_SetArm : _OM {
    public OM_TWF_SetArm(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TWF_SetArm(c);
      o.Unk1 = c.Read(2*2);
      o.Unk2 = c.ReadString();
      o.Unk3 = c.ReadDword();
      for (int i=0; i<3; ++i)
        o.AddChild(c.ReadObjRef());
      return o;
    }

    public byte[] Unk1 { get; set; }
    public string Unk2 { get; set; }
    public uint Unk3 { get; set; }
  }

  public class OM_TWF_SetGaugeSpeed : _OM {
    public OM_TWF_SetGaugeSpeed(IMultiFileCursor c) : base(c) { }
    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new OM_TWF_SetGaugeSpeed(c);
      o.Unk1 = c.ReadWord();
      o.Unk2 = c.ReadDword();
      o.Unk3 = c.ReadDword();
      return o;
    }

    public ushort Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public uint Unk3 { get; set; }
  }

  public class CVmRet : RugpObject {
    public CVmRet(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmRet(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
  }

  public class CVmCall : RugpObject {
    public CVmCall(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmCall(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.AddChild(c.ReadObjRef());

      var numItems = c.ReadWord();
      for (ushort i=0; i<numItems; ++i)
        o.AddChild(c.ReadObject<RSAsub>());

      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
  }

  public class CVmSync : RugpObject {
    public CVmSync(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmSync(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      o.Unk3 = c.ReadByte();
      o.Unk4 = c.ReadDword();
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public byte Unk3 { get; set; }
    public uint Unk4 { get; set; }
  }

  public class CVmLabel : RugpObject {
    public CVmLabel(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmLabel(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
  }

  public class CVmJump : RugpObject {
    public CVmJump(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmJump(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
  }

  public class CVmBlt : RugpObject {
    public CVmBlt(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmBlt(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.ClassID = c.ReadClassID();
      if (o.ClassID != null) {
        c.Ocean.DupCache();
        var obj = c.Ocean.LoadObjectAtCursorByName(c, o.ClassID);
        o.AddChild(obj);
      }

      o.Unk3 = c.ReadWord();
      o.Unk4 = c.ReadByte();
      o.Unk5 = c.ReadByte();
      o.AddChild(c.ReadObject<RSAsub>());
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public ClassID ClassID { get; set; }
    public ushort Unk3 { get; set; }
    public byte Unk4 { get; set; }
    public byte Unk5 { get; set; }
  }

  public class CVmMsg : RugpObject {
    public CVmMsg(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmMsg(c);
      o.Unk1 = c.ReadDword();
      o.Flags = c.ReadDword();
      o.AddChild(c.ReadObjRef());
      uint N = c.ReadByte();
      if (N != 0xFF)
        o.Name = Encoding.GetEncoding("shift_jis").GetString(c.Read(N));
      else {
        N = c.ReadWord();
        o.Name = Encoding.GetEncoding("shift_jis").GetString(c.Read(N));
      }

      o.AddChild(c.ReadObjRef());
      if ((o.Flags & 0x04000000) != 0)
        o.AddChild(c.ReadObjRef());

      if ((o.Flags & 0x00200000) != 0) {
        var w = c.ReadWord();
        if ((w & 0x03) != 0) {
          var N_ = c.ReadWord();
          for (ushort i=0; i<N_; ++i) {
            o.AddChild(c.ReadObjRef());
            if ((w & 0x03) == 3)
              o.AddChild(c.ReadObjRef());
          }
        }
      }

      return o;
    }

    public uint Unk1 { get; set; }
    public uint Flags { get; set; }
    public string Name { get; set; }
  }

  public class CVmImage : RugpObject {
    public CVmImage(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new CVmImage(c);
      o.Unk1 = c.ReadDword();
      o.Unk2 = c.ReadDword();
      o.Unk3 = c.ReadWord();
      o.Unk4 = c.ReadWord();
      o.Unk5 = c.ReadByte();
      o.Unk6 = c.ReadByte();
      o.AddChild(c.ReadObject<RSAsub>());
      o.AddChild(c.ReadObject<RSAsub>());
      return o;
    }

    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
    public ushort Unk3 { get; set; }
    public ushort Unk4 { get; set; }
    public byte Unk5 { get; set; }
    public byte Unk6 { get; set; }
  }

  public class ObjRef : RugpObject {
    public ObjRef(IMultiFileCursor c) : base(c) { }

    public static RugpObject fromOcean(IMultiFileCursor c) {
      var o = new ObjRef(c);
      if (!c.DoneCPC) {
        c.Ocean.PadCache(c.ReadByte());
        c.DoneCPC = true;
      }

      o.ClassID = c.ReadClassID();
      if (o.ClassID == null)
        return new NullReference(c);

      var flags = c.ReadWord();
      var fB = flags & 0x0007;
      var fA = (flags & 0x0040) != 0;
      var fC = (flags & 0x8000) != 0;

      if (fA) {
        c.Ocean.ClassIDCache.Add(o.ClassID);
        var obj = c.Ocean.LoadObjectAtCursorByName(c, o.ClassID);
        //var obj = o.ClassID.InstantiateFromOcean(c);
        o.AddChild(obj);
        o.Inline = true;
      } else {
        o.Extent = c.ReadExtent();
        o.AddChild(fromOcean(c));
        if (fB == 0x01)
          o.Unk1 = c.ReadDword();
        else if (fC)
          o.Unk2 = c.ReadByte();
        else
          o.Unk3 = c.ReadWord();
        c.Ocean.ClassIDCache.Add(o.ClassID);
      }
      return o;
    }

    public override string ToString() {
      return String.Format("OR: {0}\n  {1}", ClassID.ToString(), Extent);
    }

    public override IEnumerator<RugpObject> GetAdditionalChildren() {
      if (Inline)
        return base.GetAdditionalChildren();

      return Enumerable.Repeat(ExObject, 1).GetEnumerator();
    }

    RugpObject _GetExObject() {
      RugpObject tgt;
      if (!_exObject.TryGetTarget(out tgt)) {
        tgt = Ocean.LoadObjectFromExtent(Extent, ClassID);
        _exObject.SetTarget(tgt);
      }

      return tgt;
      /*
      if (_exObject == null)
        _exObject = Ocean.LoadObjectFromExtent(Extent, ClassID);

      return _exObject;*/
    }

    public ClassID ClassID { get; set; }
    public Extent Extent { get; set; }
    public uint Unk1 { get; set; }
    public byte Unk2 { get; set; }
    public ushort Unk3 { get; set; }

    private WeakReference<RugpObject> _exObject = new WeakReference<RugpObject>(null);
    public RugpObject ExObject { get { return _GetExObject(); } }
    public bool Inline { get; set; }
  }
}
