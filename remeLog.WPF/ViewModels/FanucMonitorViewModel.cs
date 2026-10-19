using libeLog;
using libeLog.Base;
using libeLog.FanucApi;
using remeLog.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using libeLog.Views;

namespace remeLog.ViewModels
{
    public class FanucMonitorViewModel : ViewModel
    {
        private readonly FanucService _fanucService;
        private ushort _handle = 0;
        private short _ret = 0;
        private bool _isConnected = false;
        private string _ipAddress;
        private MachineStatus _status;
        private int _axisCount = 0;
        public string IPAddress
        {
            get => _ipAddress;
            set
            {
                _ipAddress = value;
                OnPropertyChanged(nameof(IPAddress));
            }
        }

        public MachineStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }


        public ICommand ConnectCommand { get; }

        public FanucMonitorViewModel()
        {
            _fanucService = new FanucService();
            _ipAddress = "127.0.0.1";
            _status = new MachineStatus();
            ConnectCommand = new LambdaCommand(OnConnectCommandExecuted, CanConnectCommandExecute);
        }

        private void OnConnectCommandExecuted(object p)
        {
            Task.Run(() => ConnectToMachine());
        }

        private static bool CanConnectCommandExecute(object p) => true;

        private async Task ConnectToMachine()
        {
            if (_isConnected)
            {
                _isConnected = false;
                Focas1.cnc_freelibhndl(_handle);
                await Task.Delay(1000);
            }

            await Task.Run(() =>
            {
                _ret = Focas1.cnc_allclibhndl3(IPAddress, 8193, 6, out _handle);
                if (_ret == Focas1.EW_OK)
                {
                    IsConnected = true;
                    MonitorMachine();
                }
                else
                {
                    IsConnected = false;
                    MessageBoxWindow.Show($"Unable to connect. Return Code: {_ret}");
                }
            });
        }

        private void MonitorMachine()
        {
            try
            {
                Status.MaxRpm = _fanucService.GetMaxRpm(_handle);
                var axisNames = _fanucService.GetAllAxisNames(_handle);
                _axisCount = axisNames.Count(ax => !string.IsNullOrEmpty(ax));
                if (axisNames.Count >= 5)
                {
                    Status.FirstAxisName = axisNames[0].ToString();
                    Status.SecondAxisName = axisNames[1].ToString();
                    Status.ThirdAxisName = axisNames[2].ToString();
                    Status.FourthAxisName = axisNames[3].ToString();
                    Status.FivethAxisName = axisNames[4].ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBoxWindow.Show(ex.Message);
                return;
            }
            while (IsConnected)
            {
                try
                {
                    UpdateAxisValues();
                    (Status.Speed, Status.Feed) = _fanucService.GetActRpmAndFeedrate(_handle);
                    Status.IsOperating = _fanucService.GetOpSignal(_handle);
                    Status.Mode = _fanucService.GetMode(_handle);
                    Status.Status = _fanucService.GetStatus(_handle);
                    (Status.UsedPrograms, Status.UnusedPrograms, Status.UsedMem, Status.UnusedMem) = _fanucService.GetProgramDataInfo(_handle);
                    Status.Alarms = _fanucService.GetAlarms(_handle);
                    Status.Spindle1Load = _fanucService.GetSpindleLoad(_handle, 1);
                    Status.ProgramName = _fanucService.GetProgramName(_handle);

                    OnPropertyChanged(nameof(Status));

                    Thread.Sleep(250);
                }
                catch (Exception ex)
                {
                    MessageBoxWindow.Show(ex.Message, "Monitoring Error");
                }
            }
        }

        private void UpdateAxisValues()
        {
            short num = Focas1.MAX_AXIS;
            var odb = new Focas1.ODBPOS();

            short ret = Focas1.cnc_rdposition(_handle, Focas1.ALL_AXES, ref num, odb);
            if (ret == Focas1.EW_OK)
            {
                for (int i = 1; i <= _axisCount; i++)
                {
                    switch (i)
                    {
                        case 1:
                            Status.FirstRelativeAxisValue = odb.p1.rel.data / Math.Pow(10, odb.p1.rel.dec);
                            Status.FirstAbsoluteAxisValue = odb.p1.abs.data / Math.Pow(10, odb.p1.abs.dec);
                            Status.FirstMachineAxisValue = odb.p1.mach.data / Math.Pow(10, odb.p1.mach.dec);
                            Status.FirstDistanceToGoAxisValue = odb.p1.dist.data / Math.Pow(10, odb.p1.dist.dec);
                            break;
                        case 2:
                            Status.SecondRelativeAxisValue = odb.p2.rel.data / Math.Pow(10, odb.p2.rel.dec);
                            Status.SecondAbsoluteAxisValue = odb.p2.abs.data / Math.Pow(10, odb.p2.abs.dec);
                            Status.SecondMachineAxisValue = odb.p2.mach.data / Math.Pow(10, odb.p2.mach.dec);
                            Status.SecondDistanceToGoAxisValue = odb.p2.dist.data / Math.Pow(10, odb.p2.dist.dec);
                            break;
                        case 3:
                            Status.ThirdRelativeAxisValue = odb.p3.rel.data / Math.Pow(10, odb.p3.rel.dec);
                            Status.ThirdAbsoluteAxisValue = odb.p3.abs.data / Math.Pow(10, odb.p3.abs.dec);
                            Status.ThirdMachineAxisValue = odb.p3.mach.data / Math.Pow(10, odb.p3.mach.dec);
                            Status.ThirdDistanceToGoAxisValue = odb.p3.dist.data / Math.Pow(10, odb.p3.dist.dec);
                            break;
                        case 4:
                            Status.FourthRelativeAxisValue = odb.p4.rel.data / Math.Pow(10, odb.p4.rel.dec);
                            Status.FourthAbsoluteAxisValue = odb.p4.abs.data / Math.Pow(10, odb.p4.abs.dec);
                            Status.FourthMachineAxisValue = odb.p4.mach.data / Math.Pow(10, odb.p4.mach.dec);
                            Status.FourthDistanceToGoAxisValue = odb.p4.dist.data / Math.Pow(10, odb.p4.dist.dec);
                            break;
                        case 5:
                            Status.FivethRelativeAxisValue = odb.p5.rel.data / Math.Pow(10, odb.p5.rel.dec);
                            Status.FivethAbsoluteAxisValue = odb.p5.abs.data / Math.Pow(10, odb.p5.abs.dec);
                            Status.FivethMachineAxisValue = odb.p5.mach.data / Math.Pow(10, odb.p5.mach.dec);
                            Status.FivethDistanceToGoAxisValue = odb.p5.dist.data / Math.Pow(10, odb.p5.dist.dec);
                            break;
                        default:
                            break;
                    }
                }
            }
        }
    }
}
