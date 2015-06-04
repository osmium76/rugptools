using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;

namespace RugpLib {
  public static class Hex {
    public static string Encode(byte[] data) {
      var sb = new StringBuilder();
      uint pos = 0;
      const uint numBytesPerRow = 16;
      for (; pos < data.Length; ) {
        uint limit = pos + numBytesPerRow;
        if (limit > data.Length)
          limit = (uint)data.Length;

        uint j;
        sb.AppendFormat("{0,8:X})  ", pos);
        for (j = pos; j < limit; ++j)
          sb.AppendFormat("{0,2:X2} ", data[j]);

        for (; (j % numBytesPerRow) != 0; ++j)
          sb.Append("   ");

        sb.Append("  ");
        for (j = pos; j < limit; ++j)
          sb.AppendFormat("{0}", toChar(data[j]));

        sb.Append("\n");
        pos = limit;
      }

      sb.AppendFormat("{0,8:X})  ", pos);
      return sb.ToString();
    }

    static string toChar(byte b) {
      if (b >= 0x20 && b < 0x7F)
        return new String((char)b, 1);
      else
        return ".";
    }
  }

  public struct Extent {
    public Extent(ulong offset, ulong length) {
      Offset = offset;
      Length = length;
    }

    public override string ToString() {
      return String.Format("Extent(0x{0:x},{1})", Offset, Length);      
    }

    public ulong Offset;
    public ulong Length;
  }

  public class ClassID {
    public ClassID() {
    }

    public ClassID(string name, ushort schema) {
      Name = name;
      Schema = schema;
    }

    public override string ToString() {
      if (Name == null && !Schema.HasValue)
        return "#?";
      return String.Format("{0}#{1}", Name, Schema);
    }

    /*public RugpObject InstantiateFromOcean(IMultiFileCursorBase c) {
      var A = Assembly.GetExecutingAssembly();
      Type T = A.GetType(Name, true);
      var argTypes = new Type[] {typeof(IMultiFileCursorBase)};
      var method = T.GetMethod("fromOcean", BindingFlags.Static, null, argTypes, null);
      if (method == null)
        throw new Exception(String.Format("Cannot find fromOcean method when trying to instantiate {0}", this));
      var rv = method.Invoke(null, new object[] { c });
      if (rv == null)
        throw new Exception(String.Format("Failed to instantiate via fromOcean: {0}", Name)); // return null;

      return (RugpObject)rv;
    }*/

    public string Name { get; set; }
    public ushort? Schema { get; set; }
  }

  public interface IOcean {
    void TrimCache(uint n);
    void PadCache(uint n);
    void DupCache();
    void AddToCache(ClassID classID);
    byte[] GetRawDataAtExtent(Extent e);

    CrelicUnitedGameProject Project { get; }
    List<ClassID> ClassIDCache { get; set; }
    RugpObject LoadObjectFromExtent(Extent e, ClassID clsid);
    RugpObject LoadObjectAtCursorByName(IMultiFileCursor c, ClassID clsid);
    RugpObject LoadObjectAtCursorByType<T>(IMultiFileCursor c);
  }

  public static class RugpUtils {
    public static bool ReadPackedBit(this IMultiFileCursorBase c) {
      if (c.BitDelta == 0)
        c.BitTemp = c.ReadByte();

      bool b = (c.BitTemp & 1) != 0;
      c.BitTemp = (byte)(c.BitTemp >> (byte)1);
      c.BitDelta = (byte)((c.BitDelta + 1) % 8);

      return b;
    }

    public static uint ReadPackedUnsigned(this IMultiFileCursorBase c) {
      if (!c.ReadPackedBit())
        return 0;
      uint t = 1;
      for (; ; ) {
        t = (t << 1) | (uint)(c.ReadPackedBit() ? 1 : 0);
        if (!c.ReadPackedBit())
          break;
      }
      return t - 1;
    }

    public static int ReadPackedSigned(this IMultiFileCursorBase c) {
      if (!c.ReadPackedBit())
        return 0;

      int s = 1 - (c.ReadPackedBit() ? 1 : 0)*2;
      int t = 1;
      int i = 0;
      for (; ; ) {
        if (i >= 6 || !c.ReadPackedBit())
          break;
        t = (t << 1) | (c.ReadPackedBit() ? 1 : 0);
        ++i;
      }

      return s * t;
    }

