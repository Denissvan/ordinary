using System;
using System.Collections.Generic;

namespace UI
{
    public enum CaptureTicketState
    {
        Pending,
        Processing,
        Expired,
        Completed,
        Discarded
    }

    /// <summary>
    /// 回图认领结果
    /// </summary>
    public enum CaptureFrameKind
    {
        /// <summary>正常认领到本批次Ticket</summary>
        Ticketed,
        /// <summary>认领到Ticket，但相机触发号与本批次首帧推算值不一致(可能丢帧/多帧)</summary>
        TicketedTriggerMismatch,
        /// <summary>当前没有待认领的Ticket</summary>
        NoTicket,
        /// <summary>相机处于丢帧恢复期(不存在任何有效Ticket)，图像身份不明确</summary>
        DuringRecovery
    }

    public sealed class CaptureTicket<TTask>
    {
        internal CaptureTicket(long sessionId, long ticketId, int sequence, int positionIndex, TTask task)
        {
            SessionId = sessionId;
            TicketId = ticketId;
            Sequence = sequence;
            PositionIndex = positionIndex;
            Task = task;
            State = CaptureTicketState.Pending;
            ConfiguredAtUtc = DateTime.UtcNow;
        }

        public long SessionId { get; private set; }
        public long TicketId { get; private set; }
        public int Sequence { get; private set; }
        public int PositionIndex { get; private set; }
        public int TriggerNumber { get; internal set; }
        public TTask Task { get; private set; }
        public CaptureTicketState State { get; internal set; }
        public DateTime ConfiguredAtUtc { get; private set; }
        public DateTime? FrameReceivedAtUtc { get; internal set; }
        public DateTime? ProcessingStartedAtUtc { get; internal set; }
        public DateTime? ExpiredAtUtc { get; internal set; }

        public long ConfigToFrameMs
        {
            get
            {
                return FrameReceivedAtUtc.HasValue
                    ? (long)(FrameReceivedAtUtc.Value - ConfiguredAtUtc).TotalMilliseconds
                    : -1;
            }
        }

        public long FrameToProcessingMs
        {
            get
            {
                return FrameReceivedAtUtc.HasValue && ProcessingStartedAtUtc.HasValue
                    ? (long)(ProcessingStartedAtUtc.Value - FrameReceivedAtUtc.Value).TotalMilliseconds
                    : -1;
            }
        }
    }

    /// <summary>
    /// 拍照Ticket队列：每次拍照先登记Ticket，回图按FIFO认领。
    /// 所有状态读写都在 sync 锁内完成，供相机回调线程、VPP线程、流程线程共同使用。
    /// </summary>
    public sealed class CaptureTicketQueue<TTask>
    {
        private readonly object sync = new object();
        private readonly Queue<CaptureTicket<TTask>> pending = new Queue<CaptureTicket<TTask>>();
        private readonly Dictionary<long, CaptureTicket<TTask>> outstanding = new Dictionary<long, CaptureTicket<TTask>>();
        private long activeSessionId;
        private long nextTicketId;
        private int nextSequence;

        // 本批次触发号基准 = Sequence 0 对应的TriNum
        private bool hasSessionTriggerBase;
        private long sessionTriggerBase;

        // 上一张收到的图像触发号(包括被丢弃、手动、实时显示的图像)，用于验证新批次首帧是否与之衔接。
        // 连续性未知(无历史、恢复、批次作废、取图异常)时不信任，新批次首帧只能自建基准并标注"未验证"。
        private bool hasLastTrigger;
        private int lastTrigger;
        private bool lastTriggerTrusted;
        private bool hasExpectedFirst;
        private long expectedFirstTrigger;

        // 回收过丢失帧后必须先完成相机恢复(关触发+清FIFO+静默确认)才能建立新批次；
        // 恢复期间收到的任何图像都没有合法Ticket，一律丢弃。
        private bool recoveryRequired;

        public bool HasOutstanding
        {
            get
            {
                lock (sync)
                {
                    return outstanding.Count > 0;
                }
            }
        }

        public bool HasPending
        {
            get
            {
                lock (sync)
                {
                    return pending.Count > 0;
                }
            }
        }

        public bool IsRecoveryRequired
        {
            get
            {
                lock (sync)
                {
                    return recoveryRequired;
                }
            }
        }

        public long ActiveSessionId
        {
            get
            {
                lock (sync)
                {
                    return activeSessionId;
                }
            }
        }

        public CaptureTicketState GetState(CaptureTicket<TTask> ticket)
        {
            if (ticket == null) return CaptureTicketState.Discarded;
            lock (sync)
            {
                return ticket.State;
            }
        }

