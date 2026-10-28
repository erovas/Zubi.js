using System.Security.Cryptography;
using System.Text;

namespace Zubi.js.Bridge
{
    /// <summary>
    /// Crea y verifica el token para conexion
    /// </summary>
    internal sealed class SessionStore
    {
        #region Fields

        private readonly object _lock;
        private readonly string _masterToken;
        private readonly HashSet<string> _tabTokens;
        private bool _masterConsumed;

        #endregion

        public string MasterToken
        {
            get
            {
                lock (_lock)
                    return _masterToken;
            }
        }

        public SessionStore()
        {
            _lock = new();
            _masterToken = NewToken();
            _tabTokens = new(StringComparer.Ordinal);
        }

        public string? ConsumeMasterToken(string? presented)
        {
            if (string.IsNullOrWhiteSpace(presented))
                return null;

            lock (_lock)
            {
                if (_masterConsumed)
                    return null;

                if (!FixedTimeEquals(presented, _masterToken))
                    return null;

                _masterConsumed = true;
                var tab = NewToken();
                _tabTokens.Add(tab);
                return tab;
            }
        }

        public bool IsValidTabToken(string? presented)
        {
            if (string.IsNullOrWhiteSpace(presented))
                return false;

            lock (_lock)
                return _tabTokens.Contains(presented);
        }

        #region Helpers

        private static string NewToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var ab = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
        }

        #endregion

    }

}