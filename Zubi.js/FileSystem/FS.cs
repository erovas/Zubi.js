using System.Runtime.CompilerServices;
using System.Text;
using Zubi.js;

namespace FileSystem
{
    public class FS : Plugin
    {
        public Task<string> ReadFileAsync(string path, string? encoding, CancellationToken ct)
        => File.ReadAllTextAsync(path, ParseEncoding(encoding), ct);

        public Task<byte[]> ReadFileBytesAsync(string path, CancellationToken ct)
            => File.ReadAllBytesAsync(path, ct);

        public Task WriteFileAsync(string path, string content, string? encoding, CancellationToken ct)
            => File.WriteAllTextAsync(path, content, ParseEncoding(encoding), ct);

        public Task WriteFileBytesAsync(string path, byte[] data, CancellationToken ct)
            => File.WriteAllBytesAsync(path, data, ct);

        public string[] ReadDir(string path) => Directory.GetFileSystemEntries(path);
        public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDir(string path, bool recursive) => Directory.Delete(path, recursive);
        public void CreateDir(string path) => Directory.CreateDirectory(path);
        public void Move(string from, string to) => File.Move(from, to, overwrite: true);
        public void Copy(string from, string to) => File.Copy(from, to, overwrite: true);

        public FileStat Stat(string path)
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return new FileStat(true, false, 0, di.LastWriteTimeUtc, di.Attributes.ToString());
            }
            var fi = new FileInfo(path);
            return new FileStat(fi.Exists, true, fi.Exists ? fi.Length : 0,
                fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue,
                fi.Exists ? fi.Attributes.ToString() : "");
        }

        public async IAsyncEnumerable<byte[]> ReadStreamAsync(string path, int chunkSize, [EnumeratorCancellation] CancellationToken ct)
        {
            if (chunkSize <= 0) 
                chunkSize = 64 * 1024;

            await using var fs = File.OpenRead(path);
            var buf = new byte[chunkSize];
            int n;
            while ((n = await fs.ReadAsync(buf.AsMemory(0, chunkSize), ct)) > 0)
                yield return buf[..n];
        }

        private static Encoding ParseEncoding(string? enc) => enc switch
        {
            null or "utf8" or "utf-8" => Encoding.UTF8,
            "ascii" => Encoding.ASCII,
            "latin1" or "iso-8859-1" => Encoding.Latin1,
            "utf16" or "utf-16" => Encoding.Unicode,
            _ => throw new Exception($"encoding no soportado: {enc}")
        };


        protected override void Dispose(bool disposing)
        {
            // Do nothing
        }
    }
}

public record FileStat(bool Exists, bool IsFile, long Size, DateTime LastWriteUtc, string Attributes);