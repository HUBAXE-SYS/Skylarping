/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Shared HTTP for the app on .NET 4.0 / Windows XP.
// Approach from SimpleLLMChat / NyoCoder (same author): HttpWebRequest first, and curl.exe
// as a fallback when Windows' own TLS can't reach the server (no TLS 1.2+ on XP).
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Skylarping.Net
{
    /// <summary>An HTTP request. Set <see cref="Body"/> for POST/PUT.</summary>
    public sealed class HttpRequest
    {
        public HttpRequest(string url, string method = "GET")
        {
            Url = url;
            Method = method;
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TimeoutMilliseconds = Http.DefaultTimeoutMilliseconds;
        }

        public string Url { get; set; }
        public string Method { get; set; }

        /// <summary>Request headers (e.g. Authorization, Accept). Content-Type goes in <see cref="ContentType"/>.</summary>
        public IDictionary<string, string> Headers { get; private set; }

        public byte[] Body { get; set; }
        public string ContentType { get; set; }

        /// <summary>Whole-request timeout; 0 or less means none (for long streamed responses).</summary>
        public int TimeoutMilliseconds { get; set; }

        /// <summary>Skip HttpWebRequest and go straight to curl.exe.</summary>
        public bool ForceCurl { get; set; }

        /// <summary>Sets a UTF-8 text body (e.g. JSON) and its content type.</summary>
        public HttpRequest WithText(string body, string contentType = "application/json")
        {
            Body = Encoding.UTF8.GetBytes(body ?? string.Empty);
            ContentType = contentType;
            return this;
        }
    }

    /// <summary>An open HTTP response. Read <see cref="Body"/> (streams as data arrives), then dispose.</summary>
    public sealed class HttpResponse : IDisposable
    {
        private readonly IDisposable _owner;

        internal HttpResponse(int statusCode, Stream body, bool usedCurl, IDisposable owner)
        {
            StatusCode = statusCode;
            Body = body;
            UsedCurl = usedCurl;
            _owner = owner;
        }

        public int StatusCode { get; private set; }
        public bool IsSuccess { get { return StatusCode >= 200 && StatusCode < 300; } }
        public Stream Body { get; private set; }

        /// <summary>True if the request went through curl.exe instead of HttpWebRequest.</summary>
        public bool UsedCurl { get; private set; }

        public byte[] ReadAllBytes()
        {
            using (var ms = new MemoryStream())
            {
                byte[] buffer = new byte[16384];
                int read;
                while ((read = Body.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);
                return ms.ToArray();
            }
        }

        public string ReadAllText()
        {
            return Encoding.UTF8.GetString(ReadAllBytes());
        }

        public void Dispose()
        {
            try { Body.Dispose(); } catch { }
            if (_owner != null)
                try { _owner.Dispose(); } catch { }
        }
    }

    /// <summary>Thrown when a request fails, or by the convenience helpers on a non-2xx status.</summary>
    public sealed class HttpException : Exception
    {
        public HttpException(string message, int statusCode = 0, string responseBody = null, Exception inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        /// <summary>HTTP status, or 0 if no response was received.</summary>
        public int StatusCode { get; private set; }
        public string ResponseBody { get; private set; }
    }

    public static class Http
    {
        public const int DefaultTimeoutMilliseconds = 30000;

        static Http()
        {
            UserAgent = "Skylarping";
            CurlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "curl.exe");
            PreferCurlForHttps = Environment.OSVersion.Version.Major < 6; // XP / Server 2003: no usable TLS 1.2
            EnableModernTls();
        }

        public static string UserAgent { get; set; }

        /// <summary>curl.exe used as the TLS fallback (curl-ca-bundle.crt beside it is picked up automatically).</summary>
        public static string CurlPath { get; set; }

        /// <summary>Use curl directly for HTTPS when available instead of trying .NET first (default on XP).</summary>
        public static bool PreferCurlForHttps { get; set; }

        public static bool CurlAvailable
        {
            get { return !string.IsNullOrEmpty(CurlPath) && File.Exists(CurlPath); }
        }

        // ---------------------------------------------------------------- convenience helpers

        public static byte[] GetBytes(string url)
        {
            using (HttpResponse response = Open(new HttpRequest(url)))
                return ReadSuccess(response, url);
        }

        public static string GetString(string url)
        {
            return Encoding.UTF8.GetString(GetBytes(url));
        }

        public static Task<byte[]> GetBytesAsync(string url)
        {
            return Task.Factory.StartNew(() => GetBytes(url), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public static Task<HttpResponse> OpenAsync(HttpRequest request)
        {
            return Task.Factory.StartNew(() => Open(request), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static byte[] ReadSuccess(HttpResponse response, string url)
        {
            byte[] body = response.ReadAllBytes();
            if (!response.IsSuccess)
                throw new HttpException("HTTP " + response.StatusCode + " from " + url,
                    response.StatusCode, Encoding.UTF8.GetString(body));
            return body;
        }

        // ---------------------------------------------------------------- core

        /// <summary>
        /// Sends the request and returns the response with its body still open for reading.
        /// Non-2xx statuses are returned, not thrown; connection/TLS failures throw <see cref="HttpException"/>.
        /// </summary>
        public static HttpResponse Open(HttpRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            bool https = request.Url.StartsWith("https:", StringComparison.OrdinalIgnoreCase);

            if (request.ForceCurl || (https && PreferCurlForHttps && CurlAvailable))
                return OpenWithCurl(request);

            try
            {
                return OpenWithWebRequest(request);
            }
            catch (WebException ex)
            {
                if (https && CurlAvailable && IsTlsOrConnectionFailure(ex))
                    return OpenWithCurl(request);
                throw new HttpException(ex.Message, 0, null, ex);
            }
        }

        private static void EnableModernTls()
        {
            // .NET 4.0 doesn't know TLS 1.1/1.2 by name, but Windows 7+ with .NET 4.5+ installed accepts
            // the numeric values. Only touch it when an explicit list is set (newer runtimes default to the OS).
            try
            {
                if ((int)ServicePointManager.SecurityProtocol != 0)
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)(768 | 3072);
            }
            catch (NotSupportedException) { } // XP: not available; curl handles HTTPS there
        }

        private static bool IsTlsOrConnectionFailure(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.SecureChannelFailure:
                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.ReceiveFailure:
                case WebExceptionStatus.ServerProtocolViolation:
                    return true;
                default:
                    return ex.InnerException != null && ex.InnerException.GetType().Name.Contains("Authentication");
            }
        }

        // ---------------------------------------------------------------- HttpWebRequest

        private static HttpResponse OpenWithWebRequest(HttpRequest request)
        {
            var web = (HttpWebRequest)WebRequest.Create(request.Url);
            web.Method = request.Method;
            web.UserAgent = UserAgent;
            web.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            web.Timeout = request.TimeoutMilliseconds > 0 ? request.TimeoutMilliseconds : Timeout.Infinite;
            web.ReadWriteTimeout = web.Timeout;

            foreach (KeyValuePair<string, string> header in request.Headers)
            {
                switch (header.Key.ToLowerInvariant())
                {
                    case "accept": web.Accept = header.Value; break;
                    case "user-agent": web.UserAgent = header.Value; break;
                    case "referer": web.Referer = header.Value; break;
                    case "content-type": web.ContentType = header.Value; break;
                    default: web.Headers[header.Key] = header.Value; break;
                }
            }

            if (request.Body != null)
            {
                if (request.ContentType != null) web.ContentType = request.ContentType;
                web.ContentLength = request.Body.Length;
                using (Stream stream = web.GetRequestStream())
                    stream.Write(request.Body, 0, request.Body.Length);
            }

            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)web.GetResponse();
            }
            catch (WebException ex)
            {
                // Non-2xx responses arrive as exceptions; hand them back like any other response.
                response = ex.Response as HttpWebResponse;
                if (response == null)
                    throw;
            }

            return new HttpResponse((int)response.StatusCode, response.GetResponseStream(), false, response);
        }

        // ---------------------------------------------------------------- curl.exe

        private static HttpResponse OpenWithCurl(HttpRequest request)
        {
            if (!CurlAvailable)
                throw new HttpException("curl.exe not found at " + CurlPath + " (needed for HTTPS on this system).");

            // Everything except the body goes in a curl config read from stdin (-K -), so the URL and
            // headers (API keys) never appear on curl's command line, which other processes can read.
            var config = new StringBuilder();
            AddConfig(config, "url", request.Url);
            AddConfig(config, "request", request.Method);
            AddConfig(config, "user-agent", UserAgent);
            foreach (KeyValuePair<string, string> header in request.Headers)
                AddConfig(config, "header", header.Key + ": " + header.Value);
            if (request.ContentType != null)
                AddConfig(config, "header", "Content-Type: " + request.ContentType);
            if (request.TimeoutMilliseconds > 0)
                AddConfig(config, "max-time", Math.Max(1, request.TimeoutMilliseconds / 1000).ToString());

            // The body (if any) streams through a named pipe, so it never touches the disk either.
            NamedPipeServerStream pipe = null;
            string pipeName = null;
            if (request.Body != null)
            {
                pipeName = "skylarping_curl_" + Guid.NewGuid().ToString("N");
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte);
                AddConfig(config, "data-binary", "@\\\\.\\pipe\\" + pipeName);
            }

            var psi = new ProcessStartInfo
            {
                FileName = CurlPath,
                // -s -S quiet but report errors, -N no buffering (streaming), -D - status line and headers
                // to stdout ahead of the body, --compressed accept gzip.
                Arguments = "-s -S -N -D - --compressed --suppress-connect-headers --connect-timeout 30 -K -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Exception ex)
            {
                if (pipe != null) pipe.Dispose();
                throw new HttpException("Could not start curl.exe: " + ex.Message, 0, null, ex);
            }

            var session = new CurlSession(process, pipe, pipeName);
            try
            {
                session.Start(request.Body, Encoding.UTF8.GetBytes(config.ToString()));
                int status = session.ReadStatusAndHeaders();
                return new HttpResponse(status, new CurlBodyStream(session), true, session);
            }
            catch (Exception ex)
            {
                string error = session.FinishAndGetError();
                session.Dispose();
                if (ex is HttpException) throw;
                throw new HttpException("curl failed: " + (string.IsNullOrEmpty(error) ? ex.Message : error), 0, null, ex);
            }
        }

        /// <summary>Appends `name = "value"` using curl's config quoting rules.</summary>
        private static void AddConfig(StringBuilder config, string name, string value)
        {
            var escaped = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '"': escaped.Append("\\\""); break;
                    case '\r': case '\n': break; // never allow line breaks (header injection)
                    default: escaped.Append(c); break;
                }
            }
            config.Append(name).Append(" = \"").Append(escaped).Append("\"\n");
        }

        /// <summary>One running curl.exe: feeds its inputs and drains stderr.</summary>
        private sealed class CurlSession : IDisposable
        {
            private readonly Process _process;
            private readonly NamedPipeServerStream _pipe;
            private readonly string _pipeName;
            private readonly StringBuilder _stderr = new StringBuilder();
            private Thread _stderrThread, _pipeThread;
            private bool _disposed;

            public CurlSession(Process process, NamedPipeServerStream pipe, string pipeName)
            {
                _process = process;
                _pipe = pipe;
                _pipeName = pipeName;
            }

            public Stream Output { get { return _process.StandardOutput.BaseStream; } }

            public void Start(byte[] body, byte[] config)
            {
                _stderrThread = new Thread(() =>
                {
                    try
                    {
                        string line;
                        while ((line = _process.StandardError.ReadLine()) != null)
                            lock (_stderr) _stderr.AppendLine(line);
                    }
                    catch { }
                }) { IsBackground = true };
                _stderrThread.Start();

                if (_pipe != null)
                {
                    _pipeThread = new Thread(() =>
                    {
                        try
                        {
                            _pipe.WaitForConnection();
                            _pipe.Write(body, 0, body.Length);
                            _pipe.Flush();
                            _pipe.Close();
                        }
                        catch { }
                    }) { IsBackground = true };
                    _pipeThread.Start();
                }

                // Write raw UTF-8 bytes: StandardInput's TextWriter would use the console code page.
                Stream stdin = _process.StandardInput.BaseStream;
                stdin.Write(config, 0, config.Length);
                stdin.Flush();
                _process.StandardInput.Close();
            }

            /// <summary>Reads the header block(s) curl writes before the body and returns the final status.</summary>
            public int ReadStatusAndHeaders()
            {
                while (true)
                {
                    string statusLine = ReadLine();
                    if (statusLine == null)
                        throw new HttpException("curl failed: " + FinishAndGetError());

                    // "HTTP/1.1 200 OK" or "HTTP/2 200"
                    string[] parts = statusLine.Split(' ');
                    int status;
                    if (parts.Length < 2 || !parts[0].StartsWith("HTTP/") || !int.TryParse(parts[1], out status))
                        throw new HttpException("Unexpected response from curl: " + statusLine);

                    string line;
                    while ((line = ReadLine()) != null && line.Length > 0) { } // skip headers

                    if (status >= 100 && status < 200)
                        continue; // e.g. 100 Continue: the real status follows
                    return status;
                }
            }

            private string ReadLine()
            {
                var bytes = new List<byte>(64);
                int b;
                while ((b = Output.ReadByte()) != -1)
                {
                    if (b == '\n') break;
                    if (b != '\r') bytes.Add((byte)b);
                }
                if (b == -1 && bytes.Count == 0) return null;
                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            /// <summary>Waits for curl to exit; returns its error output if it failed, else null.</summary>
            public string FinishAndGetError()
            {
                try
                {
                    if (!_process.WaitForExit(15000))
                        return "curl did not exit";
                    if (_stderrThread != null) _stderrThread.Join(2000);
                    if (_process.ExitCode == 0) return null;
                    lock (_stderr)
                    {
                        string text = _stderr.ToString().Trim();
                        return "exit code " + _process.ExitCode + (text.Length > 0 ? ": " + text : "");
                    }
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { if (!_process.HasExited) _process.Kill(); } catch { }

                // Release the pipe thread if curl never connected to the pipe.
                if (_pipeThread != null && _pipeThread.IsAlive)
                {
                    try
                    {
                        using (var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.In))
                            client.Connect(200);
                    }
                    catch { }
                }
                if (_pipe != null) try { _pipe.Dispose(); } catch { }
                try { _process.Dispose(); } catch { }
            }
        }

        /// <summary>curl's stdout after the headers; reports a curl failure at the end of the stream.</summary>
        private sealed class CurlBodyStream : Stream
        {
            private readonly CurlSession _session;
            private bool _finished;

            public CurlBodyStream(CurlSession session)
            {
                _session = session;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_finished) return 0;
                int read = _session.Output.Read(buffer, offset, count);
                if (read == 0)
                {
                    _finished = true;
                    string error = _session.FinishAndGetError();
                    if (error != null)
                        throw new IOException("curl failed while receiving the response: " + error);
                }
                return read;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
