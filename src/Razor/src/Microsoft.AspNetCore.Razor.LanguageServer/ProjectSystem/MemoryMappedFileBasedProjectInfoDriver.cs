// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.LanguageServer.EndpointContracts;
using Microsoft.AspNetCore.Razor.ProjectSystem;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json;
using static Microsoft.AspNetCore.Razor.LanguageServer.ProjectSystem.MemoryMappedFileBasedProjectInfoDriver;

namespace Microsoft.AspNetCore.Razor.LanguageServer.ProjectSystem;

[RazorLanguageServerEndpoint("razor/projectInfoUpdated")]
internal class MemoryMappedFileBasedProjectInfoDriver : AbstractRazorProjectInfoDriver, IRazorNotificationHandler<ProjectInfoUpdatedParams>
{
    public MemoryMappedFileBasedProjectInfoDriver(ILoggerFactory loggerFactory) : base(loggerFactory, TimeSpan.FromMilliseconds(10))
    {
    }

    public bool MutatesSolutionState => true;

    public async Task HandleNotificationAsync(ProjectInfoUpdatedParams request, RazorRequestContext requestContext, CancellationToken cancellationToken)
    { 
        using var memoryMappedFile = MemoryMappedFile.CreateNew(request.FileName.AssumeNotNull(), 100, MemoryMappedFileAccess.Read);
        using var stream = memoryMappedFile.CreateViewStream();
        var projectInfos = await RazorProjectInfo.DeserializeMultipleFromAsync(stream, cancellationToken).ConfigureAwait(false);

        foreach (var projectInfo in projectInfos)
        {
            EnqueueUpdate(projectInfo);
        }
    }

    protected override Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    [DataContract]
    public class ProjectInfoUpdatedParams
    {
        [JsonPropertyName("fileName")]
        [JsonProperty("fileName")]
        public string? FileName { get; set; }
    }
}
