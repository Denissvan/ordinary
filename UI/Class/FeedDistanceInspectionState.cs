using System;
using System.Threading;

namespace UI
{
    internal sealed class FeedDistanceInspectionState
    {
        private readonly object sync = new object();
        private long requested;
        private long completed;
        private long inspecting;
        private int motions;
        private int authorizedStation = -1;

        public bool HasPending { get { lock (sync) return requested > completed; } }
        public bool IsInspecting { get { lock (sync) return inspecting != 0; } }
        public bool HasCompleted { get { lock (sync) return completed != 0; } }

        public void Request()
        {
            lock (sync) requested++;
        }

        public void AuthorizeLoading(int station)
        {
            lock (sync) authorizedStation = station;
        }

        public void RevokeLoading()
        {
            lock (sync) authorizedStation = -1;
        }

        public bool TryBeginLoading(int station, bool requireAuthorization, out IDisposable scope)
        {
            lock (sync)
            {
                scope = null;
                if (inspecting != 0 || (requireAuthorization && authorizedStation != station)) return false;
                if (requireAuthorization) authorizedStation = -1;
                motions++;
                scope = new MotionScope(this);
                return true;
            }
        }

        public bool TryBeginMotion(out IDisposable scope)
        {
            return TryBeginLoading(-1, false, out scope);
        }

        public bool TryBeginInspection(out long cycle)
        {
            lock (sync)
            {
                cycle = 0;
                if (requested <= completed || inspecting != 0 || motions != 0) return false;
                cycle = inspecting = requested;
                authorizedStation = -1;
                return true;
            }
        }

        public void EndInspection(long cycle, bool passed)
        {
            lock (sync)
            {
                if (cycle == 0 || inspecting != cycle) throw new InvalidOperationException("Invalid inspection cycle.");
                if (passed) completed = cycle;
                inspecting = 0;
            }
        }

        private sealed class MotionScope : IDisposable
        {
            private FeedDistanceInspectionState owner;
            public MotionScope(FeedDistanceInspectionState owner) { this.owner = owner; }
            public void Dispose()
            {
                var state = Interlocked.Exchange(ref owner, null);
                if (state != null) lock (state.sync) state.motions--;
            }
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static bool IsPass(double actual, double standard, double tolerance, double compensation, out double deviation)
        {
            deviation = Math.Abs(actual + compensation - standard);
            return IsFinite(actual) && actual > 0 && IsFinite(standard) && standard > 0 &&
                IsFinite(tolerance) && tolerance >= 0 && IsFinite(compensation) &&
                IsFinite(deviation) && deviation <= tolerance;
        }
    }
}
