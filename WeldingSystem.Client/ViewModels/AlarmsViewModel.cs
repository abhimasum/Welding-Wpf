using System;
using System.Collections.ObjectModel;
using Prism.Events;
using WeldingSystem.Client.Events;

namespace WeldingSystem.Client.ViewModels
{
    /// <summary>
    /// SUBSCRIBER #2 of the SAME MachineAlarmEvent that DashboardViewModel also subscribes to.
    /// This is the concrete proof-point of "multiple subscriber ViewModels react to one event":
    /// neither ViewModel references the other, yet both update independently from one callback.
    /// </summary>
    public class AlarmsViewModel : ViewModelBase
    {
        public ObservableCollection<string> AlarmLog { get; } = new ObservableCollection<string>();

        public AlarmsViewModel(IEventAggregator eventAggregator)
        {
            eventAggregator.GetEvent<MachineAlarmEvent>()
                .Subscribe(OnAlarm, ThreadOption.UIThread);
        }

        private void OnAlarm(MachineAlarmPayload alarm)
        {
            // ObservableCollection raises CollectionChanged, which WPF's ListBox/ItemsControl
            // binding listens to directly \u2014 no manual UI refresh code needed.
            AlarmLog.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {alarm.MachineId}: {alarm.Message} (code {alarm.ErrorCode})");
        }
    }
}
