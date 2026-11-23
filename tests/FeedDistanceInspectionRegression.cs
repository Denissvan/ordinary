using System;
using System.Threading;
using UI;

internal static class FeedDistanceInspectionRegression
{
    private static void Check(bool value, string reason)
    {
        if (!value) throw new Exception(reason);
    }

    public static int Main()
    {
        var state = new FeedDistanceInspectionState();
        long cycle;
        IDisposable motion;
        Check(!state.TryBeginInspection(out cycle), "no request must not inspect");
        state.Request();
        Check(!state.TryBeginLoading(2, true, out motion), "stale ready must not authorize loading");
        Check(state.TryBeginInspection(out cycle), "pending cycle starts");
        Check(!state.TryBeginMotion(out motion), "inspection excludes motion");
        state.EndInspection(cycle, false);
        Check(state.HasPending, "failed/stopped inspection remains pending");
        Check(state.TryBeginInspection(out cycle), "retry begins");
        state.Request();
        state.EndInspection(cycle, true);
        Check(state.HasPending, "old completion must not consume newer request");
        Check(state.TryBeginInspection(out cycle), "new request begins");
        state.EndInspection(cycle, true);
        Check(!state.HasPending, "success completes snapshot");
        Check(!state.TryBeginInspection(out cycle), "do not repeat completed cycle");
        state.AuthorizeLoading(2);
        state.Request();
        Check(state.TryBeginLoading(2, true, out motion), "request after feed admission waits for next safe window");
        Check(!state.TryBeginInspection(out cycle), "loading excludes inspection");
        motion.Dispose(); motion.Dispose();
        Check(!state.TryBeginLoading(2, true, out motion), "feed permit is single use");
        Check(state.TryBeginInspection(out cycle), "operation lease released");
        state.EndInspection(cycle, false);
        Check(state.TryBeginLoading(1, false, out motion), "other stations may load with pending request");
        motion.Dispose();

        double deviation;
        Check(FeedDistanceInspectionState.IsPass(100, 100, 0, 0, out deviation), "exact match");
        Check(FeedDistanceInspectionState.IsPass(99, 100, 0.5, 0.5, out deviation), "inclusive tolerance with compensation");
        Check(!FeedDistanceInspectionState.IsPass(99, 100, 0.1, 0, out deviation), "out of tolerance");
        foreach (double invalid in new[] { -1.0, 0, double.NaN, double.PositiveInfinity })
            Check(!FeedDistanceInspectionState.IsPass(invalid, 100, 1000, 0, out deviation), "invalid sample rejected");
        Check(!FeedDistanceInspectionState.IsPass(100, 0, 1000, 0, out deviation), "unconfigured standard");
        Check(!FeedDistanceInspectionState.IsPass(100, 100, -1, 0, out deviation), "negative tolerance");

        state = new FeedDistanceInspectionState();
        state.Request();
        Check(state.TryBeginMotion(out motion), "manual motion lease");
        bool racedInspection = true;
        var thread = new Thread(() => { long id; racedInspection = state.TryBeginInspection(out id); });
        thread.Start(); thread.Join();
        Check(!racedInspection, "concurrent inspection cannot pass active motion");
        motion.Dispose();
        Check(state.TryBeginInspection(out cycle), "inspection after motion");
        bool racedMotion = true;
        thread = new Thread(() => { IDisposable scope; racedMotion = state.TryBeginMotion(out scope); });
        thread.Start(); thread.Join();
        Check(!racedMotion, "concurrent motion cannot pass active inspection");
        state.EndInspection(cycle, false);
        Console.WriteLine("PASS: feed inspection cycle, admission, exclusion and distance validation.");
        return 0;
    }
}
