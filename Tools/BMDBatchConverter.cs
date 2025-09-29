using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace Tools
{
    public class BMDBatchConverter:IDisposable
    {
        private readonly BMDToFBXConverter _converter;

        public BMDBatchConverter()
        {
            _converter = new BMDToFBXConverter();
        }

        public async Task<ConversionResult> ConvertDirectoryAsync(string inputDir, string outputDir, bool recursive = true)
        {
            var result = new ConversionResult();
            
            if (!Directory.Exists(inputDir))
            {
                throw new DirectoryNotFoundException($"输入目录不存在: {inputDir}");
            }

            Directory.CreateDirectory(outputDir);

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var bmdFiles = Directory.GetFiles(inputDir, "*.bmd", searchOption);

            Console.WriteLine($"找到 {bmdFiles.Length} 个BMD文件");

            var tasks = new List<Task>();

            foreach (var bmdFile in bmdFiles)
            {
                tasks.Add(ConvertFileAsync(bmdFile, inputDir, outputDir, result));
            }

            await Task.WhenAll(tasks);

            Console.WriteLine($"转换完成: 成功 {result.SuccessCount}, 失败 {result.FailureCount}");
            return result;
        }

        private async Task ConvertFileAsync(string bmdFile, string inputDir, string outputDir, ConversionResult result ) {
            var relativePath = Path.GetRelativePath(inputDir, bmdFile);
            var outputFile = Path.Combine(
                outputDir,
                Path.ChangeExtension(relativePath, ".fbx"));

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

            Console.WriteLine($"转换中: {relativePath}");

            if (await _converter.ConvertBMDToFBX(bmdFile, outputFile)) {
                result.IncrementSuccess();
                Console.WriteLine($"✓ 成功: {relativePath}");
            } else {
                result.IncrementFailure();
                Console.WriteLine($"✗ 失败: {relativePath}");
            }
        }

        public void Dispose()
        {
            _converter?.Dispose();
        }
    }

    public class ConversionResult
    {
        private int _successCount = 0;
        private int _failureCount = 0;
        private readonly object _lock = new object();

        public int SuccessCount => _successCount;
        public int FailureCount => _failureCount;

        public void IncrementSuccess()
        {
            lock (_lock) { _successCount++; }
        }

        public void IncrementFailure()
        {
            lock (_lock) { _failureCount++; }
        }
    }
}