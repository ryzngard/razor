// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

#if NET
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.ProjectSystem;
using Microsoft.AspNetCore.Razor.Serialization;
using Microsoft.AspNetCore.Razor.Utilities;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
#endif

namespace Microsoft.AspNetCore.Razor.Microbenchmarks.Serialization;

public class RazorProjectInfoSerializationBenchmark_NetOnly
{
    // These benchmarks are only intended to run in NET5+ as the serialization/deserialization logic already has that requirement
#if NET

    [ParamsAllValues]
    public ResourceSet ResourceSet { get; set; }

    [Params(100, 1000, 10000000)]
    public int FileCount { get; set; }

    [Params(1, 10)]
    public int SerializationCount { get; set; }

    private ImmutableArray<TagHelperDescriptor> TagHelpers
        => ResourceSet switch
        {
            ResourceSet.Telerik => CommonResources.TelerikTagHelpers,
            _ => CommonResources.LegacyTagHelpers
        };

    [AllowNull]
    private RazorProjectInfo _projectInfo;

    [AllowNull]
    private Pipe _pipe;

    [GlobalSetup(Targets = [nameof(RoundTrip_Serialization)])]
    public void GlobalSetup()
    {
        _pipe = new();
        _projectInfo = new RazorProjectInfo(
            new ProjectKey(@"C:\project\output\obj"),
            @"C:\project\project.csproj",
            FallbackRazorConfiguration.Latest,
            "TestNamespace",
            "Project",
            ProjectWorkspaceState.Create(TagHelpers, CodeAnalysis.CSharp.LanguageVersion.Latest),
            GetDocuments(@"C:\project", FileCount));
    }

    private ImmutableArray<DocumentSnapshotHandle> GetDocuments(string baseDurectory, int fileCount)
    {
        var documentShapshots = new DocumentSnapshotHandle[fileCount];

        for (var i = 0; i < fileCount; i++)
        {
            var documentFullPath = Path.Combine(baseDurectory, "Pages", $"Page{i}.razor");
            var outputPath = Path.Combine(baseDurectory, "out", $"Page{i}.razor");
            documentShapshots[i] = new DocumentSnapshotHandle(documentFullPath, outputPath, FileKinds.Component);
        }

        return documentShapshots.ToImmutableArray();
    }

    [Benchmark(Description = "Round Trip Serialization")]
    public async Task RoundTrip_Serialization()
    {
        var reader = _pipe.Reader;
        var writer = _pipe.Writer;

        Debug.WriteLine($"Starting test with iterations: {SerializationCount}");

        var writerTask = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < SerializationCount; i++)
                {
                    Debug.WriteLine($"Writing iteration {i}");
                    await writer.AsStream(leaveOpen: true).WriteProjectInfoAsync(_projectInfo, CancellationToken.None);
                    await writer.FlushAsync();
                }

                await writer.CompleteAsync();
            }
            catch (InvalidOperationException)
            {
                // just in case we try to read too much, we can ignore this.
            }
        });

        var readerTask = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < SerializationCount; i++)
                {
                    using var readStream = reader.AsStream(leaveOpen: true);
                    Debug.WriteLine($"Reading iteration {i}");
                    var action = readStream.ReadProjectInfoAction();

                    Debug.WriteLine($"Deserializing project {i}");
                    _ = await readStream.ReadProjectInfoAsync(CancellationToken.None);
                }

                await reader.CompleteAsync();
            }
            catch (InvalidOperationException)
            {
                // just in case we try to read too much, we can ignore this.
            }
        });

        await Task.WhenAll(writerTask, readerTask);

        Debug.WriteLine("Completed test");
    }
#else
    [Benchmark(Description = "Round Trip Serialization")]
    public Task RoundTrip_Serialization() => Task.CompletedTask;
    #endif

}
