// Aver Engine — Copyright (c) 2026 Hydrogen-Isotope.
// Developed by Vectoric-Core-Systems.
// SPDX-License-Identifier: LGPL-2.1-only. See LICENSE at the repository root.
// Godot's HTTPRequest node (GodotSharp's HttpRequest) over .NET's HttpClient, and ZIPReader (ZipReader) over System.IO.Compression.
// The request runs off the main thread; request_completed is sent from the node's process, on the main thread, as Godot sends it.

using System.Net.Http;

namespace Aver.Fabric;

/// <summary>Godot's HTTPClient: only its <see cref="Method"/> enum, which <see cref="HttpRequest.Request"/> takes. Aver has no low-level HTTP client.</summary>
public class HttpClient : RefCounted
{
    public enum Method { Get = 0, Head = 1, Post = 2, Put = 3, Delete = 4, Options = 5, Trace = 6, Connect = 7, Patch = 8, Max = 9 }
}

/// <summary>Sends one HTTP request at a time and reports it with <c>request_completed(result, response_code, headers, body)</c>.</summary>
public class HttpRequest : Node
{
    public enum Result
    {
        Success = 0, ChunkedBodySizeMismatch = 1, CantConnect = 2, CantResolve = 3, ConnectionError = 4, TlsHandshakeError = 5, NoResponse = 6,
        BodySizeLimitExceeded = 7, BodyDecompressFailed = 8, RequestFailed = 9, DownloadFileCantOpen = 10, DownloadFileWriteError = 11,
        RedirectLimitReached = 12, Timeout = 13,
    }

    public new class SignalName : Node.SignalName
    {
        public static readonly StringName RequestCompleted = "request_completed";
    }

    private static readonly System.Net.Http.HttpClient s_client = new(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 });

    private Task<(Result, long, string[], byte[])>? _pending;
    private CancellationTokenSource? _cancel;

    public string DownloadFile { get; set; } = "";
    public int BodySizeLimit { get; set; } = -1;
    public int MaxRedirects { get; set; } = 8;
    public double Timeout { get; set; }
    public bool UseThreads { get; set; }
    public bool AcceptGzip { get; set; } = true;
    public int DownloadChunkSize { get; set; } = 65536;

    /// <summary>Starts the request; <see cref="Error.Busy"/> while another is running, <see cref="Error.InvalidParameter"/> for an address that is not http(s).</summary>
    public Error Request(string url, string[]? customHeaders = null, HttpClient.Method method = HttpClient.Method.Get, string requestData = "") =>
        Start(url, customHeaders, method, System.Text.Encoding.UTF8.GetBytes(requestData));

    public Error RequestRaw(string url, string[]? customHeaders = null, HttpClient.Method method = HttpClient.Method.Get, byte[]? requestDataRaw = null) =>
        Start(url, customHeaders, method, requestDataRaw ?? System.Array.Empty<byte>());

    public void CancelRequest()
    {
        _cancel?.Cancel();
        _pending = null;
    }

    public HttpClientStatus GetHttpClientStatus() => _pending == null ? HttpClientStatus.Disconnected : HttpClientStatus.Requesting;
    public enum HttpClientStatus { Disconnected = 0, Resolving = 1, CantResolve = 2, Connecting = 3, CantConnect = 4, Connected = 5, Requesting = 6, Body = 7, ConnectionError = 8, TlsHandshakeError = 9 }

    private Error Start(string url, string[]? headers, HttpClient.Method method, byte[] body)
    {
        if (_pending != null) return Error.Busy;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return Error.InvalidParameter;
        _cancel = new CancellationTokenSource();
        if (Timeout > 0) _cancel.CancelAfter(TimeSpan.FromSeconds(Timeout));
        var msg = new HttpRequestMessage(new HttpMethod(method.ToString().ToUpperInvariant()), uri);
        if (body.Length > 0 && method is not (HttpClient.Method.Get or HttpClient.Method.Head)) msg.Content = new ByteArrayContent(body);
        foreach (string h in headers ?? System.Array.Empty<string>())
        {
            int colon = h.IndexOf(':');
            if (colon <= 0) continue;
            string name = h[..colon].Trim(), value = h[(colon + 1)..].Trim();
            if (!msg.Headers.TryAddWithoutValidation(name, value)) msg.Content?.Headers.TryAddWithoutValidation(name, value);
        }
        _pending = Send(msg, _cancel.Token, BodySizeLimit, DownloadFile);
        return Error.Ok;
    }

    private static async Task<(Result, long, string[], byte[])> Send(HttpRequestMessage msg, CancellationToken token, int limit, string downloadFile)
    {
        try
        {
            using HttpResponseMessage res = await s_client.SendAsync(msg, token).ConfigureAwait(false);
            byte[] body = await res.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            if (limit >= 0 && body.Length > limit) return (Result.BodySizeLimitExceeded, (long)res.StatusCode, System.Array.Empty<string>(), System.Array.Empty<byte>());
            string[] headers = res.Headers.Concat(res.Content.Headers).SelectMany(h => h.Value.Select(v => $"{h.Key}: {v}")).ToArray();
            if (downloadFile.Length > 0)
            {
                try { File.WriteAllBytes(FabricPaths.Globalize(downloadFile), body); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (Result.DownloadFileWriteError, (long)res.StatusCode, headers, System.Array.Empty<byte>()); }
                body = System.Array.Empty<byte>();
            }
            return (Result.Success, (long)res.StatusCode, headers, body);
        }
        catch (OperationCanceledException) { return (Result.Timeout, 0, System.Array.Empty<string>(), System.Array.Empty<byte>()); }
        catch (HttpRequestException ex) { return (ex.InnerException is System.Net.Sockets.SocketException ? Result.CantConnect : Result.RequestFailed, 0, System.Array.Empty<string>(), System.Array.Empty<byte>()); }
    }

    internal override bool UsesInternalProcess => true;

    internal override void InternalProcess(double delta)
    {
        if (_pending is not { IsCompleted: true } done) return;
        _pending = null;
        (Result result, long code, string[] headers, byte[] body) = done.Result;
        EmitSignal(SignalName.RequestCompleted, (long)result, code, headers, body);
    }

    internal override void OnExitingTree()
    {
        CancelRequest();
        base.OnExitingTree();
    }
}

/// <summary>Reads the files of a .zip archive (Godot's ZIPReader).</summary>
public class ZipReader : RefCounted
{
    private System.IO.Compression.ZipArchive? _zip;

    public Error Open(string path)
    {
        Close();
        try { _zip = System.IO.Compression.ZipFile.OpenRead(FabricPaths.Globalize(path)); return Error.Ok; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return Error.FileCantOpen; }
    }

    public Error Close()
    {
        _zip?.Dispose();
        _zip = null;
        return Error.Ok;
    }

    public string[] GetFiles() => _zip?.Entries.Select(e => e.FullName).ToArray() ?? System.Array.Empty<string>();

    public bool FileExists(string path, bool caseSensitive = true) => Find(path, caseSensitive) != null;

    public byte[] ReadFile(string path, bool caseSensitive = true)
    {
        System.IO.Compression.ZipArchiveEntry? e = Find(path, caseSensitive);
        if (e == null) return System.Array.Empty<byte>();
        using Stream s = e.Open();
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    private System.IO.Compression.ZipArchiveEntry? Find(string path, bool caseSensitive) =>
        _zip?.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
}
