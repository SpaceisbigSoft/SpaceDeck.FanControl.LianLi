using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using FanControl.LianLi.Devices;
using FanControl.LianLi.SpaceDeck;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.SpaceDeck;

/// <summary>
/// The screen client on a fake clock, pipe and service control manager: connect, check the server,
/// read each message in its three parts, hand the frame to its cooler, refuse whatever breaks a rule
/// (each kind logged once), ration the messages, back off while the collector is away, the off
/// switch, and the thread stopping.
/// </summary>
public class SpaceDeckScreenClientTests {
    private readonly FakeDeviceCallClock _clock = new FakeDeviceCallClock();
    private readonly FakeNamedPipeApi _pipe = new FakeNamedPipeApi();
    private readonly FakeServiceControlApi _services = new FakeServiceControlApi();
    private readonly FakeLogger _log = new FakeLogger();
    private readonly CoolerHandoff _cooler = new CoolerHandoff(new FakeClock());

    private SpaceDeckScreenClient NewClient()
        => new SpaceDeckScreenClient(
            new Dictionary<string, CoolerHandoff>(StringComparer.Ordinal) { ["LianLi/0"] = _cooler },
            _pipe,
            _services,
            _clock,
            _log);

    private void Script(params byte[][] chunks)
        => _pipe.ConfigureReader = reader => {
            foreach (byte[] chunk in chunks) {
                reader.Send(chunk);
            }
        };

    private static string NotReading(string reason)
        => "SpaceDeck screen: not reading SpaceDeck's collector (" + reason + "); trying again in the background, at most every 30 s (logged once)";

    private const string Connected
        = "SpaceDeck screen: connected to SpaceDeck's collector (process 4242); showing its frames on 1 cooler screen(s)";

    [Fact]
    public void AFrame_IsHandedToItsCooler_AfterTheServerIsChecked() {
        byte[] jpeg = ScreenMessage.Jpeg(3000);
        Script(ScreenMessage.Build("LianLi/0", jpeg));
        using SpaceDeckScreenClient client = NewClient();

        Assert.Equal(0, client.Step());

        Assert.Equal(new[] { SpaceDeckPublisher.ServiceName }, _services.Queries);
        Assert.Equal(new[] { "SpaceDeck.Screen" }, _pipe.ReadAttempts);
        Assert.Equal(new[] { 250 }, _pipe.ReadTimeouts);
        Assert.Empty(_pipe.Attempts);
        Assert.Equal(jpeg, _cooler.TakeFrame());
        Assert.Equal(Connected, Assert.Single(_log.Messages));
    }

    [Fact]
    public void AMessage_ArrivingInPieces_IsReadWhole() {
        byte[] message = ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(100));
        Script(message.Take(3).ToArray(), message.Skip(3).Take(10).ToArray(), message.Skip(13).ToArray());
        using SpaceDeckScreenClient client = NewClient();

        Assert.Equal(0, client.Step());

