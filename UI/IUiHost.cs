using SysWatch.Commands;
using SysWatch.Config;
using SysWatch.Monitoring;

namespace SysWatch.UI;

// What the windows need from the app; lets them be rendered against a fake host in tests.
public interface IUiHost : IAppContext
{
    MetricHistory History { get; }
    bool PawnIoInstalled { get; }
    List<SensorGroup> Sensors();
    void SaveAndApply(AppConfig next);
    void Mute(int minutes);
    void OpenHistory();
    void OpenSettings();
    void OpenPage(int page);
    Task InstallUpdateAsync(Updates.UpdateInfo update);
}
