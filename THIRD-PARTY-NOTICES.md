# Third-party notices

`TempMonitor.exe` embeds the libraries below (via Costura). Each remains under its own license. Source code for the MPL-2.0 components is available, unmodified, at the linked repositories.

| Component | Version | License | Source |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| HidSharp | 2.6.4 | Apache-2.0 | https://software.seekye.com/hidsharp |
| System.Management | 10.0.2 | MIT | https://github.com/dotnet/runtime |
| System.CodeDom | 10.0.2 | MIT | https://github.com/dotnet/runtime |
| System.Threading.AccessControl | 10.0.3 | MIT | https://github.com/dotnet/runtime |
| System.Security.AccessControl | 6.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Security.Principal.Windows | 5.0.0 | MIT | https://github.com/dotnet/runtime |
| System.Memory | 4.6.3 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Buffers | 4.6.1 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Numerics.Vectors | 4.6.1 | MIT | https://github.com/dotnet/maintenance-packages |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT | https://github.com/dotnet/maintenance-packages |
| Costura (runtime loader) | 6.0.0 | MIT | https://github.com/Fody/Costura |

License texts: [MPL-2.0](https://www.mozilla.org/MPL/2.0/), [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0), [MIT](https://opensource.org/license/mit).

## Not bundled

**PawnIO** (https://pawnio.eu, https://github.com/namazso/PawnIO.Setup) is not included in this app. When the user chooses to install it, Temp Monitor downloads the official installer from its GitHub release and checks it against a pinned SHA-256 checksum before running it.
