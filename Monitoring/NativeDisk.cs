using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SysWatch.Monitoring;

// Drive temperatures via IOCTL_STORAGE_QUERY_PROPERTY: the NVMe SMART/health log for NVMe drives,
// StorageDeviceTemperatureProperty for others. LHM's storage group costs ~40 MB when elevated; this costs almost nothing.
public sealed class NativeDisk : IDisposable
{
    public sealed record Reading(string Name, double? Temperature);

    private sealed class Drive
    {
        public required SafeFileHandle Handle;
        public required string Name;
        public bool Nvme;
    }

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceTemperatureProperty = 49;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int ProtocolSpecificDataSize = 40;
    private const int NvmeLogSize = 512;
    private const int BufferSize = 8 + ProtocolSpecificDataSize + NvmeLogSize;

    private readonly List<Drive> _drives = new();
    private readonly IntPtr _buffer = Marshal.AllocHGlobal(BufferSize);
    private static readonly byte[] Zeros = new byte[BufferSize];

    public NativeDisk()
    {
        for (var i = 0; i < 32; i++)
        {
            var handle = CreateFile($@"\\.\PhysicalDrive{i}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                continue;
            }
            var drive = new Drive { Handle = handle, Name = Model(handle) ?? $"Disk {i}", Nvme = true };
            if (QueryTemperature(drive) == null)
            {
                drive.Nvme = false;
                if (QueryTemperature(drive) == null)
                {
                    handle.Dispose();
                    continue;
                }
            }
            _drives.Add(drive);
        }
        Log.Info(L.T("Yerel disk sıcaklığı: ", "Native disk temperature: ") + (_drives.Count == 0 ? L.T("desteklenen disk yok", "no supported disk") : string.Join(", ", _drives.Select(d => d.Name))));
    }

    public List<Reading> Read() => _drives.Select(d => new Reading(d.Name, QueryTemperature(d))).ToList();

    private double? QueryTemperature(Drive d)
    {
        Marshal.Copy(Zeros, 0, _buffer, BufferSize);
        if (d.Nvme)
        {
            // STORAGE_PROPERTY_QUERY header, then STORAGE_PROTOCOL_SPECIFIC_DATA asking for log page 02h (SMART / health).
            Marshal.WriteInt32(_buffer, 0, StorageDeviceProtocolSpecificProperty);
            Marshal.WriteInt32(_buffer, 8, 3);                          // ProtocolTypeNvme
            Marshal.WriteInt32(_buffer, 12, 2);                         // NVMeDataTypeLogPage
            Marshal.WriteInt32(_buffer, 16, 2);                         // health information log
            Marshal.WriteInt32(_buffer, 24, ProtocolSpecificDataSize);  // ProtocolDataOffset
            Marshal.WriteInt32(_buffer, 28, NvmeLogSize);               // ProtocolDataLength
            if (!Ioctl(d.Handle, BufferSize)) return null;
            // Reply: STORAGE_PROTOCOL_DATA_DESCRIPTOR (8 + 40 bytes), then the log; composite temperature is bytes 1-2, in Kelvin.
            var kelvin = (ushort)Marshal.ReadInt16(_buffer, 8 + ProtocolSpecificDataSize + 1);
            return Plausible(kelvin - 273.0);
        }

        Marshal.WriteInt32(_buffer, 0, StorageDeviceTemperatureProperty);
        if (!Ioctl(d.Handle, 12)) return null;
        // STORAGE_TEMPERATURE_DATA_DESCRIPTOR: InfoCount @12, TemperatureInfo[0].Temperature @26, in Celsius.
        return Marshal.ReadInt16(_buffer, 12) == 0 ? null : Plausible(Marshal.ReadInt16(_buffer, 26));
    }

    private static double? Plausible(double c) => c is > 0 and < 150 ? c : null;

    private string? Model(SafeFileHandle handle)
    {
        Marshal.Copy(Zeros, 0, _buffer, BufferSize);
        Marshal.WriteInt32(_buffer, 0, StorageDeviceProperty);
        if (!Ioctl(handle, 12)) return null;
        // STORAGE_DEVICE_DESCRIPTOR: ProductIdOffset @16.
        var offset = Marshal.ReadInt32(_buffer, 16);
        if (offset <= 0 || offset >= BufferSize) return null;
        return Marshal.PtrToStringAnsi(_buffer + offset)?.Trim();
    }

    private bool Ioctl(SafeFileHandle handle, int inSize) =>
        DeviceIoControl(handle, IoctlStorageQueryProperty, _buffer, inSize, _buffer, BufferSize, out _, IntPtr.Zero);

    public void Dispose()
    {
        foreach (var d in _drives) d.Handle.Dispose();
        _drives.Clear();
        Marshal.FreeHGlobal(_buffer);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr inBuffer, int inSize,
        IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);
}
