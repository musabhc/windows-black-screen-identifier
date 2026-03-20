# Black Screen Identifier

Windows cold-boot siyah ekranını nokta atışı teşhis etmeye odaklanan portable masaüstü uygulaması.

## V1 kapsamı

- `.NET 8` WPF tabanlı Windows uygulaması
- Türkçe öncelikli arayüz
- Pasif tarama + admin ile derin analiz + rehberli boot capture
- Event Log, registry, dump ayarları, güç profili, servisler ve cihaz envanteri korelasyonu
- Güvenli otomasyon odaklı aksiyon kataloğu
- GitHub Releases üzerinden sürüm kontrolü
- Portable `win-x64` release pipeline

## Çözüm yapısı

- `src/BlackScreenIdentifier.App`: WPF kabuğu, viewmodel’ler ve çok kipli executable
- `src/BlackScreenIdentifier.Core`: ortak modeller, enum’lar ve servis sözleşmeleri
- `src/BlackScreenIdentifier.Diagnostics`: Windows veri toplayıcıları
- `src/BlackScreenIdentifier.Rules`: kanıt ağırlıklı teşhis kural motoru
- `src/BlackScreenIdentifier.Actions`: düzeltmeler, rollback, export ve sürüm servisi
- `tests/BlackScreenIdentifier.Tests`: harici test framework kullanmayan hafif test koşucusu

## Yerelde çalıştırma

```powershell
dotnet restore src/BlackScreenIdentifier.App/BlackScreenIdentifier.App.csproj
dotnet build src/BlackScreenIdentifier.App/BlackScreenIdentifier.App.csproj
dotnet run --project src/BlackScreenIdentifier.App/BlackScreenIdentifier.App.csproj
```

## Test koşucusu

```powershell
dotnet run --project tests/BlackScreenIdentifier.Tests/BlackScreenIdentifier.Tests.csproj
```

## Release

`v*` tag’i push edildiğinde GitHub Actions self-contained `win-x64` portable çıktı, ZIP, Inno Setup installer `.exe`si ve SHA256 dosyası üretir.
