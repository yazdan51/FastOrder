using System;
using System.Collections.Generic;

namespace FastOrder
{
    internal sealed class ScheduledSlice
    {
        public ScheduledSlice(
            OrderSession session,
            DateTimeOffset targetTime,
            int priority,
            long sliceSequence)
        {
            ArgumentNullException.ThrowIfNull(
                session);

            if (targetTime < session.StartTime ||
                targetTime >= session.EndTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetTime),
                    "Scheduled slice must be inside its session window.");
            }

            if (sliceSequence <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sliceSequence));
            }

            Session =
                session;

            TargetTime =
                targetTime;

            Priority =
                priority;

            SliceSequence =
                sliceSequence;
        }

        public OrderSession Session
        {
            get;
        }

        public DateTimeOffset TargetTime
        {
            get;
        }

        /// <summary>
        /// Lower numeric values are dequeued first after TargetTime.
        /// </summary>
        public int Priority
        {
            get;
        }

        public long SliceSequence
        {
            get;
        }
    }

    internal readonly record struct ScheduledSlicePriority(
        DateTimeOffset TargetTime,
        int Priority,
        long SessionCreationSequence,
        long SliceSequence);

    internal sealed class ScheduledSlicePriorityComparer :
        IComparer<ScheduledSlicePriority>
    {
        public int Compare(
            ScheduledSlicePriority left,
            ScheduledSlicePriority right)
        {
            int targetTimeComparison =
                left.TargetTime.CompareTo(
                    right.TargetTime);

            if (targetTimeComparison != 0)
            {
                return targetTimeComparison;
            }

            int priorityComparison =
                left.Priority.CompareTo(
                    right.Priority);

            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            int sessionSequenceComparison =
                left.SessionCreationSequence.CompareTo(
                    right.SessionCreationSequence);

            if (sessionSequenceComparison != 0)
            {
                return sessionSequenceComparison;
            }

            return left.SliceSequence.CompareTo(
                right.SliceSequence);
        }
    }

    /// <summary>
    /// Thread-safe global queue containing at most one next eligible slice per session.
    /// </summary>
    internal sealed class GlobalNextDueQueue
    {
        private readonly object _syncRoot =
            new object();

        private readonly PriorityQueue<ScheduledSlice, ScheduledSlicePriority>
            _queue =
                new PriorityQueue<ScheduledSlice, ScheduledSlicePriority>(
                    new ScheduledSlicePriorityComparer());

        private readonly HashSet<Guid> _queuedSessionIds =
            new HashSet<Guid>();

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _queue.Count;
                }
            }
        }

        public void Enqueue(
            ScheduledSlice slice)
        {
            ArgumentNullException.ThrowIfNull(
                slice);

            lock (_syncRoot)
            {
                if (!_queuedSessionIds.Add(
                    slice.Session.SessionId))
                {
                    throw new InvalidOperationException(
                        "A next-due slice is already queued for this session.");
                }

                _queue.Enqueue(
                    slice,
                    CreatePriority(
                        slice));
            }
        }

        public bool TryPeek(
            out ScheduledSlice? slice)
        {
            lock (_syncRoot)
            {
                return _queue.TryPeek(
                    out slice,
                    out _);
            }
        }

        public bool TryDequeue(
            out ScheduledSlice? slice)
        {
            lock (_syncRoot)
            {
                if (!_queue.TryDequeue(
                    out slice,
                    out _))
                {
                    return false;
                }

                _queuedSessionIds.Remove(
                    slice.Session.SessionId);

                return true;
            }
        }

        public int RemoveSession(
            Guid sessionId)
        {
            lock (_syncRoot)
            {
                if (!_queuedSessionIds.Contains(
                    sessionId))
                {
                    return 0;
                }

                List<ScheduledSlice> retainedSlices =
                    new List<ScheduledSlice>(
                        Math.Max(
                            0,
                            _queue.Count - 1));

                int removedCount =
                    0;

                foreach ((ScheduledSlice Element, ScheduledSlicePriority Priority) item
                    in _queue.UnorderedItems)
                {
                    if (item.Element.Session.SessionId ==
                        sessionId)
                    {
                        removedCount++;
                    }
                    else
                    {
                        retainedSlices.Add(
                            item.Element);
                    }
                }

                _queue.Clear();
                _queuedSessionIds.Clear();

                foreach (ScheduledSlice retainedSlice in retainedSlices)
                {
                    _queue.Enqueue(
                        retainedSlice,
                        CreatePriority(
                            retainedSlice));

                    _queuedSessionIds.Add(
                        retainedSlice.Session.SessionId);
                }

                return removedCount;
            }
        }

        private static ScheduledSlicePriority CreatePriority(
            ScheduledSlice slice)
        {
            return new ScheduledSlicePriority(
                slice.TargetTime,
                slice.Priority,
                slice.Session.CreationSequence,
                slice.SliceSequence);
        }
    }
}
