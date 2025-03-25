using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading;
using System.Windows;

namespace HWiNFOReaderServices
{
    /// <summary>
    /// 传感器读数数据结构 sensor reading data structure
    /// </summary>
    public class HWiNFOData
    {
        public float? CpuTemperature { get; set; }
        public float? CpuPower { get; set; }
        public float? GpuFrequency { get; set; }
        public float? CpuVoltage { get; set; }
        public float? CpuUsage { get; set; }
        public float? CpuCurrent { get; set; }
    }

    /// <summary>
    /// HWiNFO共享内存读取器 HWiNFO shared memory reader
    /// </summary>
    public class HWiNFOReader : IDisposable
    {
        private const string MapName = "Global\\HWiNFO_SENS_SM2";
        public const int HWiNFO_SENSORS_STRING_LEN2 = 128;
        public const int HWiNFO_UNIT_STRING_LEN = 16;
        private MemoryMappedFile _mmf;
        private Timer _timer;
        private bool _isDisposed;

        public enum SENSOR_READING_TYPE
        {
            SENSOR_TYPE_NONE = 0,
            SENSOR_TYPE_TEMP,
            SENSOR_TYPE_VOLT,
            SENSOR_TYPE_FAN,
            SENSOR_TYPE_CURRENT,
            SENSOR_TYPE_POWER,
            SENSOR_TYPE_FREQUENCY,
            SENSOR_TYPE_USAGE,
            SENSOR_TYPE_OTHER,
            SENSOR_TYPE_CLOCK,
        };

        // HWiNFO共享内存头部签名（固定值）HWiNFO shared memory header signature
        private const uint HWINFO_SIGNATURE = 0x004F494D; // "HWiNFO"的十六进制表示

        /// <summary>
        /// 共享内存头部结构（必须与HWiNFO严格对齐）shared memory header structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
        public struct HWiNFO_SENSORS_HEADER
        {
            public UInt32 dwSignature;
            public UInt32 dwVersion;
            public UInt32 dwRevision;
            public long poll_time;
            public UInt32 dwOffsetOfSensorSection;
            public UInt32 dwSizeOfSensorElement;
            public UInt32 dwNumSensorElements;
            // descriptors for the Readings section
            public UInt32 dwOffsetOfReadingSection; // Offset of the Reading section from beginning of HWiNFO_SENSORS_SHARED_MEM2
            public UInt32 dwSizeOfReadingElement;   // Size of each Reading element = sizeof( HWiNFO_SENSORS_READING_ELEMENT )
            public UInt32 dwNumReadingElements;     // Number of Reading elements
        };

