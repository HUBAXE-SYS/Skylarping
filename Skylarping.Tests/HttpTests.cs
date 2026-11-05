/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Tests for the shared HTTP code, against a raw socket server on loopback, which
// (unlike HttpListener) can drop a kept-alive connection on purpose.
/*==========================================================*/

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Skylarping.Net;

namespace Skylarping.Tests
{
    [TestFixture]
    public class HttpTests
    {
        /// <summary>
        ///  Answers the first request on each connection and offers to keep it alive, then drops
        ///  the connection instead of answering a second one. Lemonade does much the same to a
        ///  connection left idle for 5 s, which a client can't see until it has sent the request.
        /// </summary>
        private sealed class ForgetfulServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);

            public ForgetfulServer()
            {
                _listener.Start();
                new Thread(Accept) { IsBackground = true }.Start();
            }

            public string Url
            {
                get { return "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/"; }
            }

            private void Accept()
            {
                try
                {
                    while (true)
                    {
                        TcpClient client = _listener.AcceptTcpClient();
                        new Thread(() => Serve(client)) { IsBackground = true }.Start();
                    }
                }
                catch (SocketException)
                {
                    // Stopped.
                }
                catch (ObjectDisposedException)
                {
                    // Stopped.
                }
            }

            private static void Serve(TcpClient client)
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    try
                    {
                        if (!ReadRequest(stream))
                            return;
                        byte[] reply = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\nKeep-Alive: timeout=5, max=100\r\nContent-Length: 2\r\n\r\nok");
                        stream.Write(reply, 0, reply.Length);
                        ReadRequest(stream); // whatever comes next on this connection goes unanswered
                    }
                    catch (IOException)
                    {
                        // The client went away, which is fine here.
                    }
                }
            }

            /// <summary>Reads one request. False if the connection closed first.</summary>
            private static bool ReadRequest(Stream stream)
            {
                var text = new StringBuilder();
                int end;
                while ((end = text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal)) < 0)
                {
                    int b = stream.ReadByte();
                    if (b < 0)
                        return false;
                    text.Append((char)b);
                }

                int length = 0;
                foreach (string line in text.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        length = int.Parse(line.Substring(line.IndexOf(':') + 1).Trim());
                for (int i = 0; i < length; i++)
                    if (stream.ReadByte() < 0)
                        return false;
                return true;
            }

            public void Dispose()
            {
                _listener.Stop();
            }
        }

        private static string Post(string url)
        {
            var request = new HttpRequest(url, "POST") { TimeoutMilliseconds = 5000 }.WithText("{}");
            using (HttpResponse response = Http.Open(request))
                return response.ReadAllText();
        }

        [Test]
        public void Open_ServerDroppedTheLastConnection_RequestStillGoesThrough()
        {
            using (var server = new ForgetfulServer())
            {
                Assert.That(Post(server.Url), Is.EqualTo("ok"));
                Assert.That(Post(server.Url), Is.EqualTo("ok"));
            }
        }
    }
}
