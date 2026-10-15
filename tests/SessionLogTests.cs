using System;
using System.IO;
using TerrariaAutoFish;

internal static class SessionLogTests
{
    private static int checks;
    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerrariaAutoFish-LogTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var log = new SessionLog(directory, 100);
            Assert(log.Append("鱼饵已耗尽，自动停止"), "Unicode stop reason is saved");
            string first = log.CurrentPath;
            Assert(File.ReadAllText(first).Contains("鱼饵已耗尽"), "stop reason readable immediately without closing process");
            log.Append(new String('x', 200));
            Assert(log.Append("next part"), "logging continues after size limit");
            Assert(first != log.CurrentPath && File.Exists(first), "rotation preserves the previous log");

            string blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "this is a file, not a directory");
            var unavailable = new SessionLog(blocked);
            Assert(!unavailable.Append("stop"), "unwritable destination does not throw into UI or game");
            Assert(!String.IsNullOrWhiteSpace(unavailable.LastError), "logging failure is observable");
            File.Delete(blocked);
            Assert(unavailable.Append("recovered"), "logging can recover when destination becomes available");
            Assert(unavailable.LastError == null, "successful recovery clears previous error");
            File.Delete(unavailable.CurrentPath);
            Directory.Delete(blocked);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Console.WriteLine("PASS: " + checks + " persistent log checks.");
    }
}
