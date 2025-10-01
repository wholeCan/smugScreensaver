using System;
using System.Threading;
using System.Threading.Tasks;

namespace andyScreenSaver.windows.Helpers
{
    internal sealed class AsyncManualResetEvent
    {
        private volatile TaskCompletionSource<bool> _tcs;

        public AsyncManualResetEvent(bool initialState = false)
        {
            _tcs = CreateTcs(initialState);
        }

        private static TaskCompletionSource<bool> CreateTcs(bool set)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (set)
            {
                tcs.TrySetResult(true);
            }
            return tcs;
        }

        public void Set()
        {
            var tcs = _tcs;
            tcs.TrySetResult(true);
        }

        public void Reset()
        {
            if (_tcs.Task.IsCompleted)
            {
                Interlocked.Exchange(ref _tcs, CreateTcs(false));
            }
        }

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            var waitTask = _tcs.Task;
            if (!cancellationToken.CanBeCanceled)
            {
                await waitTask.ConfigureAwait(false);
                return;
            }

            if (waitTask.IsCompleted)
            {
                await waitTask.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), cancelTcs))
            {
                var completed = await Task.WhenAny(waitTask, cancelTcs.Task).ConfigureAwait(false);
                if (completed == cancelTcs.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                await waitTask.ConfigureAwait(false);
            }
        }
    }
}
