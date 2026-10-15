using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TerrariaAutoFish
{
    // This type has no Terraria references: dependency resolution must precede runtime loading.
    public static class GameLoader
    {
        private static Assembly game;
        private static string gameDirectory;
        private static readonly object resolverLock = new object();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        public static Assembly Prepare(string path)
        {
            path = Path.GetFullPath(path);
            gameDirectory = Path.GetDirectoryName(path);
            if (!File.Exists(path) || !Directory.Exists(Path.Combine(gameDirectory, "Content")))
                throw new InvalidOperationException("请选择包含 Content 目录的正版 Terraria.exe。");
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (version != "1.4.5.8")
                throw new InvalidOperationException("本工具仅适配 1.4.5.8，检测到：" + version);
            if (!SetDllDirectory(gameDirectory))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Directory.SetCurrentDirectory(gameDirectory);
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            game = Assembly.LoadFrom(path);
            ConfigureXnaContentLocation();
            return game;
        }

        private static void ConfigureXnaContentLocation()
        {
            // XNA resolves Content against the entry EXE, not the working directory. The entry
            // EXE is our launcher, so point its cached title location at the installed game.
            var xna = Assembly.Load("Microsoft.Xna.Framework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553");
            var location = xna.GetType("Microsoft.Xna.Framework.TitleLocation", true);
            var cache = location.GetField("_titleLocation", BindingFlags.NonPublic | BindingFlags.Static);
            if (cache == null) throw new MissingFieldException(location.FullName, "_titleLocation");
            cache.SetValue(null, gameDirectory);
            var titleContainer = xna.GetType("Microsoft.Xna.Framework.TitleContainer", true);
            using (var stream = (Stream)titleContainer.GetMethod("OpenStream").Invoke(null,
                new object[] { "Content\\PixelShader.xnb" }))
            {
                if (stream.Length == 0) throw new InvalidDataException("游戏着色器资源为空。");
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            lock (resolverLock)
            {
                var name = new AssemblyName(args.Name).Name;
                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                    if (loaded.GetName().Name == name) return loaded;
                if (game != null)
                {
                    foreach (var resource in game.GetManifestResourceNames())
                    {
                        if (!resource.EndsWith("." + name + ".dll", StringComparison.OrdinalIgnoreCase)
                            && !resource.Equals(name + ".dll", StringComparison.OrdinalIgnoreCase)) continue;
                        using (var input = game.GetManifestResourceStream(resource))
                        using (var output = new MemoryStream())
                        {
                            input.CopyTo(output);
                            return Assembly.Load(output.ToArray());
                        }
                    }
                }
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".dll");
                if (File.Exists(local)) return Assembly.LoadFrom(local);
                string installed = Path.Combine(gameDirectory, name + ".dll");
                return File.Exists(installed) ? Assembly.LoadFrom(installed) : null;
            }
        }

        public static IBotBridge CreateRuntime()
        {
            var runtime = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FishingRuntime.dll"));
            return (IBotBridge)runtime.GetType("TerrariaAutoFish.RuntimeBridge", true)
                .GetMethod("Install", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        }

        public static void RunGame()
        {
            // Invoke the original entry point, retaining Terraria's own initialization and Steam support.
            game.EntryPoint.Invoke(null, new object[] { new string[0] });
        }

        public static void CheckStartup(string isolatedSaveDirectory)
        {
            var runtime = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FishingRuntime.dll"));
            var bridgeType = runtime.GetType("TerrariaAutoFish.RuntimeBridge", true);
            bridgeType.GetMethod("EnableStartupCheck").Invoke(null, null);
            // Exercise the original entry point and LaunchGame. The check-only prefix stops before
            // constructing a game window, loading Steam, or opening any characters/worlds.
            game.EntryPoint.Invoke(null, new object[] { new[] { "-savedirectory", isolatedSaveDirectory } });
            if (!(bool)bridgeType.GetMethod("StartupVerified").Invoke(null, null))
                throw new InvalidOperationException("未执行到预期的游戏启动检查点。");
        }
    }
}
