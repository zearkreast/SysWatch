<p align="center">
  <img src="{{raw}}/docs/logo.png" width="88" alt="">
</p>

<h1 align="center">SysWatch {{version}}</h1>

<p align="center">
  A lightweight Windows tray app that alerts you when your PC runs hot or under heavy load.<br>
  <sub>Bilgisayarınız ısınınca ya da zorlanınca haber veren hafif bir Windows tepsi uygulaması. · <a href="#turkce">Türkçe aşağıda</a></sub>
</p>

<p align="center">
  <a href="{{download}}/SysWatch_x64-setup.exe"><img alt="Download the installer" src="https://img.shields.io/badge/Installer-Download-0078D4?style=for-the-badge&logo=windows&logoColor=white"></a>
  <a href="{{download}}/SysWatch_x64-portable.zip"><img alt="Download the portable build" src="https://img.shields.io/badge/Portable-Download-5C6BC0?style=for-the-badge&logo=files&logoColor=white"></a>
</p>

<p align="center">
  <img src="{{raw}}/docs/screens/light-en.png" width="860" alt="The SysWatch main window and right-click panel">
</p>

## What's new

{{changes_en}}

## Which file do I need?

| | File |
| --- | --- |
| **For most people:** the installer. Installs .NET 8 if it's missing and can start SysWatch with Windows. | [`SysWatch_x64-setup.exe`]({{download}}/SysWatch_x64-setup.exe) |
| **Portable:** nothing to install, settings stay inside its folder. Needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). | [`SysWatch_x64-portable.zip`]({{download}}/SysWatch_x64-portable.zip) |

<details>
<summary><b>Installing</b></summary>

1. Download and open `SysWatch_x64-setup.exe`.
2. If a blue "Windows protected your PC" window appears, click **More info**, then **Run anyway**. The file isn't code-signed, which is why the warning appears.
3. When setup finishes, SysWatch sits in the tray: left-click for the main window, right-click for the summary panel.

An earlier version is upgraded in place and your settings stay. CPU temperature needs the free [PawnIO](https://pawnio.eu/) driver. Setting up the channels is covered in the [README](https://github.com/{{repo}}#setting-up-notification-channels).
</details>

---

<a id="turkce"></a>
<details>
<summary><b>Türkçe</b></summary>

{{changes}}

**Kurulum:** `SysWatch_x64-setup.exe` dosyasını çalıştırın (gerekirse .NET 8 Masaüstü Çalışma Zamanı'nı kendisi kurar). **Taşınabilir:** `SysWatch_x64-portable.zip` dosyasını açıp `SysWatch.exe`'yi çalıştırın; .NET 8 Masaüstü Çalışma Zamanı gerekir. Dosyalar kod imzalı olmadığı için SmartScreen **Ek bilgi → Yine de çalıştır** isteyebilir. CPU sıcaklığı için ücretsiz [PawnIO](https://pawnio.eu/) sürücüsü gerekir. Arayüz, Windows Türkçeyse Türkçe açılır. Ayrıntılar: [README.tr.md](https://github.com/{{repo}}/blob/main/README.tr.md).
</details>

<sub>SHA-256 checksums for every file are in `SHA256SUMS.txt`. · Built from <a href="https://github.com/{{repo}}/commit/{{sha}}"><code>{{short}}</code></a> · GPL-3.0</sub>
