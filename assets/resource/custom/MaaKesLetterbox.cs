using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;

namespace MaaKes.Custom;

// Loaded from resource/custom by the unmodified MFAAvalonia custom-action loader.
public sealed class MaaKesLetterbox : IMaaCustomAction
{
    private static readonly ConditionalWeakTable<MaaController, LetterboxController> Sessions = new();
    private static readonly ConditionalWeakTable<MaaTasker, object> TaskerGates = new();
    private readonly Func<MaaController, LetterboxController> _create;
    public string Name { get; set; } = "MaaKesLetterbox";

    public MaaKesLetterbox() : this(CreateWin32) { }
    internal MaaKesLetterbox(Func<MaaController, LetterboxController> create) { _create = create; }

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            using var parameters = JsonDocument.Parse(args.ActionParam);
            return SetEnabled((MaaTasker)context.Tasker, parameters.RootElement.GetProperty("enabled").GetBoolean());
        }
        catch (Exception exception)
        {
            Console.WriteLine("[MaaKesLetterbox] " + exception.Message);
            return false;
        }
    }

    internal bool SetEnabled(MaaTasker tasker, bool enabled)
    {
        lock (TaskerGates.GetValue(tasker, _ => new object()))
            return SetEnabledCore(tasker, enabled);
    }

    private bool SetEnabledCore(MaaTasker tasker, bool enabled)
    {
        var current = tasker.Controller;
        if (Sessions.TryGetValue(current, out var session))
        {
            if (enabled)
                return current.Screencap().Wait() == MaaJobStatus.Succeeded;
            // Keep the previous UI-owned controller untouched, including its scaling options.
            tasker.Controller = session.Original!;
            session.ReleaseOriginal();
            Sessions.Remove(current);
            current.Dispose();
            return true;
        }
        if (!enabled) return true;

        var adapter = _create(current);
        MaaCustomController? outer = null;
        var bound = false;
        try
        {
            outer = new MaaCustomController(adapter);
            if (!outer.SetOption_ScreenshotTargetShortSide(720))
                throw new InvalidOperationException("Cannot configure 1280x720 recognition.");
            Sessions.Add(outer, adapter);
            tasker.Controller = outer;
            adapter.TakeOriginal(current);
            bound = true;
            // Keep the adapter bound on invalid bars so following tasks cannot fall
            // back to uncorrected coordinates. A valid capture permits recovery.
            return outer.Screencap().Wait() == MaaJobStatus.Succeeded;
        }
        catch
        {
            if (!bound)
            {
                if (outer != null) { Sessions.Remove(outer); outer.Dispose(); }
                else adapter.Dispose();
            }
            throw;
        }
    }

    private static LetterboxController CreateWin32(MaaController original)
    {
        if (!OperatingSystem.IsWindows() || original is not MaaWin32Controller)
            throw new NotSupportedException("Letterbox cropping requires a Win32 controller.");
        using var document = JsonDocument.Parse(original.Info ?? "{}");
        var info = document.RootElement;
        if (!string.Equals(info.GetProperty("type").GetString(), "win32", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The controller is not a Win32 window.");
        var hwnd = new nint(info.GetProperty("hwnd").GetInt64());
        var raw = new MaaWin32Controller(hwnd,
            (Win32ScreencapMethods)info.GetProperty("screencap_method").GetInt64(),
            (Win32InputMethod)info.GetProperty("mouse_method").GetInt64(),
            (Win32InputMethod)info.GetProperty("keyboard_method").GetInt64(), LinkOption.None);
        return new LetterboxController(raw, () => LetterboxController.GetClientSize(hwnd),
            message => Console.WriteLine("[MaaKesLetterbox] " + message));
    }
}


/// <summary>A content rectangle in an unscaled client-area screenshot.</summary>
internal readonly record struct LetterboxViewport(int SourceWidth, int SourceHeight, int Top, int Height)
{
    public bool TryMap(ref int x, ref int y)
    {
        // Maa uses -1 to leave an axis unchanged.
        if (x < -1 || y < -1 || x >= SourceWidth || y >= Height)
            return false;
        if (y >= 0)
            y += Top;
        return true;
    }

    public static bool TryCreate(ReadOnlySpan<byte> pixels, int width, int height, int channels,
        out LetterboxViewport viewport)
    {
        viewport = default;
        if (width <= 0 || height <= 0 || channels is not (3 or 4)
            || (long)width * height * channels > pixels.Length)
            return false;

        // Existing 16:9 (or wider) screenshots pass through. This option only removes
        // horizontal letterbox bars, never guesses an arbitrary dark content rectangle.
        var contentHeight = (int)(((long)width * 9 + 8) / 16);
        if (height <= contentHeight)
        {
            viewport = new(width, height, 0, height);
            return true;
        }

        var difference = height - contentHeight;
        // Pixel rounding may put the extra row on either black band.
        for (var top = difference / 2; top <= (difference + 1) / 2; top++)
        {
            if (!IsBlackBand(pixels, width, channels, 0, top)
                || !IsBlackBand(pixels, width, channels, top + contentHeight, height))
                continue;
            viewport = new(width, height, top, contentHeight);
            return true;
        }
        return false;
    }

    private static bool IsBlackBand(ReadOnlySpan<byte> pixels, int width, int channels, int start, int end)
    {
        long dark = 0, total = 0;
        if (start == end) return true;
        for (var y = start; ; y = Math.Min(y + 4, end - 1))
        {
            for (var x = 0; x < width; x += 4)
            {
                var offset = (y * width + x) * channels;
                if (pixels[offset] <= 16 && pixels[offset + 1] <= 16 && pixels[offset + 2] <= 16)
                    dark++;
                total++;
            }
            // Include both band boundaries, including a one-pixel band.
            if (y == end - 1) break;
        }
        return total > 0 && dark * 1000 >= total * 995;
    }
}

/// <summary>
/// Uses the public custom-controller API. The inner controller keeps raw dimensions;
/// only the outer controller applies the project's normal screenshot scaling.
/// </summary>
internal sealed class LetterboxController : IMaaCustomController
{
    private readonly MaaController _inner;
    private readonly Func<(int Width, int Height)> _clientSize;
    private readonly Action<string>? _report;
    private readonly object _gate = new();
    private LetterboxViewport? _viewport;
    private string? _lastState;
    private bool _disposed;
    internal MaaController? Original { get; private set; }
    internal void TakeOriginal(MaaController original) { lock (_gate) Original = original; }
    internal void ReleaseOriginal() { lock (_gate) Original = null; }

    public string Name { get; set; } = "Win32Letterbox16By9";

    // Takes ownership of inner, including on initialization failure.
    internal LetterboxController(MaaController inner, Func<(int Width, int Height)> clientSize,
        Action<string>? report = null)
    {
        _inner = inner;
        _clientSize = clientSize;
        _report = report;
        try
        {
            if (!inner.SetOption_ScreenshotUseRawSize(true))
                throw new InvalidOperationException("Letterbox input requires an unscaled inner controller.");
        }
        catch
        {
            inner.Dispose();
            throw;
        }
    }

    // Native callbacks must not let managed exceptions cross the ABI boundary.
    private bool Guard(Func<bool> operation)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            try { return operation(); }
            catch (Exception exception)
            {
                _viewport = null;
                ReportState($"Letterbox controller: {exception.Message}");
                return false;
            }
        }
    }

    private void ReportState(string state)
    {
        if (_lastState == state) return;
        _lastState = state;
        try { _report?.Invoke(state); }
        catch { /* Logging must not fail a native callback. */ }
    }

    private static bool Succeeded(MaaJob job) => job.Wait() == MaaJobStatus.Succeeded;
    public bool Connect() => Guard(() => { _viewport = null; return Succeeded(_inner.LinkStart()); });
    public bool Connected() => Guard(() => _inner.IsConnected);
    public bool RequestUuid(IMaaStringBuffer buffer) => Guard(() => buffer.TrySetValue(_inner.Uuid ?? string.Empty));
    public ControllerFeatures GetFeatures() => ControllerFeatures.UseMouseDownAndUpInsteadOfClick
        | ControllerFeatures.UseKeyboardDownAndUpInsteadOfClick;
    public bool StartApp(string intent) => Guard(() => Succeeded(_inner.StartApp(intent)));
    public bool StopApp(string intent) => Guard(() => Succeeded(_inner.StopApp(intent)));

    public bool Screencap(IMaaImageBuffer buffer) => Guard(() => Capture(buffer));

    private unsafe bool Capture(IMaaImageBuffer buffer)
    {
        _viewport = null;
        buffer.TryClear();
        using var source = new MaaImageBuffer();
        if (!Succeeded(_inner.Screencap()) || !_inner.GetCachedImage(source)
            || !source.TryGetRawData(out var data, out var width, out var height, out var type)
            || type is not (16 or 24) || buffer is not MaaImageBuffer output)
            return false; // CV_8UC3 / CV_8UC4, tightly packed MaaImageBuffer data.

        var pixels = new ReadOnlySpan<byte>((void*)data, checked(width * height * source.Channels));
        if (!LetterboxViewport.TryCreate(pixels, width, height, source.Channels, out var viewport))
        {
            ReportState($"Letterbox crop rejected ({width}x{height}): expected centered 16:9 content with black top/bottom bars.");
            return false;
        }
        if (_clientSize() != (width, height)) return false;
        // Full-width row slices remain contiguous. MaaImageBuffer copies the data.
        if (!output.TrySetRawData(data + checked(viewport.Top * width * source.Channels),
                width, viewport.Height, type))
            return false;
        _viewport = viewport;
        ReportState($"Letterbox: {width}x{height} -> {width}x{viewport.Height}, top={viewport.Top}");
        return true;
    }

    private bool TryMap(ref int x, ref int y)
    {
        if (_viewport is not { } viewport) return false;
        if (_clientSize() != (viewport.SourceWidth, viewport.SourceHeight))
        {
            _viewport = null; // Require a fresh frame even if the size changes back.
            return false;
        }
        // Check immediately before enqueueing input. Windows resizing and native
        // input dispatch cannot be made atomic through the controller API.
        return viewport.TryMap(ref x, ref y);
    }

    public bool Click(int x, int y) => Guard(() => TryMap(ref x, ref y) && Succeeded(_inner.Click(x, y)));
    public bool Swipe(int x1, int y1, int x2, int y2, int duration) => Guard(() =>
        TryMap(ref x1, ref y1) && TryMap(ref x2, ref y2) && Succeeded(_inner.Swipe(x1, y1, x2, y2, duration)));
    public bool TouchDown(int contact, int x, int y, int pressure) => Guard(() =>
        TryMap(ref x, ref y) && Succeeded(_inner.TouchDown(contact, x, y, pressure)));
    public bool TouchMove(int contact, int x, int y, int pressure) => Guard(() =>
        TryMap(ref x, ref y) && Succeeded(_inner.TouchMove(contact, x, y, pressure)));
    // Release is allowed even after resize or capture failure.
    public bool TouchUp(int contact) => Guard(() => Succeeded(_inner.TouchUp(contact)));
    public bool ClickKey(int keycode) => Guard(() => Succeeded(_inner.ClickKey(keycode)));
    public bool KeyDown(int keycode) => Guard(() => Succeeded(_inner.KeyDown(keycode)));
    public bool KeyUp(int keycode) => Guard(() => Succeeded(_inner.KeyUp(keycode)));
    public bool InputText(string text) => Guard(() => Succeeded(_inner.InputText(text)));
    public bool Scroll(int dx, int dy) => Guard(() => Succeeded(_inner.Scroll(dx, dy)));
    public bool RelativeMove(int dx, int dy) => Guard(() => Succeeded(_inner.RelativeMove(dx, dy)));
    public bool Inactive() => Guard(() => Succeeded(_inner.Inactive()));
    public bool Shell(string cmd, long millisecondsTimeout, IMaaStringBuffer buffer) => Guard(() =>
        Succeeded(_inner.Shell(cmd, millisecondsTimeout)) && _inner.GetShellOutput(out var result) && buffer.TrySetValue(result));
    public bool GetInfo(IMaaStringBuffer buffer) => Guard(() => buffer.TrySetValue(_inner.Info ?? "{}"));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _viewport = null;
            _inner.Dispose();
            Original?.Dispose();
            Original = null;
        }
    }

    internal static (int, int) GetClientSize(nint hwnd)
    {
        var previous = SetThreadDpiAwarenessContext(new nint(-4)); // PER_MONITOR_AWARE_V2
        try { return GetClientRect(hwnd, out var rect) ? (rect.Right - rect.Left, rect.Bottom - rect.Top) : (0, 0); }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
}
