// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.LanguageServer.ContainedLanguage.MessageInterception;

/// <summary>
/// Contains an updated message token and a signal of whether the document Uri was changed.
/// </summary>
public struct InterceptionResult
{
    public static readonly InterceptionResult NoChange = new(null, false);

    public InterceptionResult(object? newToken, bool changedDocumentUri)
    {
        UpdatedToken = newToken;
        ChangedDocumentUri = changedDocumentUri;
    }

    public object? UpdatedToken { get; }
    public bool ChangedDocumentUri { get; }
}
