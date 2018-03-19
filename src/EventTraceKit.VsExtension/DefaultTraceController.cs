namespace EventTraceKit.VsExtension
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using EnvDTE;
    using EventTraceKit.Tracing;
    using EventTraceKit.VsExtension.Debugging;
    using Microsoft.VisualStudio.Threading;
    using Process = System.Diagnostics.Process;

    public interface ITraceController
    {
        event Action<TraceLog> SessionStarting;
        event Action<EventSession> SessionStarted;
        event Action<EventSession> SessionStopped;

        Task<EventSession> StartSessionAsync(TraceProfileDescriptor descriptor);
        Task StopSessionAsync();

        bool IsAutoLogEnabled { get; }
        void EnableAutoLog(TraceProfileDescriptor profile);
        void DisableAutoLog();
    }

    internal interface ITraceControllerInternal : ITraceController
    {
        void LaunchTraceTargets(IReadOnlyList<TraceLaunchTarget> targets);
    }

    [Guid("F1D64E54-9508-47B3-90E1-30135AF5D992")]
    public sealed class STraceController
    {
    }

    public class DefaultTraceController : ITraceControllerInternal
    {
        private readonly object mutex = new object();

        private EventSession runningSession;
        private bool autoLogEnabled;
#pragma warning disable 649
        private bool asyncAutoLog;
#pragma warning restore 649
        private TraceProfileDescriptor autoLogProfile;
        private CancellationTokenSource autoLogExitCts;

        public event Action<TraceLog> SessionStarting;
        public event Action<EventSession> SessionStarted;
        public event Action<EventSession> SessionStopped;
        public bool IsAutoLogEnabled => autoLogEnabled;

        public void EnableAutoLog(TraceProfileDescriptor profile)
        {
            lock (mutex) {
                autoLogProfile = profile;
                autoLogEnabled = true;
            }
        }

        public void DisableAutoLog()
        {
            lock (mutex) {
                autoLogEnabled = false;
                autoLogProfile = null;
            }
        }

        public EventSession StartSession(TraceProfileDescriptor descriptor)
        {
            if (runningSession != null)
                throw new InvalidOperationException("Session already in progress.");

            var traceLog = new TraceLog();
            var session = new EventSession(descriptor, traceLog);
            SessionStarting?.Invoke(traceLog);
            session.Start();

            runningSession = session;
            SessionStarted?.Invoke(runningSession);
            return session;
        }

        public async Task<EventSession> StartSessionAsync(TraceProfileDescriptor descriptor)
        {
            if (runningSession != null)
                throw new InvalidOperationException("Session already in progress.");

            var traceLog = new TraceLog();
            var session = new EventSession(descriptor, traceLog);
            SessionStarting?.Invoke(traceLog);

            await session.StartAsync();

            runningSession = session;
            SessionStarted?.Invoke(runningSession);
            return session;
        }

        public void StopSession()
        {
            if (runningSession == null)
                return;

            autoLogExitCts?.Cancel();
            using (runningSession) {
                runningSession.Stop();
                SessionStopped?.Invoke(runningSession);
                runningSession = null;
            }
        }

        public async Task StopSessionAsync()
        {
            if (runningSession == null)
                return;

            autoLogExitCts?.Cancel();
            using (runningSession) {
                await Task.Run(() => runningSession.Stop());
                SessionStopped?.Invoke(runningSession);
                runningSession = null;
            }
        }

        private static bool IsUsableProfile(TraceProfileDescriptor descriptor)
        {
            return
                descriptor != null &&
                descriptor.Collectors.Count == 1 &&
                descriptor.Collectors[0] is EventCollectorDescriptor collector &&
                collector.Providers.Count > 0;
        }

        private TraceProfileDescriptor AugmentTraceProfile(
            TraceProfileDescriptor descriptor, IReadOnlyList<TraceLaunchTarget> targets)
        {
            foreach (var collector in descriptor.Collectors.OfType<EventCollectorDescriptor>()) {
                foreach (var provider in collector.Providers) {
                    if (provider.StartupProjects == null)
                        continue;

                    foreach (var project in provider.StartupProjects) {
                        var target = targets.FirstOrDefault(
                            x => string.Equals(x.ProjectPath, project, StringComparison.OrdinalIgnoreCase));

                        if (target != null)
                            provider.ProcessIds.Add(target.ProcessId);
                    }
                }
            }

            return descriptor;
        }

        public void LaunchTraceTargets(IReadOnlyList<TraceLaunchTarget> targets)
        {
            if (!autoLogEnabled || runningSession != null || !IsUsableProfile(autoLogProfile))
                return;

            autoLogExitCts?.Cancel();
            autoLogExitCts = new CancellationTokenSource();

            var profile = AugmentTraceProfile(autoLogProfile, targets);

            if (asyncAutoLog)
                StartSessionAsync(profile).Forget();
            else
                StartSession(profile);

            var processTasks = targets.Select(
                x => Process.GetProcessById((int)x.ProcessId).WaitForExitAsync());

            Task.WhenAll(processTasks).ContinueWith(t => {
                ExitTraceTargets(targets);
            }, autoLogExitCts.Token).Forget();
        }

        private void ExitTraceTargets(IReadOnlyList<TraceLaunchTarget> targets)
        {
            if (!autoLogEnabled || runningSession == null)
                return;

            StopSession();
        }
    }

    internal static class PropertiesExtensions
    {
        public static T GetValue<T>(this Properties properties, string name)
        {
            try {
                var property = properties.Item(name);
                if (property?.Value is T val) {
                    return val;
                }

                return default;
            } catch (Exception) {
                return default;
            }
        }

        public static bool TryGetProperty<T>(this Properties properties, string name, out T value)
        {
            try {
                var property = properties.Item(name);
                if (property?.Value is T val) {
                    value = val;
                    return true;
                }

                value = default;
                return false;
            } catch (Exception) {
                value = default;
                return false;
            }
        }
    }
}
