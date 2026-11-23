param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$ticketSource = Join-Path $RepoRoot 'UI\Class\CaptureTicketQueue.cs'
$camSource = Join-Path $RepoRoot 'UI\Class\Cam.cs'
$upDownLoadSource = Join-Path $RepoRoot 'UI\Class\UpDownLoad.cs'

if (-not (Test-Path -LiteralPath $ticketSource)) {
    throw 'CaptureTicketQueue.cs is required to bind each camera frame to its original task.'
}

$testSource = Join-Path $env:TEMP 'CaptureTicketQueueRegression.cs'
$testExe = Join-Path $env:TEMP 'CaptureTicketQueueRegression.exe'

@'
using System;
using System.Threading;
using UI;

internal static class CaptureTicketQueueRegression
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static int Main()
    {
        LateFrameKeepsOriginalTicket();
        NewSessionWaitsForProcessingFrame();
        LostFrameRequiresExplicitRecovery();
        OldFrameWithoutHistoryIsDroppedDuringRecovery();
        UntriggeredTicketDoesNotShiftTriggerNumbers();
        RecoveryCannotCompleteWithOutstandingTickets();
        ConsecutiveAndAppendedCapturesAreUnaffected();
        TriggerGapIsReported();
        FirstFrameIsCheckedAgainstPreviousFrame();
        ContinuityIsUnknownAfterInvalidation();
        LiveFramesKeepTriggerContinuity();
        NoTicketFrameIsReported();
        Console.WriteLine("PASS: capture ticket queue binds frames to their own ticket and requires explicit recovery after lost frames.");
        return 0;
    }

    private static CaptureTicketQueue<string> NewQueue(out long session)
    {
        var queue = new CaptureTicketQueue<string>();
        string error;
        Assert(queue.TryBeginSession(out session, out error), "Session must start.");
        return queue;
    }

    // 超时后迟到图像只能消费自己的失效Ticket，不能写给下一孔位
    private static void LateFrameKeepsOriginalTicket()
    {
        long session;
        var queue = NewQueue(out session);
        var timedOut = queue.Register(session, "hole-4", 4);
        var current = queue.Register(session, "hole-7", 7);
        queue.Expire(timedOut);

        CaptureTicket<string> frameTicket;
        string note;
        queue.TakeNextFrame(101, out frameTicket, out note);
        Assert(Object.ReferenceEquals(timedOut, frameTicket), "The late frame must not be assigned to the next hole.");
        Assert(queue.GetState(frameTicket) == CaptureTicketState.Expired, "A timed-out ticket must remain expired.");
        queue.Complete(frameTicket);
        Assert(frameTicket.State == CaptureTicketState.Discarded, "An expired ticket must end as discarded.");

        queue.TakeNextFrame(102, out frameTicket, out note);
        Assert(Object.ReferenceEquals(current, frameTicket), "The second frame must keep its own ticket.");
        queue.Complete(frameTicket);
        queue.Complete(frameTicket);
        Assert(!queue.HasOutstanding && !queue.IsRecoveryRequired, "Complete must be idempotent; frames that arrived need no recovery.");
    }

    // VPP仍在处理时不能开始新批次(防止复用任务对象)
    private static void NewSessionWaitsForProcessingFrame()
    {
        long session;
        var queue = NewQueue(out session);
        queue.Register(session, "hole-1", 1);
        CaptureTicket<string> processing;
        string note;
        queue.TakeNextFrame(201, out processing, out note);
        Assert(queue.AbandonOutstanding() == 1, "Starting a new batch must expire the unfinished ticket.");

        int reclaimed;
        string detail;
        string error;
        long blocked;
        Assert(!queue.TryReclaimLostFrames(TimeSpan.Zero, out reclaimed, out detail), "A processing ticket must not be reclaimed.");
        Assert(!queue.TryBeginSession(out blocked, out error), "A processing ticket must block the next batch.");
        queue.Complete(processing);
        Assert(queue.TryReclaimLostFrames(TimeSpan.Zero, out reclaimed, out detail) && reclaimed == 0, "After VPP completes the queue is drained.");
        Assert(!queue.IsRecoveryRequired, "A frame that arrived does not require camera recovery.");
        Assert(queue.TryBeginSession(out session, out error), "The next batch starts after VPP completes.");
    }

    // 帧永远不到：隔离期后回收，但必须由相机显式恢复后才能建立新批次
    private static void LostFrameRequiresExplicitRecovery()
    {
        long session;
        var queue = NewQueue(out session);
        queue.Register(session, "hole-2", 2);
        queue.InvalidateSession(session);

        int reclaimed;
        string detail;
        string error;
        Assert(!queue.TryReclaimLostFrames(TimeSpan.FromSeconds(30), out reclaimed, out detail), "A lost frame must not be reclaimed inside the quarantine.");
        Thread.Sleep(20);
        Assert(queue.TryReclaimLostFrames(TimeSpan.FromMilliseconds(10), out reclaimed, out detail) && reclaimed == 1, "A lost frame is reclaimed after the quarantine.");
        Assert(queue.IsRecoveryRequired, "Reclaiming a lost frame must enter camera recovery.");
        long next;
        Assert(!queue.TryBeginSession(out next, out error), "No new batch before the camera recovery is completed.");
        Assert(queue.CompleteRecovery(), "Recovery completes when nothing is outstanding.");
        Assert(queue.TryBeginSession(out next, out error), "A new batch is allowed after recovery.");
    }

    // 问题1：没有任何历史触发号时，回收后的旧图也不能被新批次认领
    private static void OldFrameWithoutHistoryIsDroppedDuringRecovery()
    {
        long session;
        var queue = NewQueue(out session);
        queue.Register(session, "OLD-hole4", 4);
        queue.InvalidateSession(session);
        Thread.Sleep(20);
        int reclaimed;
        string detail;
        Assert(queue.TryReclaimLostFrames(TimeSpan.FromMilliseconds(10), out reclaimed, out detail) && reclaimed == 1, "The lost ticket is reclaimed.");

        long next;
        string error;
        Assert(!queue.TryBeginSession(out next, out error), "NEW-hole7 cannot be registered before recovery.");
        CaptureTicket<string> ticket;
        string note;
        Assert(queue.TakeNextFrame(101, out ticket, out note) == CaptureFrameKind.DuringRecovery && ticket == null, "An old frame during recovery must be dropped.");
        Assert(queue.CompleteRecovery(), "Recovery completes.");
        Assert(queue.TryBeginSession(out next, out error), "New session starts.");
        var fresh = queue.Register(next, "NEW-hole7", 7);
        Assert(queue.TakeNextFrame(102, out ticket, out note) == CaptureFrameKind.Ticketed && Object.ReferenceEquals(fresh, ticket), "The new frame is bound to NEW-hole7.");
        queue.Complete(ticket);
    }

    // 问题2：登记但未触发的Ticket被回收后，不得把正常新图当成旧图
    private static void UntriggeredTicketDoesNotShiftTriggerNumbers()
    {
        long session;
        var queue = NewQueue(out session);
        CaptureTicket<string> ticket;
        string note;
        string error;
        queue.Register(session, "warm-up", 0);
        queue.TakeNextFrame(300, out ticket, out note);
        queue.Complete(ticket);

        Assert(queue.TryBeginSession(out session, out error), "Session must start.");
        queue.Register(session, "never-triggered", 1);
        queue.AbandonOutstanding();
        Thread.Sleep(20);
        int reclaimed;
        string detail;
        Assert(queue.TryReclaimLostFrames(TimeSpan.FromMilliseconds(10), out reclaimed, out detail) && reclaimed == 1, "The untriggered ticket is reclaimed.");
        Assert(queue.CompleteRecovery(), "Recovery completes.");

        Assert(queue.TryBeginSession(out session, out error), "New session must start.");
        var fresh = queue.Register(session, "fresh", 2);
        Assert(queue.TakeNextFrame(301, out ticket, out note) == CaptureFrameKind.Ticketed && Object.ReferenceEquals(fresh, ticket), "Normal frame 301 must not be dropped as stale.");
        Assert(!queue.HasPending, "No ticket may be left waiting.");
        queue.Complete(ticket);
    }

    // 恢复期间若仍有未完成Ticket，恢复不能结束
    private static void RecoveryCannotCompleteWithOutstandingTickets()
    {
        long session;
        var queue = NewQueue(out session);
        queue.Register(session, "processing", 1);
        CaptureTicket<string> ticket;
        string note;
        queue.TakeNextFrame(1, out ticket, out note);
        Assert(!queue.CompleteRecovery(), "Recovery must not complete while a ticket is outstanding.");
        queue.Complete(ticket);
        Assert(queue.CompleteRecovery(), "Recovery completes once drained.");
    }

    // 正常连续定拍、重拍、飞拍追加不受影响
    private static void ConsecutiveAndAppendedCapturesAreUnaffected()
    {
        var queue = new CaptureTicketQueue<string>();
        long session;
        string error;
        string note;
        CaptureTicket<string> ticket;
        int reclaimed;
        string detail;
        for (int i = 0; i < 5; i++)
        {
            Assert(queue.TryReclaimLostFrames(TimeSpan.FromSeconds(2), out reclaimed, out detail) && reclaimed == 0, "Normal capture never reclaims.");
            Assert(queue.TryBeginSession(out session, out error), "Consecutive direct capture must start immediately.");
            var t = queue.Register(session, "direct-" + i, -1);
            Assert(queue.TakeNextFrame(100 + i, out ticket, out note) == CaptureFrameKind.Ticketed && Object.ReferenceEquals(t, ticket), "Direct capture frame keeps its ticket.");
            queue.Complete(ticket);
        }
        Assert(!queue.IsRecoveryRequired, "Normal capture never enters recovery.");

        Assert(queue.TryBeginSession(out session, out error), "Fly-shot session starts.");
        var w0 = queue.Register(session, "ws-0", 0);
        var w1 = queue.Register(session, "ws-1", 1);
        var m0 = queue.Register(session, "tray-4", 4);
        var m1 = queue.Register(session, "tray-5", 5);
        var expected = new[] { w0, w1, m0, m1 };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert(queue.TakeNextFrame(105 + i, out ticket, out note) == CaptureFrameKind.Ticketed, "Appended tickets keep consecutive trigger numbers (counter continues after 104).");
            Assert(Object.ReferenceEquals(expected[i], ticket), "Appended tickets are taken in registration order.");
            queue.Complete(ticket);
        }
    }

    // 同一批次内触发号跳号要报告(可能丢帧/多帧)
    private static void TriggerGapIsReported()
    {
        long session;
        var queue = NewQueue(out session);
        string note;
        CaptureTicket<string> ticket;
        queue.Register(session, "hole-4", 4);
        queue.Register(session, "hole-5", 5);
        queue.Register(session, "hole-6", 6);
        Assert(queue.TakeNextFrame(500, out ticket, out note) == CaptureFrameKind.Ticketed, "The first frame sets the trigger base.");
        queue.Complete(ticket);
        Assert(queue.TakeNextFrame(501, out ticket, out note) == CaptureFrameKind.Ticketed, "A consecutive trigger is normal.");
        queue.Complete(ticket);
        Assert(queue.TakeNextFrame(503, out ticket, out note) == CaptureFrameKind.TicketedTriggerMismatch, "A skipped trigger number must be reported.");
        queue.Complete(ticket);
    }

    // Codex例子：应收100/101/102，实际100丢失又多收103；首帧必须与上一帧(99)衔接校验
    private static void FirstFrameIsCheckedAgainstPreviousFrame()
    {
        long session;
        var queue = NewQueue(out session);
        string error;
        string note;
        CaptureTicket<string> ticket;
        queue.Register(session, "previous", 0);
        queue.TakeNextFrame(99, out ticket, out note);
        queue.Complete(ticket);

        Assert(queue.TryBeginSession(out session, out error), "Session must start.");
        queue.Register(session, "m0", 0);
        queue.Register(session, "m1", 1);
        queue.Register(session, "m2", 2);
        Assert(queue.TakeNextFrame(101, out ticket, out note) == CaptureFrameKind.TicketedTriggerMismatch, "A first frame that skips 100 must be reported even though 101/102/103 are consecutive.");
        queue.Complete(ticket);
    }

    // 批次作废后触发号衔接未知：下一批首帧只能自建基准，并标注未验证(已知限制)
    private static void ContinuityIsUnknownAfterInvalidation()
    {
        long session;
        var queue = NewQueue(out session);
        string error;
        string note;
        CaptureTicket<string> ticket;
        queue.Register(session, "a", 0);
        queue.TakeNextFrame(10, out ticket, out note);
        queue.Complete(ticket);
        queue.InvalidateSession(session);

        Assert(queue.TryBeginSession(out session, out error), "Session must start.");
        queue.Register(session, "b", 0);
        Assert(queue.TakeNextFrame(20, out ticket, out note) == CaptureFrameKind.Ticketed, "After invalidation the first frame cannot be checked.");
        Assert(note.Length > 0, "An unverified trigger base must be reported in the note.");
        queue.Complete(ticket);
    }

    // 实时显示等不认领的图像也要记录触发号，避免下一次定拍误报
    private static void LiveFramesKeepTriggerContinuity()
    {
        long session;
        var queue = NewQueue(out session);
        string error;
        string note;
        CaptureTicket<string> ticket;
        queue.Register(session, "a", 0);
        queue.TakeNextFrame(10, out ticket, out note);
        queue.Complete(ticket);
        queue.NoteFrameTrigger(11);
        queue.NoteFrameTrigger(12);

        Assert(queue.TryBeginSession(out session, out error), "Session must start.");
        queue.Register(session, "b", 0);
        Assert(queue.TakeNextFrame(13, out ticket, out note) == CaptureFrameKind.Ticketed, "A frame following live frames must be continuous.");
        queue.Complete(ticket);
    }

    // 没有登记Ticket的图像要能被识别出来
    private static void NoTicketFrameIsReported()
    {
        var queue = new CaptureTicketQueue<string>();
        string note;
        CaptureTicket<string> ticket;
        Assert(queue.TakeNextFrame(700, out ticket, out note) == CaptureFrameKind.NoTicket && ticket == null, "A frame without any ticket must be reported as NoTicket.");
    }
}
'@ | Set-Content -LiteralPath $testSource -Encoding UTF8

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

