using System;
using System.Globalization;

namespace FanControl.LianLi.Devices;

/// <summary>
/// What one cooler's screen frames cost on their way out, for one plugin.log line a minute while
/// frames are sent: how many went out, how many the <see cref="CoolerHandoff"/> dropped before they
/// could (replaced by a newer one, or expired), the average and longest time one frame's packets took
/// to write, and the average frame's size. A minute opens with the first frame sent after the last
/// line, so a screen nobody draws on logs nothing, and the line for the last frames of a run comes a
/// minute after its first. The times are the caller's, read from its injected clock; the tally reads
/// none itself. Worker-thread only.
/// </summary>
internal sealed class ScreenFrameTally {
    /// <summary>
    /// How long a line covers: often enough to watch a frame rate being tried, seldom enough for a log
    /// that runs for months.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Since the first frame after the last line; null while none has been sent since.
    private DateTime? _sinceUtc;
    private int _frames;
    private long _bytes;
    private TimeSpan _writing;
    private TimeSpan _longest;

    /// <summary>
    /// A frame of <paramref name="bytes"/> went out, its packets written from
    /// <paramref name="startedUtc"/> to <paramref name="finishedUtc"/>.
    /// </summary>
    public void Sent(int bytes, DateTime startedUtc, DateTime finishedUtc) {
        // A clock that stepped back during the write counts the write as taking no time.
        TimeSpan took = finishedUtc > startedUtc ? finishedUtc - startedUtc : TimeSpan.Zero;
        _sinceUtc ??= startedUtc;
        _frames++;
        _bytes += bytes;
        _writing += took;
        if (took > _longest) {
            _longest = took;
        }
    }

    /// <summary>
    /// Whether a line is due at <paramref name="nowUtc"/>: a frame has been sent since the last one,
    /// the first of them <see cref="Interval"/> or more ago. A clock that stepped back counts as long
    /// ago, so the line still comes.
    /// </summary>
    public bool IsDue(DateTime nowUtc) => _sinceUtc is DateTime since && ClockSpan.Since(nowUtc, since) >= Interval;

    /// <summary>
    /// The line for cooler <paramref name="index"/>, with the <paramref name="dropped"/> frames the
    /// handoff dropped meanwhile, and a fresh start for the next. Call only when <see cref="IsDue"/>.
    /// </summary>
    public string Close(int index, int dropped) {
        // IsDue holds, so at least one frame was sent and the averages divide by a count above zero.
        string line = string.Format(
            CultureInfo.InvariantCulture,
            "G{0}: screen frames in the last minute: {1} sent, {2} dropped before sending (replaced by a newer one or expired), {3:0.0} ms average and {4:0.0} ms longest to write one, {5} bytes average",
            index,
            _frames,
            dropped,
            _writing.TotalMilliseconds / _frames,
            _longest.TotalMilliseconds,
            _bytes / _frames);
        _sinceUtc = null;
        _frames = 0;
        _bytes = 0;
        _writing = TimeSpan.Zero;
        _longest = TimeSpan.Zero;
        return line;
    }
}
