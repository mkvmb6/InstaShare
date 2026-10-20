using Newtonsoft.Json;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;

namespace InstaShare.Services
{
    /// <summary>
    /// Keeps one InstaShare per user. Later launches (Explorer multi-select, Send To, toasts) hand their
    /// arguments to the running instance over a named pipe and exit.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private static readonly string Id = $"{Constants.AppName}-{WindowsIdentity.GetCurrent().User?.Value}";
        private static string PipeName => $"{Id}-args";

        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _stop = new();

        private SingleInstance(Mutex mutex) => _mutex = mutex;

        /// <summary>
        /// Raised on a background thread with the arguments of a later launch.
        /// </summary>
        public event Action<string[]>? ArgumentsReceived;

        /// <returns>The instance lock, or null if another instance already holds it.</returns>
        public static SingleInstance? TryAcquire()
        {
            var mutex = new Mutex(initiallyOwned: true, $@"Local\{Id}", out var createdNew);
            if (createdNew)
                return new SingleInstance(mutex);
            mutex.Dispose();
            return null;
        }

        /// <returns>False if the running instance couldn't be reached.</returns>
        public static bool TrySendToRunningInstance(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                // The other instance may have only just started; Connect waits for its pipe to appear.
                client.Connect(TimeSpan.FromSeconds(5));
                using var writer = new StreamWriter(client);
                writer.Write(JsonConvert.SerializeObject(args));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void StartListening() => _ = Listen(_stop.Token);

        private async Task Listen(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(server);
                    var args = JsonConvert.DeserializeObject<string[]>(await reader.ReadToEndAsync(cancellationToken));
                    if (args != null)
                        ArgumentsReceived?.Invoke(args);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // A client that hung up or sent junk; keep serving the next one.
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
