using System.ComponentModel;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Zubi.js.Helpers
{
    internal sealed class CodeException : Exception
    {
        public string Code { get; }
        public CodeException(string code, string message) : base(message) 
        {
            Code = code;
        }

        public static CodeException FromException(Exception ex)
        {
            // 1. Unwrap (reflexión, AggregateException de Tasks)
            while (true)
            {
                if (ex is CodeException rex) return rex;
                if (ex is TargetInvocationException { InnerException: not null } tie) { ex = tie.InnerException; continue; }
                if (ex is AggregateException { InnerExceptions.Count: > 0 } agg) { ex = agg.InnerExceptions[0]; continue; }
                break;
            }

            // 2. Cancelación / timeout
            if (ex is OperationCanceledException oce)
            {
                return oce.CancellationToken.IsCancellationRequested
                    ? new CodeException("EABORT", "Cancelado por el cliente")
                    : new CodeException("ETIMEDOUT", "Tiempo de espera agotado");
            }
            if (ex is TimeoutException)
                return new CodeException("ETIMEDOUT", ex.Message);

            // 3. Argumentos
            if (ex is ArgumentNullException ane)
                return new CodeException("EINVAL", $"'{ane.ParamName}' es obligatorio");
            if (ex is ArgumentOutOfRangeException aore)
                return new CodeException("ERANGE", $"'{aore.ParamName}' fuera de rango: {aore.Message}");
            if (ex is ArgumentException ae)
                return new CodeException("EINVAL", ae.Message);

            // 4. Formato / parseo
            if (ex is FormatException or JsonException)
                return new CodeException("EINVAL", ex.Message);
            if (ex is OverflowException)
                return new CodeException("ERANGE", ex.Message);

            // 5. FS — antes que IOException genérico, porque son subclases
            if (ex is FileNotFoundException or DirectoryNotFoundException)
                return new CodeException("ENOENT", ex.Message);
            if (ex is PathTooLongException)
                return new CodeException("ENAMETOOLONG", ex.Message);
            if (ex is DriveNotFoundException)
                return new CodeException("ENODEV", ex.Message);
            if (ex is UnauthorizedAccessException or System.Security.SecurityException)
                return new CodeException("EACCES", ex.Message);

            // 6. IO por código del SO
            if (ex is IOException io)
                return FromIOException(io);

            // 7. Memoria
            if (ex is OutOfMemoryException or InsufficientMemoryException)
                return new CodeException("ENOMEM", "Memoria insuficiente");

            // 8. Criptografía — AuthenticationTagMismatch antes que Cryptographic genérico
            if (ex is AuthenticationTagMismatchException)
                return new CodeException("EAUTH", "Tag de autenticación inválido");
            if (ex is CryptographicException)
                return new CodeException("ECRYPTO", ex.Message);

            // 9. Red
            if (ex is SocketException se) return FromSocketException(se);
            if (ex is HttpRequestException hre) return FromHttpException(hre);
            if (ex is System.Net.WebException we) return FromWebException(we);

            // 10. Procesos / código nativo
            if (ex is Win32Exception w32)
                return FromWin32Exception(w32);

            // 11. Semánticas comunes
            if (ex is PlatformNotSupportedException)
                return new CodeException("ENOTSUP", $"No soportado en esta plataforma: {ex.Message}");
            if (ex is NotSupportedException)
                return new CodeException("ENOTSUP", ex.Message);
            if (ex is NotImplementedException)
                return new CodeException("ENOTIMPL", ex.Message);
            if (ex is ObjectDisposedException ode)
                return new CodeException("EINVAL", $"Objeto liberado: {ode.ObjectName ?? ode.GetType().Name}");
            if (ex is InvalidOperationException)
                return new CodeException("EINVAL", ex.Message);

            // 12. Fallback
            return new CodeException("EINTERNAL", $"{ex.GetType().Name}: {ex.Message}");
        }

        // ---------- Mapeos por SO ----------

        private static CodeException FromIOException(IOException io)
        {
            // Windows: HResult 0x8007XXXX → código Win32 en los 16 bits bajos
            // Unix:    HResult 0x80070000|errno → errno en los 16 bits bajos
            var code = io.HResult & 0xFFFF;
            if (code == 0)
                return new CodeException("EIO", io.Message);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return code switch
                {
                    2 => new CodeException("ENOENT", io.Message),       // FILE_NOT_FOUND
                    3 => new CodeException("ENOENT", io.Message),       // PATH_NOT_FOUND
                    19 => new CodeException("EROFS", io.Message),        // WRITE_PROTECT
                    32 => new CodeException("EBUSY", io.Message),        // SHARING_VIOLATION
                    39 or 112 => new CodeException("ENOSPC", io.Message),       // HANDLE_DISK_FULL / DISK_FULL
                    145 => new CodeException("ENOTEMPTY", io.Message),    // DIR_NOT_EMPTY
                    206 => new CodeException("ENAMETOOLONG", io.Message), // FILENAME_EXCED_RANGE
                    109 or 232 => new CodeException("EPIPE", io.Message),        // BROKEN_PIPE / NO_DATA
                    _ => new CodeException("EIO", io.Message),
                };
            }

            // Unix (Linux + macOS, mismo estándar errno)
            return code switch
            {
                2 => new CodeException("ENOENT", io.Message),
                5 => new CodeException("EIO", io.Message),
                13 => new CodeException("EACCES", io.Message),
                16 => new CodeException("EBUSY", io.Message),
                17 => new CodeException("EEXIST", io.Message),
                20 => new CodeException("ENOTDIR", io.Message),
                21 => new CodeException("EISDIR", io.Message),
                28 => new CodeException("ENOSPC", io.Message),
                30 => new CodeException("EROFS", io.Message),
                32 => new CodeException("EPIPE", io.Message),
                36 => new CodeException("ENAMETOOLONG", io.Message),
                39 => new CodeException("ENOTEMPTY", io.Message),
                50 => new CodeException("ENODEV", io.Message),
                110 => new CodeException("ETIMEDOUT", io.Message),
                _ => new CodeException("EIO", io.Message),
            };
        }

        private static CodeException FromSocketException(SocketException se) =>
            se.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => new CodeException("ECONNREFUSED", se.Message),
                SocketError.ConnectionReset => new CodeException("ECONNRESET", se.Message),
                SocketError.ConnectionAborted => new CodeException("ECONNABORTED", se.Message),
                SocketError.HostUnreachable => new CodeException("EHOSTUNREACH", se.Message),
                SocketError.NetworkUnreachable => new CodeException("ENETUNREACH", se.Message),
                SocketError.NetworkDown => new CodeException("ENETDOWN", se.Message),
                SocketError.HostNotFound => new CodeException("ENOTFOUND", se.Message),
                SocketError.NoData => new CodeException("ENOTFOUND", se.Message),
                SocketError.TimedOut => new CodeException("ETIMEDOUT", se.Message),
                SocketError.AddressAlreadyInUse => new CodeException("EADDRINUSE", se.Message),
                SocketError.AddressNotAvailable => new CodeException("EADDRNOTAVAIL", se.Message),
                SocketError.AccessDenied => new CodeException("EACCES", se.Message),
                SocketError.NoBufferSpaceAvailable => new CodeException("ENOBUFS", se.Message),
                SocketError.IsConnected => new CodeException("EISCONN", se.Message),
                SocketError.NotConnected => new CodeException("ENOTCONN", se.Message),
                _ => new CodeException("ENET", se.Message),
            };

        private static CodeException FromHttpException(HttpRequestException hre)
        {
            if (hre.InnerException is SocketException inner)
                return FromSocketException(inner);
            if (hre.StatusCode is { } status)
            {
                var n = (int)status;
                return n switch
                {
                    401 or 403 => new CodeException("EACCES", $"HTTP {n}: {hre.Message}"),
                    404 => new CodeException("ENOENT", $"HTTP 404: {hre.Message}"),
                    408 => new CodeException("ETIMEDOUT", $"HTTP 408: {hre.Message}"),
                    429 => new CodeException("EBUSY", $"HTTP 429: {hre.Message}"),
                    >= 500 => new CodeException("EHTTP", $"HTTP {n}: {hre.Message}"),
                    _ => new CodeException("EHTTP", $"HTTP {n}: {hre.Message}"),
                };
            }
            return new CodeException("ENET", hre.Message);
        }

        private static CodeException FromWebException(System.Net.WebException we)
        {
            if (we.InnerException is SocketException inner)
                return FromSocketException(inner);
            return we.Status switch
            {
                System.Net.WebExceptionStatus.Timeout => new CodeException("ETIMEDOUT", we.Message),
                System.Net.WebExceptionStatus.ConnectFailure => new CodeException("ECONNREFUSED", we.Message),
                System.Net.WebExceptionStatus.NameResolutionFailure => new CodeException("ENOTFOUND", we.Message),
                _ => new CodeException("ENET", we.Message),
            };
        }

        private static CodeException FromWin32Exception(Win32Exception w32)
        {
            // Win32Exception.NativeErrorCode = Win32 error en Windows, errno en Unix
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return w32.NativeErrorCode switch
                {
                    2 => new CodeException("ENOENT", w32.Message),     // FILE_NOT_FOUND
                    3 => new CodeException("ENOENT", w32.Message),     // PATH_NOT_FOUND
                    5 => new CodeException("EACCES", w32.Message),     // ACCESS_DENIED
                    32 => new CodeException("EBUSY", w32.Message),      // SHARING_VIOLATION
                    87 => new CodeException("EINVAL", w32.Message),     // INVALID_PARAMETER
                    193 => new CodeException("EBADEXEC", w32.Message),   // BAD_EXE_FORMAT
                    267 => new CodeException("ENOTDIR", w32.Message),    // DIRECTORY
                    _ => new CodeException("EEXEC", w32.Message),
                };
            }
            return w32.NativeErrorCode switch
            {
                2 => new CodeException("ENOENT", w32.Message),    // ENOENT
                13 => new CodeException("EACCES", w32.Message),    // EACCES
                8 => new CodeException("EBADEXEC", w32.Message),  // ENOEXEC
                22 => new CodeException("EINVAL", w32.Message),    // EINVAL
                _ => new CodeException("EEXEC", w32.Message),
            };
        }

    }
}