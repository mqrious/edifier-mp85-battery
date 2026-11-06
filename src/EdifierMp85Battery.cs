// Read the battery level of an Edifier MP85 speaker over Bluetooth LE.
// Tested only on the MP85. Other Edifier models can use a different protocol.
//
// Protocol (decoded from an iOS Edifier ConnectX capture of an MP85):
//   Request : AA EC <cmd> 00 <len> <data...> <checksum>
//   Reply   : BB EC <cmd> 00 <len> <data...> <checksum>
//   Checksum: sum of all previous bytes, mod 256.
//   Battery : cmd 0xD0, reply data = 1 byte, percent.
//
// Usage: edifier-mp85-battery.exe <BLE address, e.g. 08:F0:B0:1C:XX:XX> [-v]
//
// The BLE address is the classic Bluetooth address with the 3rd byte B6 changed to B0
// (seen on two MP85 units: 08:F0:B6:1C:XX:XX -> 08:F0:B0:1C:XX:XX).

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

static class EdifierMp85Battery
{
    static readonly Guid ServiceUuid = new Guid("4809A901-1A48-11E9-AB14-D663BD873D93");
    const string DeviceName = "EDIFIER MP85";
    const byte CmdBattery = 0xD0;
    const int ScanSeconds = 15;
    const int ReplySeconds = 5;

    static bool verbose;

    static int Main(string[] args)
    {
        string address = null;
        foreach (string a in args)
        {
            if (a == "-v") verbose = true;
            else address = a;
        }

        if (address == null)
        {
            Console.Error.WriteLine("Usage: edifier-mp85-battery.exe <BLE address> [-v]");
            Console.Error.WriteLine("Example: edifier-mp85-battery.exe 08:F0:B0:1C:XX:XX");
            return 2;
        }

        try
        {
            using (BluetoothLEDevice device = Connect(ParseAddress(address)))
            {
                int battery = ReadBattery(device);
                Console.WriteLine("{0}: battery {1}%", device.Name, battery);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    // --- Connection ----------------------------------------------------------

    static BluetoothLEDevice Connect(ulong address)
    {
        BluetoothLEDevice device = BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask().Result;
        if (device != null) return device;

        Log("Device not known yet. Scanning for advertisements...");
        ulong found = Scan(address);
        device = BluetoothLEDevice.FromBluetoothAddressAsync(found).AsTask().Result;
        if (device == null) throw new Exception("Cannot open the BLE device.");
        return device;
    }

    // Find the speaker by its exact BLE address.
    // Do not match by name: every MP85 advertises the same name.
    static ulong Scan(ulong address)
    {
        ulong result = 0;
        var done = new ManualResetEventSlim(false);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (w, e) =>
        {
            string name = e.Advertisement.LocalName ?? "";
            Log(string.Format("ADV {0} \"{1}\" {2} dBm", FormatAddress(e.BluetoothAddress), name, e.RawSignalStrengthInDBm));
            if (e.BluetoothAddress == address)
            {
                result = e.BluetoothAddress;
                done.Set();
            }
        };
        watcher.Start();
        done.Wait(TimeSpan.FromSeconds(ScanSeconds));
        watcher.Stop();

        if (result == 0)
            throw new Exception(DeviceName + " at " + FormatAddress(address) + " not found. Turn on the speaker, close the Edifier app on your phone (it holds the BLE link), and try again.");
        Log("Found speaker at " + FormatAddress(result));
        return result;
    }

    // --- Battery request -----------------------------------------------------

    static int ReadBattery(BluetoothLEDevice device)
    {
        GattDeviceServicesResult services = device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached).AsTask().Result;
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            throw new Exception("Edifier service not found (status: " + services.Status + ").");

        using (GattDeviceService service = services.Services[0])
        {
            GattCharacteristicsResult chars = service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached).AsTask().Result;
            GattCharacteristic notify = chars.Characteristics.FirstOrDefault(c => Has(c, GattCharacteristicProperties.Notify));
            GattCharacteristic write = chars.Characteristics.FirstOrDefault(c =>
                Has(c, GattCharacteristicProperties.Write) || Has(c, GattCharacteristicProperties.WriteWithoutResponse));
            if (notify == null || write == null) throw new Exception("Notify or write characteristic not found.");
            Log("Notify " + notify.Uuid + ", write " + write.Uuid);

            var replies = new BlockingCollection<byte[]>();
            notify.ValueChanged += (s, e) => replies.Add(e.CharacteristicValue.ToArray());

            GattCommunicationStatus status = notify.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask().Result;
            if (status != GattCommunicationStatus.Success) throw new Exception("Cannot turn on notifications (status: " + status + ").");

            byte[] request = BuildRequest(CmdBattery, new byte[0]);
            Log("TX " + Hex(request));
            status = write.WriteValueAsync(request.AsBuffer()).AsTask().Result;
            if (status != GattCommunicationStatus.Success) throw new Exception("Write failed (status: " + status + ").");

            DateTime deadline = DateTime.Now.AddSeconds(ReplySeconds);
            byte[] frame;
            while (replies.TryTake(out frame, Math.Max(0, (int)(deadline - DateTime.Now).TotalMilliseconds)))
            {
                Log("RX " + Hex(frame));
                if (IsValidReply(frame, CmdBattery)) return frame[5];
            }
            throw new Exception("No battery reply from the speaker.");
        }
    }

    // --- Protocol ------------------------------------------------------------

    static byte[] BuildRequest(byte command, byte[] data)
    {
        byte[] frame = new byte[6 + data.Length];
        frame[0] = 0xAA;
        frame[1] = 0xEC;
        frame[2] = command;
        frame[3] = 0x00;
        frame[4] = (byte)data.Length;
        Array.Copy(data, 0, frame, 5, data.Length);
        frame[frame.Length - 1] = Checksum(frame, frame.Length - 1);
        return frame;
    }

    static bool IsValidReply(byte[] frame, byte command)
    {
        if (frame.Length < 6 || frame[0] != 0xBB || frame[1] != 0xEC || frame[2] != command) return false;
        if (frame.Length != 6 + frame[4]) return false;
        return frame[frame.Length - 1] == Checksum(frame, frame.Length - 1);
    }

    static byte Checksum(byte[] bytes, int count)
    {
        int sum = 0;
        for (int i = 0; i < count; i++) sum += bytes[i];
        return (byte)sum;
    }

    // --- Helpers -------------------------------------------------------------

    static bool Has(GattCharacteristic c, GattCharacteristicProperties p)
    {
        return (c.CharacteristicProperties & p) != 0;
    }

    static ulong ParseAddress(string text)
    {
        return Convert.ToUInt64(text.Replace(":", ""), 16);
    }

    static string FormatAddress(ulong address)
    {
        string h = address.ToString("X12");
        return string.Join(":", Enumerable.Range(0, 6).Select(i => h.Substring(i * 2, 2)));
    }

    static string Hex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", " ");
    }

    static void Log(string message)
    {
        if (verbose) Console.WriteLine(message);
    }
}
