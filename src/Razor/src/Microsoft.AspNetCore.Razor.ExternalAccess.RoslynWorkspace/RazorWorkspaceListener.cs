﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.IO.MemoryMappedFiles;
using Microsoft.AspNetCore.Razor.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Razor.ExternalAccess.RoslynWorkspace;

public partial class RazorWorkspaceListener : IDisposable
{
    private static readonly TimeSpan s_debounceTime = TimeSpan.FromMilliseconds(500);

    private readonly ILogger _logger;

    private string? _projectInfoFileName;
    private Workspace? _workspace;

    // Use an immutable dictionary for ImmutableInterlocked operations. The value isn't checked, just
    // the existance of the key so work is only done for projects with dynamic files.
    private ImmutableDictionary<ProjectId, bool> _projectsWithDynamicFile = ImmutableDictionary<ProjectId, bool>.Empty;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly AsyncBatchingWorkQueue<ProjectId> _workQueue;
    private readonly Func<string, CancellationToken, Task> _notifyMemoryMappedFileNameAsync;
    private MemoryMappedFile? _mappedFile;

    public RazorWorkspaceListener(ILoggerFactory loggerFactory, Func<string, CancellationToken, Task> notifyMemoryMappedFileNameAsync)
    {
        _logger = loggerFactory.CreateLogger(nameof(RazorWorkspaceListener));
        _workQueue = new(TimeSpan.FromMilliseconds(500), UpdateCurrentProjectsAsync, EqualityComparer<ProjectId>.Default, _disposeTokenSource.Token);
        _notifyMemoryMappedFileNameAsync = notifyMemoryMappedFileNameAsync;
    }

    public void Dispose()
    {
        if (_workspace is not null)
        {
            _workspace.WorkspaceChanged -= Workspace_WorkspaceChanged;
        }

        if (_disposeTokenSource.IsCancellationRequested)
        {
            return;
        }

        _disposeTokenSource.Cancel();
        _disposeTokenSource.Dispose();
        _mappedFile?.Dispose();
        _mappedFile = null;
    }

    public void EnsureInitialized(Workspace workspace, string projectInfoFileName)
    {
        // Make sure we don't hook up the event handler multiple times
        if (_projectInfoFileName is not null)
        {
            return;
        }

        _projectInfoFileName = projectInfoFileName;
        _workspace = workspace;
        _workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
    }

    public void NotifyDynamicFile(ProjectId projectId)
    {
        // Since there is no "un-notify" API to indicate that callers no longer care about a project, it's entirely
        // possible that by the time we get notified, a project might have been removed from the workspace. Whilst
        // that wouldn't cause any issues we may as well avoid creating a task scheduler.
        if (_workspace is null || !_workspace.CurrentSolution.ContainsProject(projectId))
        {
            return;
        }

        ImmutableInterlocked.GetOrAdd(ref _projectsWithDynamicFile, projectId, static (_) => true);

        // Schedule a task, in case adding a dynamic file is the last thing that happens
        _logger.LogTrace("{projectId} scheduling task due to dynamic file", projectId);
        _workQueue.AddWork(projectId);
    }

    private void Workspace_WorkspaceChanged(object? sender, WorkspaceChangeEventArgs e)
    {
        switch (e.Kind)
        {
            case WorkspaceChangeKind.SolutionChanged:
            case WorkspaceChangeKind.SolutionReloaded:
                foreach (var project in e.NewSolution.Projects)
                {
                    EnqueueUpdate(project);
                }

                break;

            case WorkspaceChangeKind.SolutionAdded:
                foreach (var project in e.NewSolution.Projects)
                {
                    EnqueueUpdate(project);
                }

                break;

            case WorkspaceChangeKind.ProjectReloaded:
                EnqueueUpdate(e.NewSolution.GetProject(e.ProjectId));
                break;

            case WorkspaceChangeKind.ProjectRemoved:
                RemoveProject(e.ProjectId.AssumeNotNull());
                break;

            case WorkspaceChangeKind.ProjectAdded:
            case WorkspaceChangeKind.ProjectChanged:
            case WorkspaceChangeKind.DocumentAdded:
            case WorkspaceChangeKind.DocumentRemoved:
            case WorkspaceChangeKind.DocumentReloaded:
            case WorkspaceChangeKind.DocumentChanged:
            case WorkspaceChangeKind.AdditionalDocumentAdded:
            case WorkspaceChangeKind.AdditionalDocumentRemoved:
            case WorkspaceChangeKind.AdditionalDocumentReloaded:
            case WorkspaceChangeKind.AdditionalDocumentChanged:
            case WorkspaceChangeKind.DocumentInfoChanged:
            case WorkspaceChangeKind.AnalyzerConfigDocumentAdded:
            case WorkspaceChangeKind.AnalyzerConfigDocumentRemoved:
            case WorkspaceChangeKind.AnalyzerConfigDocumentReloaded:
            case WorkspaceChangeKind.AnalyzerConfigDocumentChanged:
                var projectId = e.ProjectId ?? e.DocumentId?.ProjectId;
                if (projectId is not null)
                {
                    EnqueueUpdate(e.NewSolution.GetProject(projectId));
                }

                break;

            case WorkspaceChangeKind.SolutionCleared:
            case WorkspaceChangeKind.SolutionRemoved:
                foreach (var project in e.OldSolution.Projects)
                {
                    RemoveProject(project.Id);
                }

                break;

            default:
                break;
        }
    }

    private void RemoveProject(ProjectId projectId)
    {
        ImmutableInterlocked.TryRemove(ref _projectsWithDynamicFile, projectId, out var _);
    }

    private void EnqueueUpdate(Project? project)
    {
        if (_projectInfoFileName is null ||
            project is not
            {
                Language: LanguageNames.CSharp
            })
        {
            return;
        }

        // Don't queue work for projects that don't have a dynamic file
        if (!_projectsWithDynamicFile.TryGetValue(project.Id, out var _))
        {
            return;
        }

        var projectId = project.Id;
        _workQueue.AddWork(projectId);
    }

    private async ValueTask UpdateCurrentProjectsAsync(ImmutableArray<ProjectId> projectIds, CancellationToken cancellationToken)
    {
        var solution = _workspace.AssumeNotNull().CurrentSolution;

        var projectsToTryAndSerialize = projectIds
            .SelectAsArray(p => solution.GetProject(p))
            .OfType<Project>()
            .ToImmutableArray();

        if (projectsToTryAndSerialize.Length == 0)
        {
            return;
        }

        await SerializeProjectsAsync(projectsToTryAndSerialize, cancellationToken).ConfigureAwait(false);
    }

    private protected virtual async Task SerializeProjectsAsync(ImmutableArray<Project> projectsToTryAndSerialize, CancellationToken cancellationToken)
    {
        var pair = await RazorProjectInfoSerializer.SerializeProjectsAsync(projectsToTryAndSerialize, _logger, cancellationToken).ConfigureAwait(false);

        if (pair is null)
        {
            _logger?.LogTrace("No projects identified to serialize, returning");
            return;
        }

        _logger?.LogTrace("Notifying and holding onto new mapped file {mappedFileName}", pair.Value.fileName);
        await _notifyMemoryMappedFileNameAsync.Invoke(pair.Value.fileName, cancellationToken).ConfigureAwait(false);

        // Hold on to the most recently notified file
        _mappedFile = pair.Value.file;
    }
}
