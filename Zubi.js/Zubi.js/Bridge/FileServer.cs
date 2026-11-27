using System.Net;
using System.Text;
using Zubi.js.Helpers;
using Zubi.js.PluginManager;
using Zubi.js.Scripts;

namespace Zubi.js.Bridge
{
    /// <summary>
    /// Genera el servidor http para servir los archivos
    /// </summary>
    internal sealed class FileServer
    {
        private readonly HttpListener _listener = new();
        private readonly string _rootPath;
        private readonly SessionStore _session;
        private readonly Plugins _plugins;
        private int _indexRequestCount;

        public Func<HttpListenerContext, Task>? WebSocketHandler { get; set; }

        public FileServer(string baseUrl, string rootPath, SessionStore session, Plugins plugins)
        {
            _listener.Prefixes.Add(baseUrl);
            _rootPath = Path.GetFullPath(rootPath);
            _session = session;
            _plugins = plugins;
        }

        public void Start()
        {
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public void Stop()
        {
            try { _listener.Stop(); }
            catch { }
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;

                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleAsync(ctx);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[http] {ex.Message}");
                        try
                        {
                            ctx.Response.StatusCode = 500;
                            ctx.Response.Close();
                        }
                        catch { }
                    }
                });
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var path = req.Url!.AbsolutePath;

            // Se está llamando al WebSocket
            if (req.IsWebSocketRequest && path == "/ws" && WebSocketHandler is not null)
            {
                await WebSocketHandler(ctx);
                return;
            }

            var rel = path.TrimStart('/');

            if (string.IsNullOrWhiteSpace(rel))
                rel = Utils.INDEX_HTML;

            var full = Path.GetFullPath(Path.Combine(_rootPath, rel));
            var isIndex = rel.Equals(Utils.INDEX_HTML, StringComparison.OrdinalIgnoreCase);
            var exists = File.Exists(full);

            if (!isIndex && !exists)
            {
                await WriteTextAsync(ctx, 404, "Not Found");
                return;
            }

            var contentType = GetContentType(full);

            // Se va a cargar el index.html
            // Se inyecta la magia
            if (isIndex)
            {
                var html = exists ? await File.ReadAllTextAsync(full) : Resources.ErrorPage;

                if (req.HttpMethod == "GET")
                {
                    // Solo limpia a partir de la segunda petición (recarga real)
                    if (Interlocked.Increment(ref _indexRequestCount) > 1)
                        _plugins.DisposeAll();
                    
                    var tabToken = _session.ConsumeMasterToken(req.QueryString["token"]);
                    var meta = tabToken is not null ? $"<meta name=\"bridge-token\" content=\"{tabToken}\">" : string.Empty;
                    var script = "<script>";
                    script += Environment.NewLine;
                    script += Resources.MainScript;
                    script += Environment.NewLine;
                    script += "</script>";
                    var result = Environment.NewLine + meta + Environment.NewLine + script + Environment.NewLine;
                    html = result + html;
                }

                await WriteBytesAsync(ctx, 200, Encoding.UTF8.GetBytes(html), contentType, true);
                return;
            }

