// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.LanguageServer.ContainedLanguage;

internal class ReinvocationResponse<TResponseType>
{
    public ReinvocationResponse(TResponseType? response)
    {
        Response = response;
    }

    public TResponseType? Response { get; }
}
