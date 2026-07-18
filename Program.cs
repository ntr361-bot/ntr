using System;
using System.Windows.Forms;

namespace 六合分析软件
{
    internal static class Program
    {

        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => AppLogger.Error("界面线程未处理异常", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    AppLogger.Error("后台线程未处理异常", ex);
            };

            try
            {
                DatabaseHelper.InitializeDatabase();

                // 报告模式不执行每日备份和全量旧数据修复，避免只读诊断带来额外副作用。
                if (TryRunReportMode(args))
                    return;

                AppLogger.Info("程序启动", $"数据目录：{AppPaths.DataDirectory}");
                AppLogger.Info("数据库备份", DatabaseBackupService.Backup());

                int migrated = DatabaseHelper.MigrateOldData();
                if (migrated > 0)
                    AppLogger.Info("旧数据迁移", $"已补全 {migrated} 条记录");

                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                AppLogger.Error("程序启动失败", ex);
                MessageBox.Show($"程序启动失败：{ex.Message}\r\n\r\n日志目录：{AppPaths.LogDirectory}",
                    "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool TryRunReportMode(string[] args)
        {
            if (Array.Exists(args, a => a == "--v6-report"))
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.WriteLine(V66UpgradeReportService.GenerateReport(500));
                return true;
            }

            return false;
        }

    }
}
