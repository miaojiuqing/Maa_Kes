using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MaaKes.Custom;
using Xunit;

namespace MaaKes.Letterbox.Tests;

public partial class LetterboxTests
{
    [Theory]
    [InlineData(2560, 1600, 80, 1440)]
    [InlineData(1920, 1200, 60, 1080)]
    [InlineData(1280, 800, 40, 720)]
    [InlineData(1280, 720, 0, 720)]
    [InlineData(2560, 1440, 0, 1440)]
    [InlineData(1920, 1080, 0, 1080)]
    [InlineData(1366, 854, 43, 768)]
    [InlineData(1366, 855, 43, 768)]
    [InlineData(1366, 855, 44, 768)]
    [InlineData(2560, 1601, 80, 1440)]
    [InlineData(2560, 1601, 81, 1440)]
    [InlineData(1366, 768, 0, 768)]
    public void ContentRectangleAndBounds(int width, int height, int top, int contentHeight)
    {
        var pixels = Image(width, height, contentHeight, top);
        Assert.True(LetterboxViewport.TryCreate(pixels, width, height, 3, out var view));
        Assert.Equal(new LetterboxViewport(width, height, top, contentHeight), view);
        int x = width - 1, y = contentHeight - 1;
        Assert.True(view.TryMap(ref x, ref y));
        Assert.Equal(top + contentHeight - 1, y);
        x = y = -1;
        Assert.True(view.TryMap(ref x, ref y));
        Assert.Equal(-1, y);
        x = width; y = 0;
        Assert.False(view.TryMap(ref x, ref y));
        x = 0; y = contentHeight;
        Assert.False(view.TryMap(ref x, ref y));
        x = -2; y = 0;
        Assert.False(view.TryMap(ref x, ref y));
    }

    [Fact]
    public void RejectsEitherNonBlackBandAndMalformedInput()
    {
        var pixels = Image(2560, 1600);
        Array.Fill(pixels, (byte)255, 0, 80 * 2560 * 3);
        Assert.False(LetterboxViewport.TryCreate(pixels, 2560, 1600, 3, out _));
        pixels = Image(2560, 1600);
        Array.Fill(pixels, (byte)255, 1520 * 2560 * 3, 80 * 2560 * 3);
        Assert.False(LetterboxViewport.TryCreate(pixels, 2560, 1600, 3, out _));
        Assert.False(LetterboxViewport.TryCreate([], 2560, 1600, 3, out _));
        Assert.False(LetterboxViewport.TryCreate(pixels, 2560, 1600, 1, out _));
        Assert.False(LetterboxViewport.TryCreate(pixels, 2560, 1601, 3, out _));
    }

    [Theory]
    [InlineData(854, 43)]
    [InlineData(855, 44)]
    public void RoundedGeometryStillRequiresBothBlackBands(int height, int top)
    {
        var pixels = Image(1366, height, 768, top);
        Array.Fill(pixels, (byte)255, 0, top * 1366 * 3);
        Assert.False(LetterboxViewport.TryCreate(pixels, 1366, height, 3, out _));
        pixels = Image(1366, height, 768, top);
        var bottom = (top + 768) * 1366 * 3;
        Array.Fill(pixels, (byte)255, bottom, pixels.Length - bottom);
        Assert.False(LetterboxViewport.TryCreate(pixels, 1366, height, 3, out _));
    }

