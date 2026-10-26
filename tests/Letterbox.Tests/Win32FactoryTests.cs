using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaKes.Custom;
using Xunit;

namespace MaaKes.Letterbox.Tests;

public partial class LetterboxTests
{
    [Fact]
    public void DefaultActionUsesRealWin32FactoryAndShippedTask()
    {
        // All capture/input targets this test's invisible window. No game,
        // desktop capture, foreground activation, or cursor movement is used.
        using var window = new TestWindow();
        var shipped = Path.Combine(AppContext.BaseDirectory, "shipped");
        using var resource = new MaaResource(Path.Combine(shipped, "resource"));
        var original = new MaaWin32Controller(window.Handle, Win32ScreencapMethods.PrintWindow,
            Win32InputMethod.PostMessage, Win32InputMethod.PostMessage);
        Assert.True(original.SetOption_ScreenshotTargetShortSide(720));
        using var tasker = new MaaTasker(original, resource, DisposeOptions.Controller);
        Assert.True(resource.Register(new MaaKesLetterbox()));
        var definition = JsonNode.Parse(File.ReadAllText(Path.Combine(shipped, "tasks", "黑边适配.json")))!;
        var entry = definition["task"]![0]!["entry"]!.GetValue<string>();
        Assert.Equal(MaaJobStatus.Succeeded, tasker.AppendTask(entry).Wait());
        Assert.NotSame(original, tasker.Controller);
        Assert.Equal(MaaJobStatus.Succeeded, tasker.Controller.Screencap().Wait());
        using var image = new MaaImageBuffer();
        Assert.True(tasker.Controller.GetCachedImage(image));
        Assert.Equal((1280, 720), (image.Width, image.Height));
        Assert.Equal(MaaJobStatus.Succeeded, tasker.Controller.Click(100, 100).Wait());
        Assert.True(SpinWait.SpinUntil(() => window.Clicks.Contains((25, 55)), TimeSpan.FromSeconds(2)));

        var optionName = definition["task"]![0]!["option"]![0]!.GetValue<string>();
        var off = definition["option"]![optionName]!["cases"]!.AsArray()
            .Single(c => c!["name"]!.GetValue<string>() == "No")!["pipeline_override"]!.ToJsonString();
        Assert.Equal(MaaJobStatus.Succeeded, tasker.AppendTask(entry, off).Wait());
        Assert.Same(original, tasker.Controller);
        Assert.Equal(MaaJobStatus.Succeeded, original.Screencap().Wait());
        Assert.True(original.GetCachedImage(image));
        Assert.Equal((960, 720), (image.Width, image.Height));
    }

    private sealed class TestWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly WndProc _procedure;
        private readonly TaskCompletionSource<nint> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public nint Handle { get; }
        public ConcurrentQueue<(int, int)> Clicks { get; } = new();

        public TestWindow()
        {
            _procedure = ProcessMessage;
            _thread = new Thread(Run) { IsBackground = true, Name = "Letterbox test window" };
            _thread.Start();
            Handle = _ready.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }

        private void Run()
        {
            var name = "MaaKesLetterboxTest_" + Guid.NewGuid().ToString("N");
            var instance = GetModuleHandle(null);
            var window = nint.Zero;
            ushort atom = 0;
            var previousDpi = SetThreadDpiAwarenessContext(new nint(-4));
            try
            {
                var cls = new WindowClass { Instance = instance, ClassName = name,
                    Procedure = Marshal.GetFunctionPointerForDelegate(_procedure) };
                atom = RegisterClass(ref cls);
                if (atom == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                // WS_EX_NOACTIVATE, WS_POPUP; deliberately omit WS_VISIBLE.
                window = CreateWindowEx(0x08000000, name, name, 0x80000000,
                    0, 0, 320, 240, 0, 0, instance, 0);
                if (window == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                _ready.TrySetResult(window);
                while (GetMessage(out var message, 0, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            catch (Exception exception) { _ready.TrySetException(exception); }
            finally
            {
                if (window != 0) DestroyWindow(window);
                if (atom != 0) UnregisterClass(name, instance);
                if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
            }
        }

        private nint ProcessMessage(nint hwnd, uint message, nuint wparam, nint lparam)
        {
            switch (message)
            {
                case 0x0317: // WM_PRINT
                case 0x0318: // WM_PRINTCLIENT
                    var full = new Rect { Right = 320, Bottom = 240 };
                    FillRect((nint)wparam, ref full, GetStockObject(4)); // BLACK_BRUSH
                    var content = new Rect { Top = 30, Right = 320, Bottom = 210 };
                    var brush = CreateSolidBrush(0x006d471f);
                    try { FillRect((nint)wparam, ref content, brush); }
                    finally { DeleteObject(brush); }
                    return 0;
                case 0x0201: // WM_LBUTTONDOWN
                    Clicks.Enqueue(((short)(lparam.ToInt64() & 0xffff), (short)((lparam.ToInt64() >> 16) & 0xffff)));
                    return 0;
                case 0x0010: // WM_CLOSE
                    DestroyWindow(hwnd);
                    return 0;
                case 0x0002: // WM_DESTROY
                    PostQuitMessage(0);
                    return 0;
                default: return DefWindowProc(hwnd, message, wparam, lparam);
            }
        }

        public void Dispose()
        {
            PostMessage(Handle, 0x0010, 0, 0);
            if (!_thread.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test window did not exit.");
            GC.KeepAlive(_procedure);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate nint WndProc(nint hwnd, uint message, nuint wparam, nint lparam);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Style;
            public nint Procedure;
            public int ClassExtra, WindowExtra;
            public nint Instance, Icon, Cursor, Background;
            public string? MenuName, ClassName;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public nint Hwnd;
            public uint Id;
            public nuint Wparam;
            public nint Lparam;
            public uint Time;
            public int X, Y;
            public uint Private;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass cls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(ref Message message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nuint wparam, nint lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint hwnd, uint message, nuint wparam, nint lparam);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] private static extern int FillRect(nint hdc, ref Rect rect, nint brush);
        [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
        [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
        [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    }
}
