#if ENABLE_LIGHTING
using System;
using System.IO;
using System.Linq;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.OpenRgb;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.OpenRgb;

/// <summary>
/// The server on fake sockets: each client served packet by packet, every way a client can misbehave
/// closing only that client (each kind logged once), at most four clients, the listener on its own
/// thread, and everything closed at Dispose.
/// </summary>
public sealed class OpenRgbServerTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lianli-openrgb-server-" + Guid.NewGuid().ToString("N"));
    private readonly FakeLoopbackListenerApi _api = new FakeLoopbackListenerApi();
    private readonly FakeLogger _log = new FakeLogger();
    private readonly CoolerHandoff _cooler = new CoolerHandoff(new FakeClock());

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private OpenRgbServer NewServer(FakeDeviceCallClock? clock = null)
        => new OpenRgbServer(
            new[] { new OpenRgbRing("LianLi/0", _cooler) }, new OpenRgbLookFile(_directory), "2.0.9", _api, clock ?? new FakeDeviceCallClock(), _log);

    private static byte[] U32(long value) => new OpenRgbWriter().UInt32(value).ToArray();

    [Fact]
    public void AClient_IsAnsweredPacketByPacket_UntilItLeaves() {
        using OpenRgbServer server = NewServer();
        var client = new FakeLoopbackConnection();
        byte[] version = OpenRgbProtocol.Packet(0, 40, U32(5));
        client.Send(version.Take(5).ToArray());
        client.Send(version.Skip(5).ToArray());
        client.Send(OpenRgbProtocol.Packet(0, 50, System.Text.Encoding.ASCII.GetBytes("OpenRGB\0")));
        client.Send(OpenRgbProtocol.Packet(0, 0, Array.Empty<byte>()));
        client.Send(OpenRgbProtocol.Packet(0, 50, System.Text.Encoding.ASCII.GetBytes("OpenRGB\0")));

        server.Serve(client);

        Assert.Equal(new[] { OpenRgbProtocol.Packet(0, 40, U32(6)), OpenRgbProtocol.Packet(0, 0, U32(1)) }, client.Writes);
        Assert.True(client.Disposed);
        Assert.Equal(System.Threading.Timeout.Infinite, client.Timeouts[0]);
        Assert.All(client.Timeouts.Skip(1).Where(t => t != System.Threading.Timeout.Infinite), t => Assert.InRange(t, 1, 10000));
        Assert.Equal(new[] { "OpenRGB: client \"OpenRGB\" connected" }, _log.Messages);
    }

    [Fact]
    public void AMalformedPacket_ClosesTheClient_AndEachKindIsLoggedOnce() {
        using OpenRgbServer server = NewServer();
        for (int i = 0; i < 2; i++) {
            var client = new FakeLoopbackConnection();
            byte[] packet = OpenRgbProtocol.Packet(0, 0, Array.Empty<byte>());
            packet[0] = (byte)'X';
            client.Send(packet);
            client.Send(OpenRgbProtocol.Packet(0, 0, Array.Empty<byte>()));

            server.Serve(client);

            Assert.Empty(client.Writes);
            Assert.True(client.Disposed);
        }

        Assert.Equal(new[] { "OpenRGB: a client sent a packet that does not start with ORGB, so it was disconnected (logged once)" }, _log.Messages);
    }

    [Fact]
    public void ACommandTheRingCannotTake_ClosesTheClient() {
        using OpenRgbServer server = NewServer();
        var client = new FakeLoopbackConnection();
        client.Send(OpenRgbProtocol.Packet(3, 1, Array.Empty<byte>()));

        server.Serve(client);

        Assert.Equal(new[] { "OpenRGB: a client sent a controller the server does not have, so it was disconnected (logged once)" }, _log.Messages);
    }

    [Fact]
    public void APacketThatDoesNotArriveWholeInTenSeconds_ClosesTheClient() {
        using OpenRgbServer server = NewServer(new FakeDeviceCallClock(stepMilliseconds: 20000));
        var client = new FakeLoopbackConnection();
        client.Send(OpenRgbProtocol.Packet(0, 0, Array.Empty<byte>()));

        server.Serve(client);

        Assert.Equal(new[] { "OpenRGB: a client was disconnected: a packet did not arrive whole within 10000 ms (logged once)" }, _log.Messages);
    }

    [Fact]
    public void AClientThatLeavesMidPacket_OrCannotBeWrittenTo_IsClosed() {
        using OpenRgbServer server = NewServer();
        var leaving = new FakeLoopbackConnection();
        leaving.Send(OpenRgbProtocol.Packet(0, 40, U32(5)).Take(18).ToArray());
        server.Serve(leaving);

        var deaf = new FakeLoopbackConnection { WriteFails = new IOException("An existing connection was forcibly closed.") };
        deaf.Send(OpenRgbProtocol.Packet(0, 40, U32(5)));
        server.Serve(deaf);

        Assert.Equal(
            new[] {
                "OpenRGB: a client was disconnected: the client closed its end in the middle of a packet (logged once)",
                "OpenRGB: a client was disconnected: An existing connection was forcibly closed. (logged once)",
            },
            _log.Messages);
        Assert.True(deaf.Disposed);
    }

    [Fact]
    public void TheServer_ListensOn6743_ServesItsClientsOnTheirOwnThreads_AndClosesEverythingAtDispose() {
        var server = NewServer();
        var client = new FakeLoopbackConnection { BlockWhenEmpty = true };
        client.Send(OpenRgbProtocol.Packet(0, 40, U32(6)));

        server.Start();
        _api.Listener.Connect(client);

        Assert.True(SpinWait.SpinUntil(() => client.Writes.Count == 1, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, server.ClientCount);
        Assert.Equal(new[] { 6743 }, _api.Ports);
        Assert.Contains(
            "OpenRGB: SDK server listening on 127.0.0.1:6743 for 1 screen ring(s); in OpenRGB, add 127.0.0.1:6743 under Settings, SDK Client",
            _log.Messages);

        server.Dispose();

        Assert.True(server.HasStopped);
        Assert.True(client.Disposed);
        Assert.True(_api.Listener.Disposed);
        Assert.True(SpinWait.SpinUntil(() => server.ClientCount == 0, TimeSpan.FromSeconds(10)));
        Assert.DoesNotContain(_log.Messages, m => m.Contains("disconnected") || m.Contains("stopped"));
        server.Dispose();
    }

    [Fact]
    public void AFifthClient_IsTurnedAway_AndThatIsLoggedOnce() {
        using OpenRgbServer server = NewServer();
        FakeLoopbackConnection[] clients = Enumerable.Range(0, 6).Select(_ => new FakeLoopbackConnection { BlockWhenEmpty = true }).ToArray();

        foreach (FakeLoopbackConnection client in clients) {
            server.Accept(client);
        }

        Assert.Equal(4, server.ClientCount);
        Assert.False(clients[3].Disposed);
        Assert.True(clients[4].Disposed);
        Assert.True(clients[5].Disposed);
        Assert.Single(_log.Messages, m => m == "OpenRGB: a client was turned away: 4 are connected already, the most taken (logged once)");
    }

    [Fact]
    public void AClientAcceptedAsTheServerStops_IsClosed() {
        OpenRgbServer server = NewServer();
        server.Dispose();
        var client = new FakeLoopbackConnection();

        server.Accept(client);

        Assert.True(client.Disposed);
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void APortThatCannotBeHad_IsLogged_AndTheServerStops() {
        _api.ListenFails = new IOException("Only one usage of each socket address is normally permitted.");
        using OpenRgbServer server = NewServer();

        server.Start();

        Assert.True(SpinWait.SpinUntil(() => server.HasStopped, TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { "OpenRGB: the SDK server on 127.0.0.1:6743 stopped: Only one usage of each socket address is normally permitted." }, _log.Messages);
    }

    [Fact]
    public void AListenerThatFails_IsLogged_AndTheServerStops() {
        _api.Listener.AcceptFails = new IOException("The network went away.");
        using OpenRgbServer server = NewServer();

        server.Start();

        Assert.True(SpinWait.SpinUntil(() => server.HasStopped, TimeSpan.FromSeconds(10)));
        Assert.Contains("OpenRGB: the SDK server on 127.0.0.1:6743 stopped: The network went away.", _log.Messages);
    }

    [Fact]
    public void AListenerThatOpensAfterDispose_IsClosedAtOnce_AndTheSlowStopIsLogged() {
        using var gate = new ManualResetEventSlim(false);
        _api.ListenGate = gate;
        var server = NewServer();
        server.Start();
        Assert.True(SpinWait.SpinUntil(() => _api.Ports.Count == 1, TimeSpan.FromSeconds(10)));

        server.Dispose();
        gate.Set();

        Assert.True(SpinWait.SpinUntil(() => server.HasStopped, TimeSpan.FromSeconds(10)));
        Assert.True(_api.Listener.Disposed);
        Assert.Equal(new[] { "OpenRGB: a thread of the SDK server was still running at Close; it stops when its socket call returns" }, _log.Messages);
    }

    [Fact]
    public void AServerNeverStarted_HasStopped()
        => Assert.True(NewServer().HasStopped);

    [Fact]
    public void NullArguments_AreRejected() {
        var rings = Array.Empty<OpenRgbRing>();
        var looks = new OpenRgbLookFile(_directory);
        var clock = new FakeDeviceCallClock();
        Assert.Throws<ArgumentNullException>(() => new OpenRgbServer(null!, looks, "v", _api, clock, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbServer(rings, null!, "v", _api, clock, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbServer(rings, looks, null!, _api, clock, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbServer(rings, looks, "v", null!, clock, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbServer(rings, looks, "v", _api, null!, _log));
        Assert.Throws<ArgumentNullException>(() => new OpenRgbServer(rings, looks, "v", _api, clock, null!));
        using OpenRgbServer server = NewServer();
        Assert.Throws<ArgumentNullException>(() => server.Serve(null!));
        Assert.Throws<ArgumentNullException>(() => server.Accept(null!));
    }
}
#endif
