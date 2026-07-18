using System;
using System.IO;

namespace 六合分析软件
{
    internal static class AppPaths
    {
        public static string DataDirectory { get; } = CreateDirectory(
            Environment.GetEnvironmentVariable("LIUHE_DATA_DIR") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "六合分析软件"));

        public static string BackupDirectory { get; } = CreateDirectory(Path.Combine(DataDirectory, "Backup"));
        public static string LogDirectory { get; } = CreateDirectory(Path.Combine(DataDirectory, "Logs"));
        public static string CacheDirectory { get; } = CreateDirectory(Path.Combine(DataDirectory, "Cache"));

        private static string CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
