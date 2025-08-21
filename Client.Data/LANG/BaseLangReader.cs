using ICSharpCode.SharpZipLib.Zip;
using Org.BouncyCastle.Tls.Crypto.Impl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Data.LANG
{
    public abstract class BaseLangReader<T>
    {

        public async Task<T> Load(ZipFile zFile, string path)
        {
            var zEntry = zFile.GetEntry(path);

            if (zEntry == null)
            {
                var normalizedPath = string.Join('\\', path.Split('/'));
                zEntry = zFile.GetEntry(normalizedPath);
            }

            if (zEntry == null)
            {
                throw new Exception($"Entry {path} not found");
            }

            using var ms = new MemoryStream();
            await zFile.GetInputStream(zEntry).CopyToAsync(ms);
            return Read(ms.ToArray());
        }

        public async Task<T> Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}", path);

            var buffer = await File.ReadAllBytesAsync(path);

            return Read(buffer);
        }

        protected abstract T Read(byte[] buffer);
    }
}
