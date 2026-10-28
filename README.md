# 🛡️ Nusamba NodeCore Endpoint & Remote Agent (v3.0.0)

[![Release](https://img.shields.io/github/v/release/Nusamba-dev/NodeCore?color=06b6d4&label=NodeCore%20Release)](https://github.com/Nusamba-dev/NodeCore/releases)
[![Build and Release](https://github.com/Nusamba-dev/NodeCore/actions/workflows/build-release.yml/badge.svg)](https://github.com/Nusamba-dev/NodeCore/actions/workflows/build-release.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64%20%7C%20ARM64-blue.svg)](https://github.com/Nusamba-dev/NodeCore)
[![Architecture](https://img.shields.io/badge/.NET-8.0--windows-512bd4.svg)](https://dotnet.microsoft.com/)

**Nusamba NodeCore** adalah agen endpoint management, audit hardware/software terpusat, telemetry jaringan, serta remote assistance dengan keamanan ganda (*Double-Lock Verification*) untuk seluruh armada PC operasional BPR Nusamba.

---

## 🚀 Fitur Utama

1. **Telemetry & Audit Hardware Otomatis**
   - Mengumpulkan data CPU, RAM, GPU, Motherboard, Disk S.M.A.R.T, dan Multi-IP LAN secara akurat tanpa membebani performa PC.
   - Peta topologi jaringan lokal (*LAN Gateway & Subnet Prober*) untuk mendeteksi anomali latensi & status koneksi cabang.

2. **Remote Software Deployment & Privilege Elevation (Bypass UAC)**
   - Eksekusi instalasi & uninstalasi aplikasi silent (`msiexec /qn`, InnoSetup, NSIS) dari Command Center pusat.
   - **Dual-Engine Elevation**: Mendukung eksekusi dengan hak `NT AUTHORITY\SYSTEM` atau akun administrator lokal tersimpan (*Task Scheduler Elevation*) sehingga staf cabang dengan akun standard user tidak terganggu oleh dialog prompt UAC.

3. **External Package Repository & Zero Egress CDN**
   - Mendukung distribusi installer payload langsung dari **GitHub Releases Fastly CDN** atau Cloudflare R2, menghemat 100% bandwidth server lokal BPR Nusamba.

4. **Double-Lock Web Remote Assistance**
   - Remote desktop berbasis Web Canvas terenkripsi TLS dengan konfirmasi ganda: **Persetujuan Layar + Verifikasi PIN Dinamis 6 Digit**.
   - Indikator bar melayang (*Floating Privacy Bar*) yang memungkinkan operator cabang memutuskan sesi remote kapan saja demi perlindungan data nasabah.

5. **Self-Healing & Auto-Update Senyap**
   - Agen secara otomatis mendeteksi rilis versi terbaru di server dan melakukan migrasi biner *in-place* tanpa memerlukan intervensi teknisi manual di cabang.

---

## 🛠️ Build & Packaging

Agen dikompilasi menggunakan .NET 8 SDK dan dipaketkan ke dalam installer `.msi` berbasis WiX Toolset v4:

```powershell
# Publish executable untuk Windows x64
dotnet publish PCSpecAgent/PCSpecAgent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# Paketkan ke MSI menggunakan WiX v4
wix build Package.wxs -o NusambaNodeCore-Setup.msi
```

---

## 🏢 Kepemilikan & Hak Cipta
Dikembangkan khusus untuk infrastruktur Teknologi Informasi **PT. BPR Nusamba**.
