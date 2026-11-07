# edifier-mp85-battery

Read the battery level of an **Edifier MP85** speaker on Windows over Bluetooth LE.

This tool is tested only on the MP85. Other Edifier models can use a different protocol.

## Build

```powershell
.\build.ps1
```

Requirements: Windows 10/11, .NET Framework 4.7.2+, and the Windows SDK.

## Use

```powershell
.\bin\edifier-mp85-battery.exe 08:F0:B0:1C:XX:XX      # prints: EDIFIER MP85 BLE: battery 40%
.\bin\edifier-mp85-battery.exe 08:F0:B0:1C:XX:XX -v   # also prints the scan and the packets
```

Replace `08:F0:B0:1C:XX:XX` with the BLE address of your speaker (see below).

After a successful read, the tool saves the address to `%APPDATA%\edifier-mp85-battery\address.txt`. On the next runs, you can omit the address:

```powershell
.\bin\edifier-mp85-battery.exe        # uses the saved address
```

To use a different speaker, give its address again. The tool replaces the saved address.

If you give no address and there is no saved address, the tool scans for MP85 speakers for 10 seconds:

- One speaker found: the tool uses it, saves its address, and shows the battery.
- Two or more found: the tool lists their addresses and signal strengths. Run the tool again with the address you want.
- None found: the tool shows an error.

So on the first run, you usually do not need the address.

Close the Edifier app on your phone first. The phone can hold the BLE link.

The tool connects only to the exact BLE address. It does not match by name, because every MP85 advertises the same name ("EDIFIER MP85 BLE").

To find the BLE address of your speaker:

1. Get the classic Bluetooth address from Windows (Device Manager > the speaker > Details > Bluetooth device address).
2. Change the 3rd byte from `B6` to `B0`. Example: `08:F0:B6:1C:XX:XX` → `08:F0:B0:1C:XX:XX`.

This rule was seen on two MP85 units. To check it, run with `-v` and compare the reply to command `C8` with the classic address.

## Protocol

Decoded from an iOS Bluetooth capture of the Edifier ConnectX app.

| Item | Value |
|---|---|
| Service | `4809A901-1A48-11E9-AB14-D663BD873D93` |
| Write characteristic | `48090002-1A48-11E9-AB14-D663BD873D93` |
| Notify characteristic | `48090001-1A48-11E9-AB14-D663BD873D93` |
| Request frame | `AA EC <cmd> 00 <len> <data...> <checksum>` |
| Reply frame | `BB EC <cmd> 00 <len> <data...> <checksum>` |
| Checksum | Sum of all previous bytes, mod 256 |

Commands seen in the capture:

| Cmd | Reply data (example) | Meaning |
|---|---|---|
| `D0` | `28` | Battery percent (0x28 = 40%) |
| `C9` | `EDIFIER MP85` (ASCII) | Device name |
| `C8` | `08 F0 B6 1C XX XX` | Classic Bluetooth address |
| `D8`, `61`, `C3`, `D5`, `F2`, `91`, `C6`, `66` | various | Not decoded |

The classic serial port (SPP, `COM4`) does not reply to these frames.
