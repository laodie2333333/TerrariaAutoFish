using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

// Extract the installed game's Golden Fishing Rod (item 2294) for the local app icon.
internal sealed class ExportGoldenRod : IGraphicsDeviceService
{
    public GraphicsDevice GraphicsDevice { get; private set; }
    public event EventHandler<EventArgs> DeviceCreated { add { } remove { } }
    public event EventHandler<EventArgs> DeviceDisposing { add { } remove { } }
    public event EventHandler<EventArgs> DeviceReset { add { } remove { } }
    public event EventHandler<EventArgs> DeviceResetting { add { } remove { } }

    [STAThread]
    private static void Main(string[] args)
    {
        string gameDirectory = Path.GetFullPath(args[0]);
        typeof(Vector2).Assembly.GetType("Microsoft.Xna.Framework.TitleLocation", true)
            .GetField("_titleLocation", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, gameDirectory);
        using (var host = new Control())
        using (var device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.Reach,
            new PresentationParameters { DeviceWindowHandle = host.Handle, IsFullScreen = false,
                BackBufferWidth = 64, BackBufferHeight = 64 }))
        {
            var services = new GameServiceContainer();
            services.AddService(typeof(IGraphicsDeviceService), new ExportGoldenRod { GraphicsDevice = device });
            using (var content = new ContentManager(services, "Content"))
            using (var output = File.Create(args[1]))
            {
                var texture = content.Load<Texture2D>("Images/Item_2294");
                texture.SaveAsPng(output, texture.Width, texture.Height);
                Console.WriteLine("Golden Fishing Rod: " + texture.Width + " x " + texture.Height);
            }
        }
    }
}