    public static uint DecodeOffset(uint offset) {
      return 3 + 2 * (offset - 0xA2FB6AD1);
    }

    public static uint DecodeSize(uint size) {
      uint a = (size - 0xE7B5D9F8);
      uint c = a >> 0x0D;
      uint d = c & 0xFFF;
      uint a2 = a - d;
      uint x = (a2 << 0x13) | c;
      return x - 3;
    }

    public static byte ReadByte(this IMultiFileCursorBase c) {
      var buf = c.Read(1);
      return buf[0];
    }

    public static ushort ReadWord(this IMultiFileCursorBase c) {
      var buf = c.Read(2);
      return BitConverter.ToUInt16(buf, 0);
    }

    public static uint ReadDword(this IMultiFileCursorBase c) {
      var buf = c.Read(4);
      return BitConverter.ToUInt32(buf, 0);
    }

    public static UInt64 ReadQword(this IMultiFileCursorBase c) {
      var buf = c.Read(8);
      return BitConverter.ToUInt64(buf, 0);
    }

    public static Extent ReadExtent(this IMultiFileCursorBase c) {
      var offset = DecodeOffset(c.ReadDword());
      var length = DecodeSize(c.ReadDword());
      return new Extent(offset, length);
    }

    public static string ReadString(this IMultiFileCursorBase c) {
      Encoding enc = Encoding.GetEncoding("shift_jis");

      var length = c.ReadByte();
      if (length == 0xFF)
        throw new Exception("long string");

      var buf = c.Read(length);

      return enc.GetString(buf);
    }

    public static string ReadString2B(this IMultiFileCursorBase c) {
      Encoding enc = Encoding.GetEncoding("shift_jis");

      var length = c.ReadWord();
      var buf = c.Read(length);

      return enc.GetString(buf);
    }

    public static ClassID ReadClassID(this IMultiFileCursorBase c, IOcean ocean=null) {
      if (ocean == null)
        ocean = c.Ocean;
      if (ocean == null)
        throw new ArgumentNullException("ocean");
      var w = c.ReadWord();
      ushort schema;
      string name;
      if (w == 0xFFFF) {
        schema = c.ReadWord();
        name = c.ReadClassName();
        var cl = new ClassID(name, schema);
        //Console.WriteLine(String.Format("RCL: New: {0} (->{1})", cl, ocean.ClassIDCache.Count));
        ocean.ClassIDCache.Add(cl);
        return cl;
      } else if ((w & 0x8000) != 0) {
        var n = w & 0x7FFF;
        if (n >= ocean.ClassIDCache.Count)
          throw new IndexOutOfRangeException(
            String.Format("Cache item {0} requested but not in cache ({1} items in cache)",
              n, ocean.ClassIDCache.Count));

        //Console.WriteLine(String.Format("RCL: Ref: {0} ({1})", ocean.ClassIDCache[n], n));
        return ocean.ClassIDCache[n];
      } else {
        //Console.WriteLine("RCL: None");
        return null;
      }
    }

    public static ClassID ReadRawClassID(this IMultiFileCursorBase c, IOcean ocean = null) {
      if (ocean == null)
        ocean = c.Ocean;
      if (ocean == null)
        throw new ArgumentNullException("ocean");
      var w = c.ReadWord();
      if (w == 0xFFFF) {
        var schema = c.ReadByte();
        var name = c.ReadString();
        var cl = new ClassID(name, schema);
        ocean.ClassIDCache.Add(cl);
        return cl;
      } else if ((w & 0x8000) != 0) {
        var n = w & 0x7FFF;
        if (n >= ocean.ClassIDCache.Count)
          throw new IndexOutOfRangeException(
            String.Format("Raw cache item {0} requested but not in cache ({1} items in cache)",
            n, ocean.ClassIDCache.Count));
        return ocean.ClassIDCache[n];
      } else
        return null;
    }

    public static ClassID ReadArchiveClassID(this IMultiFileCursorBase c, IOcean ocean = null) {
      if (ocean == null)
        ocean = c.Ocean;
      if (ocean == null)
        throw new ArgumentNullException("ocean");
      var w = c.ReadWord();
      if (w == 0xFFFF) {
        var schema = c.ReadWord();
        var n = c.ReadWord();
        var nameb = c.Read(n);
        var name = Encoding.GetEncoding("shift_jis").GetString(nameb);
        var cl = new ClassID(name, schema);
        ocean.ClassIDCache.Add(cl);
        return cl;
      } else if ((w & 0x8000) != 0) {
        var n = w & 0x7FFF;
        if (n >= ocean.ClassIDCache.Count)
          throw new IndexOutOfRangeException(
            String.Format("Archive cache item {0} requested but not in cache ({1} items in cache)",
            n, ocean.ClassIDCache.Count));
        return ocean.ClassIDCache[n];
      } else
        return null;
    }

