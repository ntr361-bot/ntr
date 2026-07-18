using System;
using System.IO;

namespace 六合分析软件
{
    public static class TestV68
    {
        public static void Run()
        {
            Console.WriteLine("════════════════════════════════════════════════════════");
            Console.WriteLine("         v6.8 权重敏感性分析 - 测试运行");
            Console.WriteLine("════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("对5个模型权重分别进行 ±10%, ±20%, ±30% 扰动测试...");
            Console.WriteLine();

            try
            {
                var report = WeightSensitivityAnalysis.Run(trainPeriods: 300, testPeriods: 200);
                Console.WriteLine("分析完成！正在生成报告...\n");

                string reportText = WeightSensitivityAnalysis.GenerateReport(report);
                Console.WriteLine(reportText);

                string outputPath = Path.Combine(Environment.CurrentDirectory, "v68-report.txt");
                File.WriteAllText(outputPath, reportText, System.Text.Encoding.UTF8);
                Console.WriteLine();
                Console.WriteLine($"报告已保存至: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
