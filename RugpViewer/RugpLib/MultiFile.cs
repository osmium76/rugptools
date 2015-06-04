using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace RugpLib {
  public interface IMultiFileStream {
    byte[] ReadAt(ulong offset, ulong size);
    ulong Length { get; }

    // May be null, if this is not a sub-buffer.
    SubBufferInfo SubBufferInfo { get; }
  }

  public interface IMultiFileCursorBase {
    byte[] Read(ulong size);

    IMultiFileCursorBase Clone();

    byte BitDelta { get; set; }
    byte BitTemp { get; set; }

    IOcean Ocean { get; set; }
    bool DoneCPC { get; set; }
  }

  public interface IMultiFileCursor :IMultiFileCursorBase {
    void Seek(long offset, SeekOrigin origin);
    ulong Position { get; }
    IMultiFileStream Stream { get; }
  }

  // Used to provide information on how a MultiFileBuffer relates to a parent stream.
  public class SubBufferInfo {
    public IMultiFileStream ContainingStream { get; set; }

    public SubBufferInfo ParentSubBuffer { get { return ContainingStream != null ? ContainingStream.SubBufferInfo : null; } }

    // The extent of the sub-buffer within the containing stream.
    public Extent Extent { get; set; }

    // The length of the data contained in the buffer (excludes crypt buffer overheads).
    public ulong InnerLength { get; set; }

    // The full sub-buffer.
    public byte[] Buffer { get; set; }

    // The decryption key for a crypted buffer.
    public uint Key { get; set; }
    public bool IsCrypted { get; set; }

    public override string ToString() {
      string s = ", ";
      if (ParentSubBuffer != null)
        s += ParentSubBuffer.ToString();
      else
        s = "";

      if (IsCrypted)
        return String.Format("SubBufferInfo(Extent={0}, InnerLength={1}, Key=0x{2:X8}{3})", Extent, InnerLength, Key, s);
      else
        return String.Format("SubBufferInfo(Extent={0}, InnerLength={1}{2})", Extent, InnerLength, s);
    }
  }

  class MultiFileBuffer : IMultiFileStream {
    public MultiFileBuffer(byte[] buf) {
      this.buf = buf;
    }

    public byte[] ReadAt(ulong offset, ulong size) {
      return buf.Skip((int)offset).Take((int)size).ToArray();
    }
    public ulong Length { get { return (ulong)buf.Length; } }
    byte[] buf;

    public SubBufferInfo SubBufferInfo { get; set; }
  }

  public class MultiFile :IMultiFileStream {
    class FileInfo {
      public FileInfo(FileStream stream, ulong offset, ulong length) {
        Stream = stream;
        Offset = offset;
        Length = length;
      }

      public FileStream Stream { get; set; }
      public ulong Offset { get; set; }
      public ulong Length { get; set; }
    }

    public SubBufferInfo SubBufferInfo { get { return null; } }

    public MultiFile(string filename) {
      Filename = filename;
      for (int i = 1; i < 999; ++i) {
        var fn = makeRioFilename(Filename, i);
        if (!File.Exists(fn))
          break;
        var f = File.Open(fn, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        appendFile(f);
      }
      if (files.Count == 0)
        throw new FileNotFoundException("Cannot find file:", filename);
    }

    public byte[] ReadAt(ulong offset, ulong size) {
      var fi = findOffset(offset);
      if (fi == null)
        return new byte[] {};
      fi.Stream.Seek((long)(offset - fi.Offset), SeekOrigin.Begin);
      if ((offset + size) > (fi.Offset + fi.Length)) {
        var Lx = fi.Offset + fi.Length - offset;
        return readExact(fi.Stream, Lx).Concat(ReadAt(offset + Lx, size - Lx)).ToArray();
      }
      return readExact(fi.Stream, size);
    }

    internal static byte[] readExact(FileStream f, ulong size) {
      var buf = new byte[size];
      var bufnr = f.Read(buf, 0, (int)size);
      if ((ulong)bufnr < size)
        throw new IOException("short read");
      return buf;
    }

    void appendFile(FileStream f) {
      ulong offset = 0;
      if (files.Count > 0) {
        var lf = files.Last();
        offset = lf.Offset + lf.Length;
      }
      var length = (ulong)f.Seek(0, SeekOrigin.End);
      f.Seek(0, SeekOrigin.Begin);
      var fi = new FileInfo(f, offset, length);
      files.Add(fi);
      this.length += length;
    }

    FileInfo findOffset(ulong offset) {
      foreach (var fi in files)
        if (offset >= fi.Offset && offset < (fi.Offset + fi.Length))
          return fi;
      return null;
    }

    static string makeRioFilename(string filename, int i) {
      if (i == 1)
        return filename;
      return String.Format("{0}.{1:000}", filename, i);
    }

    public ulong Length { get { return length; } }

    public string Filename { get; protected set; }
    List<FileInfo> files = new List<FileInfo>();
    ulong length = 0;
  }

  class MultiFileCrop :IMultiFileStream {
    public MultiFileCrop(MultiFile mf, ulong offset, ulong length) {
      multiFile = mf;
      this.offset = offset;
      this.length = length;
    }

    public byte[] ReadAt(ulong offset, ulong length) {
      if (offset + length > this.length)
        length = this.length - offset;
      if (length == 0)
        return new byte[] { };
      return multiFile.ReadAt(this.offset + offset, length);
    }

    public ulong Length { get { return length; } }

    public SubBufferInfo SubBufferInfo {
      get { return multiFile.SubBufferInfo; }
      set { throw new Exception("can't set"); }
    }

    IMultiFileStream multiFile;
    ulong offset, length;
  }

  public class MultiFileCursor :IMultiFileCursor {
    public MultiFileCursor(IMultiFileStream mf) {
      multiFile = mf;
      position = 0;
      DoneCPC = false;
    }

    public MultiFileCursor(MultiFileCursor c) {
      multiFile = c.multiFile;
      position = c.position;
      DoneCPC = c.DoneCPC;// false;
    }

    public byte[] Read(ulong size) {
      var buf = multiFile.ReadAt(position, size);
      position += (ulong)buf.Length;
      return buf;
    }

    public IMultiFileCursorBase Clone() {
      return new MultiFileCursor(this);
    }

    public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin) {
      if (origin == SeekOrigin.Begin)
        position = (ulong)offset;
      else if (origin == SeekOrigin.Current)
        position += (ulong)offset;
      else if (origin == SeekOrigin.End)
        position = multiFile.Length + (ulong)offset;
    }

    public byte BitTemp { get; set; }
    public byte BitDelta { get; set; }

    public IOcean Ocean { get; set; }
    public ulong Position { get { return position; } }

    public IMultiFileStream Stream { get { return multiFile; } }

    IMultiFileStream multiFile;
    ulong position;

    public bool DoneCPC { get; set; }
  }
}
