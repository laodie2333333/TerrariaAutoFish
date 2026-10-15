using System;
using System.IO;
using System.Text;

namespace TerrariaAutoFish
{
    // Only the UI thread writes here. No disk I/O is performed on the game's update thread.
    public sealed class SessionLog
    {
        private readonly string directory;
        private readonly string sessionName;
        private readonly long maxBytes;
        private int part;
        public string LastError { get; private set; }
        public string CurrentPath { get; private set; }

        public SessionLog(string directory, long maxBytes = 5 * 1024 * 1024)
        {
            if (maxBytes < 1) throw new ArgumentOutOfRangeException("maxBytes");
            this.directory = directory;
            this.maxBytes = maxBytes;
            sessionName = "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-"
                + System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        public bool Append(string line)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, sessionName + "-" + part + ".log");
                if (File.Exists(path) && new FileInfo(path).Length >= maxBytes)
                    path = Path.Combine(directory, sessionName + "-" + (++part) + ".log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line
                    + Environment.NewLine, new UTF8Encoding(false));
                CurrentPath = path;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }
}
