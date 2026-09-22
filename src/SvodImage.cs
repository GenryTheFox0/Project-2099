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
    // Informational only: which tool built the container, not whether we can read it.
    public bool HasXsfMarker { get; private set; }
    readonly uint startDataBlock;
    readonly long baseOffset;
    bool disposed;

    struct Cursor {
      public string Prefix;
      public uint Block;
      public uint Ordinal;
      public Cursor(string prefix, uint block, uint ordinal) { Prefix = prefix; Block = block; Ordinal = ordinal; }
    }

    // A GOD rip travels as <anything>/<TitleId>/00007000/<container>, and players pick
    // whichever folder they see: the outer one, the title id, or the .data directory.
    // Only the middle one used to be accepted, so a perfectly readable image was turned
    // away with "choose an ISO, ZIP or GOD/00007000". Find the container instead.
    // A data fragment holds BlocksPerFile blocks plus its hash tables: one L0
    // table per BlocksPerL0Hash blocks and one L1 table. Every fragment but the
    // last is exactly that long, so a shorter one is an image that was cut off --
    // an extraction that ran out of disk, a download that never finished. Naming
    // it here beats failing ten megabytes into some level package later.
    public static readonly long FragmentBytes =
      (long)BlocksPerFile * BlockSize + ((long)BlocksPerFile / BlocksPerL0Hash) * HashBlockSize + HashBlockSize;

    static void RequireWholeFragments(string[] fragments) {
      for (int i = 0; i < fragments.Length; i++) {
        long actual = new FileInfo(fragments[i]).Length;
        bool last = i == fragments.Length - 1;
        if ((!last && actual != FragmentBytes) || (last && actual > FragmentBytes) || actual == 0)
          throw new InvalidDataException("Образ обрезан: фрагмент " + Path.GetFileName(fragments[i]) + " весит " + actual +
            " байт вместо " + FragmentBytes + ". Распакуйте архив заново на диск, где есть место, или докачайте образ.");
      }
    }

    /// <summary>A ZIP that carries a GOD image rather than loose game files. The
    /// container and its fragments are unpacked once onto a drive with room,
    /// each fragment checked against the length the archive declares, and the
    /// result is opened like any other GOD. Returns false when the ZIP holds
    /// something else, so the loose-file reader can have its turn.</summary>
    public static bool TryExtractFromZip(string zipPath, Action<InstallProgress> progress, out string container, out string scratch) {
      container = null; scratch = null;
      using (var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
      using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, false, Encoding.UTF8)) {
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        string containerName = null;
        foreach (var entry in archive.Entries) {
          string name = entry.FullName.Replace('\\', '/');
          if (name.EndsWith("/") || entry.Length < 0x3AD) continue;
          string leaf = name.Substring(name.LastIndexOf('/') + 1);
          if (leaf.IndexOf('.') >= 0) continue;
          if (!names.Any(n => n.StartsWith(name + ".data/", StringComparison.OrdinalIgnoreCase))) continue;
          byte[] head = new byte[4];
          using (Stream probe = entry.Open()) { if (probe.Read(head, 0, 4) != 4) continue; }
          if (!IsPackageMagic(head)) continue;
          containerName = name; break;
        }
        if (containerName == null) return false;

        var wanted = archive.Entries.Where(e => {
          string n = e.FullName.Replace('\\', '/');
          return !n.EndsWith("/") && (String.Equals(n, containerName, StringComparison.OrdinalIgnoreCase) ||
            n.StartsWith(containerName + ".data/", StringComparison.OrdinalIgnoreCase));
        }).OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        long total = wanted.Sum(e => e.Length);
        string key = SafeKey(Path.GetFileNameWithoutExtension(zipPath)) + "-" + new FileInfo(zipPath).Length.ToString();
        scratch = Path.Combine(PayloadProvider.ScratchRoot(total + total / 20, "распаковка образа"), "svod", key);
        string leafName = containerName.Substring(containerName.LastIndexOf('/') + 1);
        container = Path.Combine(scratch, leafName);
        string ready = Path.Combine(scratch, ".ready");

        bool reusable = File.Exists(ready);
        if (reusable) {
          foreach (var entry in wanted) {
            string target = Path.Combine(scratch, Relative(entry.FullName, containerName));
            if (!File.Exists(target) || new FileInfo(target).Length != entry.Length) { reusable = false; break; }
          }
        }
        if (reusable) return true;

        if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        Directory.CreateDirectory(scratch);
        long done = 0;
        var buffer = new byte[1 << 20];
        foreach (var entry in wanted) {
          string target = Path.Combine(scratch, Relative(entry.FullName, containerName));
          Directory.CreateDirectory(Path.GetDirectoryName(target));
          long written = 0;
          using (Stream input = entry.Open())
          using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan)) {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
              output.Write(buffer, 0, read); written += read; done += read;
              if (progress != null) progress(new InstallProgress { Phase = "Распаковка образа из архива",
                CurrentFile = Path.GetFileName(target), CompletedBytes = done, TotalBytes = total });
            }
          }
          if (written != entry.Length)
            throw new InvalidDataException("Архив повреждён: " + entry.FullName + " распаковался в " + written +
              " байт вместо " + entry.Length + ". Скачайте архив заново.");
        }
        File.WriteAllText(ready, new FileInfo(zipPath).Length.ToString(), Encoding.ASCII);
        return true;
      }
    }

    static string Relative(string fullName, string containerName) {
      string name = fullName.Replace('\\', '/');
      string parent = containerName.Substring(0, containerName.LastIndexOf('/') + 1);
      string relative = name.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ? name.Substring(parent.Length) : name;
      return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    static string SafeKey(string value) {
      var keep = new StringBuilder();
      foreach (char c in value) keep.Append(Char.IsLetterOrDigit(c) ? c : '_');
      string key = keep.ToString().Trim('_');
      return key.Length == 0 ? "image" : (key.Length > 48 ? key.Substring(0, 48) : key);
    }

    public static bool TryFindContainer(string selectedPath, out string container) {
      container = null;
      if (File.Exists(selectedPath)) {
        if (IsContainerHeader(selectedPath)) { container = Path.GetFullPath(selectedPath); return true; }
        return TryContainerOfDataDirectory(Path.GetDirectoryName(Path.GetFullPath(selectedPath)), out container);
      }
      if (!Directory.Exists(selectedPath)) return false;
      string selected = Path.GetFullPath(selectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      if (TryContainerOfDataDirectory(selected, out container)) return true;
      return TryFindContainerIn(selected, 0, new int[] { 512 }, out container);
    }

    // The selection is the data directory of a container sitting next to it.
    static bool TryContainerOfDataDirectory(string directory, out string container) {
      container = null;
      if (directory == null) return false;
      string trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      if (!trimmed.EndsWith(".data", StringComparison.OrdinalIgnoreCase)) return false;
      string candidate = trimmed.Substring(0, trimmed.Length - ".data".Length);
      if (!File.Exists(candidate) || !IsContainerHeader(candidate)) return false;
      container = Path.GetFullPath(candidate);
      return true;
    }

    // Bounded walk, so pointing the installer at a whole drive costs a few hundred
    // directory listings rather than a full scan.
    static bool TryFindContainerIn(string directory, int depth, int[] budget, out string container) {
      container = null;
      if (budget[0]-- <= 0) return false;
      string[] files;
      try { files = Directory.GetFiles(directory); }
      catch (UnauthorizedAccessException) { return false; }
      catch (IOException) { return false; }
      foreach (string file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
        if (Directory.Exists(file + ".data") && IsContainerHeader(file)) { container = Path.GetFullPath(file); return true; }
      }
      if (depth >= 3) return false;
      string[] children;
      try { children = Directory.GetDirectories(directory); }
      catch (UnauthorizedAccessException) { return false; }
      catch (IOException) { return false; }
      foreach (string child in children.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Take(64)) {
        if (child.EndsWith(".data", StringComparison.OrdinalIgnoreCase)) continue;
        if (TryFindContainerIn(child, depth + 1, budget, out container)) return true;
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
      RequireWholeFragments(dataPaths);
      if ((features & 0x40) != 0 && HasMagic(dataPaths[0], 0x2000)) {
        layout = Layout.EnhancedGdf; baseOffset = 0;
      } else if (HasMagic(dataPaths[0], 0x12000)) {
        // The filesystem magic at 0x12000 is what identifies this layout; the
        // "XSF" string at 0x2000 only says which tool built the container and
        // plenty of perfectly readable GOD rips do not carry it. Rejecting on a
        // missing marker turned working images away with "Unknown SVOD XSF
        // layout", so it is recorded and ignored.
        HasXsfMarker = HasAscii(dataPaths[0], 0x2000, "XSF");
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