        Assert.NotNull(_cooler.TakeFrame());
        Assert.True(_pipe.Readers[0].Reads >= 4);
    }

    [Fact]
    public void ThePipeClosing_DropsTheConnection_AndTheNextTryWaitsASecond() {
        Script(ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(10)));
        using SpaceDeckScreenClient client = NewClient();
        client.Step();

        Assert.Equal(1000, client.Step());

        Assert.True(_pipe.Readers[0].Disposed);
        Assert.Contains(NotReading("the collector closed its screen pipe"), _log.Messages);
        _clock.NowMilliseconds = 400;
        Assert.Equal(600, client.Step());
        Assert.Single(_pipe.ReadAttempts);
    }

    [Fact]
    public void AFrameForADeviceWithoutAScreen_IsIgnored_AndLoggedOnce() {
        Script(
            ScreenMessage.Build("LianLi/9", ScreenMessage.Jpeg(10)),
            ScreenMessage.Build("LianLi/9", ScreenMessage.Jpeg(10)),
            ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(12)));
        using SpaceDeckScreenClient client = NewClient();

        client.Step();
        client.Step();
        client.Step();

        Assert.Equal(12, _cooler.TakeFrame()!.Length);
        Assert.Single(_log.Messages, m => m == "SpaceDeck screen: frames for LianLi/9, which has no screen the plugin drives, are ignored (logged once)");
    }

    public static TheoryData<byte[], string> BrokenMessages() {
        byte[] wrongMagic = ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(10));
        wrongMagic[0] = 0x00;
        return new TheoryData<byte[], string> {
            { wrongMagic, "a message that does not start with SDSF" },
            { ScreenMessage.Header(8, version: 2), "a message of a version other than 1" },
            { ScreenMessage.Header(8, kind: 3), "a message of an unknown kind" },
            { ScreenMessage.Header(0), "an id length outside 1 to 128" },
            { ScreenMessage.Header(8).Concat(ScreenMessage.IdAndLength("LianLi 0", 10)).ToArray(), "an id that breaks the id rule" },
            { ScreenMessage.Header(8).Concat(ScreenMessage.IdAndLength("LianLi/0", 0)).ToArray(), "a payload length outside 1 to 262144" },
            { ScreenMessage.Build("LianLi/0", new byte[] { 0xFF, 0xD8, 0x00, 0x00 }), "a payload that is not a JPEG (FF D8 ... FF D9)" },
        };
    }

    [Theory]
    [MemberData(nameof(BrokenMessages))]
    public void AMessageThatBreaksARule_DropsTheConnection_AndEachKindIsLoggedOnce(byte[] message, string rule) {
        Script(message);
        using SpaceDeckScreenClient client = NewClient();

        Assert.Equal(1000, client.Step());
        _clock.NowMilliseconds = 1000;
        Assert.Equal(2000, client.Step());

        Assert.All(_pipe.Readers, reader => Assert.True(reader.Disposed));
        Assert.Equal(2, _pipe.Readers.Count);
        Assert.Single(_log.Messages, m => m == NotReading("the collector sent " + rule + ", so the connection was dropped"));
        Assert.Null(_cooler.TakeFrame());
    }

    [Fact]
    public void AStreamOfMessages_IsRationedToABurstOfSixty_ThenThirtyASecond() {
        Assert.Equal(60, SpaceDeckScreenClient.BurstMessages);
        Assert.Equal(30, SpaceDeckScreenClient.MessagesPerSecond);
        byte[] message = ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(10));
        Script(Enumerable.Repeat(message, 63).ToArray());
        using SpaceDeckScreenClient client = NewClient();

        for (int i = 0; i < 60; i++) {
            Assert.Equal(0, client.Step());
        }

        // 34 ms refills one message (a thirtieth of a second is 33.3 ms), and no more.
        _clock.NowMilliseconds = 34;
        Assert.Equal(0, client.Step());
        Assert.Equal(1000, client.Step());

        Assert.Contains(NotReading("the collector sent more than 30 messages a second, so the connection was dropped"), _log.Messages);
    }

    [Fact]
    public void TheBucket_RefillsNoFurtherThanItsBurst() {
        byte[] message = ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(10));
        Script(Enumerable.Repeat(message, 62).ToArray());
        using SpaceDeckScreenClient client = NewClient();
        client.Step();

        _clock.NowMilliseconds = 60_000;
        for (int i = 0; i < 60; i++) {
            Assert.Equal(0, client.Step());
        }

        Assert.Equal(1000, client.Step());
    }

    [Fact]
    public void AValidMessage_EndsARunOfFailures_SoTheNextTryWaitsASecondAgain() {
        _pipe.ReadersAvailable = false;
        using SpaceDeckScreenClient client = NewClient();
        Assert.Equal(1000, client.Step());
        _clock.NowMilliseconds = 1000;
        Assert.Equal(2000, client.Step());

        _pipe.ReadersAvailable = true;
        Script(ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(10)));
        _clock.NowMilliseconds = 3000;
        Assert.Equal(0, client.Step());
        Assert.Equal(1000, client.Step());

        Assert.Single(_log.Messages, m => m == NotReading("its screen pipe took no connection within 250 ms"));
    }

    [Fact]
    public void ACollectorThatIsNotInstalled_IsLoggedOnce_AndTheClientStops() {
        _services.ProcessId = null;
        using SpaceDeckScreenClient client = NewClient();

        Assert.Equal(SpaceDeckScreenClient.Stopped, client.Step());
        Assert.Equal(SpaceDeckScreenClient.Stopped, client.Step());

        Assert.Single(_services.Queries);
        Assert.Empty(_pipe.ReadAttempts);
        Assert.Equal(
            "SpaceDeck screen: the SpaceDeck collector is not installed (there is no SpaceDeckCollector service), so nothing is shown on the coolers' screens",
            Assert.Single(_log.Messages));
    }

    [Fact]
    public void ACollectorThatIsNotRunning_IsTriedAgainAfterGapsDoublingToThirtySeconds() {
        _services.ProcessId = 0;
        using SpaceDeckScreenClient client = NewClient();
        var waits = new List<int>();
        for (int i = 0; i < 7; i++) {
            int wait = client.Step();
            waits.Add(wait);
            _clock.NowMilliseconds += wait;
        }

        Assert.Equal(new[] { 1000, 2000, 4000, 8000, 16000, 30000, 30000 }, waits);
        Assert.Empty(_pipe.ReadAttempts);
        Assert.Equal(NotReading("the SpaceDeckCollector service is not running"), Assert.Single(_log.Messages));
    }

    [Fact]
    public void APipeServedByAnotherProcess_IsLeftUnread() {
        _pipe.ServerProcessId = 666;
        using SpaceDeckScreenClient client = NewClient();

        Assert.Equal(1000, client.Step());

        FakeNamedPipeReader reader = Assert.Single(_pipe.Readers);
        Assert.True(reader.Disposed);
        Assert.Equal(0, reader.Reads);
        Assert.Equal(
            NotReading("the screen pipe's server is process 666, not the SpaceDeckCollector service's process 4242, so it was left unread"),
            Assert.Single(_log.Messages));
    }

    [Fact]
    public void AServerThatCannotBeIdentified_IsLeftUnread() {
        _pipe.ConfigureReader = reader => reader.ServerProcessIdFails = new Win32Exception(5, "Access is denied");
        using SpaceDeckScreenClient client = NewClient();

        Assert.Equal(1000, client.Step());

        Assert.True(Assert.Single(_pipe.Readers).Disposed);
        Assert.Equal(NotReading("Access is denied"), Assert.Single(_log.Messages));
    }

    [Fact]
    public void FailuresOfTheServiceQueryAndTheRead_AreLoggedAndTriedAgain() {
        _services.Fails = new Win32Exception(5, "Access is denied");
        using SpaceDeckScreenClient client = NewClient();
        Assert.Equal(1000, client.Step());

        _services.Fails = null;
        _pipe.ConfigureReader = reader => reader.ReadFails = new IOException("The pipe has been ended.");
        _clock.NowMilliseconds = 1000;
        Assert.Equal(2000, client.Step());

        Assert.Equal(new[] { NotReading("Access is denied"), Connected, NotReading("The pipe has been ended.") }, _log.Messages);
    }

    [Fact]
    public void DisposingWhileAReadWaits_EndsTheStep_WithoutAFailure() {
        SpaceDeckScreenClient client = NewClient();
        _pipe.ConfigureReader = reader => reader.OnRead = client.Dispose;

        Assert.Equal(SpaceDeckScreenClient.Stopped, client.Step());
        Assert.Equal(SpaceDeckScreenClient.Stopped, client.Step());

        Assert.True(Assert.Single(_pipe.Readers).Disposed);
        Assert.Equal(new[] { Connected }, _log.Messages);
    }

    [Fact]
    public void DisposingWhileTheServerIsChecked_ClosesTheNewConnection() {
        SpaceDeckScreenClient client = NewClient();
        _pipe.ConfigureReader = reader => reader.OnReadServerProcessId = client.Dispose;

        client.Step();

        FakeNamedPipeReader only = Assert.Single(_pipe.Readers);
        Assert.True(only.Disposed);
        Assert.Equal(0, only.Reads);
        Assert.Equal(SpaceDeckScreenClient.Stopped, client.Step());
        Assert.Empty(_log.Messages);
    }

    [Fact]
    public void TheThread_HandsFramesOver_AndStopsWhenDisposed() {
        using var offered = new ManualResetEventSlim(false);
        _cooler.WakeWith(offered.Set);
        _pipe.ConfigureReader = reader => {
            reader.Send(ScreenMessage.Build("LianLi/0", ScreenMessage.Jpeg(10)));
            reader.BlockWhenEmpty = true;
        };
        var client = NewClient();

        client.Start();
        Assert.True(SpinWait.SpinUntil(() => offered.IsSet, TimeSpan.FromSeconds(10)));
        client.Dispose();

        Assert.True(client.HasStopped);
        Assert.True(Assert.Single(_pipe.Readers).Disposed);
        client.Dispose();
    }

    [Fact]
    public void TheThread_StopsByItself_WhenTheCollectorIsNotInstalled() {
        _services.ProcessId = null;
        using SpaceDeckScreenClient client = NewClient();

        client.Start();

        Assert.True(SpinWait.SpinUntil(() => client.HasStopped, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void AThreadStillReadingAtDispose_IsLogged_AndStopsWhenTheReadReturns() {
        using var hold = new ManualResetEventSlim(false);
        using var reading = new ManualResetEventSlim(false);
        _pipe.ConfigureReader = reader => {
            reader.OnRead = reading.Set;
            reader.HoldGate = hold;
        };
        var client = NewClient();
        client.Start();
        Assert.True(SpinWait.SpinUntil(() => reading.IsSet, TimeSpan.FromSeconds(10)));

        client.Dispose();

        Assert.Contains("SpaceDeck screen: the client was still reading at Close; it stops when the read returns", _log.Messages);
        hold.Set();
        Assert.True(SpinWait.SpinUntil(() => client.HasStopped, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void AClientNeverStarted_HasStopped_AndDisposes() {
        SpaceDeckScreenClient client = NewClient();

        Assert.True(client.HasStopped);
        client.Dispose();
        Assert.Equal(SpaceDeckScreenClient.Stopped, client.Step());
    }

    [Fact]
    public void NullArguments_AreRejected() {
        var screens = new Dictionary<string, CoolerHandoff>();
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckScreenClient(null!, _pipe, _services, _clock, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckScreenClient(screens, null!, _services, _clock, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckScreenClient(screens, _pipe, null!, _clock, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckScreenClient(screens, _pipe, _services, null!, _log));
        Assert.Throws<ArgumentNullException>(() => new SpaceDeckScreenClient(screens, _pipe, _services, _clock, null!));
    }
}
