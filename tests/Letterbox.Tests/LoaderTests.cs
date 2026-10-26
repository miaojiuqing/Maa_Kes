using System.Reflection;
using System.Reflection.Metadata;
using MaaFramework.Binding.Custom;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace MaaKes.Letterbox.Tests;

public class LoaderTests
{
    [Fact]
    public unsafe void CompilesAndInstantiatesAsStandaloneCustomFile()
    {
        // Match MFAAvalonia's per-file compiler: loaded assembly metadata, no
        // project references or implicit usings, and unsafe code enabled.
        _ = typeof(System.Text.Json.JsonDocument).Assembly;
        _ = typeof(MaaFramework.Binding.MaaTasker).Assembly;
        var references = new List<MetadataReference>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic && assembly.TryGetRawMetadata(out var blob, out var length))
                references.Add(AssemblyMetadata.Create(ModuleMetadata.CreateFromMetadata((nint)blob, length)).GetReference());
        var compilation = CSharpCompilation.Create("DynamicLetterboxTest")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CustomSource.cs.txt")), cancellationToken: TestContext.Current.CancellationToken))
            .AddReferences(references);
        using var output = new MemoryStream();
        var result = compilation.Emit(output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var loaded = Assembly.Load(output.ToArray());
        var type = Assert.Single(loaded.GetTypes(), t => typeof(IMaaCustomAction).IsAssignableFrom(t));
        var action = Assert.IsAssignableFrom<IMaaCustomAction>(Activator.CreateInstance(type));
        Assert.Equal("MaaKesLetterbox", action.Name);
    }
}