    public static ClassID RequireClass(this IMultiFileCursorBase c, IOcean ocean = null) {
      var cl = c.ReadClassID(ocean);
      if (cl == null)
        throw new ArgumentNullException("Got null class ID, but class ID is required");
      return cl;
    }

    public static string ReadClassName(this IMultiFileCursorBase c) {
      var length = c.ReadByte();
      var d = c.Read(length);
      return DecodeClassName(d);
    }

    static bool bitIsSet(byte[] d, int bitNum) {
      return (d[bitNum / 8] & (1 << (bitNum%8))) != 0;
    }
    public static string DecodeClassName(byte[] d) {
      int s = 0;
      int t, i;
      var name = "";
      if (!bitIsSet(d,s))
        name += "C";
      s++;
      for (;;) {
        if (s >= d.Length * 8)
          break;
        if (!bitIsSet(d, s)) {
          s++;
          if (s + 3 >= d.Length * 8)
            break;
          t = 0;
          i = 0;
          while (i < 4) {
            if (bitIsSet(d,s))
              t |= 1<<i;
            s++;
            i++;
          }
          name += "eaitrosducmnSglR"[t];
        } else {
          s++;
          if (s >= d.Length*8)
            break;
          if (!bitIsSet(d, s)) {
            s++;
            if (s+3 >= d.Length*8)
              break;
            t=0;
            i=0;
            while (i<4) {
              if (bitIsSet(d,s))
                t |= 1<<i;
              s++;
              i++;
            }
            if (t != 0)
              name += "_COFLfBMxphyAVbI"[t];
            else {
              if (s+7 >= d.Length*8)
                break;
              t=0;
              i=0;
              while (i<8) {
                if (bitIsSet(d,s))
                  t |= 1<<i;
                s++;
                i++;
              }
              name += char.ToString((char)t);
            }
          } else {
            s++;
            if (s + 4 >= d.Length * 8)
              break;
            t = 0;
            i = 0;
            while (i < 5) {
              if (bitIsSet(d,s))
                t |= 1 << i;
              s++;
              i++;
            }
            if (t < 31)
              name += "EHTDPWXkqvNjwGz02U_K15JQZ467839"[t];
          }
        }
      }
      return name;
    }

    public static IMultiFileStream CreateCryptedSubBuffer(this IMultiFileCursor c, uint key) {
      var sbi = new SubBufferInfo();
      ulong pos = c.Position;
      sbi.IsCrypted = true;
      sbi.Key = key;

      var eb = c.ReadCryptedBuffer(key);
      var mfb = new MultiFileBuffer(eb);
      sbi.Extent = new Extent(pos, c.Position - pos);
      sbi.InnerLength = (ulong)eb.LongLength;
      sbi.Buffer = eb;

      mfb.SubBufferInfo = sbi;
      return mfb;
    }

    public static byte[] ReadCryptedBuffer(this IMultiFileCursorBase c, uint key) {
      var size1 = ~(c.ReadDword() ^ 0xC92E568B);
      var size2 = (c.ReadDword() ^ 0xC92E568F) / 8;
      ushort cc = 0;
      var buf = new byte[size2];
      for (int i = 0; i < size2; ++i) {
        buf[i] = (byte)(c.ReadByte() ^ (byte)key);
        cc += (ushort)(buf[i] * (0x20 - i % 0x20));
        key = ~(key * 2 + 0xA3B376C9 + ((key >> 15) & 1));
        if (i % 0x20 == 0x1F) {
          var check = c.ReadWord();
          if (cc != check)
            throw new IOException("crypted buffer checksum mismatch");
          cc = 0;
        }
      }
      return buf;
    }

    public static RugpObject ReadObject<T>(this IMultiFileCursorBase c) {
      return c.Ocean.LoadObjectAtCursorByType<T>((IMultiFileCursor)c);
    }

    public static RugpObject ReadObjRef(this IMultiFileCursorBase c) {
      return c.ReadObject<ObjRef>();
    }
  }
}
