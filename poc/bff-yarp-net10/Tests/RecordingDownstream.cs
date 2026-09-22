using System.Net;
using Yarp.ReverseProxy.Forwarder;

namespace Croesus.BffYarp.Tests;

/// <summary>
/// Stands in for the owned API. It records exactly what the proxy sent, which is what makes the credential
/// stripping and token attachment claims checkable rather than asserted.
/// </summary>
internal sealed class RecordingDownstream : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private readonly Lock _gate = new();

    public HttpStatusCode NextStatusCode { get; set; } = HttpStatusCode.OK;

    public string? NextWwwAuthenticate { get; set; }

    public string NextBody { get; set; } = """{"ok":true}""";

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var headers = request.Headers
            .ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            _requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!, headers));
        }

        var response = new HttpResponseMessage(NextStatusCode)
        {
            Content = new StringContent(NextBody, System.Text.Encoding.UTF8, "application/json"),
            RequestMessage = request
        };

        if (NextWwwAuthenticate is not null)
        {
            response.Headers.TryAddWithoutValidation("WWW-Authenticate", NextWwwAuthenticate);
        }

        response.Headers.TryAddWithoutValidation("Set-Cookie", "downstream-session=must-not-reach-the-browser; Path=/");
        return Task.FromResult(response);
    }

    internal sealed record RecordedRequest(string Method, Uri Uri, IReadOnlyDictionary<string, string> Headers);
}

internal sealed class RecordingForwarderHttpClientFactory(RecordingDownstream downstream) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
        new(downstream, disposeHandler: false);
}
