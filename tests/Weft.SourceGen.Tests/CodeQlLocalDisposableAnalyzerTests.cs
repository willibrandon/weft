using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Weft.SourceGen.Tests;

/// <summary>
/// Verifies disposable construction is protected before fallible work and ownership transfer.
/// </summary>
/// <param name="testContext">The running test's cooperative cancellation context.</param>
[TestClass]
public sealed class CodeQlLocalDisposableAnalyzerTests(TestContext testContext)
{
    /// <summary>
    /// Rejects factory-created resources transferred after an earlier owner's fallible cleanup.
    /// </summary>
    /// <param name="factory">The resource acquisition expression.</param>
    [TestMethod]
    [DataRow("File.OpenRead(path)")]
    [DataRow("choose ? File.OpenRead(path) : File.OpenRead(alternate)")]
    [DataRow("(File.OpenRead(path))")]
    public async Task ReportsFactoryResultTransferAfterFallibleCleanup(string factory)
    {
        string source = $$"""
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, string alternate, bool choose)
                {
                    FileStream current = null;
                    try
                    {
                        current = File.OpenRead(path);
                        for (int index = 0; index < 2; index++)
                        {
                            FileStream child = {{factory}};
                            current.Dispose();
                            current = child;
                        }
                    }
                    finally { current?.Dispose(); }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source).ConfigureAwait(false);
        AssertReportsLocal(diagnostics, "child");
    }

    /// <summary>
    /// Preserves transfers with no intervening failure and transfers protected by unconditional cleanup.
    /// </summary>
    /// <param name="transfer">The protected or immediate local ownership transfer.</param>
    [TestMethod]
    [DataRow("current = child;")]
    [DataRow("try { current.Dispose(); current = child; } catch { child.Dispose(); throw; }")]
    [DataRow("try { current.Dispose(); current = child; } catch { using (child) { } throw; }")]
    public async Task AcceptsProtectedFactoryResultTransfer(string transfer)
    {
        string source = $$"""
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path)
                {
                    FileStream current = null;
                    try
                    {
                        FileStream child = File.OpenRead(path);
                        {{transfer}}
                    }
                    finally { current?.Dispose(); }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source).ConfigureAwait(false);
        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Accepts factory-created arrays whose type has no containing assembly or disposable contract.
    /// </summary>
    [TestMethod]
    public async Task AcceptsArrayFactoryWithoutContainingAssembly()
    {
        const string Source = """
            using System;
            internal static class Reader
            {
                internal static int Read()
                {
                    int[] values = Array.Empty<int>();
                    return values.Length;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);
        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Rejects a factory-owned stream whose cleanup starts after another throwing constructor.
    /// </summary>
    [TestMethod]
    public async Task ReportsFactoryBeforeCleanupRegion()
    {
        const string Source = """
            using System.IO;
            using System.Diagnostics;
            internal static class Reader
            {
                internal static FileStream Read(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    var start = new ProcessStartInfo();
                    try { start.FileName = path; return stream; }
                    catch { stream.Dispose(); throw; }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);
        AssertReportsLocal(diagnostics, "stream");
    }

    /// <summary>
    /// Verifies a second pipe constructor cannot precede the first pipe's cleanup region.
    /// </summary>
    [TestMethod]
    public async Task ReportsConstructorBeforeCleanupRegion()
    {
        const string Source = """
            using System.IO.Pipes;
            using System.Threading.Tasks;
            internal static class Pipes
            {
                internal static async Task<(NamedPipeServerStream, NamedPipeClientStream)> CreateAsync(string name)
                {
                    var reader = new NamedPipeServerStream(name, PipeDirection.In);
                    var writer = new NamedPipeClientStream(".", name, PipeDirection.Out);
                    try
                    {
                        Task connection = reader.WaitForConnectionAsync();
                        await writer.ConnectAsync();
                        await connection;
                        return (reader, writer);
                    }
                    catch
                    {
                        await writer.DisposeAsync();
                        await reader.DisposeAsync();
                        throw;
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "reader");
    }

    /// <summary>
    /// Verifies cleanup also protects earlier variables within a multiple-initializer declaration.
    /// </summary>
    [TestMethod]
    public async Task ReportsSecondInitializerBeforeCleanup()
    {
        const string Source = """
            using System.IO;
            internal static class Streams
            {
                internal static void Open()
                {
                    MemoryStream first = new(), second = new();
                    try { first.WriteByte(1); second.WriteByte(2); }
                    finally { second.Dispose(); first.Dispose(); }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "first");
    }

    /// <summary>
    /// Verifies a tuple return does not protect resources from earlier asynchronous failures.
    /// </summary>
    [TestMethod]
    public async Task ReportsTupleFactoryWithoutFailureCleanup()
    {
        const string Source = """
            using System.IO;
            using System.Threading.Tasks;
            internal static class Streams
            {
                internal static async Task<(MemoryStream, int)> CreateAsync()
                {
                    var stream = new MemoryStream();
                    await Task.Yield();
                    return (stream, 1);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "stream");
    }

    /// <summary>
    /// Verifies typed and filtered catches do not protect a tuple factory against every failure.
    /// </summary>
    /// <param name="handler">The exception handler with incomplete coverage.</param>
    [TestMethod]
    [DataRow("catch (IOException)")]
    [DataRow("catch (Exception) when (handle)")]
    public async Task ReportsFactoryWithIncompleteFailureCleanup(string handler)
    {
        string source = $$"""
            using System;
            using System.IO;
            using System.Threading.Tasks;
            internal static class Streams
            {
                internal static async Task<(MemoryStream, int)> CreateAsync(bool handle)
                {
                    var stream = new MemoryStream();
                    try { await Task.Yield(); return (stream, 1); }
                    {{handler}} { stream.Dispose(); throw; }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "stream");
    }

    /// <summary>
    /// Verifies asynchronous-only disposal is recognized without requiring IDisposable.
    /// </summary>
    [TestMethod]
    public async Task ReportsUnprotectedAsyncDisposableOnlyResource()
    {
        const string Source = """
            using System;
            using System.Threading.Tasks;
            internal sealed class AsyncResource : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;

                internal static async Task<(AsyncResource, int)> CreateAsync()
                {
                    var resource = new AsyncResource();
                    await Task.Yield();
                    return (resource, 1);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "resource");
    }

    /// <summary>
    /// Verifies non-disposable tuple elements need no cleanup across asynchronous operations.
    /// </summary>
    [TestMethod]
    public async Task AcceptsNonDisposableTupleLocal()
    {
        const string Source = """
            using System.Text;
            using System.Threading.Tasks;
            internal static class TextFactory
            {
                internal static async Task<(StringBuilder, int)> CreateAsync()
                {
                    var text = new StringBuilder();
                    await Task.Yield();
                    return (text, 1);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies nested ownership protection permits a legitimate asynchronous tuple factory.
    /// </summary>
    [TestMethod]
    public async Task AcceptsExceptionSafeAsyncFactory()
    {
        const string Source = """
            using System;
            using System.IO.Pipes;
            using System.Threading.Tasks;
            internal static class Pipes
            {
                internal static async Task<(NamedPipeServerStream, NamedPipeClientStream)> CreateAsync(string name)
                {
                    var reader = new NamedPipeServerStream(name, PipeDirection.In);
                    try
                    {
                        var writer = new NamedPipeClientStream(".", name, PipeDirection.Out);
                        try
                        {
                            Task connection = reader.WaitForConnectionAsync();
                            await writer.ConnectAsync();
                            await connection;
                            return (reader, writer);
                        }
                        catch (Exception) { await writer.DisposeAsync(); throw; }
                    }
                    catch { await reader.DisposeAsync(); throw; }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies structured using ownership protects each pipe before constructing the next.
    /// </summary>
    [TestMethod]
    public async Task AcceptsScopedConstruction()
    {
        const string Source = """
            using System.IO.Pipes;
            using System.Threading.Tasks;
            internal static class Pipes
            {
                internal static async Task ConnectAsync(string name)
                {
                    await using var reader = new NamedPipeServerStream(name, PipeDirection.In);
                    await using var writer = new NamedPipeClientStream(".", name, PipeDirection.Out);
                    Task connection = reader.WaitForConnectionAsync();
                    await writer.ConnectAsync();
                    await connection;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies immediate direct and tuple returns do not create an exception window after allocation.
    /// </summary>
    [TestMethod]
    public async Task AcceptsImmediateOwnershipTransfer()
    {
        const string Source = """
            using System.IO;
            internal static class Streams
            {
                internal static MemoryStream Create()
                {
                    var stream = new MemoryStream();
                    return stream;
                }
                internal static (MemoryStream, int) CreatePair()
                {
                    var stream = new MemoryStream();
                    return (stream, 1);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies conditional validation cleanup does not protect a handle factory's initialization.
    /// </summary>
    [TestMethod]
    public async Task ReportsSafeHandleInitializationBeforeCleanup()
    {
        const string Source = """
            using System;
            using Microsoft.Win32.SafeHandles;
            internal sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                private NativeHandle() : base(true) { }
                protected override bool ReleaseHandle() => true;

                internal static NativeHandle Open(uint port, int result)
                {
                    var handle = new NativeHandle();
                    handle.SetHandle(checked((nint)port));
                    if (result != 0 || handle.IsInvalid)
                    {
                        handle.Dispose();
                        throw new InvalidOperationException();
                    }

                    return handle;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "handle");
    }

    /// <summary>
    /// Verifies direct handle returns require exception cleanup after fallible initialization.
    /// </summary>
    /// <param name="handler">The absent or incomplete initialization cleanup.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("catch (IOException) { handle.Dispose(); throw; }")]
    [DataRow("catch (Exception) when (cleanup) { handle.Dispose(); throw; }")]
    public async Task ReportsHandleFactoryWithUnprotectedInitialization(string handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        string initialization = handler.Length == 0
            ? "handle.SetHandle(port); return handle;"
            : $"try {{ handle.SetHandle(port); return handle; }} {handler}";
        string source = $$"""
            using System;
            using System.IO;
            using Microsoft.Win32.SafeHandles;
            internal sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                private NativeHandle() : base(true) { }
                protected override bool ReleaseHandle() => true;

                internal static NativeHandle Open(nint port, bool cleanup)
                {
                    var handle = new NativeHandle();
                    {{initialization}}
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "handle");
    }

    /// <summary>
    /// Verifies handle initialization and validation share unconditional exception cleanup.
    /// </summary>
    [TestMethod]
    public async Task AcceptsProtectedSafeHandleFactory()
    {
        const string Source = """
            using System;
            using Microsoft.Win32.SafeHandles;
            internal sealed class NativeHandle : SafeHandleZeroOrMinusOneIsInvalid
            {
                private NativeHandle() : base(true) { }
                protected override bool ReleaseHandle() => true;

                internal static NativeHandle Open(uint port, int result)
                {
                    var handle = new NativeHandle();
                    try
                    {
                        handle.SetHandle(checked((nint)port));
                        if (result != 0 || handle.IsInvalid)
                        {
                            throw new InvalidOperationException();
                        }

                        return handle;
                    }
                    catch
                    {
                        handle.Dispose();
                        throw;
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies merely declaring a deferred allocation does not execute it before the cleanup region.
    /// </summary>
    [TestMethod]
    public async Task AcceptsDeferredAllocationBeforeCleanup()
    {
        const string Source = """
            using System;
            using System.IO;
            internal static class Streams
            {
                internal static (MemoryStream, Func<MemoryStream>) Create()
                {
                    var stream = new MemoryStream();
                    Func<MemoryStream> factory = static () => new MemoryStream();
                    try { return (stream, factory); }
                    catch { stream.Dispose(); throw; }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies configured disposal aliases do not conceal a library resource's ownership.
    /// </summary>
    /// <param name="declaration">The constructed library resource declaration.</param>
    /// <param name="cleanupType">The explicit or inferred configured cleanup type.</param>
    [TestMethod]
    [DataRow("var stream = new FileStream(path, FileMode.CreateNew);", "ConfiguredAsyncDisposable")]
    [DataRow("FileStream stream = new(path, FileMode.CreateNew);", "var")]
    [DataRow("var stream = new MemoryStream();", "var")]
    public async Task ReportsLibraryResourceWithConfiguredDisposalAlias(string declaration, string cleanupType)
    {
        string source = $$"""
            using System.IO;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            internal static class Streams
            {
                internal static async Task WriteAsync(string path)
                {
                    {{declaration}}
                    await using {{cleanupType}} cleanup = stream.ConfigureAwait(false);
                    await stream.WriteAsync(new byte[] { 1 });
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "stream");
    }

    /// <summary>
    /// Verifies configured using blocks expose each library resource to the same ownership check as aliases.
    /// </summary>
    /// <param name="constructor">The stream constructor observed by remote analysis.</param>
    [TestMethod]
    [DataRow("new FileStream(path, FileMode.CreateNew)")]
    [DataRow("new StreamWriter(path)")]
    public async Task ReportsLibraryResourceWithConfiguredDisposalBlock(string constructor)
    {
        string source = $$"""
            using System.IO;
            using System.Threading.Tasks;
            internal static class Streams
            {
                internal static async Task WriteAsync(string path)
                {
                    var stream = {{constructor}};
                    await using (stream.ConfigureAwait(false))
                    {
                        await Task.Yield();
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source).ConfigureAwait(false);

        AssertReportsLocal(diagnostics, "stream");
    }

    /// <summary>
    /// Verifies a directly scoped library resource has visible exception-safe cleanup.
    /// </summary>
    [TestMethod]
    public async Task AcceptsDirectlyScopedLibraryResource()
    {
        const string Source = """
            using System.IO;
            using System.Threading.Tasks;
            internal static class Streams
            {
                internal static async Task WriteAsync(string path)
                {
                    using var stream = new FileStream(path, FileMode.CreateNew);
                    await stream.WriteAsync(new byte[] { 1 }).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies nested direct scopes protect both the backup stream and its asynchronously flushed writer.
    /// </summary>
    [TestMethod]
    public async Task AcceptsDirectlyScopedStreamAndWriter()
    {
        const string Source = """
            using System.IO;
            using System.Threading.Tasks;
            internal static class Streams
            {
                internal static async Task WriteAsync(string path)
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew))
                    using (var writer = new StreamWriter(stream))
                    {
                        await writer.WriteAsync("policy").ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies application-owned asynchronous disposal can remain in a configured scope.
    /// </summary>
    [TestMethod]
    public async Task AcceptsConfiguredApplicationResource()
    {
        const string Source = """
            using System;
            using System.Threading.Tasks;
            internal sealed class AsyncResource : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;

                internal static async Task UseAsync()
                {
                    var resource = new AsyncResource();
                    await using var cleanup = resource.ConfigureAwait(false);
                    await Task.Yield();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private static void AssertReportsLocal(ImmutableArray<Diagnostic> diagnostics, string name)
    {
        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{name}'", diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.StartsWith(name, diagnostic.Location.SourceTree?.GetText()
            .ToString(diagnostic.Location.SourceSpan));
    }

    /// <summary>
    /// Verifies a local declared as the disposable interface itself is reported when a collection acquires it unscoped.
    /// </summary>
    [TestMethod]
    public async Task ReportsInterfaceTypedLocalAcquiredWithoutScope()
    {
        const string Source = """
            using System;
            using System.IO;
            namespace Weft.Debugger
            {
                internal sealed class DisposableCollection<T> where T : IDisposable
                {
                    public T Acquire(Func<T> create) => create();
                }
            }

            namespace Sample
            {
                internal static class Holder
                {
                    internal static void Keep(Weft.Debugger.DisposableCollection<IDisposable> owner)
                    {
                        IDisposable resource = new MemoryStream();
                        owner.Acquire(() => resource);
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsNotEmpty(diagnostics);
        Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Id == CodeQlLocalDisposableAnalyzer.DiagnosticId));
    }

    /// <summary>
    /// Verifies a disposable that is neither disposed nor handed off before the block ends is reported.
    /// </summary>
    [TestMethod]
    public async Task ReportsLocalStillOwnedAtBlockExit()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static int Read(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    stream.ReadByte();
                    return 0;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a disposable passed to another call may have changed hands and is not reported at block exit.
    /// </summary>
    [TestMethod]
    public async Task AcceptsLocalPassedOn()
    {
        const string Source = """
            using System.Collections.Generic;
            using System.IO;
            internal static class Reader
            {
                internal static void Keep(string path, List<FileStream> owner)
                {
                    FileStream stream = File.OpenRead(path);
                    owner.Add(stream);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a conditional disposal call counts as cleanup.
    /// </summary>
    [TestMethod]
    public async Task AcceptsConditionalDisposalCall()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    stream?.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies catch-only cleanup does not end ownership, so later fallible work before a return is reported.
    /// </summary>
    [TestMethod]
    public async Task ReportsFallibleWorkAfterCatchOnlyCleanup()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static FileStream Open(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    try
                    {
                        Work();
                    }
                    catch
                    {
                        stream.Dispose();
                        throw;
                    }

                    Work();
                    return stream;
                }

                private static void Work()
                {
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies catch-only cleanup followed directly by the ownership return is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsCatchOnlyCleanupFollowedByReturn()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static FileStream Open(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    try
                    {
                        Work();
                    }
                    catch
                    {
                        stream.Dispose();
                        throw;
                    }

                    return stream;
                }

                private static void Work()
                {
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies catch-only cleanup with no disposal on the normal path is reported as a leak.
    /// </summary>
    [TestMethod]
    public async Task ReportsCatchOnlyCleanupWithoutNormalDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    try
                    {
                        stream.ReadByte();
                    }
                    catch
                    {
                        stream.Dispose();
                        throw;
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    private async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        string sourcePath = Path.Join(Path.GetTempPath(), $"weft-disposable-local-{Guid.NewGuid():N}.cs");
        try
        {
            await File.WriteAllTextAsync(sourcePath, source, testContext.CancellationToken).ConfigureAwait(false);
            SyntaxTree syntaxTree;
            using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var text = SourceText.From(stream, Encoding.UTF8);
                syntaxTree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp14),
                    path: sourcePath, cancellationToken: testContext.CancellationToken);
            }

            string trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                ?? throw new InvalidOperationException("The runtime did not expose trusted platform assemblies.");
            IEnumerable<MetadataReference> references = trustedAssemblies.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("AnalyzerInput", [syntaxTree], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.IsEmpty(compilation.GetDiagnostics(testContext.CancellationToken).Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error));

            var analysisOptions = new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null,
                concurrentAnalysis: true, logAnalyzerExecutionTime: true, reportSuppressedDiagnostics: false);
            return await compilation.WithAnalyzers([new CodeQlLocalDisposableAnalyzer()], analysisOptions)
                .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }
    /// <summary>
    /// Verifies a handler that disposes only under a condition does not count as exception cleanup.
    /// </summary>
    [TestMethod]
    public async Task ReportsConditionalCleanupInHandler()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool cleanup)
                {
                    FileStream current = File.OpenRead(path);
                    FileStream child = File.OpenRead(path);
                    try
                    {
                        current.Dispose();
                    }
                    catch
                    {
                        if (cleanup)
                        {
                            child.Dispose();
                        }

                        throw;
                    }

                    current = child;
                    current.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        // The child leaks when the handler skips it, and the first stream leaks if opening the child throws.
        Assert.HasCount(2, diagnostics);
        Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Id == CodeQlLocalDisposableAnalyzer.DiagnosticId));
    }

    /// <summary>
    /// Verifies a disposable returned directly after fallible work is reported for any disposable type.
    /// </summary>
    [TestMethod]
    public async Task ReportsDirectReturnAfterFallibleWork()
    {
        const string Source = """
            using System.IO;
            using System.Threading.Tasks;
            internal static class Reader
            {
                internal static async Task<FileStream> OpenAsync(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    await Task.Delay(1).ConfigureAwait(false);
                    return stream;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a try whose first handler swallows without cleanup is not protected by a later handler.
    /// </summary>
    [TestMethod]
    public async Task ReportsHandlerThatInterceptsWithoutCleanup()
    {
        const string Source = """
            using System;
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path)
                {
                    FileStream current = File.OpenRead(path);
                    FileStream child = File.OpenRead(path);
                    try
                    {
                        current.Dispose();
                    }
                    catch (IOException)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        child.Dispose();
                        throw;
                    }

                    current = child;
                    current.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsNotEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a local declared as the disposable interface itself is analyzed like any other owner.
    /// </summary>
    [TestMethod]
    public async Task ReportsInterfaceTypedLocalTransferredAfterFallibleWork()
    {
        const string Source = """
            using System;
            using System.IO;
            internal static class Factory
            {
                internal static IDisposable Open(string path)
                {
                    IDisposable resource = File.OpenRead(path);
                    Console.WriteLine(path);
                    return resource;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a return under a condition does not end the scan for the paths that continue.
    /// </summary>
    [TestMethod]
    public async Task ReportsFallibleWorkAfterConditionalReturn()
    {
        const string Source = """
            using System.IO;
            using System.Threading.Tasks;
            internal static class Reader
            {
                internal static async Task<FileStream> OpenAsync(string path, bool cached)
                {
                    FileStream stream = File.OpenRead(path);
                    if (cached)
                    {
                        return stream;
                    }

                    await Task.Delay(1).ConfigureAwait(false);
                    return stream;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.ContainsSingle(diagnostics);
    }

    /// <summary>
    /// Verifies a conditional return followed by a safe disposal on the other path is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsConditionalReturnFollowedBySafeDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static FileStream? Open(string path, bool keep)
                {
                    FileStream stream = File.OpenRead(path);
                    if (keep)
                    {
                        return stream;
                    }

                    stream.Dispose();
                    return null;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies fallible work before a direct disposal is reported because a failure skips the disposal.
    /// </summary>
    [TestMethod]
    public async Task ReportsFallibleWorkBeforeDirectDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Swap(FileStream current, string path)
                {
                    FileStream child = File.OpenRead(path);
                    current.Dispose();
                    child.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a local from an ordinary call is not treated as an owned resource at block exit.
    /// </summary>
    [TestMethod]
    public async Task AcceptsLookupResultLeftUndisposed()
    {
        const string Source = """
            using System.IO;
            internal sealed class Cache
            {
                private readonly FileStream _stream;

                internal Cache(FileStream stream)
                {
                    _stream = stream;
                }

                internal long Length()
                {
                    FileStream stream = Current();
                    return stream.Length;
                }

                private FileStream Current() => _stream;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a disposal that runs only under a condition does not end ownership on the other path.
    /// </summary>
    [TestMethod]
    public async Task ReportsDisposalOnlyUnderCondition()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool cleanup)
                {
                    FileStream stream = File.OpenRead(path);
                    if (cleanup)
                    {
                        stream.Dispose();
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a branch that hands the local off and one that disposes it together end ownership.
    /// </summary>
    [TestMethod]
    public async Task AcceptsHandoffOrDisposalOnEveryBranch()
    {
        const string Source = """
            using System.Collections.Generic;
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool keep, List<FileStream> owner)
                {
                    FileStream stream = File.OpenRead(path);
                    if (keep)
                    {
                        owner.Add(stream);
                    }
                    else
                    {
                        stream.Dispose();
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a handoff through an argument after fallible work is reported like a return.
    /// </summary>
    [TestMethod]
    public async Task ReportsArgumentHandoffAfterFallibleWork()
    {
        const string Source = """
            using System.Collections.Generic;
            using System.IO;
            internal static class Reader
            {
                internal static void Keep(string path, List<FileStream> owner)
                {
                    FileStream stream = File.OpenRead(path);
                    Prepare();
                    owner.Add(stream);
                }

                private static void Prepare()
                {
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a store that happens only under a condition leaves the local owned on the other path.
    /// </summary>
    [TestMethod]
    public async Task ReportsConditionalStoreLeavingOwnership()
    {
        const string Source = """
            using System.IO;
            internal sealed class Holder
            {
                private FileStream? _owner;

                internal void Open(string path, bool keep)
                {
                    FileStream stream = File.OpenRead(path);
                    if (keep)
                    {
                        _owner = stream;
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies disposing the local on an early exit is not counted as risk for the disposal that follows.
    /// </summary>
    [TestMethod]
    public async Task AcceptsEarlyDisposalBeforeFinalDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool fail)
                {
                    FileStream stream = File.OpenRead(path);
                    if (fail)
                    {
                        stream.Dispose();
                        return;
                    }

                    stream.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a resource assigned to a local after its declaration is tracked from that assignment.
    /// </summary>
    [TestMethod]
    public async Task ReportsResourceAssignedAfterDeclaration()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static int Read()
                {
                    MemoryStream stream;
                    stream = new MemoryStream();
                    return stream.ReadByte();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource assigned after its declaration and disposed afterwards is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsResourceAssignedAfterDeclarationAndDisposed()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    MemoryStream stream;
                    stream = new MemoryStream();
                    stream.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a local with a placeholder initializer is tracked from the later assignment that gives it a resource.
    /// </summary>
    [TestMethod]
    public async Task ReportsResourceAssignedAfterPlaceholderInitializer()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static int Read()
                {
                    MemoryStream? stream = null;
                    stream = new MemoryStream();
                    return stream.ReadByte();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies an exit before the disposal inside a branch keeps the branch from ending ownership.
    /// </summary>
    [TestMethod]
    public async Task ReportsExitBeforeDisposalInsideBranch()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool condition, bool skip)
                {
                    FileStream stream = File.OpenRead(path);
                    if (condition)
                    {
                        if (skip)
                        {
                            return;
                        }

                        stream.Dispose();
                    }
                    else
                    {
                        stream.Dispose();
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies an early return while the local is still owned is reported even when a later statement disposes it.
    /// </summary>
    [TestMethod]
    public async Task ReportsEarlyReturnWhileOwning()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool skip)
                {
                    FileStream stream = File.OpenRead(path);
                    if (skip)
                    {
                        return;
                    }

                    stream.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a break out of a switch section is not an exit, so a disposal after the switch still counts.
    /// </summary>
    [TestMethod]
    public async Task AcceptsSwitchBreakBeforeDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static int Read(string path, int mode)
                {
                    int count = 0;
                    FileStream stream = File.OpenRead(path);
                    switch (mode)
                    {
                        case 1:
                            count = 1;
                            break;
                        default:
                            break;
                    }

                    stream.Dispose();
                    return count;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies cleanup that runs fallible work before the disposal does not protect the local.
    /// </summary>
    [TestMethod]
    public async Task ReportsFallibleWorkBeforeCleanupDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path)
                {
                    FileStream stream = File.OpenRead(path);
                    try
                    {
                        stream.ReadByte();
                    }
                    finally
                    {
                        Prepare();
                        stream.Dispose();
                    }
                }

                private static void Prepare()
                {
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource assigned inside a branch is tracked from that assignment.
    /// </summary>
    [TestMethod]
    public async Task ReportsResourceAssignedInsideBranch()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(bool condition)
                {
                    MemoryStream? stream = null;
                    if (condition)
                    {
                        stream = new MemoryStream();
                        stream.ReadByte();
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource assigned inside a branch and disposed after the branch is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsResourceAssignedInsideBranchAndDisposedAfter()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(bool condition)
                {
                    MemoryStream? stream = null;
                    if (condition)
                    {
                        stream = new MemoryStream();
                    }

                    stream?.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a second resource assigned after the first was disposed starts its own tracking.
    /// </summary>
    [TestMethod]
    public async Task ReportsSecondResourceAfterDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static int Read()
                {
                    MemoryStream? stream = null;
                    stream = new MemoryStream();
                    stream.Dispose();
                    stream = new MemoryStream();
                    return stream.ReadByte();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a disposable declared directly in a switch section is analyzed.
    /// </summary>
    [TestMethod]
    public async Task ReportsLocalDeclaredInSwitchSection()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(int mode)
                {
                    switch (mode)
                    {
                        case 0:
                            var stream = new MemoryStream();
                            stream.ReadByte();
                            break;
                        default:
                            break;
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies fallible work that runs only after the disposal on every branch is not counted as risk.
    /// </summary>
    [TestMethod]
    public async Task AcceptsFallibleWorkAfterDisposalOnEveryBranch()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool condition)
                {
                    FileStream stream = File.OpenRead(path);
                    if (condition)
                    {
                        stream.Dispose();
                        Prepare();
                    }
                    else
                    {
                        stream.Dispose();
                    }
                }

                private static void Prepare()
                {
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies fallible work before the disposal on one branch is still reported.
    /// </summary>
    [TestMethod]
    public async Task ReportsFallibleWorkBeforeDisposalOnOneBranch()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(string path, bool condition)
                {
                    FileStream stream = File.OpenRead(path);
                    if (condition)
                    {
                        Prepare();
                        stream.Dispose();
                    }
                    else
                    {
                        stream.Dispose();
                    }
                }

                private static void Prepare()
                {
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource created by an awaited factory is tracked from its declaration.
    /// </summary>
    [TestMethod]
    public async Task ReportsAwaitedResourceLeftUndisposed()
    {
        const string Source = """
            using System;
            using System.Threading.Tasks;
            internal sealed class Resource : IDisposable
            {
                public static Task<Resource> CreateAsync() => Task.FromResult(new Resource());

                public int Read() => 0;

                public void Dispose()
                {
                }
            }

            internal static class Reader
            {
                internal static async Task<int> ReadAsync()
                {
                    Resource resource = await Resource.CreateAsync().ConfigureAwait(false);
                    return resource.Read();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource created by an awaited factory is tracked from a later assignment.
    /// </summary>
    [TestMethod]
    public async Task ReportsAwaitedResourceAssignedLater()
    {
        const string Source = """
            using System;
            using System.Threading.Tasks;
            internal sealed class Resource : IDisposable
            {
                public static Task<Resource> CreateAsync() => Task.FromResult(new Resource());

                public int Read() => 0;

                public void Dispose()
                {
                }
            }

            internal static class Reader
            {
                internal static async Task<int> ReadAsync()
                {
                    Resource? resource = null;
                    resource = await Resource.CreateAsync().ConfigureAwait(false);
                    return resource.Read();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource declared by a for initializer is reported when the loop can be left without disposing it.
    /// </summary>
    [TestMethod]
    public async Task ReportsLoopDeclaredResourceLeftUndisposed()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    for (var stream = new MemoryStream(); stream.Length < 8; )
                    {
                        stream.WriteByte(1);
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource declared by a for initializer and disposed before the loop is left is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsLoopDeclaredResourceDisposedInBody()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    for (var stream = new MemoryStream(); ; )
                    {
                        stream.Dispose();
                        break;
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a for initializer resource with a braceless body that never disposes it is reported.
    /// </summary>
    [TestMethod]
    public async Task ReportsLoopDeclaredResourceWithEmbeddedBody()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    for (var stream = new MemoryStream(); ; )
                        stream.WriteByte(1);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a loop condition that can be false leaks a for initializer resource even when the body disposes it.
    /// </summary>
    [TestMethod]
    public async Task ReportsLoopDeclaredResourceWhenConditionCanSkipBody()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(bool condition)
                {
                    for (var stream = new MemoryStream(); condition; )
                    {
                        stream.Dispose();
                        break;
                    }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a break out of a loop leads to cleanup after the loop when the local outlives it.
    /// </summary>
    [TestMethod]
    public async Task AcceptsLoopBreakFollowedByDisposalAfterLoop()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(bool condition)
                {
                    MemoryStream? stream = null;
                    while (condition)
                    {
                        stream = new MemoryStream();
                        break;
                    }

                    stream?.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a resource assigned each iteration without disposal is reported, since the next iteration overwrites it.
    /// </summary>
    [TestMethod]
    public async Task ReportsResourceOverwrittenByNextIteration()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read(bool condition)
                {
                    MemoryStream? stream = null;
                    while (condition)
                    {
                        stream = new MemoryStream();
                    }

                    stream?.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a parenthesized return still hands the resource to the caller.
    /// </summary>
    [TestMethod]
    public async Task AcceptsParenthesizedReturn()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static MemoryStream Open()
                {
                    var stream = new MemoryStream();
                    return (stream);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies a second resource assigned after the initializer's resource was disposed is tracked on its own.
    /// </summary>
    [TestMethod]
    public async Task ReportsSecondResourceAfterInitializerDisposal()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static int Read()
                {
                    var stream = new MemoryStream();
                    stream.Dispose();
                    stream = new MemoryStream();
                    return stream.ReadByte();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource copied into another local that never disposes it is reported.
    /// </summary>
    [TestMethod]
    public async Task ReportsResourceLeftInLocalAlias()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static int Read()
                {
                    var stream = new MemoryStream();
                    Stream alias = stream;
                    return alias.ReadByte();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource disposed through a local alias is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsResourceDisposedThroughLocalAlias()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    var stream = new MemoryStream();
                    Stream alias = stream;
                    alias.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies overwriting a local that still owns its resource is reported as dropping that resource.
    /// </summary>
    [TestMethod]
    public async Task ReportsReassignmentWhileOwned()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    var stream = new MemoryStream();
                    stream = new MemoryStream();
                    stream.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Diagnostic diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual(CodeQlLocalDisposableAnalyzer.DiagnosticId, diagnostic.Id);
    }

    /// <summary>
    /// Verifies a resource copied into an alias and then disposed through the original is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsResourceDisposedThroughOriginalAfterAlias()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    var stream = new MemoryStream();
                    Stream alias = stream;
                    stream.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    /// <summary>
    /// Verifies overwriting an alias while the original still references and disposes the resource is accepted.
    /// </summary>
    [TestMethod]
    public async Task AcceptsAliasDroppedWhileOriginalDisposes()
    {
        const string Source = """
            using System.IO;
            internal static class Reader
            {
                internal static void Read()
                {
                    var stream = new MemoryStream();
                    Stream? alias = stream;
                    alias = null;
                    stream.Dispose();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAsync(Source).ConfigureAwait(false);

        Assert.IsEmpty(diagnostics);
    }

    private Task<ImmutableArray<Diagnostic>> RunAsync(string source) => CodeQlFileCompilation.AnalyzeAsync(
        source, new CodeQlLocalDisposableAnalyzer(), testContext.CancellationToken);
}
