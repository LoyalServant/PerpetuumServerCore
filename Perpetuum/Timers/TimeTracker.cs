using System;

namespace Perpetuum.Timers
{
    /// <summary>
    /// Simplified time tracker
    /// </summary>
    public class TimeTracker
    {
        public TimeTracker(int duration) : this(TimeSpan.FromMilliseconds(duration))
        {
            Elapsed = TimeSpan.Zero;
        }

        public TimeTracker(TimeSpan duration)
        {
            Elapsed = TimeSpan.Zero;
            Duration = duration;
        }

        public void Update(TimeSpan time)
        {
            Elapsed += time;
        }

        public TimeSpan Duration { get; private set; }
        public TimeSpan Elapsed { get; private set; }

        public bool Expired
        {
            get { return Elapsed >= Duration; }
        }

        public TimeSpan Remaining
        {
            get { return Duration - Elapsed; }
        }

        public void Reset()
        {
            Elapsed = TimeSpan.Zero;
        }

        public override string ToString()
        {
            return $"Duration: {Duration}, Elapsed: {Elapsed}, Expired: {Expired}, Remaining: {Remaining}";
        }
    }
}