& $csc /nologo /codepage:65001 /out:$testExe $ticketSource $testSource
if ($LASTEXITCODE -ne 0) {
    throw 'CaptureTicketQueue regression compilation failed.'
}

& $testExe
if ($LASTEXITCODE -ne 0) {
    throw 'CaptureTicketQueue regression failed.'
}

$cam = [System.IO.File]::ReadAllText($camSource, [System.Text.Encoding]::UTF8)
$upDownLoad = [System.IO.File]::ReadAllText($upDownLoadSource, [System.Text.Encoding]::UTF8)

if ($cam -notmatch 'CaptureTicketQueue<VisionTask>') {
    throw 'Cam must own a capture ticket queue.'
}
if ($cam.Contains('TaskRunImage(taskid, curImage)')) {
    throw 'Camera callbacks must not process images by the mutable task index.'
}
if ($cam -notmatch 'captureTickets\.TakeNextFrame\(info\.TriggerNumber') {
    throw 'Camera callbacks must take the next frame through the capture ticket queue.'
}
if ($cam -notmatch 'if \(!taskScheduled\) captureTickets\.Complete\(ticket\);') {
    throw 'A callback exception must release its capture ticket.'
}
if ($cam -notmatch 'CaptureFrameKind\.DuringRecovery') {
    throw 'Frames arriving during camera recovery must be dropped.'
}
if ($cam -match 'StaleAfterReclaim|staleTriggerFloor') {
    throw 'Old frames must not be identified by guessing trigger numbers from reclaimed ticket counts.'
}
# 恢复：关触发 -> Flush 成功 -> 静默窗口 -> CompleteRecovery；Flush 异常不得结束恢复
if ($cam -notmatch 'TriggerEnabled = false;[\s\S]{0,200}mAcqFifo\.Flush\(\);[\s\S]{0,4000}captureTickets\.CompleteRecovery\(\)') {
    throw 'Recovery must disable triggers and flush before completing.'
}
# 恢复结束必须与回调"取图->认领"使用同一把锁，且锁内确认FIFO无已完成图像
if ($cam -notmatch 'Monitor\.TryEnter\(obj,[\s\S]{0,1500}GetFifoState\([\s\S]{0,1500}captureTickets\.CompleteRecovery\(\)[\s\S]{0,800}Monitor\.Exit\(obj\)') {
    throw 'Ending camera recovery must hold the callback lock and re-check the FIFO.'
}
if ($cam -notmatch 'lock \(obj\)[\s\S]{0,400}GetFifoState\(out numPending, out numReady, out busy\);\s*if \(numReady <= 0\) return;[\s\S]{0,600}CompleteAcquireEx') {
    throw 'The callback must re-check the FIFO inside the lock before taking an image.'
}
if ($cam -notmatch 'IsRecoveryRequired\)[\s\S]{0,200}RunCaptureRecovery\(') {
    throw 'A new session must run camera recovery after lost frames.'
}
# 多余/未登记图像不得写入任务
if ($cam -notmatch 'InvalidateCaptureSession\("收到未登记Ticket的图像"\);\s*return;') {
    throw 'Unregistered frames must invalidate the batch and must not be processed.'
}
if ($cam -notmatch 'InvalidateCaptureSession\("相机图像丢失\(Overrun\)"\);') {
    throw 'Overrun must always invalidate the batch.'
}
if ($cam -notmatch 'TryConsumeManualCredit\(\)') {
    throw 'Frames without a ticket must only use the old curTask path for manual triggers.'
}
if ($cam -notmatch 'task\.CaptureTicketId != ticket\.TicketId') {
    throw 'VPP must not write a frame into a task that was re-bound to a newer ticket.'
}
if ($cam -notmatch 'captureTickets\.Complete\(ticket\);\s*RecordDisplay\(displayImage') {
    throw 'The ticket must be released before saving/displaying the image.'
}
# 定拍：必须先登记Ticket再清结果
if ($cam -notmatch 'if \(!Demo\)\s*\{\s*EM_RES prepareRes = Camera\.PrepareDirectCapture\(this\);\s*if \(prepareRes != EM_RES\.OK\) return prepareRes;\s*\}\s*//clear result\s*ResData\.Clear\(\);') {
    throw 'Direct capture must register its ticket before clearing the previous result.'
}
# 飞拍：必须先占用批次再清空任务列表
if ($cam -notmatch 'BeginCaptureSession\([^)]*\)\) return EM_RES\.CAM_ERR;[\s\S]{0,400}List_vs_task_cur\.Clear\(\);\s*ListResultTemp\.Clear\(\);') {
    throw 'Fly-shot configuration must begin the capture session before clearing the task list.'
}
if ($upDownLoad -notmatch 'CamPickTrace') {
    throw 'Tray picking must emit the camera-to-nozzle trace log.'
}
if ($upDownLoad -notmatch 'LogCamDirectPickTrace\(xt, upcam\.curTask') {
    throw 'Direct-capture picking must emit the camera-to-nozzle trace log.'
}

Write-Host 'PASS: camera callbacks and tray picking provide a correlated capture trace.'
