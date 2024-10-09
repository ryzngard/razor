// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using Microsoft.AspNetCore.Razor.LanguageServer.CodeActions.Models;
using Microsoft.AspNetCore.Razor.PooledObjects;
using Microsoft.AspNetCore.Razor.Threading;
using Microsoft.CodeAnalysis.Razor.Logging;
using Microsoft.CodeAnalysis.Razor.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.AspNetCore.Razor.LanguageServer.CodeActions.Razor;

internal sealed class ExtractToComponentCodeActionProvider(ILoggerFactory loggerFactory) : IRazorCodeActionProvider
{
    private readonly ILogger _logger = loggerFactory.GetOrCreateLogger<ExtractToComponentCodeActionProvider>();

    public Task<ImmutableArray<RazorVSInternalCodeAction>> ProvideAsync(RazorCodeActionContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return SpecializedTasks.EmptyImmutableArray<RazorVSInternalCodeAction>();
        }

        if (!context.SupportsFileCreation)
        {
            return SpecializedTasks.EmptyImmutableArray<RazorVSInternalCodeAction>();
        }

        if (!FileKinds.IsComponent(context.CodeDocument.GetFileKind()))
        {
            return SpecializedTasks.EmptyImmutableArray<RazorVSInternalCodeAction>();
        }

        var syntaxTree = context.CodeDocument.GetSyntaxTree();
        if (syntaxTree?.Root is null)
        {
            return SpecializedTasks.EmptyImmutableArray<RazorVSInternalCodeAction>();
        }

        // Make sure the selection starts on an element tag
        var (startElementNode, endElementNode) = GetStartAndEndElements(context, syntaxTree, _logger);
        if (startElementNode is null)
        {
            return SpecializedTasks.EmptyImmutableArray<RazorVSInternalCodeAction>();
        }

        if (!TryGetNamespace(context.CodeDocument, out var @namespace))
        {
            return SpecializedTasks.EmptyImmutableArray<RazorVSInternalCodeAction>();
        }

        // If we have a start element we have an end element
        endElementNode.AssumeNotNull();

        var actionParams = CreateActionParams(context, startElementNode, @namespace, syntaxTree);

        var resolutionParams = new RazorCodeActionResolutionParams()
        {
            Action = LanguageServerConstants.CodeActions.ExtractToNewComponentAction,
            Language = LanguageServerConstants.CodeActions.Languages.Razor,
            Data = actionParams,
        };

        var codeAction = RazorCodeActionFactory.CreateExtractToComponent(resolutionParams);
        return Task.FromResult<ImmutableArray<RazorVSInternalCodeAction>>([codeAction]);
    }

    private static (MarkupElementSyntax? Start, MarkupElementSyntax? End) GetStartAndEndElements(RazorCodeActionContext context, RazorSyntaxTree syntaxTree, ILogger logger)
    {
        var selectionStart = context.Request.Range.Start;
        var selectionEnd = context.Request.Range.End;
        var owner = syntaxTree.Root.FindInnermostNode(context.StartLocation.AbsoluteIndex, includeWhitespace: true);
        if (owner is null)
        {
            logger.LogWarning($"Owner should never be null.");
            return (null, null);
        }

        var startElementNode = owner.FirstAncestorOrSelf<MarkupElementSyntax>();
        if (startElementNode is not { EndTag: not null } || LocationOutsideNode(context.StartLocation, startElementNode))
        {
            return (null, null);
        }

        var endElementNode = context.StartLocation == context.EndLocation
            ? startElementNode
            : GetEndElementNode(context, syntaxTree);

        return (startElementNode, endElementNode);
    }

    private static bool LocationOutsideNode(SourceLocation location, MarkupElementSyntax node)
    {
        return location.AbsoluteIndex > node.StartTag.Span.End &&
               location.AbsoluteIndex < node.EndTag.SpanStart;
    }

    private static MarkupElementSyntax? GetEndElementNode(RazorCodeActionContext context, RazorSyntaxTree syntaxTree)
    {
        var endOwner = syntaxTree.Root.FindInnermostNode(context.EndLocation.AbsoluteIndex, includeWhitespace: true);
        if (endOwner is null)
        {
            return null;
        }

        // Correct selection to include the current node if the selection ends immediately after a closing tag.
        if (endOwner is MarkupTextLiteralSyntax && endOwner.ContainsOnlyWhitespace() && endOwner.TryGetPreviousSibling(out var previousSibling))
        {
            endOwner = previousSibling;
        }

        return endOwner.FirstAncestorOrSelf<MarkupElementSyntax>();
    }

    private static ExtractToComponentCodeActionParams CreateActionParams(RazorCodeActionContext context, MarkupElementSyntax startNode, MarkupElementSyntax endNode, string @namespace, RazorSyntaxTree syntaxTree)
    {
        var selectionSpan = new TextSpan(startNode.Span.Start, endNode.Span.End - startNode.Span.Start);

        // Find a valid node that encompasses both the start and the end to
        // become the selection.
        SyntaxNode selectionNode = endNode.Span.Contains(startNode.Span)
            ? endNode
            : startNode;

        while (selectionNode is MarkupElementSyntax or
                MarkupTagHelperAttributeSyntax or
                MarkupBlockSyntax)
        {
            if (selectionNode.Span.Contains(selectionSpan))
            {
                break;
            }

            selectionNode = selectionNode.Parent;
        }

        selectionSpan = selectionNode.Span;

        return new ExtractToComponentCodeActionParams
        {
            Uri = context.Request.TextDocument.Uri,
            Start = selectionSpan.Start,
            End = selectionSpan.End,
            Namespace = @namespace,
            UsingDirectives = GetUsingsInDocument(syntaxTree)
        };
    }

    private static bool TryGetNamespace(RazorCodeDocument codeDocument, [NotNullWhen(returnValue: true)] out string? @namespace)
        // If the compiler can't provide a computed namespace it will fallback to "__GeneratedComponent" or
        // similar for the NamespaceNode. This would end up with extracting to a wrong namespace
        // and causing compiler errors. Avoid offering this refactoring if we can't accurately get a
        // good namespace to extract to
        => codeDocument.TryComputeNamespace(fallbackToRootNamespace: true, out @namespace);

    private static ImmutableArray<string> GetUsingsInDocument(RazorSyntaxTree syntaxTree)
        => syntaxTree
            .Root
            .ChildNodes()
            .Select(node =>
            {
                if (node.IsUsingDirective(out var _))
                {
                    return node.ToFullString().Trim();
                }

                return null;
            })
            .WhereNotNull()
            .ToImmutableArray();
}
