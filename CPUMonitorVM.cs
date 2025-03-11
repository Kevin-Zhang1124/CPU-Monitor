using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace CPU_Monitor
{
    internal class CPUMonitorVM : INotifyPropertyChanged
    {
        private float? _cpuTemp;
        private float? _cpuPower;
        private float? _gpuFrequency;

        public float? CpuTemperature
        {
            get => _cpuTemp;
            set => SetField(ref _cpuTemp, value);
        }

        public float? CpuPower
        {
            get => _cpuPower;
            set => SetField(ref _cpuPower, value);
        }

        public float? GpuFrequency
        {
            get => _gpuFrequency;
            set => SetField(ref _gpuFrequency, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 通用属性设置方法 (common property setting method)
        /// </summary>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
