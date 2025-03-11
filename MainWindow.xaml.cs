using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HWiNFOReaderServices;

namespace CPU_Monitor
{
    public partial class MainWindow : Window
    {
        private readonly HWiNFOReader _reader;
        private readonly CPUMonitorVM _vm = new CPUMonitorVM();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;

            _reader = new HWiNFOReader();
            _reader.DataUpdated += data =>
            {
                Dispatcher.Invoke(() =>
                {
                    _vm.CpuTemperature = data.CpuTemperature;
                    _vm.CpuPower = data.CpuPower;
                    _vm.GpuFrequency = data.GpuFrequency;
                });
            };
            _reader.Start();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _reader.Dispose();
            base.OnClosing(e);
        }
    }
}
