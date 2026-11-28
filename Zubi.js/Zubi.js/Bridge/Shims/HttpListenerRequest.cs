using System.Collections.Specialized;

namespace Zubi.js.Bridge.Shims
{
    internal sealed class HttpListenerRequest
    {
        public HttpListenerRequest(string method, Uri url, NameValueCollection headers, NameValueCollection queryString)
        {
            HttpMethod = method;
            Url = url;
            Headers = headers;
            QueryString = queryString;
        }

        public string HttpMethod { get; }
        public Uri Url { get; }
        public NameValueCollection Headers { get; }
        public NameValueCollection QueryString { get; }

        public bool IsWebSocketRequest
        {
            get
            {
                var conn = Headers["Connection"];
                var upgrade = Headers["Upgrade"];
                return conn is not null &&
                       conn.Contains("upgrade", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}