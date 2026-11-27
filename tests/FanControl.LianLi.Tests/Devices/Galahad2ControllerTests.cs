using System;
using System.Collections.Generic;
using System.Linq;
using FanControl.LianLi.Devices;
using FanControl.LianLi.Protocol;
using FanControl.LianLi.Tests.Fakes;
using Xunit;

namespace FanControl.LianLi.Tests.Devices;

public class Galahad2ControllerTests {
    private const int FanChannel = 0;
    private const int PumpChannel = 1;
    private const int TrinityProductId = 0x7371;
    private const int VisionProductId = 0x7391;

    private static (Galahad2Controller controller, FakeDeviceTransport transport, FakeClock clock) NewController() {
        var transport = new FakeDeviceTransport();
        var clock = new FakeClock();
        var controller = new Galahad2Controller(0, transport, TrinityProductId, clock, new FakeLogger());
        return (controller, transport, clock);
    }

    private static (Galahad2Controller controller, FakeDeviceTransport transport) NewCooler(int productId = VisionProductId) {
        var transport = new FakeDeviceTransport();
        var controller = new Galahad2Controller(0, transport, productId, new FakeClock(), new FakeLogger());
        return (controller, transport);
    }

    // A Vision LCD's handshake reply: the RPM pairs, then the coolant valid flag, whole degrees and tenths.
    private static byte[] VisionHandshakeReply(int fanRpm, int pumpRpm, byte valid, byte whole, byte tenths) {
        return CommandPacket.Build(
            0x81,
            (byte)((fanRpm >> 8) & 0xFF),
            (byte)(fanRpm & 0xFF),
            (byte)((pumpRpm >> 8) & 0xFF),
            (byte)(pumpRpm & 0xFF),
            valid,
            whole,
            tenths);
    }

    // The handshake reply frame: fan rpm at payload 0-1, pump rpm at payload 2-3, big-endian.
    private static byte[] HandshakeReply(int fanRpm, int pumpRpm) {
        return CommandPacket.Build(
            0x81,
            (byte)((fanRpm >> 8) & 0xFF),
            (byte)(fanRpm & 0xFF),
            (byte)((pumpRpm >> 8) & 0xFF),
            (byte)(pumpRpm & 0xFF));
    }

