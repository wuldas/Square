using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace Square.VisualStudio.LanguageServer
{
    [Export(typeof(ILanguageClient))]
    [ContentType(SquareContentTypes.Sqx)]
    [ContentType(SquareContentTypes.Sqv)]
    public sealed class SquareLanguageClient : ILanguageClient
    {
        private readonly object _gate = new object();
        private Process? _serverProcess;

        public string Name => "Square Language Server";

        public IEnumerable<string>? ConfigurationSections => null;

        public object? InitializationOptions => null;

        public IEnumerable<string>? FilesToWatch => null;

        public bool ShowNotificationOnInitializeFailed => true;

        public event AsyncEventHandler<EventArgs>? StartAsync;

#pragma warning disable CS0067
        public event AsyncEventHandler<EventArgs>? StopAsync;
#pragma warning restore CS0067

        public Task<Connection?> ActivateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? throw new InvalidOperationException("Cannot locate the Visual Studio extension directory.");
            var serverDirectory = Path.Combine(assemblyDirectory, "Server");
            var serverAssembly = Path.Combine(serverDirectory, "Square.LanguageServer.dll");
            if (!File.Exists(serverAssembly))
                throw new FileNotFoundException("Bundled Square Language Server was not found.", serverAssembly);

            lock (_gate)
            {
                StopExistingServer();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "\"" + serverAssembly + "\"",
                        WorkingDirectory = serverDirectory,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                if (!process.Start())
                {
                    process.Dispose();
                    return Task.FromResult<Connection?>(null);
                }

                _serverProcess = process;
                return Task.FromResult<Connection?>(new Connection(
                    process.StandardOutput.BaseStream,
                    process.StandardInput.BaseStream));
            }
        }

        public async Task OnLoadedAsync()
        {
            var startAsync = StartAsync;
            if (startAsync != null)
                await startAsync.InvokeAsync(this, EventArgs.Empty);
        }

        public Task OnServerInitializedAsync() => Task.CompletedTask;

        public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(
            ILanguageClientInitializationInfo initializationState)
        {
            lock (_gate)
                StopExistingServer();
            return Task.FromResult<InitializationFailureContext?>(null);
        }

        private void StopExistingServer()
        {
            if (_serverProcess == null) return;
            try
            {
                if (!_serverProcess.HasExited)
                    _serverProcess.Kill();
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }
    }
}
