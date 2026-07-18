using System;
using System.IO;

namespace 六合分析软件
{
    public static class TestV67
    {
        public static void Run()
        {
            Console.WriteLine("════════════════════════════════════════════════════════");
            Console.WriteLine("           v6.7 模型贡献分析 - 测试运行");
            Console.WriteLine("════════════════════════════════════════════════════════");
            Console.WriteLine();

            try
            {
                Console.WriteLine("正在加载历史数据...");
                var report = ModelContributionAnalysis.Run(trainPeriods: 300, testPeriods: 200, segmentSize: 50);
                Console.WriteLine("分析完成！正在生成报告...");
                Console.WriteLine();

                string reportText = ModelContributionAnalysis.GenerateReport(report);
                Console.WriteLine(reportText);

                string outputPath = Path.Combine(Environment.CurrentDirectory, "v67-report.txt");
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
