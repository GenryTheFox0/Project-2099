// Xbox 360 STFS/SVOD content-container reader, independently ported to C#
// from the BSD-licensed Xenia xcontent container layout implementation.
// Copyright 2013 Ben Vanik and Xenia contributors.
// Copyright 2026 GenryTheFox contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EotInstaller {
  public sealed class SvodImage : IDisposable {
    enum Layout { EnhancedGdf, Xsf, SingleFile }
    const int BlockSize = 0x800;
    const int HashBlockSize = 0x1000;
    const int BlocksPerL0Hash = 0x198;
    const int HashesPerL1Hash = 0xA1C4;
    const int BlocksPerFile = 0x14388;
    const int MaxFileSize = 0xA290000;
    static readonly byte[] MediaMagic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");

    public sealed class Entry {
      public string Path;
      public uint Block;
      public long Length;
    }

    readonly string containerPath;
    readonly string[] dataPaths;
    readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    readonly Layout layout;
    readonly uint startDataBlock;
    readonly long baseOffset;
    bool disposed;

    struct Cursor {
      public string Prefix;
      public uint Block;
      public uint Ordinal;
      public Cursor(string prefix, uint block, uint ordinal) { Prefix = prefix; Block = block; Ordinal = ordinal; }
    }

    public static bool TryFindContainer(string selectedPath, out string container) {
      container = null;
      if (File.Exists(selectedPath) && IsContainerHeader(selectedPath)) { container = Path.GetFullPath(selectedPath); return true; }
      if (!Directory.Exists(selectedPath)) return false;
      foreach (string file in Directory.GetFiles(selectedPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
        if (Directory.Exists(file + ".data") && IsContainerHeader(file)) { container = Path.GetFullPath(file); return true; }
      }
      return false;
    }

    public SvodImage(string path) {
      containerPath = Path.GetFullPath(path);
      byte[] header = new byte[0x3AD];
      using (var stream = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read)) ReadExactly(stream, header, 0, header.Length);
      if (!IsPackageMagic(header)) throw new InvalidDataException("Not an Xbox 360 LIVE/PIRS/CON content container");
      uint volumeType = ReadBE32(header, 0x3A9);
      if (volumeType != 1) throw new InvalidDataException("The selected Xbox container is not an SVOD/GOD game");
      int descriptor = 0x379;
      if (header[descriptor] != 0x24) throw new InvalidDataException("Invalid SVOD descriptor");
      byte features = header[descriptor + 24];
      startDataBlock = ReadUInt24(header, descriptor + 28);
      string dataDirectory = containerPath + ".data";
      if (!Directory.Exists(dataDirectory)) throw new DirectoryNotFoundException("SVOD data directory is missing: " + dataDirectory);
      dataPaths = Directory.GetFiles(dataDirectory).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
      if (dataPaths.Length == 0) throw new InvalidDataException("The SVOD data directory is empty");
      if ((features & 0x40) != 0 && HasMagic(dataPaths[0], 0x2000)) {
        layout = Layout.EnhancedGdf; baseOffset = 0;
      } else if (HasMagic(dataPaths[0], 0x12000)) {
        if (!HasAscii(dataPaths[0], 0x2000, "XSF")) throw new InvalidDataException("Unknown SVOD XSF layout");
        layout = Layout.Xsf; baseOffset = 0x10000;
      } else if (HasMagic(dataPaths[0], 0xD000)) {
        layout = Layout.SingleFile; baseOffset = 0xB000;
      } else throw new InvalidDataException("SVOD filesystem magic was not found");
      long magicOffset = layout == Layout.EnhancedGdf ? 0x2000 : layout == Layout.Xsf ? 0x12000 : 0xD000;
      byte[] root = ReadPhysical(0, magicOffset + 0x14, 4);
      uint rootBlock = ReadLE32(root, 0);
      ParseDirectory(rootBlock);
      if (entries.Count == 0) throw new InvalidDataException("The SVOD filesystem contains no files");
    }

    public string Name { get { return Path.GetFileName(containerPath); } }
    public int Count { get { return entries.Count; } }
    public IEnumerable<Entry> Entries { get { return entries.Values; } }
    public bool Exists(string path) { return entries.ContainsKey(Normalize(path)); }
    public long GetLength(string path) { Entry entry; return entries.TryGetValue(Normalize(path), out entry) ? entry.Length : -1; }
    public Stream OpenRead(string path) {
      if (disposed) throw new ObjectDisposedException("SvodImage");
      Entry entry; if (!entries.TryGetValue(Normalize(path), out entry)) throw new FileNotFoundException("File not present in GOD container", path);
      return new SvodFileStream(dataPaths, layout, startDataBlock, baseOffset, entry.Block, entry.Length);
    }

    void ParseDirectory(uint rootBlock) {
      var pending = new Stack<Cursor>(); pending.Push(new Cursor("", rootBlock, 0));
      var visited = new HashSet<string>(StringComparer.Ordinal);
      while (pending.Count != 0) {
        Cursor cursor = pending.Pop();
        string visit = cursor.Block.ToString("X8") + ":" + cursor.Ordinal.ToString("X8");
        if (!visited.Add(visit)) continue;
        long ordinalOffset = (long)cursor.Ordinal * 4;
        long blockAdvance = ordinalOffset / BlockSize;
        int withinBlock = (int)(ordinalOffset % BlockSize);
        long physical; int fileIndex;
        MapBlock(layout, startDataBlock, baseOffset, checked(cursor.Block + (uint)blockAdvance), out physical, out fileIndex);
        byte[] header = ReadPhysical(fileIndex, physical + withinBlock, 14);
        ushort left = ReadLE16(header, 0), right = ReadLE16(header, 2);
        uint dataBlock = ReadLE32(header, 4), length = ReadLE32(header, 8);
        byte attributes = header[12], nameLength = header[13];
        if (nameLength == 0 || nameLength > 240) throw new InvalidDataException("Invalid SVOD directory entry name");
        byte[] nameBytes = ReadPhysical(fileIndex, physical + withinBlock + 14, nameLength);
        string name = Encoding.UTF8.GetString(nameBytes);
        if (name.IndexOfAny(new[] {'/', '\\', '\0'}) >= 0 || name == "." || name == "..")
          throw new InvalidDataException("Unsafe SVOD file name");
        if (left != 0) pending.Push(new Cursor(cursor.Prefix, cursor.Block, left));
        if (right != 0) pending.Push(new Cursor(cursor.Prefix, cursor.Block, right));
        string full = cursor.Prefix + name;
        if ((attributes & 0x10) != 0) {
          if (length > 0) pending.Push(new Cursor(full + "/", dataBlock, 0));
        } else {
          string normalized = Normalize(full);
          if (entries.ContainsKey(normalized)) throw new InvalidDataException("Duplicate SVOD path: " + full);
          entries.Add(normalized, new Entry { Path = full, Block = dataBlock, Length = length });
          if (entries.Count > 200000) throw new InvalidDataException("SVOD entry count is unreasonable");
        }
      }
    }

    byte[] ReadPhysical(int fileIndex, long offset, int count) {
      if (fileIndex < 0 || fileIndex >= dataPaths.Length) throw new InvalidDataException("SVOD file index is outside the data set");
      var info = new FileInfo(dataPaths[fileIndex]);
      if (offset < 0 || count < 0 || offset > info.Length || count > info.Length - offset)
        throw new InvalidDataException("SVOD directory entry is outside a data fragment");
      var result = new byte[count];
      using (var stream = new FileStream(dataPaths[fileIndex], FileMode.Open, FileAccess.Read, FileShare.Read)) {
        stream.Position = offset; ReadExactly(stream, result, 0, count);
      }
      return result;
    }

    static void MapBlock(Layout layout, uint startDataBlock, long baseOffset, uint block,
      out long outputOffset, out int outputFileIndex) {
      long trueBlock = (long)block - (long)startDataBlock * 2;
      if (layout == Layout.EnhancedGdf) trueBlock += 2;
      if (trueBlock < 0) throw new InvalidDataException("SVOD block precedes its data range");
      long fileBlock = trueBlock % BlocksPerFile;
      outputFileIndex = checked((int)(trueBlock / BlocksPerFile));
      long level0 = fileBlock / BlocksPerL0Hash + 1;
      long level1 = level0 / HashesPerL1Hash + 1;
      long offset = level0 * HashBlockSize + level1 * HashBlockSize;
      if (layout == Layout.SingleFile) offset += baseOffset;
      offset += fileBlock * BlockSize;
      if (offset >= MaxFileSize) { offset = offset % MaxFileSize + 0x2000; outputFileIndex++; }
      outputOffset = offset;
    }

    static bool IsContainerHeader(string path) {
      try { byte[] b = new byte[4]; using (var s = File.OpenRead(path)) { if (s.Read(b, 0, 4) != 4) return false; } return IsPackageMagic(b); }
      catch { return false; }
    }
    static bool IsPackageMagic(byte[] b) {
      return b.Length >= 4 && ((b[0] == 'L' && b[1] == 'I' && b[2] == 'V' && b[3] == 'E') ||
        (b[0] == 'P' && b[1] == 'I' && b[2] == 'R' && b[3] == 'S') ||
        (b[0] == 'C' && b[1] == 'O' && b[2] == 'N' && b[3] == ' '));
    }
    static bool HasMagic(string path, long offset) { return HasBytes(path, offset, MediaMagic); }
    static bool HasAscii(string path, long offset, string value) { return HasBytes(path, offset, Encoding.ASCII.GetBytes(value)); }
    static bool HasBytes(string path, long offset, byte[] expected) {
      try { byte[] actual = new byte[expected.Length]; using (var s = File.OpenRead(path)) { if (offset + expected.Length > s.Length) return false; s.Position = offset; ReadExactly(s, actual, 0, actual.Length); }
        for (int i = 0; i < expected.Length; i++) if (actual[i] != expected[i]) return false; return true; } catch { return false; }
    }
    static string Normalize(string path) { return InstallerCore.NormalizeRelative(path); }
    static ushort ReadLE16(byte[] b, int o) { return (ushort)(b[o] | b[o + 1] << 8); }
    static uint ReadLE32(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24); }
    static uint ReadBE32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
    static uint ReadUInt24(byte[] b, int o) { return (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16); }
    static void ReadExactly(Stream stream, byte[] buffer, int offset, int count) { while (count > 0) { int read = stream.Read(buffer, offset, count); if (read <= 0) throw new EndOfStreamException(); offset += read; count -= read; } }
    public void Dispose() { disposed = true; }

    sealed class SvodFileStream : Stream {
      readonly string[] paths; readonly Layout layout; readonly uint startDataBlock; readonly long baseOffset;
      readonly uint firstBlock; readonly long length; long position; FileStream current; int currentIndex = -1;
      public SvodFileStream(string[] paths, Layout layout, uint startDataBlock, long baseOffset, uint firstBlock, long length) {
        this.paths = paths; this.layout = layout; this.startDataBlock = startDataBlock; this.baseOffset = baseOffset;
        this.firstBlock = firstBlock; this.length = length;
      }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return true; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { return length; } }
      public override long Position { get { return position; } set { Seek(value, SeekOrigin.Begin); } }
      public override int Read(byte[] buffer, int offset, int count) {
        if (position >= length) return 0;
        count = (int)Math.Min(count, length - position); int total = 0;
        while (count > 0) {
          uint logicalBlock = checked(firstBlock + (uint)(position / BlockSize));
          int inBlock = (int)(position % BlockSize); long physical; int fileIndex;
          MapBlock(layout, startDataBlock, baseOffset, logicalBlock, out physical, out fileIndex);
          if (fileIndex < 0 || fileIndex >= paths.Length) throw new EndOfStreamException("SVOD fragment is missing");
          if (currentIndex != fileIndex) { if (current != null) current.Dispose(); current = new FileStream(paths[fileIndex], FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan); currentIndex = fileIndex; }
          long trueBlock = (long)logicalBlock - (long)startDataBlock * 2 + (layout == Layout.EnhancedGdf ? 2 : 0);
          long fileBlock = trueBlock % BlocksPerFile;
          long blocksUntilHash = BlocksPerL0Hash - fileBlock % BlocksPerL0Hash;
          long blocksUntilFragment = BlocksPerFile - fileBlock;
          long contiguous = Math.Min(blocksUntilHash, blocksUntilFragment) * BlockSize - inBlock;
          int take = (int)Math.Min(count, Math.Min(contiguous, current.Length - (physical + inBlock)));
          if (take <= 0) throw new EndOfStreamException("SVOD contiguous range is invalid");
          if (physical + inBlock + take > current.Length) throw new EndOfStreamException("SVOD block is truncated");
          long wanted = physical + inBlock; if (current.Position != wanted) current.Position = wanted;
          int read = current.Read(buffer, offset, take); if (read <= 0) throw new EndOfStreamException();
          position += read; offset += read; count -= read; total += read;
        }
        return total;
      }
      public override long Seek(long offset, SeekOrigin origin) { long next = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? position + offset : length + offset; if (next < 0 || next > length) throw new IOException("Seek outside SVOD file"); position = next; return position; }
      protected override void Dispose(bool disposing) { if (disposing && current != null) current.Dispose(); base.Dispose(disposing); }
      public override void Flush() { }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
  }
}
