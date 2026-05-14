using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Joker.UnityCli.Editor.Models;

namespace Joker.UnityCli.Editor.ScriptServer
{
    public class Session
    {
        private int _started;
        public TaskCompletionSource<ExecResult> CompletionSource { get; }
        public DateTime CreatedUtc { get; }

        public Session()
        {
            CompletionSource = new TaskCompletionSource<ExecResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            CreatedUtc = DateTime.UtcNow;
        }

        public bool TryStart()
        {
            return Interlocked.CompareExchange(ref _started, 1, 0) == 0;
        }
    }

    public static class SessionManager
    {
        private static readonly ConcurrentDictionary<string, Session> Sessions
            = new ConcurrentDictionary<string, Session>();

        private const int MaxSessions = 100;
        private static readonly TimeSpan SessionExpiry = TimeSpan.FromMinutes(5);
        private static int _cleanupCounter;

        public static Session GetOrCreate(string requestId)
        {
            var session = Sessions.GetOrAdd(requestId, _ => new Session());
            MaybeCleanup();
            return session;
        }

        public static void CancelAll()
        {
            foreach (var kvp in Sessions)
            {
                kvp.Value.CompletionSource.TrySetCanceled();
            }
            Sessions.Clear();
        }

        internal static void Clear()
        {
            Sessions.Clear();
            _cleanupCounter = 0;
        }

        private static void MaybeCleanup()
        {
            var count = Interlocked.Increment(ref _cleanupCounter);
            if (count % 10 != 0)
                return;

            var now = DateTime.UtcNow;
            foreach (var kvp in Sessions)
            {
                if (kvp.Value.CompletionSource.Task.IsCompleted
                    && now - kvp.Value.CreatedUtc > SessionExpiry)
                {
                    Session removed;
                    Sessions.TryRemove(kvp.Key, out removed);
                }
            }

            if (Sessions.Count > MaxSessions)
            {
                foreach (var kvp in Sessions)
                {
                    if (Sessions.Count <= MaxSessions)
                        break;
                    if (kvp.Value.CompletionSource.Task.IsCompleted)
                    {
                        Session removed;
                        Sessions.TryRemove(kvp.Key, out removed);
                    }
                }
            }
        }
    }
}
