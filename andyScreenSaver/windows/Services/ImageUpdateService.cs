using andyScreenSaver.windows.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace andyScreenSaver.windows.Services
{
    /// <summary>
    /// Manages the asynchronous image update loop
    /// </summary>
    public class ImageUpdateService : IDisposable
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _updateTask;
        private readonly AsyncManualResetEvent _pauseGate;
        private readonly Func<Task> _updateAction;
        private readonly Func<int> _calculateDelayMs;
        private readonly Action<Exception, string> _logError;
        private bool _isRunning;

        public bool IsRunning => _isRunning;

        public ImageUpdateService(
            Func<Task> updateAction,
            Func<int> calculateDelayMs,
            Action<Exception, string> logError)
        {
            _updateAction = updateAction ?? throw new ArgumentNullException(nameof(updateAction));
            _calculateDelayMs = calculateDelayMs ?? throw new ArgumentNullException(nameof(calculateDelayMs));
            _logError = logError ?? throw new ArgumentNullException(nameof(logError));
            _pauseGate = new AsyncManualResetEvent(initialState: true);
        }

        public void Start()
        {
            Stop();
            _cancellationTokenSource = new CancellationTokenSource();
            _updateTask = RunUpdateLoopAsync(_cancellationTokenSource.Token);
        }

        public async void Stop()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                if (_updateTask != null)
                {
                    try
                    {
                        await _updateTask.ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore cancellation exceptions during shutdown
                    }
                }
            }
            finally
            {
                _updateTask = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public void Pause()
        {
            _pauseGate.Reset();
        }

        public void Resume()
        {
            _pauseGate.Set();
        }

        private async Task RunUpdateLoopAsync(CancellationToken token)
        {
            _isRunning = true;

            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // Wait if paused
                    await _pauseGate.WaitAsync(token).ConfigureAwait(false);

                    // Execute the update action
                    await _updateAction().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logError(ex, "Image update failed: " + ex.Message);
                }

                // Calculate and apply delay
                var delayMs = _calculateDelayMs();
                if (delayMs > 0)
                {
                    try
                    {
                        await Task.Delay(delayMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            _isRunning = false;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
