#nullable disable
// Vendored AutoUpdater.NET: intentionally keeps WebClient/WebRequest for behavior
// parity with the upstream library. Rewriting to HttpClient is deferred (would
// change download/redirect semantics and risk breaking the auto-update path).
#pragma warning disable SYSLIB0014
﻿using System;
using System.Net;

namespace AutoUpdaterDotNET;

/// <inheritdoc />
public class MyWebClient : WebClient
{
    /// <summary>
    ///     Response Uri after any redirects.
    /// </summary>
    public Uri ResponseUri;

    /// <inheritdoc />
    protected override WebResponse GetWebResponse(WebRequest request, IAsyncResult result)
    {
        WebResponse webResponse = base.GetWebResponse(request, result);
        ResponseUri = webResponse.ResponseUri;
        return webResponse;
    }
}