        /// <summary>
        /// 传感器条目结构 sensor entry structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct SensorEntry
        {
            public UInt32 dwSensorID;
            public UInt32 dwSensorInst;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWiNFO_SENSORS_STRING_LEN2)]
            public string szSensorNameOrig;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWiNFO_SENSORS_STRING_LEN2)]
            public string szSensorNameUser;
        };

        /// <summary>
        /// 读数条目结构 reading entry structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct ReadingEntry
        {
            public SENSOR_READING_TYPE tReading;
            public UInt32 dwSensorIndex;
            public UInt32 dwReadingID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWiNFO_SENSORS_STRING_LEN2)]
            public string szLabelOrig;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWiNFO_SENSORS_STRING_LEN2)]
            public string szLabelUser;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = HWiNFO_UNIT_STRING_LEN)]
            public string szUnit;
            public double Value;
            public double ValueMin;
            public double ValueMax;
            public double ValueAvg;
        };

        public event Action<HWiNFOData> DataUpdated;

        /// <summary>
        /// 启动共享内存监视 start shared memory monitor
        /// </summary>
        public void Start()
        {
            try
            {
                // 需要FileIOPermission和MemoryMappedFileAccess.Read权限
                _mmf = MemoryMappedFile.OpenExisting(
                    MapName,
                    MemoryMappedFileRights.Read
                );

                // 每秒轮询一次（根据需求调整间隔）
                _timer = new Timer(ReadData, null, 0, 1000);
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show("HWiNFO未运行或未启用共享内存支持", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("需要以管理员权限运行本程序", "权限错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}", "严重错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private bool IsCpuSensor(SensorEntry sensor)
        {
            string[] keywords = { "CPU", "Core", "Package", "Clock", "Frequency", "Usage", 
                                  "Utilization", "Processor", "Load", "Temp", "Power", "Total", "Used" };
            return keywords.Any(k =>
                sensor.szSensorNameUser.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 ||
                sensor.szSensorNameOrig.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0
            );
        }

        /// <summary>
        /// 数据读取核心方法 core method of data reading 
        /// </summary>
        private void ReadData(object state)
        {
            if (_isDisposed) return;

            try
            {
                using (var accessor = _mmf.CreateViewAccessor(0, Marshal.SizeOf(typeof(HWiNFO_SENSORS_HEADER)), MemoryMappedFileAccess.Read))
                {
                    // 1. 读取并验证头部 read and verify header
                    HWiNFO_SENSORS_HEADER header;
                    accessor.Read(0, out header);

                    // 2. 遍历传感器条目 go through sensor entries
                    var sensors = new List<SensorEntry>();
                    for (int i = 0; i < header.dwNumSensorElements; i++)
                    {
                        long SensorOffset = header.dwOffsetOfSensorSection + i * header.dwSizeOfSensorElement;
                        var sensor_element_accessor = _mmf.CreateViewStream(SensorOffset, header.dwSizeOfSensorElement, MemoryMappedFileAccess.Read);
                        byte[] byteBuffer = new byte[header.dwSizeOfSensorElement];
                        
                        sensor_element_accessor.Read(byteBuffer, 0, (int)header.dwSizeOfSensorElement);
                        GCHandle handle = GCHandle.Alloc(byteBuffer, GCHandleType.Pinned);
                        SensorEntry Sensor = (SensorEntry)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(SensorEntry));
                        handle.Free();
                        sensors.Add(Sensor);
                    }

                    // 3. 读取所有读数 read all readings
                    var readings = new List<ReadingEntry>();
                    for (int j = 0; j < header.dwNumReadingElements; j++)
                    {
                        long ReadingOffset = header.dwOffsetOfReadingSection + j * header.dwSizeOfReadingElement;
                        var reading_element_accessor = _mmf.CreateViewStream(ReadingOffset, header.dwSizeOfReadingElement, MemoryMappedFileAccess.Read);
                        byte[] byteBuffer = new byte[header.dwSizeOfReadingElement];

                        reading_element_accessor.Read(byteBuffer, 0, (int)header.dwSizeOfReadingElement);
                        GCHandle handle = GCHandle.Alloc(byteBuffer, GCHandleType.Pinned);
                        ReadingEntry Reading = (ReadingEntry)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(ReadingEntry));
                        handle.Free();

                        readings.Add(Reading);
                    }

                    // 4. 提取CPU数据 obtain CPU data
                    var data = new HWiNFOData();
                    foreach (var reading in readings)
                    {
                        var sensor = sensors[(int)reading.dwSensorIndex];
                        if (!IsCpuSensor(sensor)) continue;

                        // 提取CPU温度 obtain CPU temperature
                        if (reading.tReading == HWiNFOReader.SENSOR_READING_TYPE.SENSOR_TYPE_TEMP)
                        {
                            data.CpuTemperature = (float?)reading.Value;
                        }
                        // 提取CPU功率 obtain CPU power
                        else if (reading.tReading == HWiNFOReader.SENSOR_READING_TYPE.SENSOR_TYPE_POWER)
                        {
                            data.CpuPower = (float?)reading.Value;
                        }
                        // 提取GPU频率  obtain GPU frequency
                        else if (reading.tReading == HWiNFOReader.SENSOR_READING_TYPE.SENSOR_TYPE_FREQUENCY)
                        {
                            data.GpuFrequency = (float?)reading.Value;
                        }
                        // 提取CPU电压 obtain CPU velocity
                        else if (reading.tReading == HWiNFOReader.SENSOR_READING_TYPE.SENSOR_TYPE_VOLT)
                        {
                            data.CpuVoltage = (float?)reading.Value;
                        }
                        // 提取CPU核心使用率 obtain CPU core usage
                        else if (reading.tReading == HWiNFOReader.SENSOR_READING_TYPE.SENSOR_TYPE_USAGE)
                        {
                            data.CpuUsage = (float?)reading.Value;
                        }
                        // 提取CPU风扇 obtain CPU fan
                        else if (reading.tReading == HWiNFOReader.SENSOR_READING_TYPE.SENSOR_TYPE_CURRENT)
                        {
                            data.CpuCurrent = (float?)reading.Value;
                        }
                    }

                    // 5. 触发事件 toggle event
                    DataUpdated?.Invoke(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 读取错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放资源 release resource
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;
            _timer?.Dispose();
            _mmf?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
