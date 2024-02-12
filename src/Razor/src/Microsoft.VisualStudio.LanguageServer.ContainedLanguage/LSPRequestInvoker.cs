// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;

namespace Microsoft.VisualStudio.LanguageServer.ContainedLanguage;

internal abstract class LSPRequestInvoker
{
    /// <summary>
    /// Reinvoke the request on the given server.
    /// </summary>
    /// <typeparam name="TIn"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <param name="method"></param>
    /// <param name="languageServerName"></param>
    /// <param name="parameters"></param>
    /// <param name="cancellationToken"></param>
    /// 
    /// <returns></returns>
    /// <remarks>When operating on a document the <see cref="ITextBuffer"/> overload should be used, since it guarantees ordering.</remarks>
    public abstract Task<ReinvokeResponse<TOut>> ReinvokeRequestOnServerAsync<TIn, TOut>(
        string method,
        string languageServerName,
        TIn parameters,
        CancellationToken cancellationToken)
        where TIn : notnull;

    public abstract Task<ReinvocationResponse<TOut>?> ReinvokeRequestOnServerAsync<TIn, TOut>(
        ITextBuffer textBuffer,
        string method,
        string languageServerName,
        TIn parameters,
        CancellationToken cancellationToken)
        where TIn : notnull;

    public abstract IAsyncEnumerable<ReinvocationResponse<TOut>> ReinvokeRequestOnMultipleServersAsync<TIn, TOut>(
        ITextBuffer textBuffer,
        string method,
        TIn parameters,
        CancellationToken cancellationToken)
        where TIn : notnull;
}
