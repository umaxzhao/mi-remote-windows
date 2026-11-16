using System;
using System.IO;
using System.Text;

namespace MiRemote.Core
{
    /// <summary>轻量日志：stdout + 文件 + 事件（UI 订阅）。</summary>
    public sealed class Logger
    {
        public event Action<string>? Line;
        private readonly string? _file;

        public Logger(string? file = null) => _file = file;

        public void Info(string msg) => Write("INFO", msg);
        public void Warn(string msg) => Write("WARN", msg);
        public void Error(string msg) => Write("ERROR", msg);

        private void Write(string level, string msg)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
            Console.WriteLine(line);
            Line?.Invoke(line);
            if (_file is null) return;
            try { File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }
    }
}
