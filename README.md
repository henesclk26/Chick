# Chick

Unity sürümü: **6000.3.10f1**.

## Projeyi açma

Git ve Git LFS kurulu bir terminalde:

```sh
git lfs install
git clone https://github.com/henesclk26/Chick.git
cd Chick
git lfs pull
```

Unity Hub üzerinden bu klasörü ekleyip belirtilen Unity sürümüyle açın.
Unity ilk açılışta paketleri indirir ve `Library` klasörünü yeniden oluşturur.

`Assets`, `Packages` ve `ProjectSettings` Unity projesini içerir.
`ArtSource` kaynak Blender dosyalarını, `Documentation` proje belgelerini içerir.
Büyük terrain, model, doku ve ses dosyaları Git LFS ile saklanır.
Varlıklara ait `.meta` dosyalarını ilgili varlıklarla birlikte commit edin.

## Değişiklikleri gönderme

```sh
git add .
git status
git commit -m "Değişikliği açıkla"
git push
```

Geçici Unity verileri, derlemeler, yerel yedekler ve bilgisayara özel araç
ayarları `.gitignore` ile sürüm kontrolünün dışında tutulur.
