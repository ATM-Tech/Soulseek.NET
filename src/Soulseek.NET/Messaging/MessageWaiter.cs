﻿namespace Soulseek.NET.Messaging
{
    using Soulseek.NET.Common;
    using System;
    using System.Collections.Concurrent;
    using System.Threading.Tasks;
    using SystemTimer = System.Timers.Timer;

    public class MessageWaiter
    {
        public MessageWaiter(int timeout)
        {
            Timeout = timeout;
            TimeoutTimer = new SystemTimer()
            {
                Enabled = true,
                AutoReset = true,
                Interval = 500,
            };

            TimeoutTimer.Elapsed += CompleteExpiredWaits;
        }

        private int Timeout { get; set; }
        private SystemTimer TimeoutTimer { get; set; }
        private ConcurrentDictionary<object, ConcurrentQueue<PendingWait>> Waits { get; set; } = new ConcurrentDictionary<object, ConcurrentQueue<PendingWait>>();

        public void Complete(MessageCode code, object result)
        {
            Complete(code, null, result);
        }

        public void Complete(MessageCode code, object token, object result)
        {
            var key = GetKey(code, token);

            if (Waits.TryGetValue(key, out var queue))
            {
                if (queue.TryDequeue(out var wait))
                {
                    wait.TaskCompletionSource.SetResult(result);
                }
            }
        }

        public TaskCompletionSource<object> Wait(MessageCode code, object token = null)
        {
            var key = GetKey(code, token);
            var wait = new PendingWait()
            {
                TaskCompletionSource = new TaskCompletionSource<object>(),
                DateTime = DateTime.UtcNow,
            };

            Waits.AddOrUpdate(key, new ConcurrentQueue<PendingWait>(new[] { wait }), (_, queue) =>
            {
                queue.Enqueue(wait);
                return queue;
            });

            return wait.TaskCompletionSource;
        }

        private void CompleteExpiredWaits(object sender, object e)
        {
            foreach (var queue in Waits)
            {
                if (queue.Value.TryPeek(out var nextPendingWait) && nextPendingWait.DateTime.AddSeconds(Timeout) < DateTime.UtcNow)
                {
                    if (queue.Value.TryDequeue(out var timedOutWait))
                    {
                        var code = ((Tuple<MessageCode, object>)queue.Key).Item1;
                        timedOutWait.TaskCompletionSource.SetException(new MessageTimeoutException($"Message wait for {code} timed out after {Timeout} seconds."));
                    }
                }
            }
        }

        private object GetKey(MessageCode code, object token)
        {
            return new Tuple<MessageCode, object>(code, token);
        }

        private class PendingWait
        {
            public DateTime DateTime { get; set; }
            public TaskCompletionSource<object> TaskCompletionSource { get; set; }
        }
    }
}