    [Fact]
    public void ToleratesSmallOverlayAndIgnoresAlpha()
    {
        var pixels = Image(2560, 1600);
        pixels[0] = 255;
        Assert.True(LetterboxViewport.TryCreate(pixels, 2560, 1600, 3, out _));
        var rgba = new byte[2560 * 1600 * 4];
        for (var i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        Assert.True(LetterboxViewport.TryCreate(rgba, 2560, 1600, 4, out _));
    }

    // Exercises the real native custom-controller ABI and BOTH framework scaling
    // layers. The fake backend receives input; no actual desktop window is used.
    [Fact]
    public unsafe void NativeRoundTripScalesOnlyOnceAndOffsetsInput()
    {
        using var fixture = new Fixture();
        var outer = fixture.Outer;
        Assert.Equal(MaaJobStatus.Succeeded, outer.Screencap().Wait());
        using var image = new MaaImageBuffer();
        Assert.True(outer.GetCachedImage(image));
        Assert.Equal((1280, 720), (image.Width, image.Height));
        Assert.True(image.TryGetRawData(out var pointer));
        Assert.Equal((byte)31, *(byte*)pointer);
        Assert.Equal((byte)131, *((byte*)pointer + (image.Height - 1) * image.Width * image.Channels));
        Assert.Equal(MaaJobStatus.Succeeded, outer.Click(679, 256).Wait());
        Assert.Contains((1358, 592), fixture.Backend.Points);
        fixture.Backend.Points.Clear();
        Assert.Equal(MaaJobStatus.Succeeded, outer.Swipe(100, 100, 200, 200, 100).Wait());
        Assert.Equal((200, 280), fixture.Backend.Points.First());
        Assert.Equal((400, 480), fixture.Backend.Points.Last());
        Assert.Equal(MaaJobStatus.Succeeded, outer.TouchDown(1, 10, 20, 3).Wait());
        Assert.Equal((20, 120), fixture.Backend.Points.Last());
        Assert.Equal(MaaJobStatus.Succeeded, outer.TouchMove(1, 20, 30, 3).Wait());
        Assert.Equal((40, 140), fixture.Backend.Points.Last());
        Assert.Equal(MaaJobStatus.Succeeded, outer.TouchUp(1).Wait());
        Assert.Equal(MaaJobStatus.Succeeded, outer.Scroll(120, -120).Wait());
        Assert.Equal((120, -120), fixture.Backend.ScrollDelta);
        Assert.Equal(MaaJobStatus.Succeeded, outer.RelativeMove(7, -3).Wait());
        Assert.Equal((7, -3), fixture.Backend.RelativeDelta);
        Assert.Equal(MaaJobStatus.Succeeded, outer.InputText("test").Wait());
        Assert.Equal("test", fixture.Backend.Text);
        Assert.Equal(MaaJobStatus.Succeeded, outer.KeyDown(65).Wait());
        Assert.Equal(MaaJobStatus.Succeeded, outer.KeyUp(65).Wait());
        Assert.Equal(MaaJobStatus.Succeeded, outer.Inactive().Wait());
        Assert.True(fixture.Backend.WasInactive);
    }

    [Theory]
    [InlineData(1280, 800, 40)]
    [InlineData(1366, 855, 44)]
    public unsafe void CropPreservesBothBoundaryRows(int width, int height, int top)
    {
        var contentHeight = width == 1280 ? 720 : 768;
        var backend = new FakeController { Width = width, Height = height, Top = top };
        using var adapter = new LetterboxController(new MaaCustomController(backend), () => (width, height));
        using var image = new MaaImageBuffer();
        Assert.True(adapter.Screencap(image));
        Assert.Equal((width, contentHeight), (image.Width, image.Height));
        Assert.True(image.TryGetRawData(out var pointer));
        var first = new ReadOnlySpan<byte>((void*)pointer, 3);
        var last = new ReadOnlySpan<byte>((byte*)pointer + (contentHeight - 1) * width * 3, 3);
        Assert.Equal(new byte[] { 31, 71, 109 }, first.ToArray());
        Assert.Equal(new byte[] { 131, (byte)(71 + (contentHeight - 1) % 127), 109 }, last.ToArray());
    }

    [Fact]
    public void ResizeAndInvalidCaptureBlockInputButAllowReleaseAndRecovery()
    {
        using var fixture = new Fixture();
        var outer = fixture.Outer;
        Assert.Equal(MaaJobStatus.Succeeded, outer.Screencap().Wait());
        fixture.Size = (1280, 720);
        Assert.Equal(MaaJobStatus.Failed, outer.TouchDown(0, 5, 5).Wait());
        Assert.Empty(fixture.Backend.Points);
        Assert.Equal(MaaJobStatus.Succeeded, outer.TouchUp(0).Wait());
        fixture.Size = (2560, 1600);
        Assert.Equal(MaaJobStatus.Failed, outer.TouchDown(0, 5, 5).Wait());
        Assert.Empty(fixture.Backend.Points); // A resize back still needs a new screenshot.
        fixture.Size = (1280, 720);
        fixture.Backend.Width = 1280; fixture.Backend.Height = 720;
        Assert.Equal(MaaJobStatus.Succeeded, outer.Screencap().Wait());
        Assert.Equal(MaaJobStatus.Succeeded, outer.Click(300, 200).Wait());
        Assert.Equal((300, 200), fixture.Backend.Points.Last());
        fixture.Size = (2560, 1600);
        fixture.Backend.Width = 2560; fixture.Backend.Height = 1600;
        fixture.Backend.ValidBars = false;
        Assert.Equal(MaaJobStatus.Failed, outer.Screencap().Wait());
        fixture.Backend.Points.Clear();
        Assert.Equal(MaaJobStatus.Failed, outer.TouchDown(0, 5, 5).Wait());
        Assert.Empty(fixture.Backend.Points);
        Assert.Equal(MaaJobStatus.Succeeded, outer.KeyUp(65).Wait());
        fixture.Backend.ValidBars = true;
        Assert.Equal(MaaJobStatus.Succeeded, outer.Screencap().Wait());
        fixture.Backend.CaptureFails = true;
        Assert.Equal(MaaJobStatus.Failed, outer.Screencap().Wait());
        Assert.Equal(MaaJobStatus.Failed, outer.TouchMove(0, 20, 20).Wait());
    }

    [Fact]
    public void UnwrappedControllerKeepsOriginalImageAndWrapperOwnsBackend()
    {
        var backend = new FakeController();
        using (var original = new MaaCustomController(backend))
        {
            Assert.True(original.SetOption_ScreenshotTargetShortSide(720));
            Assert.Equal(MaaJobStatus.Succeeded, original.Screencap().Wait());
            using var image = new MaaImageBuffer();
            Assert.True(original.GetCachedImage(image));
            Assert.Equal((1152, 720), (image.Width, image.Height));
        }
        var fixture = new Fixture();
        fixture.Dispose();
        Assert.Equal(1, fixture.Backend.DisposeCount);
    }

    internal static byte[] Image(int width, int height, int? contentRows = null, int? contentTop = null)
    {
        var pixels = new byte[width * height * 3];
        var contentHeight = contentRows ?? Math.Min(height, (int)Math.Round(width * 9.0 / 16));
        var top = contentTop ?? (height - contentHeight) / 2;
        for (var y = top; y < top + contentHeight; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 3;
            pixels[offset] = (byte)(y - top < contentHeight / 2 ? 31 : 131);
            pixels[offset + 1] = (byte)(71 + (y - top) % 127);
            pixels[offset + 2] = 109;
        }
        return pixels;
    }

    private sealed class Fixture : IDisposable
    {
        public readonly FakeController Backend = new();
        public (int Width, int Height) Size = (2560, 1600);
        public readonly MaaCustomController Outer;
        public Fixture()
        {
            var inner = new MaaCustomController(Backend, LinkOption.None);
            Outer = new MaaCustomController(new LetterboxController(inner, () => Size));
            Assert.True(Outer.SetOption_ScreenshotTargetShortSide(720));
        }
        public void Dispose() => Outer.Dispose();
    }

    private sealed class FakeController : IMaaCustomController
    {
        public string Name { get; set; } = "SyntheticLetterbox";
        public int Width = 2560, Height = 1600;
        public int? Top;
        public bool ValidBars = true, CaptureFails, WasInactive;
        public int DisposeCount;
        public List<(int, int)> Points = [];
        public (int, int) ScrollDelta, RelativeDelta;
        public string Text = "";
        public bool Connect() => true;
        public bool Connected() => true;
        public bool RequestUuid(IMaaStringBuffer buffer) => buffer.TrySetValue("synthetic");
        public ControllerFeatures GetFeatures() => ControllerFeatures.UseMouseDownAndUpInsteadOfClick
            | ControllerFeatures.UseKeyboardDownAndUpInsteadOfClick;
        public bool StartApp(string intent) => true;
        public bool StopApp(string intent) => true;
        public unsafe bool Screencap(IMaaImageBuffer buffer)
        {
            if (CaptureFails) return false;
            var bytes = Image(Width, Height, contentTop: Top);
            if (!ValidBars) Array.Fill(bytes, (byte)100);
            fixed (byte* pointer = bytes)
                return ((MaaImageBuffer)buffer).TrySetRawData((nint)pointer, Width, Height, 16);
        }
        public bool Click(int x, int y) { Points.Add((x, y)); return true; }
        public bool Swipe(int x1, int y1, int x2, int y2, int duration) => Click(x1, y1) && Click(x2, y2);
        public bool TouchDown(int contact, int x, int y, int pressure) => Click(x, y);
        public bool TouchMove(int contact, int x, int y, int pressure) => Click(x, y);
        public bool TouchUp(int contact) => true;
        public bool ClickKey(int keycode) => true;
        public bool InputText(string text) { Text = text; return true; }
        public bool KeyDown(int keycode) => true;
        public bool KeyUp(int keycode) => true;
        public bool Scroll(int dx, int dy) { ScrollDelta = (dx, dy); return true; }
        public bool RelativeMove(int dx, int dy) { RelativeDelta = (dx, dy); return true; }
        public bool Shell(string cmd, long millisecondsTimeout, IMaaStringBuffer buffer) => buffer.TrySetValue("output");
        public bool Inactive() { WasInactive = true; return true; }
        public bool GetInfo(IMaaStringBuffer buffer) => buffer.TrySetValue("{\"type\":\"custom\"}");
        public void Dispose() => DisposeCount++;
    }
}
