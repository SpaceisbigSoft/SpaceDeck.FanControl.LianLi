using System;
using System.Collections.Generic;
using FanControl.LianLi.Transport;

namespace FanControl.LianLi.Tests.Fakes;

/// <summary>
/// The service control manager as a test describes it: the collector's service runs in
/// <see cref="ProcessId"/> (null for not installed, 0 for stopped), or the query fails. Every query is
/// recorded with the service it asked about.
/// </summary>
internal sealed class FakeServiceControlApi : IServiceControlApi {
    public const int CollectorProcessId = 4242;

    private readonly object _gate = new object();
    private readonly List<string> _queries = new List<string>();

    public int? ProcessId { get; set; } = CollectorProcessId;

    public Exception? Fails { get; set; }

    public IReadOnlyList<string> Queries { get { lock (_gate) { return _queries.ToArray(); } } }

    public int? QueryProcessId(string serviceName) {
        lock (_gate) {
            _queries.Add(serviceName);
        }

        if (Fails != null) {
            throw Fails;
        }

        return ProcessId;
    }
}
