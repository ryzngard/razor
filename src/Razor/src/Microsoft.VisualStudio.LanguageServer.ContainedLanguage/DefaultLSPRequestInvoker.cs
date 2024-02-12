// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Text;

namespace Microsoft.VisualStudio.LanguageServer.ContainedLanguage;

[Shared]
[Export(typeof(LSPRequestInvoker))]
internal class DefaultLSPRequestInvoker : LSPRequestInvoker
{
    private readonly ILanguageServiceBroker2 _languageServiceBroker;
    private readonly FallbackCapabilitiesFilterResolver _fallbackCapabilitiesFilterResolver;

    [ImportingConstructor]
    public DefaultLSPRequestInvoker(
        ILanguageServiceBroker2 languageServiceBroker,
        FallbackCapabilitiesFilterResolver fallbackCapabilitiesFilterResolver)
    {
        if (languageServiceBroker is null)
        {
            throw new ArgumentNullException(nameof(languageServiceBroker));
        }

        if (fallbackCapabilitiesFilterResolver is null)
        {
            throw new ArgumentNullException(nameof(fallbackCapabilitiesFilterResolver));
        }

        _languageServiceBroker = languageServiceBroker;
        _fallbackCapabilitiesFilterResolver = fallbackCapabilitiesFilterResolver;
    }

    public override async Task<ReinvokeResponse<TOut>> ReinvokeRequestOnServerAsync<TIn, TOut>(
        string method,
        string languageServerName,
        TIn parameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(method))
        {
            throw new ArgumentException("message", nameof(method));
        }

        var result = await _languageServiceBroker.RequestAsync(
            new GeneralRequest<TIn, TOut> { LanguageServerName = languageServerName, Method = method, Request = parameters },
            cancellationToken);

        return result is not null ? new ReinvokeResponse<TOut>(result!) : default;
    }

    public override async Task<ReinvocationResponse<TOut>?> ReinvokeRequestOnServerAsync<TIn, TOut>(
        ITextBuffer textBuffer,
        string method,
        string languageServerName,
        TIn parameters,
        CancellationToken cancellationToken)
    {
        var response = await _languageServiceBroker.RequestAsync(
            new DocumentRequest<TIn, TOut> { ParameterFactory = _ => parameters, LanguageServerName = languageServerName, Method = method, TextBuffer = textBuffer },
            cancellationToken);

        if (response is null)
        {
            return null;
        }

        var reinvocationResponse = new ReinvocationResponse<TOut>(response);
        return reinvocationResponse;
    }

    public override async IAsyncEnumerable<ReinvocationResponse<TOut>> ReinvokeRequestOnMultipleServersAsync<TIn, TOut>(
        ITextBuffer textBuffer,
        string method,
        TIn parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requests = _languageServiceBroker.RequestMultipleAsync(
            new DocumentRequest<TIn, TOut> { ParameterFactory = _ => parameters, Method = method, TextBuffer = textBuffer },
            cancellationToken);

        await foreach (var response in requests)
        {
            var reinvocationResponse = new ReinvocationResponse<TOut>(response.response);
            yield return reinvocationResponse;
        }
    }
}
