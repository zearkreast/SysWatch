# Third-party notices

SysWatch is licensed under the GNU General Public License v3.0 (see `LICENSE`). It includes the
components below, each under its own licence; every one of them may be combined with and distributed
as part of a GPL-3.0 program. The full licence texts are in the `licenses` folder, which ships with
the installer and the portable build.

None of these components were modified. Their source code is available at the addresses given.

## Libraries bundled in SysWatch.exe

| Component | Version | Licence | Copyright | Source |
| --- | --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | LibreHardwareMonitor contributors | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | Blacktempel | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | Blacktempel | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | Blacktempel | https://github.com/Blacktempel/RAMSPDToolkit |
| PawnIO modules (compiled, embedded in LibreHardwareMonitorLib) | as shipped with 0.9.6 | LGPL-2.1-or-later | namazso | https://github.com/namazso/PawnIO.Modules |
| HidSharp | 2.6.4 | Apache-2.0 | James F. Bellinger | https://software.seekye.com/hidsharp |
| MailKit | 5.0.0 | MIT | .NET Foundation and Contributors | https://github.com/jstedfast/MailKit |
| MimeKit | 5.0.0 | MIT | .NET Foundation and Contributors | https://github.com/jstedfast/MimeKit |
| BouncyCastle.Cryptography | 2.7.0 | MIT | The Legion of the Bouncy Castle Inc. | https://github.com/bcgit/bc-csharp |
| System.Management, System.CodeDom, System.Formats.Asn1, System.IO.Ports, System.Security.Cryptography.ProtectedData, System.Threading.AccessControl | 8.0 / 10.0 | MIT | .NET Foundation and Contributors | https://github.com/dotnet/runtime |
| Mono.Posix.NETStandard (managed part) | 1.0.0 | MIT | .NET Foundation and Contributors | https://github.com/mono/mono |

Licence texts: `licenses/mpl-2.0.txt`, `licenses/lgpl-2.1.txt`, `licenses/hidsharp-apache-2.0.txt`,
`licenses/mit.txt`.

The PawnIO **driver** (GPL-2.0) is not part of SysWatch. Users who want CPU temperatures install it
themselves from https://pawnio.eu.

## Artwork

| Item | Licence | Notes |
| --- | --- | --- |
| In-app icons (`Assets/Icons`) | Public domain | Tango Desktop Project, see `licenses/tango-icons.txt` |
| Discord, Telegram and Signal logos (`Assets/Brands`) | CC0-1.0 (the files) | From Simple Icons. The logos are trademarks of their owners and are shown unmodified only to label the matching channel; see `licenses/simple-icons-notice.txt` |
| SysWatch logo | GPL-3.0, part of this project | Drawn in code (`UI/Logo.cs`) |

## Runtime

SysWatch runs on the Microsoft .NET 8 Desktop Runtime (MIT), which is installed separately and is
not redistributed with SysWatch.
