<p align="center">
  <img src="{{raw}}/docs/logo.png" width="88" alt="">
</p>

<h1 align="center">SysWatch {{version}}</h1>

<p align="center">
  Bilgisayarınız ısınınca ya da zorlanınca haber veren hafif bir Windows tepsi uygulaması.<br>
  <sub>A lightweight Windows tray app that alerts you when your PC runs hot or under heavy load. · <a href="#english">English below</a></sub>
</p>

<p align="center">
  <a href="{{download}}/SysWatch_x64-setup.exe"><img alt="Kurulum dosyasını indir" src="https://img.shields.io/badge/Kurulum-%C4%B0ndir-0078D4?style=for-the-badge&logo=windows&logoColor=white"></a>
  <a href="{{download}}/SysWatch_x64-portable.zip"><img alt="Taşınabilir sürümü indir" src="https://img.shields.io/badge/Ta%C5%9F%C4%B1nabilir-%C4%B0ndir-5C6BC0?style=for-the-badge&logo=files&logoColor=white"></a>
</p>

<p align="center">
  <img src="{{raw}}/docs/screens/light.png" width="860" alt="SysWatch ana penceresi ve sağ tık paneli">
</p>

## Yenilikler

{{changes}}

## Hangi dosya bana göre?

| | Dosya |
| --- | --- |
| **Çoğu kişi için:** kurulum dosyası. Eksikse .NET 8'i kendisi kurar, isterseniz Windows açılınca başlatır. | [`SysWatch_x64-setup.exe`]({{download}}/SysWatch_x64-setup.exe) |
| **Taşınabilir:** kurulum yok, ayarlar klasörün içinde durur. [.NET 8 Masaüstü Çalışma Zamanı](https://dotnet.microsoft.com/download/dotnet/8.0) gerekir. | [`SysWatch_x64-portable.zip`]({{download}}/SysWatch_x64-portable.zip) |

<details>
<summary><b>Kurulum</b></summary>

1. `SysWatch_x64-setup.exe` dosyasını indirip açın.
2. Mavi bir "Windows bilgisayarınızı korudu" penceresi çıkarsa **Ek bilgi**'ye, sonra **Yine de çalıştır**'a basın. Dosya kod imzalı olmadığı için bu uyarı çıkar.
3. Kurulum bitince SysWatch tepsiye yerleşir: sol tık ana pencere, sağ tık özet paneli.

Önceki bir sürüm kuruluysa üzerine kurulur, ayarlarınız kalır. CPU sıcaklığı için ücretsiz [PawnIO](https://pawnio.eu/) sürücüsü gerekir. Kanalların kurulumu [README](https://github.com/{{repo}}#bildirim-kanallarını-kurma)'de.
</details>

---

<a id="english"></a>
<details>
<summary><b>English</b></summary>

{{changes_en}}

**Installer:** run `SysWatch_x64-setup.exe` (installs the .NET 8 Desktop Runtime if needed). **Portable:** unzip `SysWatch_x64-portable.zip` and run `SysWatch.exe`; needs the .NET 8 Desktop Runtime. The files are not code-signed, so SmartScreen may ask for **More info → Run anyway**. CPU temperature needs the free [PawnIO](https://pawnio.eu/) driver.
</details>

<sub>Her dosyanın SHA-256 özeti `SHA256SUMS.txt` içinde. · Built from <a href="https://github.com/{{repo}}/commit/{{sha}}"><code>{{short}}</code></a> · GPL-3.0</sub>
