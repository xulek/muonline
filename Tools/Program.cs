using System;
using System.IO;
using System.Threading.Tasks;

namespace Tools
{
    class Program
    {
        public static void  Main(string[] args) {
            Test(args);
        }
        static async Task Test(string[] args)
        {
            Console.WriteLine("BMD到FBX批量转换工具");
            Console.WriteLine("====================");

            
            if (args.Length < 2)
            {
                Console.WriteLine("用法: BMDConverter.exe <输入目录> <输出目录> [recursive]");
                Console.WriteLine("示例: BMDConverter.exe \"C:\\MU\\Data\" \"C:\\Output\" true");
                args = new []{"D:\\App\\MU_Red_1_20_61_Full\\Data","D:\\App\\MU_Red_1_20_61_Full\\bdm2fbx"};
            }

            string inputDir = args[0];
            string outputDir =args[1];
            bool recursive = args.Length > 2 && bool.Parse(args[2]);

            try
            {
                using var converter = new BMDBatchConverter();
                var result = await converter.ConvertDirectoryAsync(inputDir, outputDir, recursive);
                
                Console.WriteLine($"\n转换统计:");
                Console.WriteLine($"成功: {result.SuccessCount}");
                Console.WriteLine($"失败: {result.FailureCount}");
                Console.WriteLine($"总计: {result.SuccessCount + result.FailureCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }
}