// Independent C# implementation of the Xbox/Xbox 360 XDVDFS directory reader.
// Layout research referenced from the BSD-licensed Xenia project.
//
// Copyright 2013 Ben Vanik and Xenia contributors.
// Copyright 2026 GenryTheFox contributors.
// Preserve the BSD notices in ../licenses/Xenia-BSD-3-Clause.txt and
// ../licenses/Genry-Installer-Legacy-BSD-3-Clause.txt. Project-owned changes
// are offered under GPL-3.0; see ../LICENSE and ../THIRD_PARTY_LICENSES.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EotInstaller {
  public sealed class XdvdfsImage : IDisposable {
    const int SectorSize = 2048;
    static readonly long[] PossibleOffsets = {
      0x00000000L, 0x0000FB20L, 0x00010600L, 0x00020600L,
      0x02070000L, 0x02080000L, 0x0FD80000L, 0x0FD90000L,
      0x182F0000L, 0x18300000L
    };
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");

    public sealed class Entry {
      public string Path;
      public long Offset;
      public long Length;
    }

    readonly string imagePath;
    readonly long imageLength;
    readonly Dictionary<string, Entry> entries =
      new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    long gameOffset;
    bool disposed;

    public XdvdfsImage(string path) {
      imagePath = System.IO.Path.GetFullPath(path);
      var info = new FileInfo(imagePath);
      if (!info.Exists) throw new FileNotFoundException("ISO not found", imagePath);
      imageLength = info.Length;
      using (var stream = OpenImage()) {
        gameOffset = FindGameOffset(stream);
        long volume = checked(gameOffset + 32L * SectorSize);
        uint rootSector = ReadUInt32(stream, volume + 20);
        uint rootSize = ReadUInt32(stream, volume + 24);
        if (rootSize < 13 || rootSize > 32U * 1024U * 1024U)
          throw new InvalidDataException("Invalid XDVDFS root directory size");
        long rootOffset = checked(gameOffset + (long)rootSector * SectorSize);
        EnsureRange(rootOffset, rootSize);
        var visitedDirectories = new HashSet<long>();
        ParseDirectory(stream, "", rootOffset, rootSize, visitedDirectories, 0);
      }
      if (entries.Count == 0) throw new InvalidDataException("The ISO contains no XDVDFS files");
    }

    public string Name { get { return System.IO.Path.GetFileName(imagePath); } }
    public int Count { get { return entries.Count; } }
    public IEnumerable<Entry> Entries { get { return entries.Values; } }

    public bool Exists(string path) { return entries.ContainsKey(Normalize(path)); }

    public long GetLength(string path) {
      Entry entry;
      if (!entries.TryGetValue(Normalize(path), out entry)) return -1;
      return entry.Length;
    }

    public Stream OpenRead(string path) {
      if (disposed) throw new ObjectDisposedException("XdvdfsImage");
      Entry entry;
      if (!entries.TryGetValue(Normalize(path), out entry))
        throw new FileNotFoundException("File not present in ISO", path);
      return new SliceStream(imagePath, entry.Offset, entry.Length);
    }

    FileStream OpenImage() {
      return new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
        1024 * 1024, FileOptions.RandomAccess);
    }

    long FindGameOffset(FileStream stream) {
      foreach (long candidate in PossibleOffsets) {
        if (HasValidVolumeDescriptor(stream, candidate)) return candidate;
      }
      long scanned = ScanForGameOffset(stream);
      if (scanned >= 0) return scanned;
      throw new InvalidDataException(
        "MICROSOFT*XBOX*MEDIA was not found in the Xbox 360 disc area. Select the original USA/Europe XDVDFS ISO, not a PS3 ISO, archive or shortcut.");
    }

    bool HasValidVolumeDescriptor(FileStream stream, long candidate) {
      long descriptor = candidate + 32L * SectorSize;
      if (candidate < 0 || descriptor < 0 || descriptor + 28 > imageLength) return false;
      var buffer = new byte[Magic.Length];
      stream.Position = descriptor;
      ReadExactly(stream, buffer, 0, buffer.Length);
      for (int i = 0; i < Magic.Length; i++) if (buffer[i] != Magic[i]) return false;
      uint rootSector = ReadUInt32(stream, descriptor + 20);
      uint rootSize = ReadUInt32(stream, descriptor + 24);
      if (rootSize < 13 || rootSize > 32U * 1024U * 1024U) return false;
      long rootOffset;
      try { rootOffset = checked(candidate + (long)rootSector * SectorSize); }
      catch (OverflowException) { return false; }
      return rootOffset >= 0 && rootOffset <= imageLength && rootSize <= imageLength - rootOffset;
    }

    long ScanForGameOffset(FileStream stream) {
      const int ChunkSize = 1024 * 1024;
      long scanLength = Math.Min(imageLength, 512L * 1024L * 1024L);
      var buffer = new byte[ChunkSize + Magic.Length - 1];
      int carry = 0;
      long position = 0;
      while (position < scanLength) {
        stream.Position = position;
        int wanted = (int)Math.Min(ChunkSize, scanLength - position);
        int read = stream.Read(buffer, carry, wanted);
        if (read <= 0) break;
        int total = carry + read;
        for (int i = 0; i <= total - Magic.Length; i++) {
          bool same = true;
          for (int j = 0; j < Magic.Length; j++) if (buffer[i + j] != Magic[j]) { same = false; break; }
          if (!same) continue;
          long magicOffset = position - carry + i;
          long candidate = magicOffset - 32L * SectorSize;
          if (HasValidVolumeDescriptor(stream, candidate)) return candidate;
        }
        carry = Math.Min(Magic.Length - 1, total);
        Buffer.BlockCopy(buffer, total - carry, buffer, 0, carry);
        position += read;
      }
      return -1;
    }

    void ParseDirectory(FileStream stream, string prefix, long tableOffset, long tableSize,
      HashSet<long> visitedDirectories, int depth) {
      if (depth > 64) throw new InvalidDataException("XDVDFS directory depth is invalid");
      if (!visitedDirectories.Add(tableOffset)) throw new InvalidDataException("XDVDFS directory cycle detected");
      EnsureRange(tableOffset, tableSize);
      var pending = new Stack<uint>();
      var visitedNodes = new HashSet<uint>();
      pending.Push(0);
      while (pending.Count != 0) {
        uint relative = pending.Pop();
        if (!visitedNodes.Add(relative)) continue;
        long node = checked(tableOffset + relative);
        if (relative >= tableSize || node + 14 > tableOffset + tableSize)
          throw new InvalidDataException("XDVDFS directory node is outside its table");
        ushort left = ReadUInt16(stream, node);
        ushort right = ReadUInt16(stream, node + 2);
        uint sector = ReadUInt32(stream, node + 4);
        uint length = ReadUInt32(stream, node + 8);
        byte attributes = ReadByte(stream, node + 12);
        byte nameLength = ReadByte(stream, node + 13);
        if (nameLength == 0 || node + 14 + nameLength > tableOffset + tableSize)
          throw new InvalidDataException("XDVDFS file name is invalid");
        var nameBytes = new byte[nameLength];
        stream.Position = node + 14;
        ReadExactly(stream, nameBytes, 0, nameBytes.Length);
        string name = Encoding.UTF8.GetString(nameBytes);
        if (name.IndexOfAny(new[] {'/', '\\', '\0'}) >= 0 || name == "." || name == "..")
          throw new InvalidDataException("Unsafe XDVDFS file name");
        string fullPath = prefix.Length == 0 ? name : prefix + "/" + name;
        if (left != 0) pending.Push(checked((uint)left * 4U));
        if (right != 0) pending.Push(checked((uint)right * 4U));
        long contentOffset = checked(gameOffset + (long)sector * SectorSize);
        EnsureRange(contentOffset, length);
        if ((attributes & 0x10) != 0) {
          if (length != 0) ParseDirectory(stream, fullPath, contentOffset, length,
            visitedDirectories, depth + 1);
        } else {
          string normalized = Normalize(fullPath);
          if (entries.ContainsKey(normalized)) throw new InvalidDataException("Duplicate XDVDFS path: " + fullPath);
          entries.Add(normalized, new Entry { Path = fullPath, Offset = contentOffset, Length = length });
          if (entries.Count > 200000) throw new InvalidDataException("XDVDFS entry count is unreasonable");
        }
      }
    }

    static string Normalize(string path) {
      if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Empty source path");
      string value = path.Replace('\\', '/').TrimStart('/');
      if (value.Split('/').AnyPartUnsafe()) throw new InvalidDataException("Unsafe source path: " + path);
      return value;
    }

    void EnsureRange(long offset, long length) {
      if (offset < 0 || length < 0 || offset > imageLength || length > imageLength - offset)
        throw new InvalidDataException("XDVDFS entry points outside the ISO");
    }

    static byte ReadByte(FileStream stream, long offset) {
      stream.Position = offset;
      int value = stream.ReadByte();
      if (value < 0) throw new EndOfStreamException();
      return (byte)value;
    }

    static ushort ReadUInt16(FileStream stream, long offset) {
      var b = new byte[2]; stream.Position = offset; ReadExactly(stream, b, 0, 2);
      return (ushort)(b[0] | b[1] << 8);
    }

    static uint ReadUInt32(FileStream stream, long offset) {
      var b = new byte[4]; stream.Position = offset; ReadExactly(stream, b, 0, 4);
      return (uint)(b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24);
    }

    static void ReadExactly(Stream stream, byte[] buffer, int offset, int count) {
      while (count > 0) {
        int read = stream.Read(buffer, offset, count);
        if (read <= 0) throw new EndOfStreamException();
        offset += read; count -= read;
      }
    }

    public void Dispose() { disposed = true; }

    sealed class SliceStream : Stream {
      readonly FileStream stream;
      readonly long start;
      readonly long length;
      long position;
      public SliceStream(string path, long offset, long length) {
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
          1024 * 1024, FileOptions.SequentialScan);
        start = offset; this.length = length; position = 0; stream.Position = start;
      }
      public override bool CanRead { get { return true; } }
      public override bool CanSeek { get { return true; } }
      public override bool CanWrite { get { return false; } }
      public override long Length { get { return length; } }
      public override long Position { get { return position; } set { Seek(value, SeekOrigin.Begin); } }
      public override int Read(byte[] buffer, int offset, int count) {
        if (position >= length) return 0;
        count = (int)Math.Min(count, length - position);
        stream.Position = start + position;
        int read = stream.Read(buffer, offset, count); position += read; return read;
      }
      public override long Seek(long offset, SeekOrigin origin) {
        long next = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? position + offset : length + offset;
        if (next < 0 || next > length) throw new IOException("Seek outside ISO file slice");
        position = next; return position;
      }
      protected override void Dispose(bool disposing) { if (disposing) stream.Dispose(); base.Dispose(disposing); }
      public override void Flush() { }
      public override void SetLength(long value) { throw new NotSupportedException(); }
      public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
  }

  static class XdvdfsPathExtensions {
    public static bool AnyPartUnsafe(this string[] parts) {
      foreach (string part in parts) if (part.Length == 0 || part == "." || part == "..") return true;
      return false;
    }
  }
}
