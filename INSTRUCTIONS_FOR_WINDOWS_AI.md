# ⚠️ PANDUAN UNTUK AI DI WINDOWS (STRICT EXECUTION ONLY)

> **PERINGATAN SANGAT PENTING (CRITICAL RULE):**
> **JANGAN UBAH CODE ATAU ARSITEKTUR APAPUN!**
> Seluruh source code C#, konfigurasi, branding **Nusamba NodeCore v3.0.0**, fitur elevasi UAC non-admin, dan integrasi backend sudah **100% SELESAI, TEPAT, DAN SUDAH SINKRON DENGAN SERVER WEB PRODUCTION**.
>
> Tugas Anda sebagai AI di Windows **HANYA 2 HAL**:
> 1. **BUILD** berkas installer `.msi` (x64 dan ARM64).
> 2. **UPLOAD / PUSH** ke repository GitHub atau rilis di GitHub Releases.

---

## 🎯 TUGAS 1: BUILD INSTALLER MSI

Buka **PowerShell** atau **CMD** di folder ini (`D:\Project4\PCSpekColeltion\ClientAgent`):

### Cara Termudah (Cukup Jalankan Batch Script):
```cmd
build-msi.bat
```

### ATAU Jalankan Perintah Manual Berikut via PowerShell:

1. **Pastikan WiX Toolset v4 terpasang:**
   ```powershell
   dotnet tool install --global wix
   ```

2. **Publish Binary x64:**
   ```powershell
   dotnet publish PCSpecAgent/PCSpecAgent.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
   ```

3. **Build Installer MSI x64:**
   ```powershell
   wix build Package.wxs -o NusambaNodeCore-Setup.msi
   ```

4. **Publish Binary ARM64:**
   ```powershell
   dotnet publish PCSpecAgent/PCSpecAgent.csproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -o publish-arm64
   ```

5. **Build Installer MSI ARM64:**
   ```powershell
   wix build Package.arm64.wxs -o NusambaNodeCore-Setup-arm64.msi
   ```

---

## 🎯 TUGAS 2: VERIFIKASI HASIL BUILD

Pastikan berkas-berkas berikut sudah terbentuk di root folder `ClientAgent`:
- `NusambaNodeCore-Setup.msi` (Ukuran sekitar 50-60 MB)
- `NusambaNodeCore-Setup-arm64.msi` (Ukuran sekitar 50 MB)

---

## 🎯 TUGAS 3: PUSH DAN RELEASE KE GITHUB

Push berkas dan rilis ke repository resmi:
Repository: `https://github.com/Nusamba-dev/NodeCore.git`

```powershell
# Pastikan remote sudah benar
git remote -v

# Push branch main
git push -u origin main

# Buat tag rilis resmi v3.0.0
git tag -a v3.0.0 -m "Release Nusamba NodeCore Agent v3.0.0"
git push origin v3.0.0
```

Jika GitHub CLI (`gh`) tersedia di sistem Windows:
```powershell
gh release create v3.0.0 NusambaNodeCore-Setup.msi NusambaNodeCore-Setup-arm64.msi --title "Nusamba NodeCore v3.0.0" --notes "Rilis resmi Nusamba NodeCore Agent v3.0.0 x64 dan ARM64"
```

---

### RINGKASAN RESTRISI AI:
- ❌ DILARANG refactor, ganti nama variabel, rename file C#, atau mengubah `JobExecutor.cs`.
- ❌ DILARANG mengubah konfigurasi database atau URL backend.
- ✅ CUKUP BUILD `build-msi.bat` lalu upload / push release ke GitHub.
