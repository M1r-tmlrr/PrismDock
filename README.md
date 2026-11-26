# PrismDock

Windows için cam görünümlü dock ve görev çubuğu. Windows 10 ve Windows 11'de çalışır.

## İndir

- [PrismDock-Setup.exe](https://github.com/M1r-tmlrr/PrismDock/releases/latest/download/PrismDock-Setup.exe): kurulum
- [PrismDock.exe](https://github.com/M1r-tmlrr/PrismDock/releases/latest/download/PrismDock.exe): kurulumsuz
- [Kaynak kod](source)

Gereksinim: .NET Framework 4.8

## Özellikler

- Canlı cam efekti ve 8 tema (Liquid Glass, Buzlu Cam, Latte, Buz Beyazı, Gece Yarısı, Aurora, Gün Batımı, Obsidyen)
- Üstte saat ve tarih gösteren ada
- Win tuşuyla açılan menü: uygulamalar, son dosyalar, kilitle, uyku, yeniden başlat, kapat
- Arama: uygulama, dosya, klasör, Windows ayarları, hesap makinesi
- Arka planda çalışan uygulamalar paneli
- Çalan şarkı / video bilgisi
- Pencere önizlemesi, simgeleri sürükleyerek sıralama
- Klavye düzeni paneli (Türkçe Q / F)
- Birden fazla monitör desteği
- Tam ekranda otomatik gizlenme
- Düşük işlemci ve bellek kullanımı

## Kısayollar

| | |
|---|---|
| Win | Başlat menüsü |
| Alt + Boşluk | Arama |
| Simgeye sağ tık | Pencereler, sabitle, görevi sonlandır |
| Adaya tıkla | Dock'u aç / kapat |

## Derleme

SDK gerekmez. `source` klasöründe `build.bat` uygulamayı, `build-setup.bat` kurulumu derler.

## Sorun giderme

- Windows görev çubuğu kaybolursa: `PrismDock.exe --restore`
- Ayarlar ve `events.log` dosyası `%AppData%\PrismDock` klasöründedir.
