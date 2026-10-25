using System.Windows;
using WeldingSystem.Client.ViewModels;

namespace WeldingSystem.Client.Views
{
    public partial class MainWindow : Window
    {
        public DashboardViewModel Dashboard { get; }
        public AlarmsViewModel Alarms { get; }

        public MainWindow(DashboardViewModel dashboardViewModel, AlarmsViewModel alarmsViewModel)
        {
            Dashboard = dashboardViewModel;
            Alarms = alarmsViewModel;

            InitializeComponent();
            DataContext = this; // exposes Dashboard/Alarms as binding paths used in the XAML above
        }
    }
}