        public bool TryBeginSession(out long sessionId, out string error)
        {
            lock (sync)
            {
                if (outstanding.Count > 0)
                {
                    sessionId = activeSessionId;
                    error = string.Format("previous capture session {0} still has {1} pending ticket(s)", activeSessionId, outstanding.Count);
                    return false;
                }

                if (recoveryRequired)
                {
                    sessionId = activeSessionId;
                    error = "camera recovery after lost frames has not completed";
                    return false;
                }

                activeSessionId++;
                nextSequence = 0;
                hasSessionTriggerBase = false;
                sessionTriggerBase = 0;
                hasExpectedFirst = hasLastTrigger && lastTriggerTrusted;
                expectedFirstTrigger = hasExpectedFirst ? (long)lastTrigger + 1 : 0;
                sessionId = activeSessionId;
                error = string.Empty;
                return true;
            }
        }

        public CaptureTicket<TTask> Register(long sessionId, TTask task, int positionIndex)
        {
            lock (sync)
            {
                if (sessionId != activeSessionId)
                {
                    throw new InvalidOperationException("Capture ticket session is not active.");
                }

                var ticket = new CaptureTicket<TTask>(sessionId, ++nextTicketId, nextSequence++, positionIndex, task);
                pending.Enqueue(ticket);
                outstanding.Add(ticket.TicketId, ticket);
                return ticket;
            }
        }

        /// <summary>
        /// 兼容旧调用：只返回是否认领到Ticket。
        /// </summary>
        public bool TryTakeNextFrame(int triggerNumber, out CaptureTicket<TTask> ticket)
        {
            string note;
            TakeNextFrame(triggerNumber, out ticket, out note);
            return ticket != null;
        }

        /// <summary>
        /// 记录不经过认领的图像触发号(如实时显示)，保持触发号衔接。
        /// </summary>
        public void NoteFrameTrigger(int triggerNumber)
        {
            lock (sync)
            {
                hasLastTrigger = true;
                lastTrigger = triggerNumber;
            }
        }

        /// <summary>
        /// 触发号衔接未知(取图异常、批次作废等)：下一批首帧不再与上一帧做衔接验证。
        /// </summary>
        public void MarkTriggerContinuityUnknown()
        {
            lock (sync)
            {
                lastTriggerTrusted = false;
            }
        }

        /// <summary>
        /// 回图认领：按FIFO取出下一个Ticket，并检查触发号连续性。
        /// 恢复期内不认领任何Ticket。
        /// 注意：触发号校验只能发现部分丢帧/多帧，不能证明图像与触发一一对应。
        /// </summary>
        public CaptureFrameKind TakeNextFrame(int triggerNumber, out CaptureTicket<TTask> ticket, out string note)
        {
            lock (sync)
            {
                CaptureFrameKind kind = ClassifyFrame(triggerNumber, out ticket, out note);
                hasLastTrigger = true;
                lastTrigger = triggerNumber;
                return kind;
            }
        }

        private CaptureFrameKind ClassifyFrame(int triggerNumber, out CaptureTicket<TTask> ticket, out string note)
        {
            {
                ticket = null;
                note = string.Empty;
                DateTime now = DateTime.UtcNow;

                if (recoveryRequired)
                {
                    note = string.Format("TriNum={0},相机恢复未完成", triggerNumber);
                    return CaptureFrameKind.DuringRecovery;
                }

                if (pending.Count == 0)
                {
                    return CaptureFrameKind.NoTicket;
                }

                ticket = pending.Dequeue();
                ticket.TriggerNumber = triggerNumber;
                ticket.FrameReceivedAtUtc = now;
                if (ticket.State == CaptureTicketState.Pending)
                {
                    ticket.State = CaptureTicketState.Processing;
                }

                long baseValue = (long)triggerNumber - ticket.Sequence;
                if (!hasSessionTriggerBase)
                {
                    hasSessionTriggerBase = true;
                    lastTriggerTrusted = true;
                    if (hasExpectedFirst)
                    {
                        // 基准来自上一张图像的触发号+1，而不是本批首帧自身
                        sessionTriggerBase = expectedFirstTrigger;
                        if (baseValue != sessionTriggerBase)
                        {
                            note = string.Format("首帧与上一帧不衔接:期望TriNum={0},实际TriNum={1}", sessionTriggerBase + ticket.Sequence, triggerNumber);
                            return CaptureFrameKind.TicketedTriggerMismatch;
                        }
                        return CaptureFrameKind.Ticketed;
                    }

                    sessionTriggerBase = baseValue;
                    note = "触发号基准由本批首帧建立(未与上一帧衔接验证)";
                    return CaptureFrameKind.Ticketed;
                }

                if (baseValue != sessionTriggerBase)
                {
                    note = string.Format("期望TriNum={0},实际TriNum={1}", sessionTriggerBase + ticket.Sequence, triggerNumber);
                    return CaptureFrameKind.TicketedTriggerMismatch;
                }

                return CaptureFrameKind.Ticketed;
            }
        }

