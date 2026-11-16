# PrismDock

Windows için cam görünümlü dock ve görev çubuğu. Windows 10 ve Windows 11'i destekler.

## İndir

- [PrismDock-Setup.exe](https://github.com/M1r-tmlrr/PrismDock/releases/latest/download/PrismDock-Setup.exe): kurulum
- [PrismDock.exe](https://github.com/M1r-tmlrr/PrismDock/releases/latest/download/PrismDock.exe): kurulumsuz
- [Kaynak kod](source)

Gereksinim: Windows 10 veya Windows 11, .NET Framework 4.8.


## Özellikler

- Arkadaki ekranı yansıtan cam efekti, 8 tema
- Üstte saat ve tarih gösteren ada (tıklayınca dock açılır)
- Win tuşuyla açılan menü: kilitle, uyku, yeniden başlat, kapat
- Arama (Alt + Boşluk): uygulama, dosya, klasör, hesap makinesi
- Sistem tepsisi ve arka planda çalışan uygulamalar (Windows 10)
- Çalan şarkı / video bilgisi
- Simgeleri sürükleyerek sıralama
- Tam ekranda otomatik gizlenme

## Kısayollar

| | |
|---|---|
| Win | Başlat menüsü |
| Alt + Boşluk | Arama |
| Simgeye sağ tık | Pencereler, sabitle, görevi sonlandır |
| Adaya tıkla | Dock'u aç / kapat |

## Derleme

SDK gerekmez. `source` klasöründe:

```
build.bat          → bin\PrismDock.exe
build-setup.bat    → bin\PrismDock-Setup.exe
```

## Sorun giderme

Windows görev çubuğu kaybolursa:

```
PrismDock.exe --restore
```

Ayarlar `%AppData%\PrismDock` klasöründe tutulur. Bir sorun olursa oradaki `events.log` dosyası ne olduğunu gösterir.
