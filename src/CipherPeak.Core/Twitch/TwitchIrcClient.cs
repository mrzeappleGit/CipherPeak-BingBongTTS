using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using CipherPeak.Core.Config;
using CipherPeak.Core.Logging;

namespace CipherPeak.Core.Twitch
{
    /// <summary>
    /// Read-only Twitch chat connection over TLS IRC (irc.chat.twitch.tv:6697), run on its own
    /// thread with exponential-backoff reconnect. Received messages land in a bounded concurrent
    /// queue that the game thread drains, so nothing here ever touches Unity objects.
    ///
    /// With no OAuth token configured it logs in anonymously as justinfan&lt;n&gt;. That form is not in
    /// Twitch's documentation but is the long-standing way to read chat without credentials; the
    /// documented authenticated path is used whenever a token is present.
    /// </summary>
    public sealed class TwitchIrcClient : IDisposable
    {
        private const int MaxPendingMessages = 200;

        private readonly Func<ModSettings> _settings;
        private readonly ILog _log;
        private readonly ConcurrentQueue<ChatMessage> _inbox = new ConcurrentQueue<ChatMessage>();

        private Thread _thread;
        private volatile bool _running;
        private volatile bool _connected;
        private volatile string _status = "stopped";
        private CancellationTokenSource _cancellation;

        /// <summary>Set once a fatal auth failure happens; stops the reconnect loop from hammering Twitch.</summary>
        private volatile bool _authFailed;

        public TwitchIrcClient(Func<ModSettings> settings, ILog log)
        {
            _settings = settings;
            _log = log ?? NullLog.Instance;
        }

        public bool IsConnected => _connected;
        public string Status => _status;

        public bool TryDequeue(out ChatMessage message) => _inbox.TryDequeue(out message);

        public void Start()
        {
            if (_running) return;

            string channel = (_settings().Twitch.Channel ?? "").Trim().TrimStart('#').ToLowerInvariant();
            if (channel.Length == 0)
            {
                _status = "no channel configured";
                _log.Warn("Twitch channel is not set; chat ingestion stays off.");
                return;
            }

            _authFailed = false;
            _running = true;
            _cancellation = new CancellationTokenSource();
            _thread = new Thread(RunLoop) { IsBackground = true, Name = "CipherPeak-Twitch" };
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _connected = false;
            _status = "stopped";
            try { _cancellation?.Cancel(); } catch { /* already disposed */ }

            var thread = _thread;
            _thread = null;
            if (thread != null && !thread.Join(TimeSpan.FromSeconds(2)))
                _log.Warn("Twitch thread did not stop in time; it is a background thread and will exit on its own.");

            while (_inbox.TryDequeue(out _)) { }
        }

        public void Dispose()
        {
            Stop();
            _cancellation?.Dispose();
            _cancellation = null;
        }

        private void RunLoop()
        {
            double delay = _settings().Twitch.ReconnectDelaySeconds;
            var token = _cancellation.Token;

            while (_running && !token.IsCancellationRequested)
            {
                try
                {
                    ConnectAndRead(token);
                    delay = _settings().Twitch.ReconnectDelaySeconds; // clean session: reset backoff
                }
                catch (Exception ex)
                {
                    _status = "disconnected: " + ex.Message;
                    _log.Warn("Twitch connection dropped: " + ex.Message);
                }

                _connected = false;
                if (!_running || token.IsCancellationRequested) break;

                if (_authFailed)
                {
                    _status = "authentication failed; fix the token and re-enable the mod";
                    _log.Error(_status);
                    break;
                }

                _status = "reconnecting in " + (int)delay + "s";
                if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(delay))) break;
                delay = Math.Min(delay * 2, Math.Max(1, _settings().Twitch.MaxReconnectDelaySeconds));
            }

            _connected = false;
            if (_status.StartsWith("reconnecting", StringComparison.Ordinal)) _status = "stopped";
        }

        private void ConnectAndRead(CancellationToken token)
        {
            var twitch = _settings().Twitch;
            string channel = (twitch.Channel ?? "").Trim().TrimStart('#').ToLowerInvariant();

            using (var tcp = new TcpClient())
            {
                tcp.Connect(twitch.Host, twitch.Port);
                tcp.ReceiveTimeout = 5 * 60 * 1000; // Twitch pings well inside this

                using (var ssl = OpenTls(tcp, twitch))
                using (var reader = new StreamReader(ssl, new UTF8Encoding(false)))
                using (var writer = new StreamWriter(ssl, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" })
                {
                    Login(writer, twitch, channel);

                    _connected = true;
                    _status = "connected to #" + channel;
                    _log.Info("Twitch chat connected to #" + channel + ".");

                    string line;
                    while (_running && !token.IsCancellationRequested && (line = reader.ReadLine()) != null)
                        HandleLine(line, writer);
                }
            }
        }

        private SslStream OpenTls(TcpClient tcp, TwitchSettings twitch)
        {
            bool warned = false;

            var stream = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (sender, cert, chain, errors) =>
                {
                    if (errors == SslPolicyErrors.None) return true;
                    if (!twitch.AllowInsecureTls) return false;
                    if (!warned)
                    {
                        warned = true;
                        _log.Warn("Twitch TLS certificate could not be validated (" + errors +
                                  "). Continuing because Twitch.AllowInsecureTls is true - this is normally " +
                                  "Unity's Mono runtime missing a root certificate store, not an attack. " +
                                  "Set AllowInsecureTls=false to refuse instead.");
                    }
                    return true;
                });

            try
            {
                stream.AuthenticateAsClient(twitch.Host, null, SslProtocols.Tls12, checkCertificateRevocation: false);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
            return stream;
        }

        private void Login(StreamWriter writer, TwitchSettings twitch, string channel)
        {
            string token = (twitch.OAuthToken ?? "").Trim();
            string username = (twitch.Username ?? "").Trim().ToLowerInvariant();

            if (token.Length > 0)
            {
                if (!token.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)) token = "oauth:" + token;
                SecretScrubber.Register(token);
                if (username.Length == 0)
                    throw new InvalidOperationException("Twitch.Username must be set when an OAuth token is configured.");
            }
            else
            {
                // Documented path needs a token; this is the anonymous read-only fallback.
                token = "SCHMOOPIIE";
                username = "justinfan" + new Random().Next(10000, 99999);
                _log.Info("No Twitch OAuth token configured; connecting anonymously (read-only).");
            }

            writer.WriteLine("PASS " + token);
            writer.WriteLine("NICK " + username);
            writer.WriteLine("CAP REQ :twitch.tv/tags twitch.tv/commands");
            writer.WriteLine("JOIN #" + channel);
        }

        private void HandleLine(string line, StreamWriter writer)
        {
            if (IrcMessageParser.IsPing(line))
            {
                writer.WriteLine(IrcMessageParser.PongFor(line));
                return;
            }

            if (IrcMessageParser.IsAuthFailure(line))
            {
                _authFailed = true;
                throw new AuthenticationException("Twitch rejected the login credentials.");
            }

            ChatMessage message;
            if (!IrcMessageParser.TryParsePrivmsg(line, DateTimeOffset.UtcNow, out message)) return;

            // Bounded: if the game thread stalls we drop the oldest rather than grow without limit.
            if (_inbox.Count >= MaxPendingMessages) _inbox.TryDequeue(out _);
            _inbox.Enqueue(message);
        }
    }
}
