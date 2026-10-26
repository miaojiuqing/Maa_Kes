using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MaaKes.Custom;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace MaaKes.Letterbox.Tests;

public partial class LetterboxTests
{
    [Fact]
    public void CustomTaskAffectsFollowingPipelineAndCanRestoreOriginal()
    {
        var fixture = new TaskFixture();
        using (fixture)
        {
            Assert.Equal(MaaJobStatus.Succeeded, fixture.Run(true));
            var wrapped = fixture.Tasker.Controller;
            Assert.NotSame(fixture.Original, wrapped);
            Assert.Equal(MaaJobStatus.Succeeded, fixture.Tasker.AppendTask("StartVerify").Wait());
            Assert.Contains((600, 480), fixture.Raw.Points);
            Assert.Empty(fixture.Before.Points);
            Assert.Equal(MaaJobStatus.Succeeded, fixture.Run(true));
            Assert.Same(wrapped, fixture.Tasker.Controller);
            Assert.Equal(1, fixture.Created);
            Assert.Equal(MaaJobStatus.Succeeded, fixture.Run(false));
            Assert.Same(fixture.Original, fixture.Tasker.Controller);
            Assert.Equal(1, fixture.Raw.DisposeCount);
            Assert.Equal(0, fixture.Before.DisposeCount);
            Assert.Equal(MaaJobStatus.Succeeded, fixture.Original.Screencap().Wait());
            using var image = new MaaImageBuffer();
            Assert.True(fixture.Original.GetCachedImage(image));
            Assert.Equal((1152, 720), (image.Width, image.Height));
        }
        Assert.Equal(1, fixture.Before.DisposeCount);
        Assert.Equal(1, fixture.Raw.DisposeCount);
    }

    [Fact]
    public void InvalidBarsBlockInputAndRecoverBeforeDisabling()
    {
        using var fixture = new TaskFixture();
        fixture.Raw.ValidBars = false;
        Assert.Equal(MaaJobStatus.Failed, fixture.Run(true));
        Assert.NotSame(fixture.Original, fixture.Tasker.Controller);
        Assert.Equal(MaaJobStatus.Failed, fixture.Tasker.Controller.TouchDown(0, 100, 100).Wait());
        Assert.Empty(fixture.Before.Points);
        Assert.Empty(fixture.Raw.Points);
        fixture.Raw.ValidBars = true;
        Assert.Equal(MaaJobStatus.Succeeded, fixture.Run(false));
        Assert.Same(fixture.Original, fixture.Tasker.Controller);
    }

