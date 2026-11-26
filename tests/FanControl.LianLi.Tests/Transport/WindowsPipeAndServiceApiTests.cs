using System;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using FanControl.LianLi.Transport;
using Xunit;

namespace FanControl.LianLi.Tests.Transport;

/// <summary>
/// The SpaceDeck publisher's two native adapters against the real Windows APIs: a write-only client
/// of a real pipe reads its server's process id and writes to it, a pipe nobody serves gives no
/// connection, and the service control manager tells a missing service from a running one. What
/// these catch is a marshalling slip, or an access right the write-only handle lacks, that the fakes
/// cannot.
/// </summary>
public class WindowsPipeAndServiceApiTests {
    [WindowsFact]
    public void NamedPipeApi_ConnectsWriteOnly_ReadsTheServersProcessId_AndWrites() {
        string name = "SpaceDeckTest." + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task waiting = server.WaitForConnectionAsync();

        using INamedPipeConnection? connection = WindowsNamedPipeApi.Instance.TryConnect(name, 2000);

        Assert.NotNull(connection);
        Assert.True(waiting.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(Environment.ProcessId, connection.ReadServerProcessId());

        connection.Write(Encoding.UTF8.GetBytes("{\"t\":\"x\"}\n"));
        var buffer = new byte[10];
        int read = 0;
        while (read < buffer.Length) {
            read += server.Read(buffer, read, buffer.Length - read);
        }

        Assert.Equal("{\"t\":\"x\"}\n", Encoding.UTF8.GetString(buffer));
    }

    [WindowsFact]
    public void NamedPipeApi_APipeNobodyServes_GivesNoConnection()
        => Assert.Null(WindowsNamedPipeApi.Instance.TryConnect("SpaceDeckTest.Nobody." + Guid.NewGuid().ToString("N"), 250));

    [WindowsFact]
    public void ServiceControlApi_AServiceThatIsNotInstalled_IsNull()
        => Assert.Null(WindowsServiceControlApi.Instance.QueryProcessId("SpaceDeckTestNoSuchService"));

    [WindowsFact]
    public void ServiceControlApi_ARunningService_HasItsProcessId() {
        int? processId = WindowsServiceControlApi.Instance.QueryProcessId("EventLog");

        Assert.NotNull(processId);
        Assert.True(processId > 0);
    }
}
