namespace FanControl.LianLi.Transport;

/// <summary>
/// The one question the plugin asks the Windows service control manager: which process a service
/// runs in. An interface so the SpaceDeck publisher's off switch and server check are tested
/// through a fake.
/// </summary>
internal interface IServiceControlApi {
    /// <summary>
    /// The process id of the service named <paramref name="serviceName"/>: null when no such service
    /// is installed, 0 when it is installed but not running. Asks for the query-status right only.
    /// Any other failure (access denied, say) throws.
    /// </summary>
    int? QueryProcessId(string serviceName);
}
