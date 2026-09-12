using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EotInstaller {
  static class EotpPatch {
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("EOTP1");

    public static void Apply(string source, string patch, string target) {
      using (var input = new FileStream(patch, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
      using (var reader = new BinaryReader(input, Encoding.UTF8)) {
        byte[] magic = reader.ReadBytes(Magic.Length);
        if (magic.Length != Magic.Length || !Equal(magic, Magic)) throw new InvalidDataException("Invalid EOTP patch magic");
        byte mode = reader.ReadByte();
        int blockSize = reader.ReadInt32();
        long sourceSize = reader.ReadInt64();
        long targetSize = reader.ReadInt64();
        byte[] sourceHash = reader.ReadBytes(32);
        byte[] targetHash = reader.ReadBytes(32);
        int records = reader.ReadInt32();
        if ((mode != 0 && mode != 1) || blockSize < 4096 || blockSize > 1024 * 1024 ||
            sourceSize < 0 || targetSize < 0 || sourceHash.Length != 32 || targetHash.Length != 32 ||
            records < 0 || records > 2000000) throw new InvalidDataException("Invalid EOTP header");
        if (!File.Exists(source) || new FileInfo(source).Length != sourceSize) throw new InvalidDataException("EOTP source size mismatch");
        if (mode == 0) File.Copy(source, target, false);
        else using (File.Create(target)) { }
        using (var output = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
          1024 * 1024, FileOptions.RandomAccess)) {
          output.SetLength(targetSize);
          long lastEnd = 0;
          for (int index = 0; index < records; index++) {
            long offset = reader.ReadInt64();
            int rawLength = reader.ReadInt32();
            int storedLength = reader.ReadInt32();
            byte codec = reader.ReadByte();
            if (offset < 0 || rawLength <= 0 || rawLength > blockSize || storedLength <= 0 ||
                storedLength > blockSize + 65536 || offset < lastEnd || offset + rawLength > targetSize || codec > 1)
              throw new InvalidDataException("Invalid EOTP record");
            byte[] stored = reader.ReadBytes(storedLength);
            if (stored.Length != storedLength) throw new EndOfStreamException("Truncated EOTP record");
            byte[] data;
            if (codec == 0) {
              if (storedLength != rawLength) throw new InvalidDataException("Invalid raw EOTP record");
              data = stored;
            } else {
              data = new byte[rawLength];
              using (var packed = new MemoryStream(stored, false))
              using (var deflate = new DeflateStream(packed, CompressionMode.Decompress)) {
                int done = 0; while (done < data.Length) { int read = deflate.Read(data, done, data.Length - done); if (read <= 0) throw new EndOfStreamException("Truncated deflate record"); done += read; }
                if (deflate.ReadByte() != -1) throw new InvalidDataException("Oversized deflate record");
              }
            }
            output.Position = offset; output.Write(data, 0, data.Length); lastEnd = offset + rawLength;
          }
          if (input.Position != input.Length) throw new InvalidDataException("Trailing data in EOTP patch");
          output.Flush(true);
        }
      }
    }

    static bool Equal(byte[] a, byte[] b) {
      if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true;
    }
  }
}
