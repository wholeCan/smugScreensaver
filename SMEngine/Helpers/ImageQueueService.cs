using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SMEngine
{
    internal sealed class ImageQueueService
    {
        private readonly CSMEngine _engine;
        public ImageQueueService(CSMEngine engine)
        {
            _engine = engine;
        }

        public void Run(CancellationToken token)
        {
            if (_engine.Running) return;
            _engine.Running = true;
            try
            {
                while (!token.IsCancellationRequested && _engine.Running)
                {
                    if (_engine.qSize < CSMEngine.MinQ && _engine.qSize < CSMEngine.MaximumQ)
                    {
                        try
                        {
                            if (!_engine.screensaverExpired())
                            {
                                var imageSet = ImageSelectionHelper.TryGetRandomImage(_engine);
                                if (imageSet != null)
                                {
                                    lock (_engine.ImageQueue)
                                    {
                                        _engine.ImageQueue.Enqueue(imageSet);
                                    }
                                }
                            }
                            else
                            {
                                Task.Delay(5000, token).Wait(token);
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch (Exception ex)
                        {
                            _engine.doException(ex.Message);
                            _engine.Running = false;
                        }
                    }
                    Task.Delay(50, token).Wait(token);
                }
            }
            finally
            {
                _engine.Running = false;
            }
        }
    }
}