    [Fact]
    public void InstancesAndControllerLifetimeStayIndependent()
    {
        var first = new TaskFixture();
        var second = new TaskFixture();
        using (first)
        using (second)
        {
            Assert.Equal(MaaJobStatus.Succeeded, first.Run(true));
            Assert.Same(second.Original, second.Tasker.Controller);
            Assert.Equal(MaaJobStatus.Succeeded, second.Run(true));
            Assert.NotSame(first.Tasker.Controller, second.Tasker.Controller);
            Assert.Equal(MaaJobStatus.Succeeded, first.Run(false));
            Assert.NotSame(second.Original, second.Tasker.Controller);
        }
        Assert.Equal(1, first.Before.DisposeCount);
        Assert.Equal(1, first.Raw.DisposeCount);
        Assert.Equal(1, second.Before.DisposeCount);
        Assert.Equal(1, second.Raw.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentEnablesOnlyCreateOneOwnedWrapper()
    {
        using var fixture = new TaskFixture(factoryDelay: true);
        using var start = new ManualResetEventSlim();
        var token = TestContext.Current.CancellationToken;
        var calls = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            start.Wait(token);
            return fixture.Action.SetEnabled(fixture.Tasker, true);
        }, token)).ToArray();
        start.Set();
        Assert.All(await Task.WhenAll(calls), Assert.True);
        Assert.Equal(1, fixture.Created);
        Assert.True(fixture.Action.SetEnabled(fixture.Tasker, false));
        Assert.Same(fixture.Original, fixture.Tasker.Controller);
        Assert.Equal(1, fixture.Raw.DisposeCount);
        Assert.Equal(0, fixture.Before.DisposeCount);
    }

    [Fact]
    public void RelinkingSameControllerKeepsAdaptationUntilExplicitlyDisabled()
    {
        using var fixture = new TaskFixture();
        Assert.Equal(MaaJobStatus.Succeeded, fixture.Run(true));
        var wrapped = fixture.Tasker.Controller;
        Assert.Equal(MaaJobStatus.Succeeded, wrapped.LinkStart().Wait());
        Assert.Same(wrapped, fixture.Tasker.Controller);
        Assert.Equal(MaaJobStatus.Succeeded, fixture.Tasker.AppendTask("StartVerify").Wait());
        Assert.Equal(MaaJobStatus.Succeeded, fixture.Run(false));
        Assert.Same(fixture.Original, fixture.Tasker.Controller);
        Assert.Equal(MaaJobStatus.Succeeded, fixture.Run(true)); // The No override must not persist.
        Assert.Equal(2, fixture.Created);
    }

    [Fact]
    public void ShippedInterfaceImportsDesktopOnlyOptionalTaskFirst()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "shipped");
        var settings = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "interface.json")), documentOptions: settings)!;
        var import = manifest["import"]![0]!.GetValue<string>();
        Assert.Equal("tasks/黑边适配.json", import);
        var task = JsonNode.Parse(File.ReadAllText(Path.Combine(root, import)))!["task"]![0]!;
        Assert.False(task["default_check"]!.GetValue<bool>());
        Assert.Equal("桌面端", Assert.Single(task["controller"]!.AsArray())!.GetValue<string>());
    }

    private sealed class VerifyFrame : IMaaCustomRecognition
    {
        public string Name { get; set; } = "VerifyFrame";
        public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
        {
            return args.Image.Width == 1280 && args.Image.Height == 720
                && results.Box.TrySetValues(300, 200, 1, 1)
                && results.Detail.TrySetValue("{}");
        }
    }

    private sealed class TaskFixture : IDisposable
    {
        public readonly FakeController Before = new();
        public readonly FakeController Raw = new();
        public readonly MaaCustomController Original;
        public readonly MaaTasker Tasker;
        public readonly MaaKesLetterbox Action;
        public int Created;
        private readonly JsonNode _definition;
        private readonly string _entry;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "MaaKesLetterboxTests", Guid.NewGuid().ToString("N"));

        public TaskFixture(bool factoryDelay = false)
        {
            var shipped = Path.Combine(AppContext.BaseDirectory, "shipped");
            _definition = JsonNode.Parse(File.ReadAllText(Path.Combine(shipped, "tasks", "黑边适配.json")))!;
            _entry = _definition["task"]![0]!["entry"]!.GetValue<string>();
            Directory.CreateDirectory(Path.Combine(_directory, "pipeline"));
            File.Copy(Path.Combine(shipped, "resource", "pipeline", "黑边适配.json"), Path.Combine(_directory, "pipeline", "黑边适配.json"));
            File.WriteAllText(Path.Combine(_directory, "pipeline", "test.json"), """
                {
                  "StartVerify": {"next": ["Verify"], "timeout": 1000},
                  "Verify": {"recognition": "Custom", "custom_recognition": "VerifyFrame", "action": "Click", "target": [300,200,1,1]}
                }
                """);
            var resource = new MaaResource(_directory);
            Action = new MaaKesLetterbox(_ =>
            {
                Interlocked.Increment(ref Created);
                if (factoryDelay) Thread.Sleep(40); // Widen concurrent callers' ownership race.
                return new LetterboxController(new MaaCustomController(Raw), () => (Raw.Width, Raw.Height));
            });
            Assert.True(resource.Register(Action));
            Assert.True(resource.Register(new VerifyFrame()));
            Original = new MaaCustomController(Before);
            Assert.True(Original.SetOption_ScreenshotTargetShortSide(720));
            Tasker = new MaaTasker(Original, resource, DisposeOptions.Controller | DisposeOptions.Resource);
        }

        public MaaJobStatus Run(bool enabled)
        {
            var optionName = _definition["task"]![0]!["option"]![0]!.GetValue<string>();
            var option = _definition["option"]![optionName]!;
            var selected = enabled ? option["default_case"]!.GetValue<string>() : "No";
            var choice = option["cases"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == selected)!;
            return Tasker.AppendTask(_entry, choice["pipeline_override"]?.ToJsonString() ?? "{}").Wait();
        }

        public void Dispose()
        {
            Tasker.Dispose();
            // Only this fixture's freshly created directory can be removed.
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MaaKesLetterboxTests")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(_directory);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) Directory.Delete(target, true);
        }
    }
}