        public void Expire(CaptureTicket<TTask> ticket)
        {
            if (ticket == null) return;

            lock (sync)
            {
                if (outstanding.ContainsKey(ticket.TicketId))
                {
                    MarkExpired(ticket, DateTime.UtcNow);
                }
            }
        }

        public void InvalidateSession(long sessionId)
        {
            lock (sync)
            {
                DateTime now = DateTime.UtcNow;
                foreach (CaptureTicket<TTask> ticket in outstanding.Values)
                {
                    if (ticket.SessionId == sessionId)
                    {
                        MarkExpired(ticket, now);
                    }
                }
                // 批次作废说明触发/回图对应关系已不可信
                lastTriggerTrusted = false;
            }
        }

        /// <summary>
        /// 开始新批次前作废所有未完成Ticket：调用方已经放弃上一批，其迟到图像只能被丢弃。
        /// </summary>
        /// <returns>本次新作废的Ticket数量</returns>
        public int AbandonOutstanding()
        {
            lock (sync)
            {
                int count = 0;
                DateTime now = DateTime.UtcNow;
                foreach (CaptureTicket<TTask> ticket in outstanding.Values)
                {
                    if (MarkExpired(ticket, now)) count++;
                }
                if (count > 0) lastTriggerTrusted = false;
                return count;
            }
        }

        /// <summary>
        /// 检查未完成Ticket能否释放。
        /// 已到图正在处理的Ticket必须等其Complete；从未到图的失效Ticket在隔离期后回收，
        /// 回收后进入恢复期：必须由相机执行恢复并调用 CompleteRecovery 后才能建立新批次。
        /// 注意：登记Ticket不代表硬件已触发，回收数量不用于推算触发号。
        /// </summary>
        /// <returns>true=已无未完成Ticket；false=仍需等待，原因见detail。返回true后仍需检查 IsRecoveryRequired</returns>
        public bool TryReclaimLostFrames(TimeSpan quarantine, out int reclaimed, out string detail)
        {
            lock (sync)
            {
                reclaimed = 0;
                detail = string.Empty;
                if (outstanding.Count == 0) return true;

                DateTime now = DateTime.UtcNow;
                var lost = new List<CaptureTicket<TTask>>();
                foreach (CaptureTicket<TTask> ticket in outstanding.Values)
                {
                    if (ticket.State != CaptureTicketState.Expired)
                    {
                        detail = string.Format("Ticket={0}仍有效({1})", ticket.TicketId, ticket.State);
                        return false;
                    }

                    if (ticket.FrameReceivedAtUtc.HasValue)
                    {
                        detail = string.Format("Ticket={0}图像处理中", ticket.TicketId);
                        return false;
                    }

                    DateTime expiredAt = ticket.ExpiredAtUtc.HasValue ? ticket.ExpiredAtUtc.Value : now;
                    if (now - expiredAt < quarantine)
                    {
                        detail = string.Format("Ticket={0}隔离期未到", ticket.TicketId);
                        return false;
                    }

                    lost.Add(ticket);
                }

                foreach (CaptureTicket<TTask> ticket in lost)
                {
                    ticket.State = CaptureTicketState.Discarded;
                    outstanding.Remove(ticket.TicketId);
                }

                int count = pending.Count;
                for (int i = 0; i < count; i++)
                {
                    CaptureTicket<TTask> ticket = pending.Dequeue();
                    if (outstanding.ContainsKey(ticket.TicketId)) pending.Enqueue(ticket);
                }

                reclaimed = lost.Count;
                if (reclaimed > 0)
                {
                    recoveryRequired = true;
                    lastTriggerTrusted = false;
                    detail = string.Format("回收{0}个未到图Ticket,进入相机恢复期", reclaimed);
                }
                return true;
            }
        }

        /// <summary>
        /// 相机恢复完成(由Cam在关触发、清FIFO成功并确认静默后调用)。
        /// </summary>
        public bool CompleteRecovery()
        {
            lock (sync)
            {
                if (outstanding.Count > 0) return false;
                recoveryRequired = false;
                return true;
            }
        }

        public void Complete(CaptureTicket<TTask> ticket)
        {
            if (ticket == null) return;

            lock (sync)
            {
                if (!outstanding.Remove(ticket.TicketId)) return;
                ticket.State = ticket.State == CaptureTicketState.Expired ? CaptureTicketState.Discarded : CaptureTicketState.Completed;
            }
        }

        private static bool MarkExpired(CaptureTicket<TTask> ticket, DateTime now)
        {
            if (ticket.State == CaptureTicketState.Pending || ticket.State == CaptureTicketState.Processing)
            {
                ticket.State = CaptureTicketState.Expired;
                ticket.ExpiredAtUtc = now;
                return true;
            }
            return false;
        }
    }
}