            // Entregar el archivo pedido por XHR, fecth, <script>, etc.
            await WriteBytesAsync(ctx, 200, await File.ReadAllBytesAsync(full), contentType);
        }

        #region Helpers

        private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Texto y documentos ──────────────────────────────────────────
            { "txt", "text/plain" },
            { "text", "text/plain" },
            { "conf", "text/plain" },
            { "def", "text/plain" },
            { "list", "text/plain" },
            { "log", "text/plain" },
            { "in", "text/plain" },
            { "ini", "text/plain" },
            { "cfg", "text/plain" },
            { "md", "text/markdown" },
            { "markdown", "text/markdown" },
            { "html", "text/html" },
            { "htm", "text/html" },
            { "xhtml", "application/xhtml+xml" },
            { "css", "text/css" },
            { "js", "text/javascript" },
            { "mjs", "text/javascript" },
            { "jsx", "text/jscript" },
            { "json", "application/json" },
            { "jsonld", "application/ld+json" },
            { "xml", "application/xml" },
            { "xsl", "application/xml" },
            { "xslt", "application/xslt+xml" },
            { "dtd", "text/xml" },
            { "csv", "text/csv" },
            { "tsv", "text/tab-separated-values" },
            { "ics", "text/calendar" },
            { "ifb", "text/calendar" },
            { "vcf", "text/vcard" },
            { "vcard", "text/vcard" },
            { "rtf", "application/rtf" },
            { "pdf", "application/pdf" },
            { "ps", "application/postscript" },
            { "eps", "application/postscript" },
            { "ai", "application/postscript" },
            { "tex", "application/x-latex" },
            { "latex", "application/x-latex" },
            { "sgml", "text/sgml" },
            { "sgm", "text/sgml" },
            { "epub", "application/epub+zip" },
            { "mobi", "application/x-mobipocket-ebook" },
            { "prc", "application/x-mobipocket-ebook" },
            { "azw", "application/vnd.amazon.ebook" },
            { "azw3", "application/vnd.amazon.ebook" },

            // ── Hojas de cálculo y ofimática ─────────────────────────────────
            { "xls", "application/vnd.ms-excel" },
            { "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            { "xlsm", "application/vnd.ms-excel.sheet.macroEnabled.12" },
            { "xlsb", "application/vnd.ms-excel.sheet.binary.macroEnabled.12" },
            { "xlt", "application/vnd.ms-excel" },
            { "xltx", "application/vnd.openxmlformats-officedocument.spreadsheetml.template" },
            { "xltm", "application/vnd.ms-excel.template.macroEnabled.12" },
            { "ods", "application/vnd.oasis.opendocument.spreadsheet" },
            { "doc", "application/msword" },
            { "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            { "docm", "application/vnd.ms-word.document.macroEnabled.12" },
            { "dot", "application/msword" },
            { "dotx", "application/vnd.openxmlformats-officedocument.wordprocessingml.template" },
            { "dotm", "application/vnd.ms-word.template.macroEnabled.12" },
            { "odt", "application/vnd.oasis.opendocument.text" },
            { "ott", "application/vnd.oasis.opendocument.text-template" },
            { "ppt", "application/vnd.ms-powerpoint" },
            { "pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
            { "pptm", "application/vnd.ms-powerpoint.presentation.macroEnabled.12" },
            { "pot", "application/vnd.ms-powerpoint" },
            { "potx", "application/vnd.openxmlformats-officedocument.presentationml.template" },
            { "potm", "application/vnd.ms-powerpoint.template.macroEnabled.12" },
            { "pps", "application/vnd.ms-powerpoint" },
            { "ppsx", "application/vnd.openxmlformats-officedocument.presentationml.slideshow" },
            { "ppsm", "application/vnd.ms-powerpoint.slideshow.macroEnabled.12" },
            { "odp", "application/vnd.oasis.opendocument.presentation" },
            { "otp", "application/vnd.oasis.opendocument.presentation-template" },
            { "pub", "application/x-mspublisher" },
            { "vsd", "application/vnd.visio" },
            { "vsdx", "application/vnd.visio" },
            { "one", "application/onenote" },
            { "onetoc", "application/onenote" },
            { "onetoc2", "application/onenote" },

            // ── Imágenes ─────────────────────────────────────────────────────
            { "png", "image/png" },
            { "apng", "image/apng" },
            { "jpg", "image/jpeg" },
            { "jpeg", "image/jpeg" },
            { "jpe", "image/jpeg" },
            { "jfif", "image/jpeg" },
            { "pjpeg", "image/jpeg" },
            { "pjp", "image/jpeg" },
            { "gif", "image/gif" },
            { "bmp", "image/bmp" },
            { "dib", "image/bmp" },
            { "webp", "image/webp" },
            { "avif", "image/avif" },
            { "heic", "image/heic" },
            { "heics", "image/heic-sequence" },
            { "heif", "image/heif" },
            { "heifs", "image/heif-sequence" },
            { "jxl", "image/jxl" },
            { "svg", "image/svg+xml" },
            { "svgz", "image/svg+xml" },
            { "ico", "image/x-icon" },
            { "cur", "image/x-icon" },
            { "tiff", "image/tiff" },
            { "tif", "image/tiff" },
            { "psd", "image/vnd.adobe.photoshop" },
            { "xcf", "image/x-xcf" },
            { "raw", "image/x-raw" },
            { "cr2", "image/x-canon-cr2" },
            { "nef", "image/x-nikon-nef" },
            { "orf", "image/x-olympus-orf" },
            { "arw", "image/x-sony-arw" },
            { "dng", "image/x-adobe-dng" },
            { "pbm", "image/x-portable-bitmap" },
            { "pgm", "image/x-portable-graymap" },
            { "ppm", "image/x-portable-pixmap" },
            { "pnm", "image/x-portable-anymap" },
            { "xbm", "image/x-xbitmap" },
            { "xpm", "image/x-xpixmap" },
            { "xwd", "image/x-xwindowdump" },
            { "ras", "image/x-cmu-raster" },
            { "rgb", "image/x-rgb" },
            { "tga", "image/x-tga" },
            { "pcx", "image/x-pcx" },
            { "djvu", "image/vnd.djvu" },
            { "djv", "image/vnd.djvu" },
            { "wbmp", "image/vnd.wap.wbmp" },
            { "wdp", "image/vnd.ms-photo" },
            { "jxr", "image/vnd.ms-photo" },
            { "hdp", "image/vnd.ms-photo" },

            // ── Audio ────────────────────────────────────────────────────────
            { "mp3", "audio/mpeg" },
            { "mpga", "audio/mpeg" },
            { "m4a", "audio/mp4" },
            { "aac", "audio/aac" },
            { "wav", "audio/x-wav" },
            { "weba", "audio/webm" },
            { "ogg", "audio/ogg" },
            { "oga", "audio/ogg" },
            { "spx", "audio/ogg" },
            { "opus", "audio/opus" },
            { "flac", "audio/flac" },
            { "mid", "audio/midi" },
            { "midi", "audio/midi" },
            { "aif", "audio/x-aiff" },
            { "aiff", "audio/x-aiff" },
            { "aifc", "audio/x-aiff" },
            { "au", "audio/basic" },
            { "snd", "audio/basic" },
            { "ra", "audio/x-pn-realaudio" },
            { "ram", "audio/x-pn-realaudio" },
            { "rm", "application/vnd.rn-realmedia" },
            { "wma", "audio/x-ms-wma" },
            { "mka", "audio/x-matroska" },
            { "amr", "audio/amr" },
            { "ac3", "audio/ac3" },
            { "dts", "audio/vnd.dts" },
            { "m3u", "audio/x-mpegurl" },
            { "m3u8", "application/vnd.apple.mpegurl" },

            // ── Vídeo ────────────────────────────────────────────────────────
            { "mp4", "video/mp4" },
            { "m4v", "video/x-m4v" },
            { "webm", "video/webm" },
            { "ogv", "video/ogg" },
            { "avi", "video/x-msvideo" },
            { "mov", "video/quicktime" },
            { "qt", "video/quicktime" },
            { "mkv", "video/x-matroska" },
            { "mk3d", "video/x-matroska" },
            { "mks", "video/x-matroska" },
            { "flv", "video/x-flv" },
            { "f4v", "video/x-f4v" },
            { "wmv", "video/x-ms-wmv" },
            { "asf", "video/x-ms-asf" },
            { "asx", "video/x-ms-asf" },
            { "mpg", "video/mpeg" },
            { "mpeg", "video/mpeg" },
            { "mpe", "video/mpeg" },
            { "m1v", "video/mpeg" },
            { "m2v", "video/mpeg" },
            { "ts", "video/mp2t" },
            { "m2t", "video/mp2t" },
            { "m2ts", "video/mp2t" },
            { "mts", "video/mp2t" },
            { "3gp", "video/3gpp" },
            { "3gpp", "video/3gpp" },
            { "3g2", "video/3gpp2" },
            { "3gp2", "video/3gpp2" },
            { "h264", "video/h264" },
            { "h261", "video/h261" },
            { "h263", "video/h263" },
            { "vob", "video/x-ms-vob" },
            { "divx", "video/divx" },
            { "xvid", "video/x-msvideo" },

            // ── Fuentes ──────────────────────────────────────────────────────
            { "woff", "font/woff" },
            { "woff2", "font/woff2" },
            { "ttf", "font/ttf" },
            { "otf", "font/otf" },
            { "eot", "application/vnd.ms-fontobject" },
            { "ttc", "font/collection" },
            { "sfnt", "font/sfnt" },

            // ── Archivos comprimidos ─────────────────────────────────────────
            { "zip", "application/zip" },
            { "zipx", "application/zip" },
            { "rar", "application/x-rar-compressed" },
            { "7z", "application/x-7z-compressed" },
            { "tar", "application/x-tar" },
            { "gz", "application/gzip" },
            { "gzip", "application/gzip" },
            { "tgz", "application/gzip" },
            { "bz2", "application/x-bzip2" },
            { "xz", "application/x-xz" },
            { "lz", "application/x-lzip" },
            { "lzma", "application/x-lzma" },
            { "zst", "application/zstd" },
            { "cab", "application/vnd.ms-cab-compressed" },
            { "iso", "application/x-iso9660-image" },
            { "dmg", "application/x-apple-diskimage" },
            { "cpio", "application/x-cpio" },
            { "shar", "application/x-shar" },
            { "arj", "application/x-arj" },
            { "lha", "application/x-lzh-compressed" },
            { "lzh", "application/x-lzh-compressed" },

            // ── Aplicaciones / ejecutables / paquetes ────────────────────────
            { "exe", "application/x-msdownload" },
            { "dll", "application/x-msdownload" },
            { "msi", "application/x-msdownload" },
            { "msp", "application/x-msdownload" },
            { "msm", "application/x-msdownload" },
            { "apk", "application/vnd.android.package-archive" },
            { "aab", "application/x-authorware-bin" },
            { "ipa", "application/x-itunes-ipa" },
            { "appx", "application/appx" },
            { "appxbundle", "application/appxbundle" },
            { "deb", "application/x-debian-package" },
            { "rpm", "application/x-redhat-package-manager" },
            { "jar", "application/java-archive" },
            { "war", "application/java-archive" },
            { "ear", "application/java-archive" },
            { "class", "application/java-vm" },
            { "wasm", "application/wasm" },
            { "swf", "application/x-shockwave-flash" },
            { "crx", "application/x-chrome-extension" },
            { "xpi", "application/x-xpinstall" },

            // ── Modelos 3D / CAD / GIS ───────────────────────────────────────
            { "gltf", "model/gltf+json" },
            { "glb", "model/gltf-binary" },
            { "obj", "model/obj" },
            { "stl", "model/stl" },
            { "ply", "application/ply" },
            { "dae", "model/vnd.collada+xml" },
            { "3ds", "image/x-3ds" },
            { "fbx", "application/octet-stream" },
            { "dwf", "model/vnd.dwf" },
            { "dwg", "image/vnd.dwg" },
            { "dxf", "image/vnd.dxf" },
            { "igs", "model/iges" },
            { "iges", "model/iges" },
            { "shp", "application/x-esri-shape" },
            { "kml", "application/vnd.google-earth.kml+xml" },
            { "kmz", "application/vnd.google-earth.kmz" },
            { "gpx", "application/gpx+xml" },

            // ── Ciencia / ingeniería / médicos ───────────────────────────────
            { "hdf", "application/x-hdf" },
            { "h5", "application/x-hdf5" },
            { "nc", "application/x-netcdf" },
            { "mat", "application/x-matlab-data" },
            { "fits", "image/fits" },
            { "dcm", "application/dicom" },
            { "nii", "application/x-nifti" },
            { "mzml", "application/mzml" },
            { "mzxml", "application/mzxml" },

            // ── Certificados / seguridad ─────────────────────────────────────
            { "cer", "application/x-x509-ca-cert" },
            { "crt", "application/x-x509-ca-cert" },
            { "der", "application/x-x509-ca-cert" },
            { "pem", "application/x-pem-file" },
            { "p12", "application/x-pkcs12" },
            { "pfx", "application/x-pkcs12" },
            { "p7b", "application/x-pkcs7-certificates" },
            { "p7c", "application/pkcs7-mime" },
            { "p7s", "application/pkcs7-signature" },
            { "crl", "application/pkix-crl" },
            { "csr", "application/pkcs10" },
            { "key", "application/x-iwork-keynote-sffkey" },
            { "asc", "application/pgp-signature" },
            { "sig", "application/pgp-signature" },

            // ── Otros formatos comunes ───────────────────────────────────────
            { "bin", "application/octet-stream" },
            { "dat", "application/octet-stream" },
            { "torrent", "application/x-bittorrent" },
            { "sql", "application/sql" },
            { "db", "application/octet-stream" },
            { "sqlite", "application/x-sqlite3" },
            { "reg", "text/plain" },
            { "inf", "application/octet-stream" },
            { "manifest", "text/cache-manifest" },
            { "appcache", "text/cache-manifest" },
            { "vtt", "text/vtt" },
            { "srt", "text/plain" },
            { "ass", "text/plain" },
            { "ssa", "text/plain" },
            { "sub", "text/vnd.dvb.subtitle" },

            // ── Extensiones web modernas y de sistema ────────────────────────
            { "map", "application/json" },
            { "webmanifest", "application/manifest+json" },
            { "appxmanifest", "application/xml" },
            { "pac", "application/x-ns-proxy-autoconfig" },
            { "wsdl", "application/wsdl+xml" },
            { "xsd", "application/xml" },
            { "rss", "application/rss+xml" },
            { "atom", "application/atom+xml" },
            { "rdf", "application/rdf+xml" },
            { "owl", "application/owl+xml" },
            { "ttl", "text/turtle" },
            { "n3", "text/n3" },
            { "nt", "application/n-triples" },

            // ── Fax / mensajería / otros industriales ────────────────────────
            { "g3", "image/g3fax" },
            { "t38", "image/t38" },
            { "hqx", "application/mac-binhex40" },
            { "cpt", "application/mac-compactpro" },
            { "sea", "application/x-stuffit" },
            { "sit", "application/x-stuffit" },
            { "sitx", "application/x-stuffitx" },
            { "mif", "application/vnd.mif" },
            { "fm", "application/vnd.framemaker" },
            { "book", "application/vnd.framemaker" },
            { "pdb", "chemical/x-pdb" },
            { "xyz", "chemical/x-xyz" },
            { "cif", "chemical/x-cif" },
            { "cml", "chemical/x-cml" },
            { "csml", "chemical/x-csml" },
        };

        private static readonly HashSet<string> CharsetTypes = new (StringComparer.OrdinalIgnoreCase)
        {
            "text/plain", "text/html", "text/css", "text/javascript",
            "text/csv", "text/xml", "text/markdown", "text/calendar",
            "text/vcard", "text/sgml", "text/richtext", "text/tab-separated-values",
            "text/vtt", "text/cache-manifest", "text/turtle", "text/n3",
            "application/json", "application/ld+json", "application/xml",
            "application/xhtml+xml", "application/rss+xml", "application/atom+xml",
            "application/manifest+json", "image/svg+xml"
        };

        private static Task WriteTextAsync(HttpListenerContext ctx, int status, string text)
        {
            return WriteBytesAsync(ctx, status, Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8");
        }

        private static async Task WriteBytesAsync(HttpListenerContext ctx, int status, byte[] body, string type, bool securityHeaders = false)
        {
            var res = ctx.Response;
            try
            {
                res.StatusCode = status;
                res.ContentType = type;
                res.ContentLength64 = body.Length;
                res.AddHeader("Cache-Control", "no-store");
                if (securityHeaders)
                {
                    res.AddHeader("Content-Security-Policy", "frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
                    res.AddHeader("X-Content-Type-Options", "nosniff");
                }
                await res.OutputStream.WriteAsync(body);
            }
            finally
            {
                try { res.Close(); }
                catch { }
            }
        }

        private static string GetContentType(string path)
        {
            string ext = Path.GetExtension(path).TrimStart('.');

            if (string.IsNullOrEmpty(ext) || !MimeTypes.TryGetValue(ext, out string? mime))
                return "application/octet-stream";

            return CharsetTypes.Contains(mime) ? mime + "; charset=utf-8" : mime;
        }

        #endregion

    }
}