    [Fact]
    public void Constructor_DoesNoIo() {
        var (_, transport, _) = NewController();
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void ApplyPending_AfterTransportReopened_ReplaysLookThenResendsFanAndPump() {
        var (controller, transport, _) = NewController();
        var replayedAt = new List<int>();
        controller.ReplayOnReconnect(() => {
            replayedAt.Add(transport.Writes.Count);
            return true;
        });
        controller.SetTarget(FanChannel, 50);
        controller.SetTarget(PumpChannel, 70);
        controller.ApplyPending();
        transport.Clear();

        // The transport lost and reopened the cooler (a wake): no setup writes of its own, so the
        // saved look replays first, then both unchanged duties are re-sent - the cooler may have reset.
        transport.Generation = 1;
        controller.ApplyPending();

        Assert.Equal(new[] { 0 }, replayedAt);
        Assert.Equal(2, transport.Writes.Count);
        Assert.Equal(0x8B, transport.Writes[0][1]); // fan
        Assert.Equal(0x8A, transport.Writes[1][1]); // pump
    }

    // While the handle is still faulted the cooler is off the bus: the look is not replayed (it
    // would be refused, or reopen the path itself and be lost on it), and the duties go out as
    // usual, one of them being what reopens the cooler on the backoff. The look follows once it
    // is back.
    [Fact]
    public void ApplyPending_WhileTheTransportIsStillFaulted_ReplaysNothing_UntilTheCoolerIsBack() {
        var (controller, transport, _) = NewController();
        int replays = 0;
        controller.ReplayOnReconnect(() => {
            replays++;
            return true;
        });
        controller.SetTarget(FanChannel, 50);
        controller.ApplyPending();
        transport.Clear();

        transport.Generation = 1;
        transport.IsFaulted = true;
        controller.ApplyPending();

        Assert.Equal(0, replays);
        Assert.Empty(transport.Writes); // the duty is fresh, and nothing was replayed

        transport.IsFaulted = false;
        controller.ApplyPending();

        Assert.Equal(1, replays);
        Assert.Single(transport.Writes); // the fan, re-sent after the look
    }

    // A look the cooler refused stays owed on its own and is tried again on the keepalive
    // interval; the duties are re-sent regardless, and the reconnect is recorded.
    [Fact]
    public void ApplyPending_ALookTheCoolerRefused_StaysOwed_AndIsTriedAgainOnTheKeepaliveInterval() {
        var (controller, transport, clock) = NewController();
        int replays = 0;
        controller.ReplayOnReconnect(() => ++replays >= 2);
        controller.SetTarget(PumpChannel, 70);
        controller.ApplyPending();
        transport.Clear();

        transport.Generation = 1;
        controller.ApplyPending();
        Assert.Equal(1, replays);
        Assert.Single(transport.Writes);

        controller.ApplyPending();
        Assert.Equal(1, replays);
        clock.Advance(ChannelWriteDecision.RefreshInterval);
        controller.ApplyPending();
        Assert.Equal(2, replays);
        clock.Advance(ChannelWriteDecision.RefreshInterval);
        controller.ApplyPending();
        Assert.Equal(2, replays);
    }

    [Fact]
    public void ApplyPending_FanChannel_WritesTheFanCommand() {
        var (controller, transport, _) = NewController();

        controller.SetTarget(FanChannel, 50);
        controller.ApplyPending();

        Assert.Single(transport.Writes);
        Assert.Equal(0x8B, transport.Writes[0][1]); // SetFan command
        Assert.Equal(0x00, transport.Writes[0][6]); // motherboard-sync flag off
        Assert.Equal(50, transport.Writes[0][7]);   // duty 1:1
    }

    [Fact]
    public void ApplyPending_PumpChannel_FloorsTheDutySoThePumpNeverStops() {
        var (controller, transport, _) = NewController();

        controller.SetTarget(PumpChannel, 0); // a curve commanding 0% must not stop the pump
        controller.ApplyPending();

        Assert.Single(transport.Writes);
        Assert.Equal(0x8A, transport.Writes[0][1]); // SetPump command
        Assert.Equal(Galahad2Protocol.PumpDutyFloor, transport.Writes[0][7]);
    }

    [Fact]
    public void ApplyPending_UnchangedAndFresh_WritesNothing() {
        var (controller, transport, _) = NewController();
        controller.SetTarget(FanChannel, 50);
        controller.ApplyPending();
        transport.Clear();

        controller.ApplyPending(); // same target, clock not advanced

        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void ApplyPending_StaleAfterFifteenSeconds_Reasserts() {
        var (controller, transport, clock) = NewController();
        controller.SetTarget(FanChannel, 50);
        controller.ApplyPending();
        transport.Clear();

        clock.Advance(TimeSpan.FromSeconds(15));
        controller.ApplyPending();

        Assert.Single(transport.Writes);
        Assert.Equal(0x8B, transport.Writes[0][1]);
    }

    [Fact]
    public void ApplyPending_LogsTheFirstSpeed_ButNotALaterChangeOrTheKeepaliveResend() {
        var clock = new FakeClock();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, new FakeDeviceTransport(), TrinityProductId, clock, logger);
        controller.SetTarget(FanChannel, 50);
        controller.ApplyPending();
        Assert.Contains("Set G0:fan = 50%", logger.Messages);
        int before = logger.Messages.Count;

        clock.Advance(ChannelWriteDecision.RefreshInterval);
        controller.ApplyPending();
        controller.SetTarget(FanChannel, 60);
        controller.ApplyPending();

        Assert.Equal(before, logger.Messages.Count);
    }

    [Fact]
    public void ReleaseChannel_StopsKeepaliveForThatChannel() {
        var (controller, transport, clock) = NewController();
        controller.SetTarget(PumpChannel, 80);
        controller.ApplyPending();
        controller.ReleaseChannel(PumpChannel);
        transport.Clear();

        clock.Advance(TimeSpan.FromSeconds(30));
        controller.ApplyPending();

        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void PollRpm_WritesHandshakeAndCachesFanAndPumpRpm() {
        var (controller, transport, _) = NewController();
        transport.ReadReplies.Enqueue(HandshakeReply(1200, 2800));

        controller.PollRpm();

        Assert.Equal(0x81, transport.Writes[0][1]); // the handshake request was written
        Assert.Equal(1, transport.InterruptReadCount); // then the reply was read
        Assert.Equal(1200f, controller.GetRpm(FanChannel));
        Assert.Equal(2800f, controller.GetRpm(PumpChannel));
    }

    // A packet that arrives after the handshake is written and before the cooler's answer (another
    // program's reply, or one the Vision sends unasked) is read past, not decoded.
    [Fact]
    public void ForgetReadings_ReadsNothing_UntilTheNextPoll() {
        var (controller, transport, _) = NewController();
        transport.ReadReplies.Enqueue(HandshakeReply(1200, 2800));
        controller.PollRpm();

        controller.ForgetReadings();
        Assert.Null(controller.GetRpm(FanChannel));
        Assert.Null(controller.GetRpm(PumpChannel));

        transport.ReadReplies.Enqueue(HandshakeReply(1300, 2900));
        controller.PollRpm();
        Assert.Equal(1300f, controller.GetRpm(FanChannel));
        Assert.Equal(2900f, controller.GetRpm(PumpChannel));
    }

    [Fact]
    public void PollRpm_ReadsPastOtherReportsToTheHandshakeReply() {
        var (controller, transport, _) = NewController();
        transport.ReadReplies.Enqueue(CommandPacket.Build(0x8A, 0x00, 0x50));
        transport.ReadReplies.Enqueue(HandshakeReply(1200, 2800));

        controller.PollRpm();

        Assert.Equal(2, transport.InterruptReadCount);
        Assert.Equal(1200f, controller.GetRpm(FanChannel));
        Assert.Equal(2800f, controller.GetRpm(PumpChannel));
    }

    [Fact]
    public void PollRpm_AReportThatIsNotAHandshakeReply_IsNeverTakenAsRpm() {
        var (controller, transport, _) = NewController();
        transport.ReadReplies.Enqueue(HandshakeReply(1200, 2800));
        controller.PollRpm();
        for (int i = 0; i < 40; i++) {
            transport.ReadReplies.Enqueue(CommandPacket.Build(0x8A, 0x03, 0xE8, 0x07, 0xD0)); // would decode to a plausible 1000 and 2000 rpm
        }

        transport.Clear();

        controller.PollRpm();

        Assert.Equal(0x81, Assert.Single(transport.Writes)[1]);
        Assert.Equal(32, transport.InterruptReadCount);
        Assert.Equal(1200f, controller.GetRpm(FanChannel));
        Assert.Equal(2800f, controller.GetRpm(PumpChannel));
    }

    // A cooler that stops answering is said once, with how far the poll got and the command the last
    // report echoed, and once more when it answers again.
    [Fact]
    public void PollRpm_WithNoHandshakeReply_IsLoggedOnce_AndOnceWhenOneComesAgain() {
        var transport = new FakeDeviceTransport();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, TrinityProductId, new FakeClock(), logger);
        transport.ReadReplies.Enqueue(HandshakeReply(1200, 2700));
        controller.PollRpm();
        transport.ReplyFor = _ => CommandPacket.Build(0x8B, 0x00, 0x32);

        controller.PollRpm();
        controller.PollRpm();
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 2800));
        controller.PollRpm();
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 2800));
        controller.PollRpm();

        Assert.Equal(
            new[] {
                "G0: the cooler did not answer the handshake (32 report(s) read, the last echoing command 0x8B); RPM left as it was (logged once until it does)",
                "G0: the cooler answers the handshake again",
            },
            logger.Messages);
        Assert.Equal(1500f, controller.GetRpm(FanChannel));
    }

    // A cooler that has never answered is not answering "again" when it first does.
    [Fact]
    public void PollRpm_TheFirstAnswerAfterAMiss_IsLoggedAsTheFirst() {
        var transport = new FakeDeviceTransport();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, TrinityProductId, new FakeClock(), logger);

        controller.PollRpm();
        Assert.Null(controller.GetRpm(FanChannel)); // a cooler that never answered has no reading, not 0
        Assert.Null(controller.GetRpm(PumpChannel));
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 2800));
        controller.PollRpm();

        Assert.Equal(
            new[] {
                "G0: the cooler did not answer the handshake (32 report(s) read, the last echoing command 0x00); RPM left as it was (logged once until it does)",
                "G0: the cooler answers the handshake for the first time",
            },
            logger.Messages);
    }

    // A stream of other reports cannot hold the tick: the poll stops reading once a second has passed,
    // even short of the report count.
    [Fact]
    public void PollRpm_StopsReadingOnceTheTimeLimitHasPassed() {
        var transport = new FakeDeviceTransport();
        var clock = new FakeClock();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, TrinityProductId, clock, logger);
        transport.ReplyFor = _ => {
            clock.Advance(TimeSpan.FromMilliseconds(200));
            return CommandPacket.Build(0x8A, 0x00, 0x50);
        };

        controller.PollRpm();

        Assert.Equal(6, transport.InterruptReadCount); // reads at 0-1000 ms; the one due at 1200 ms is not made
        Assert.Single(transport.Writes);
        Assert.Equal(
            "G0: the cooler did not answer the handshake (6 report(s) read, the last echoing command 0x8A); RPM left as it was (logged once until it does)",
            Assert.Single(logger.Messages));
    }

    // A garbage first read leaves the reading as it was: none.
    [Fact]
    public void PollRpm_AnImplausibleFirstReading_LeavesNoReading() {
        var transport = new FakeDeviceTransport();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, TrinityProductId, new FakeClock(), logger);
        transport.ReadReplies.Enqueue(HandshakeReply(50000, 2800));

        controller.PollRpm();

        Assert.Null(controller.GetRpm(FanChannel));
        Assert.Equal(2800f, controller.GetRpm(PumpChannel));
        Assert.Contains("G0:0 implausible rpm 50000 ignored, keeping no reading", logger.Messages);
    }

    [Fact]
    public void PollRpm_IgnoresImplausibleReading_AndKeepsLastGood() {
        var (controller, transport, _) = NewController();
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 2800));
        controller.PollRpm();

        // A post-hibernate garbage read decodes to ~50000 rpm; it must be ignored, not cached.
        transport.ReadReplies.Enqueue(HandshakeReply(50000, 50000));
        controller.PollRpm();

        Assert.Equal(1500f, controller.GetRpm(FanChannel));
        Assert.Equal(2800f, controller.GetRpm(PumpChannel));
    }

    [Fact]
    public void Describe_NamesFanAndPumpDistinctly() {
        var (controller, _, _) = NewController();

        ChannelDescriptor fan = controller.Describe(FanChannel);
        ChannelDescriptor pump = controller.Describe(PumpChannel);

        Assert.Contains("Fan", fan.ControlName);
        Assert.Contains("Pump", pump.ControlName);
        Assert.NotEqual(fan.ControlId, pump.ControlId);
        Assert.NotEqual(fan.ControlId, fan.RpmId); // control and rpm ids never collide
    }

    [Fact]
    public void Dispose_DisposesTransport() {
        var (controller, transport, _) = NewController();
        controller.Dispose();
        Assert.True(transport.IsDisposed);
    }

    [Fact]
    public void PollRpm_LogsImplausibleOnsetOnce_ThenRecovery() {
        var transport = new FakeDeviceTransport();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, TrinityProductId, new FakeClock(), logger);
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 2800));
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 50000));
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 50000));
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 2700));

        for (int i = 0; i < 4; i++) {
            controller.PollRpm();
        }

        Assert.Equal(2700f, controller.GetRpm(PumpChannel));
        Assert.Single(logger.Messages, m => m.Contains("G0:1 implausible rpm 50000"));
        Assert.Single(logger.Messages, m => m.Contains("G0:1 rpm recovered (2700)"));
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies() {
        Assert.Throws<ArgumentNullException>(() => new Galahad2Controller(0, null!, TrinityProductId, new FakeClock(), new FakeLogger()));
        Assert.Throws<ArgumentNullException>(() => new Galahad2Controller(0, new FakeDeviceTransport(), TrinityProductId, null!, new FakeLogger()));
        Assert.Throws<ArgumentNullException>(() => new Galahad2Controller(0, new FakeDeviceTransport(), TrinityProductId, new FakeClock(), null!));
        Assert.Throws<ArgumentNullException>(
            () => new Galahad2Controller(0, new FakeDeviceTransport(), TrinityProductId, new FakeClock(), new FakeLogger()).ReplayOnReconnect(null!));
    }

    [Fact]
    public void ApplyPending_AfterAReopen_WithNoReplayRegistered_ResendsBothDuties() {
        var (controller, transport, _) = NewController();
        controller.SetTarget(FanChannel, 50);
        controller.SetTarget(PumpChannel, 70);
        controller.ApplyPending();
        transport.Clear();

        transport.Generation = 1;
        controller.ApplyPending();

        Assert.Equal(2, transport.Writes.Count);
    }

    [Fact]
    public void PollRpm_BothChannelsChangingStateInOnePoll_AreBothLogged() {
        var transport = new FakeDeviceTransport();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, TrinityProductId, new FakeClock(), logger);
        transport.ReadReplies.Enqueue(HandshakeReply(50000, 50000));
        transport.ReadReplies.Enqueue(HandshakeReply(1500, 2800));

        controller.PollRpm();
        controller.PollRpm();

        Assert.Equal(2, logger.Messages.Count(m => m.Contains("implausible rpm")));
        Assert.Equal(2, logger.Messages.Count(m => m.Contains("rpm recovered")));
    }

    [Theory]
    [InlineData(0x7391)]
    [InlineData(0x7395)]
    public void AVisionLcd_ReportsTheCoolantTemperature_UnderItsOwnId(int productId) {
        var (controller, transport) = NewCooler(productId);
        transport.ReadReplies.Enqueue(VisionHandshakeReply(1440, 2670, 0x01, 37, 8));

        Assert.Null(controller.GetTemperature(0)); // not measured until the cooler answers
        controller.PollRpm();

        Assert.Equal(1, controller.TemperatureCount);
        TemperatureDescriptor coolant = controller.DescribeTemperature(0);
        Assert.Equal("LianLi/0/coolant/temp", coolant.Id);
        Assert.Equal("Lian Li GA II LCD Coolant", coolant.Name);
        Assert.Equal(37.8f, controller.GetTemperature(0));
        Assert.Equal(1440f, controller.GetRpm(FanChannel));
        Assert.Equal(2670f, controller.GetRpm(PumpChannel));
    }

    // A reply whose valid flag is clear has no coolant reading: the last one is not kept, so a curve
    // on it does not follow a stale value.
    [Fact]
    public void AVisionLcd_AReplyWithTheValidFlagClear_LeavesTheCoolantNotMeasured() {
        var (controller, transport) = NewCooler();
        transport.ReadReplies.Enqueue(VisionHandshakeReply(1440, 2670, 0x01, 37, 8));
        controller.PollRpm();

        transport.ReadReplies.Enqueue(VisionHandshakeReply(1440, 2670, 0x00, 37, 8));
        controller.PollRpm();

        Assert.Null(controller.GetTemperature(0));
        Assert.Equal(1440f, controller.GetRpm(FanChannel));
    }

    // A handshake that goes unanswered leaves the coolant as it was, as it does the RPM; forgetting
    // the readings forgets it too.
    [Fact]
    public void AVisionLcd_KeepsTheCoolantThroughAMissedHandshake_AndForgetsItWithTheReadings() {
        var (controller, transport) = NewCooler();
        transport.ReadReplies.Enqueue(VisionHandshakeReply(1440, 2670, 0x01, 37, 8));
        controller.PollRpm();

        controller.PollRpm(); // nothing answers
        Assert.Equal(37.8f, controller.GetTemperature(0));

        controller.ForgetReadings();
        Assert.Null(controller.GetTemperature(0));
    }

    // The Trinity's reply has bytes where the Vision keeps the coolant, but they are not a
    // temperature: it gets no temperature sensor, and its reply is never read as one.
    [Theory]
    [InlineData(0x7371)]
    [InlineData(0x7373)]
    [InlineData(0x7398)]
    public void ACoolerOtherThanTheVision_ReportsNoTemperature(int productId) {
        var (controller, transport) = NewCooler(productId);
        transport.ReadReplies.Enqueue(VisionHandshakeReply(1440, 2670, 0x01, 0x01, 0x01));

        controller.PollRpm();

        Assert.Equal(0, controller.TemperatureCount);
        Assert.Null(controller.GetTemperature(0));
        Assert.Equal(1440f, controller.GetRpm(FanChannel));
        Assert.Equal(2670f, controller.GetRpm(PumpChannel));
    }

    private static (Galahad2Controller controller, FakeDeviceTransport transport, FakeClock clock, FakeLogger logger) NewMotherboardPumpCooler() {
        var transport = new FakeDeviceTransport();
        var clock = new FakeClock();
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, 0x7395, clock, logger);
        return (controller, transport, clock, logger);
    }

    // A Vision LCD's pump follows the motherboard header: the first tick after the cooler opens
    // hands it over, with no control having set anything.
    [Fact]
    public void AVisionLcd_HandsThePumpToTheMotherboardHeader_OnTheFirstTick() {
        var (controller, transport, _, logger) = NewMotherboardPumpCooler();
        Assert.Empty(transport.Writes); // not from the constructor

        controller.ApplyPending();

        Assert.Equal(Galahad2Protocol.EncodeFollowMotherboardPump(), Assert.Single(transport.Writes));
        Assert.Contains("Set G0:pump to follow the motherboard's PWM header", logger.Messages);
    }

    // The hand-over is re-sent on the keepalive interval like a duty, beside the fan's own duty,
    // and logged only the first time.
    [Fact]
    public void AVisionLcd_ResendsTheHandOverOnTheKeepaliveInterval() {
        var (controller, transport, clock, logger) = NewMotherboardPumpCooler();
        controller.SetTarget(FanChannel, 40);
        controller.ApplyPending();
        transport.Clear();

        controller.ApplyPending();
        Assert.Empty(transport.Writes);

        clock.Advance(ChannelWriteDecision.RefreshInterval);
        controller.ApplyPending();

        Assert.Equal(2, transport.Writes.Count);
        Assert.Equal(Galahad2Protocol.EncodeSetFan(motherboardSync: false, dutyPercent: 40), transport.Writes[0]);
        Assert.Equal(Galahad2Protocol.EncodeFollowMotherboardPump(), transport.Writes[1]);
        Assert.Single(logger.Messages, m => m.Contains("follow the motherboard"));
        Assert.Contains("Set G0:fan = 40%", logger.Messages);
    }

    // A cooler that was re-enumerated may have come back reset, so the hand-over goes out again at
    // once, not at the next keepalive.
    [Fact]
    public void AVisionLcd_ResendsTheHandOverAfterAReconnect() {
        var (controller, transport, _, _) = NewMotherboardPumpCooler();
        controller.ApplyPending();
        transport.Clear();

        transport.Generation = 1;
        controller.ApplyPending();

        Assert.Equal(Galahad2Protocol.EncodeFollowMotherboardPump(), Assert.Single(transport.Writes));
    }

    // A Vision LCD is named as Lian Li sells it, whatever its index; every other Galahad keeps its
    // numbered name. Only the names differ: the ids, which FanControl's bindings key on, are the same.
    [Fact]
    public void AVisionLcd_IsNamedAsTheGaIILcd_AndATrinityKeepsItsNumberedName() {
        var vision = new Galahad2Controller(1, new FakeDeviceTransport(), VisionProductId, new FakeClock(), new FakeLogger());
        var trinity = new Galahad2Controller(1, new FakeDeviceTransport(), TrinityProductId, new FakeClock(), new FakeLogger());

        ChannelDescriptor visionFan = vision.Describe(FanChannel);
        Assert.Equal("LianLi/1/ch0/ctl", visionFan.ControlId);
        Assert.Equal("Lian Li GA II LCD Fan", visionFan.ControlName);
        Assert.Equal("Lian Li GA II LCD Fan RPM", visionFan.RpmName);
        Assert.Equal("Lian Li GA II LCD Pump RPM", vision.Describe(PumpChannel).RpmName);
        Assert.Equal("Lian Li GA II LCD Coolant", vision.DescribeTemperature(0).Name);

        ChannelDescriptor trinityPump = trinity.Describe(PumpChannel);
        Assert.Equal("LianLi/1/ch1/ctl", trinityPump.ControlId);
        Assert.Equal("Lian Li Galahad #2 Pump", trinityPump.ControlName);
        Assert.Equal("Lian Li Galahad #2 Pump RPM", trinityPump.RpmName);
    }

    // FanControl drives the pump through the motherboard header's control, so the plugin registers
    // no pump control, but the pump's RPM stays, under the id it has always had.
    [Fact]
    public void AVisionLcd_ShowsNoPumpControl_ButKeepsBothSpeedReadings() {
        var (controller, transport, _, _) = NewMotherboardPumpCooler();
        transport.ReadReplies.Enqueue(VisionHandshakeReply(0, 2910, 0x01, 21, 6));
        controller.PollRpm();

        Assert.True(controller.IsChannelPopulated(FanChannel));
        Assert.False(controller.IsChannelPopulated(PumpChannel));
        Assert.Equal(2, controller.FanSpeedCount);
        FanSpeedDescriptor pump = controller.DescribeFanSpeed(PumpChannel);
        Assert.Equal("LianLi/0/ch1/fan", pump.Id);
        Assert.Equal("Lian Li GA II LCD Pump RPM", pump.Name);
        Assert.Equal(2910f, controller.GetFanSpeed(PumpChannel));
        Assert.Equal(0f, controller.GetFanSpeed(FanChannel));
        Assert.Equal(21.6f, controller.GetTemperature(0));
    }

    // A pump control remembered from a build that had one cannot take the pump back: its duty and
    // its release are ignored, and the hand-over keeps going out.
    [Fact]
    public void AVisionLcd_IgnoresADutyOrReleaseForThePump() {
        var (controller, transport, clock, _) = NewMotherboardPumpCooler();
        controller.SetTarget(PumpChannel, 0);
        controller.ApplyPending();
        controller.ReleaseChannel(PumpChannel);
        clock.Advance(ChannelWriteDecision.RefreshInterval);
        controller.ApplyPending();

        Assert.Equal(2, transport.Writes.Count);
        Assert.All(transport.Writes, w => Assert.Equal(Galahad2Protocol.EncodeFollowMotherboardPump(), w));
    }

    // The Trinity keeps both controls and its driven pump: nothing is sent until a control sets a
    // duty, the duty goes out with the source byte clear, and the floor still applies.
    [Theory]
    [InlineData(0x7371)]
    [InlineData(0x7373)]
    public void ATrinity_KeepsItsPumpControl_AndDrivesThePumpItself(int productId) {
        var transport = new FakeDeviceTransport();
        var controller = new Galahad2Controller(0, transport, productId, new FakeClock(), new FakeLogger());

        Assert.True(controller.IsChannelPopulated(FanChannel));
        Assert.True(controller.IsChannelPopulated(PumpChannel));
        controller.ApplyPending();
        Assert.Empty(transport.Writes);

        controller.SetTarget(PumpChannel, 20);
        controller.ApplyPending();
        Assert.Equal(Galahad2Protocol.EncodeSetPump(motherboardSync: false, dutyPercent: Galahad2Protocol.PumpDutyFloor), Assert.Single(transport.Writes));

        controller.ReleaseChannel(PumpChannel);
        transport.Clear();
        controller.ApplyPending();
        Assert.Empty(transport.Writes);
    }

    // ---------- screen frames ----------

    private static (Galahad2Controller controller, FakeDeviceTransport transport, FakeClock clock, CoolerHandoff handoff, FakeLogger logger) NewScreen() {
        var transport = new FakeDeviceTransport();
        var clock = new FakeClock();
        var handoff = new CoolerHandoff(clock);
        var logger = new FakeLogger();
        var controller = new Galahad2Controller(0, transport, 0x7395, clock, logger, handoff);
        return (controller, transport, clock, handoff, logger);
    }

    private static byte[] Jpeg(int length) {
        var jpeg = new byte[length];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[length - 2] = 0xFF;
        jpeg[length - 1] = 0xD9;
        return jpeg;
    }

    private static List<byte[]> BPackets(FakeDeviceTransport transport) => transport.Writes.Where(w => w[0] == 0x02).ToList();

    [Fact]
    public void ApplyPending_SendsTheWaitingFrame_AfterTheDuties_AsSendJpegPackets_AndNeverAgain() {
        var (controller, transport, _, handoff, logger) = NewScreen();
        byte[] jpeg = Jpeg(2100);
        handoff.OfferFrame(jpeg);

        controller.ApplyPending();

        Assert.Equal(Galahad2Protocol.EncodeFollowMotherboardPump(), transport.Writes[0]);
        Assert.Equal(Galahad2ScreenEncoder.EncodeJpeg(jpeg), transport.Writes.Skip(1).ToList());
        Assert.Single(logger.Messages, m => m == "G0: showing SpaceDeck's screen frames (2100 bytes in 3 packets)");

        transport.Clear();
        controller.ApplyPending();
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public void ThreeFramesGoOutOneAfterAnother_OnePerTick_AndAFourthWaitsForTheBucketToRefill() {
        Assert.Equal(30, Galahad2Controller.MostFramesPerSecond);
        Assert.Equal(3, Galahad2Controller.FrameBurst);
        var (controller, transport, clock, handoff, logger) = NewScreen();
        for (int i = 1; i <= 3; i++) {
            handoff.OfferFrame(Jpeg(10 * i));
            controller.ApplyPending();
            Assert.Equal(10 * i, Assert.Single(BPackets(transport))[10]);
            transport.Clear();
        }

        // The bucket is empty, and refills a frame each thirtieth of a second, 33.3 ms: 33 ms is too
        // soon, 34 ms is not.
        handoff.OfferFrame(Jpeg(40));
        controller.ApplyPending();
        Assert.Empty(BPackets(transport));
        clock.Advance(TimeSpan.FromMilliseconds(33));
        controller.ApplyPending();
        Assert.Empty(BPackets(transport));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        controller.ApplyPending();
        Assert.Equal(40, Assert.Single(BPackets(transport))[10]);
        Assert.Single(logger.Messages, m => m.Contains("screen frames"));
    }

    [Fact]
    public void AFrame_AfterTheClockSteppedBack_IsNotHeldBack() {
        var (controller, transport, clock, handoff, _) = NewScreen();
        for (int i = 0; i < Galahad2Controller.FrameBurst; i++) {
            handoff.OfferFrame(Jpeg(10));
            controller.ApplyPending();
        }

        Assert.Equal(3, BPackets(transport).Count);
        transport.Clear();

        clock.Advance(TimeSpan.FromSeconds(-1));
        handoff.OfferFrame(Jpeg(20));
        controller.ApplyPending();

        Assert.Single(BPackets(transport));
    }

    [Fact]
    public void AFrame_WaitsWhileTheCoolerIsOffTheBus() {
        var (controller, transport, _, handoff, _) = NewScreen();
        transport.IsFaulted = true;
        handoff.OfferFrame(Jpeg(10));

        controller.ApplyPending();
        Assert.Empty(BPackets(transport));

        transport.IsFaulted = false;
        controller.ApplyPending();
        Assert.Single(BPackets(transport));
    }

    [Fact]
    public void AFrameThatCannotBeWritten_CostsOnlyThatFrame_AndIsLoggedOnceUntilOneIsSent() {
        var (controller, transport, clock, handoff, logger) = NewScreen();
        transport.FailWrite = report => report[0] == 0x02;
        controller.SetTarget(FanChannel, 40);

        handoff.OfferFrame(Jpeg(10));
        controller.ApplyPending();
        clock.Advance(TimeSpan.FromSeconds(1));
        handoff.OfferFrame(Jpeg(10));
        controller.ApplyPending();

        Assert.Equal(2, transport.Writes.Count); // the fan and the pump, nothing of either frame
        Assert.Single(logger.Messages, m => m == "G0: a screen frame could not be sent, so it was dropped (logged once until one is sent): simulated device write failure");
        Assert.Null(handoff.TakeFrame());

        transport.FailWrite = null;
        clock.Advance(TimeSpan.FromSeconds(1));
        handoff.OfferFrame(Jpeg(10));
        controller.ApplyPending();
        clock.Advance(TimeSpan.FromSeconds(1));
        handoff.OfferFrame(Jpeg(10));
        controller.ApplyPending();

        Assert.Equal(2, BPackets(transport).Count);
        Assert.Single(logger.Messages, m => m == "G0: screen frames are sent again (10 bytes in 1 packets)");
        Assert.DoesNotContain(logger.Messages, m => m.StartsWith("G0: showing", StringComparison.Ordinal));
    }

    [Fact]
    public void ACoolerWithAHandoff_PollsItsStatusAtMostEveryHalfSecond() {
        var (controller, transport, clock, _, _) = NewScreen();
        transport.ReplyFor = _ => VisionHandshakeReply(1200, 2700, 1, 31, 5);

        controller.PollRpm();
        controller.PollRpm();
        Assert.Single(transport.Writes);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        controller.PollRpm();
        Assert.Equal(2, transport.Writes.Count);

        clock.Advance(TimeSpan.FromSeconds(-5));
        controller.PollRpm();
        Assert.Equal(3, transport.Writes.Count);
        Assert.Equal(31.5f, controller.GetTemperature(0));
    }

    [Fact]
    public void FramesAtThirtyASecond_AreAllSent_WhileTheStatusIsPolledAtMostEveryHalfSecond_AndTheDutiesKeepTheirSchedule() {
        var (controller, transport, clock, handoff, _) = NewScreen();
        transport.ReplyFor = _ => VisionHandshakeReply(1200, 2700, 1, 31, 5);
        byte[] handshake = Galahad2Protocol.EncodeHandshakeRequest();
        byte[] pump = Galahad2Protocol.EncodeFollowMotherboardPump();
        byte[] fanAt40 = Galahad2Protocol.EncodeSetFan(motherboardSync: false, dutyPercent: 40);
        byte[] fanAt60 = Galahad2Protocol.EncodeSetFan(motherboardSync: false, dutyPercent: 60);
        int frames = 0;
        var handshakes = new List<int>();
        var fanWrites = new List<int>();
        var pumpWrites = new List<int>();
        controller.SetTarget(FanChannel, 40);

        // Twenty seconds of a frame every thirtieth of a second, each woken tick as the worker runs it.
        for (int i = 0; i < 600; i++) {
            if (i == 300) {
                controller.SetTarget(FanChannel, 60);
            }

            handoff.OfferFrame(Jpeg(10));
            controller.ApplyPending();
            controller.PollRpm();

            // A duty due on a tick is that tick's first write: a frame never goes ahead of it.
            if (i == 300) {
                Assert.Equal(fanAt60, transport.Writes[0]);
            } else if (i == 451) {
                Assert.Equal(pump, transport.Writes[0]);
            }

            frames += BPackets(transport).Count;
            handshakes.AddRange(transport.Writes.Where(w => w.SequenceEqual(handshake)).Select(_ => i));
            fanWrites.AddRange(transport.Writes.Where(w => w.SequenceEqual(fanAt40) || w.SequenceEqual(fanAt60)).Select(_ => i));
            pumpWrites.AddRange(transport.Writes.Where(w => w.SequenceEqual(pump)).Select(_ => i));
            transport.Clear();
            clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 30));
        }

        Assert.Equal(600, frames);
        Assert.Equal(0, handoff.TakeDroppedFrames());

        // Half a second is fifteen thirtieths, and the sixteenth tick is the first past it.
        Assert.Equal(Enumerable.Range(0, 38).Select(n => n * 16), handshakes);

        // The fan at the start and on the change, at once; the pump's command at the start and on
        // the fifteen-second keepalive, on the first tick past it.
        Assert.Equal(new[] { 0, 300 }, fanWrites);
        Assert.Equal(new[] { 0, 451 }, pumpWrites);
    }

    // Frame times from one second on at framesPerSecond, each early or late by up to 16 ms - a
    // Windows timer tick and more, as the collector's writes reach the plugin: alternately 16 ms early
    // and late, or spread over -16..+16 ms; never before the frame ahead of it.
    private static List<TimeSpan> JitteredFrames(int framesPerSecond, bool alternating) {
        var times = new List<TimeSpan>();
        for (int i = 0; i < 600; i++) {
            int jitterMs = alternating ? (i % 2 == 0 ? -16 : 16) : ((i * 13) % 33) - 16;
            var at = TimeSpan.FromTicks(TimeSpan.TicksPerSecond + (i * TimeSpan.TicksPerSecond / framesPerSecond) + (jitterMs * TimeSpan.TicksPerMillisecond));
            times.Add(times.Count == 0 || at > times[^1] ? at : times[^1]);
        }

        return times;
    }

    // Each frame offered at its time and its woken tick run as the worker runs it: the frames sent,
    // the ticks the status was asked for on, and the frames dropped unsent.
    private static (int Sent, List<TimeSpan> Handshakes, int Dropped) RunFrames(List<TimeSpan> times) {
        var (controller, transport, clock, handoff, _) = NewScreen();
        transport.ReplyFor = _ => VisionHandshakeReply(1200, 2700, 1, 31, 5);
        byte[] handshake = Galahad2Protocol.EncodeHandshakeRequest();
        DateTime start = clock.UtcNow;
        TimeSpan now = TimeSpan.Zero;
        int sent = 0;
        var handshakes = new List<TimeSpan>();
        foreach (TimeSpan at in times) {
            clock.Advance(at - now);
            now = at;
            handoff.OfferFrame(Jpeg(10));
            controller.ApplyPending();
            controller.PollRpm();
            sent += BPackets(transport).Count;
            handshakes.AddRange(transport.Writes.Where(w => w.SequenceEqual(handshake)).Select(_ => clock.UtcNow - start));
            transport.Clear();
        }

        return (sent, handshakes, handoff.TakeDroppedFrames());
    }

    [Theory]
    [InlineData(24, false)]
    [InlineData(30, false)]
    [InlineData(24, true)]
    [InlineData(30, true)]
    public void AJitteredStreamOfUpToThirtyFramesASecond_IsSentWhole_WhileTheStatusIsPolledAtMostEveryHalfSecond(int framesPerSecond, bool alternating) {
        var (sent, handshakes, dropped) = RunFrames(JitteredFrames(framesPerSecond, alternating));

        Assert.Equal(600, sent);
        Assert.Equal(0, dropped);
        Assert.All(handshakes.Zip(handshakes.Skip(1), (earlier, later) => later - earlier), gap => Assert.True(gap >= TimeSpan.FromMilliseconds(500)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SixtyFramesASecond_AreHeldToThirty_TheNewestWaiting(bool alternating) {
        var (sent, _, dropped) = RunFrames(JitteredFrames(60, alternating));

        // Ten seconds at thirty a second, and the bucket's three at the start; every other frame was
        // replaced by a newer one before a token came.
        Assert.InRange(sent, 300, 303);
        Assert.InRange(sent + dropped, 599, 600);
    }

    [Fact]
    public void OnceAMinuteWhileFramesAreSent_OneLineSaysWhatTheyCost() {
        var (controller, transport, clock, handoff, logger) = NewScreen();
        transport.OnWrite = report => {
            if (report[0] == 0x02) {
                clock.Advance(TimeSpan.FromMilliseconds(5));
            }
        };

        // A frame of two packets each second, and one replaced before it could be sent.
        for (int second = 0; second < 60; second++) {
            handoff.OfferFrame(Jpeg(1500));
            if (second == 10) {
                handoff.OfferFrame(Jpeg(1500));
            }

            controller.ApplyPending();
            Assert.DoesNotContain(logger.Messages, m => m.Contains("in the last minute"));
            clock.Advance(TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(10));
        }

        controller.ApplyPending();

        Assert.Single(
            logger.Messages,
            m => m == "G0: screen frames in the last minute: 60 sent, 1 dropped before sending (replaced by a newer one or expired), 10.0 ms average and 10.0 ms longest to write one, 1500 bytes average");

        // Nothing more is sent, so nothing more is logged.
        clock.Advance(TimeSpan.FromMinutes(5));
        controller.ApplyPending();
        Assert.Single(logger.Messages, m => m.Contains("in the last minute"));
    }
#if ENABLE_LIGHTING

    // ---------- the screen ring, through OpenRGB ----------

    private static Galahad2RingLook Effect(int speed) => new Galahad2RingLook(Galahad2RingMode.Breathing, speed, 4, 0, new[] { new RgbColor(9, 8, 7) });

    private static List<byte[]> RingWrites(FakeDeviceTransport transport)
        => transport.Writes.Where(w => (w[0] == 0x02 && w[1] == 0x14) || (w[0] == 0x01 && w[1] == 0x83)).ToList();

    [Fact]
    public void ADirectLook_IsSentAfterTheFrame_AtMostTenTimesASecond_TheNewestWinning() {
        var (controller, transport, clock, handoff, _) = NewScreen();
        handoff.OfferFrame(Jpeg(10));
        handoff.OfferLook(Galahad2RingLook.DirectOf(new RgbColor(1, 0, 0)));
        controller.ApplyPending();

        Assert.Equal(0x0E, transport.Writes[1][1]);
        Assert.Equal(Galahad2RingEncoder.Encode(Galahad2RingLook.DirectOf(new RgbColor(1, 0, 0))), transport.Writes.Skip(2).ToList());

        transport.Clear();
        handoff.OfferLook(Galahad2RingLook.DirectOf(new RgbColor(2, 0, 0)));
        Galahad2RingLook newest = Galahad2RingLook.DirectOf(new RgbColor(3, 0, 0));
        handoff.OfferLook(newest);
        clock.Advance(TimeSpan.FromMilliseconds(99));
        controller.ApplyPending();
        Assert.Empty(RingWrites(transport));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        controller.ApplyPending();
        Assert.Equal(Galahad2RingEncoder.Encode(newest), RingWrites(transport));
        Assert.Null(handoff.PeekLook());
    }

    [Fact]
    public void AnEffect_IsSentAsOne0x83_AtLeastASecondAfterTheLast_ButADirectLookNeedNotWaitForIt() {
        var (controller, transport, clock, handoff, _) = NewScreen();
        handoff.OfferLook(Effect(1));
        controller.ApplyPending();
        Assert.Single(RingWrites(transport));
        transport.Clear();

        handoff.OfferLook(Effect(2));
        clock.Advance(TimeSpan.FromMilliseconds(999));
        controller.ApplyPending();
        Assert.Empty(RingWrites(transport));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        controller.ApplyPending();
        Assert.Equal(Galahad2RingEncoder.Encode(Effect(2)), RingWrites(transport));

        transport.Clear();
        handoff.OfferLook(Galahad2RingLook.DirectOf(new RgbColor(5, 5, 5)));
        controller.ApplyPending();
        Assert.Single(RingWrites(transport));
    }

    [Fact]
    public void ALook_WaitsWhileTheCoolerIsOffTheBus() {
        var (controller, transport, _, handoff, _) = NewScreen();
        transport.IsFaulted = true;
        handoff.OfferLook(Effect(1));

        controller.ApplyPending();
        Assert.Empty(RingWrites(transport));

        transport.IsFaulted = false;
        controller.ApplyPending();
        Assert.Single(RingWrites(transport));
    }

    [Fact]
    public void ALookThatCannotBeWritten_CostsOnlyThatLook_AndIsLoggedOnceUntilOneIsSent() {
        var (controller, transport, clock, handoff, logger) = NewScreen();
        transport.FailWrite = report => report[1] == 0x83;
        handoff.OfferLook(Effect(1));
        controller.ApplyPending();
        clock.Advance(TimeSpan.FromSeconds(1));
        handoff.OfferLook(Effect(2));
        controller.ApplyPending();

        Assert.Single(logger.Messages, m => m == "G0: the screen ring's Breathing look could not be sent (logged once until one is sent): simulated device write failure");
        Assert.Null(handoff.PeekLook());

        transport.FailWrite = null;
        clock.Advance(TimeSpan.FromSeconds(1));
        handoff.OfferLook(Effect(3));
        controller.ApplyPending();
        clock.Advance(TimeSpan.FromSeconds(1));
        handoff.OfferLook(Effect(4));
        controller.ApplyPending();

        Assert.Equal(2, RingWrites(transport).Count);
        Assert.Single(logger.Messages, m => m == "G0: the screen ring's looks are sent again");
    }

    // The L-Connect mode in OpenRGB: the look waiting is forgotten, and the replay the Lighting build
    // registered (which shows L-Connect's look once the handoff holds none) is owed, and run by the
    // next tick's replay step, once per hand-back.
    [Fact]
    public void ARingHandedBackToLConnect_IsReplayedByTheNextTick_OncePerHandBack() {
        var (controller, transport, _, handoff, logger) = NewScreen();
        int replays = 0;
        controller.ReplayOnReconnect(() => {
            replays++;
            return true;
        });
        controller.ApplyPending();
        handoff.OfferLook(Effect(1));
        handoff.HandBackToLConnect();

        controller.ApplyPending();
        Assert.Equal(0, replays);
        Assert.Empty(RingWrites(transport));
        Assert.Single(logger.Messages, m => m == "G0: the screen ring is handed back to L-Connect's look");

        controller.ApplyPending();
        Assert.Equal(1, replays);

        controller.ApplyPending();
        Assert.Equal(1, replays);
    }
#endif
}
