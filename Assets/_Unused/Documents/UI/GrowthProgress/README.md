# Gelişim HUD — kurulum ve doğrulama

SampleScene içindeki **Growth Progress HUD** nesnesi tek UIDocument ile çalışır. Manuel referans atamak gerekmez. Ana menüde, duraklatmada ve gün geçişinde gizlenir; oyun başladığında görünür.

## Dosyalar

Yeni runtime dosyaları:

- `Assets/Scripts/UI/GrowthProgressController.cs`: sayaç, yumuşak dolum, ikon bağlama, kayıt olayları.
- `Assets/Scripts/UI/GrowthProgressBarElement.cs`: UI Toolkit custom VisualElement.
- `Assets/UI/GrowthProgress/GrowthProgressHUD.uxml`: bar, sayaç ve iki ikonun hiyerarşisi.
- `Assets/UI/GrowthProgress/GrowthProgressHUD.uss`: boyut, konum, renk ve kenarlıklar.
- `Assets/UI/GrowthProgress/GrowthPanelSettings.asset`: 1920×1080 referanslı responsive panel.
- `Assets/UI/GrowthProgress/ChickIcon.png`, `DoubleJumpIcon.png`: verilen PNG'lerin değiştirilmemiş kopyaları.

Kurulum / test:

- `Assets/Editor/GrowthProgressSetup.cs`: referansları bağlayan ve prefab kategorilerini ayarlayan kurulum aracı. Kurulum zaten uygulandı.
- `Assets/Tests/UI/GrowthProgressPlayVerification.cs`: yalnızca editörde elle başlatılan iki aşamalı Play Mode kontrolü. Kayıtlı sahneye eklenmez, build'e dahil edilmez.

Değiştirilen mevcut dosyalar:

- `Assets/Scripts/Eating/EdibleObject.cs`: kategori alanı ve başarılı tüketimin sonunda `Consumed` olayı.
- `Assets/Scripts/DayCycle/FarmSaveSystem.cs`: `FarmSaveData.eatenSeedCount`, `Saving` / `Loaded` olayları ve son yüklenen veriye erişim.
- `Assets/Scenes/SampleScene.unity`: yeni HUD nesnesi ve referansları.
- `Assets/Prefabs/Edibles/*.prefab`, `Assets/Assets/EdibleFoods/*.prefab`: yalnızca yenebilir parçaların kategori bilgisi.

## Sayaç ve kayıt

`GrowthProgressController`, `EdibleObject.Consumed` olayını dinler. Bu olay `Consume` başarılı olup nesne gizlendikten sonra çalışır. Mevcut gaga temas kontrolü ve animasyon zamanlaması kullanılmaya devam eder. Aynı tüketilmiş nesnenin yeniden `Consume` edilmesi başarısız olur; kayıt geri yükleme bu olayı yayınlamaz.

Yalnızca `Category = Seed` ilerleme verir. Mısır, ayçiçeği, pirinç, buğday prefabları ve karpuz/domates/balkabağı/elma içindeki çekirdekler bu kategoridedir. Ana yiyecek gövdeleri, kurabiye ve diğer süsleme parçaları `Other` kategorisindedir. Yeni gerçek tohum prefablarında EdibleObject > Category alanını **Seed** seçin.

Sayaç 0–50 arasında tutulur; her tohum +1 verir. Doldurma 0,26 saniyede yumuşak geçiş yapar. 50'ye ilk ulaşıldığında `OnGrowthProgressComplete` bir kez yayınlanır. Dönüşüm veya çift zıplama bu olaya bağlanmadı.

`FarmSaveSystem.Saving` mevcut Kaydet ve Çık / gün sonu kayıtlarına sayacı ekler. Menüde Yeni Oyun için yazılan sıfır kayıt korunur. `Loaded` sayacı kayıttan anında yükler. Eski kayıtlarda eksik alan 0 kabul edilir. `consumedEdibleIds` yakalama ve geri yükleme kodu değiştirilmedi.

## Görünüm

Bar 600×48 referans piksel, üstten 30 piksel boşluklu, yatayda ortalıdır. Gri zemin ve yeşil dolgu `generateVisualContent` içinde çizilir; dış koyu çerçeve USS'tedir. Yeşil dörtgenin üst sağ köşesi alt sağ köşesinden ileride olduğundan sınır **/** şeklindedir. Uçlarda eğimin genişliği küçülür: %0 tamamen gri, %100 tamamen yeşildir.

İkonlar UXML'deki UI Toolkit `Image` elemanlarına bağlanır. PNG tasarımları ve pikselleri değiştirilmedi; yalnızca şeffaf dış boşluklar Sprite gösterim dikdörtgeniyle dışarıda bırakılır. Normalize koordinatlar farklı texture çözünürlüklerini destekler.

Son kullanıcı düzeltmesiyle sağ ikon 72'den **60 piksel yüksekliğe** küçültüldü, sağa ve yukarı taşındı. Ok barın altına sarkmaz. Sol ikon 72 piksel yüksekliğindedir. Sağ ikonun bu yeni yerleşimi ilk eşit boy / yarım örtüşme isteğinin yerine geçer.

## Test sonucu

35 Play Mode kontrolü geçti. İstenen 13 test başlığının tamamı kapsandı:

- Gerçek sol tık / animasyon: başlamada artış yok, başarılı gaga temasında +1.
- 0, 1, 10, 25, 49, 50 ve 51. tohum durumları; düzgün animasyon, %100'de gri alanın tamamen kapanması.
- Çifte sayımın engellenmesi, Seed olmayan tüketimin sayılmaması, completion olayının bir kez çalışması.
- Eski 17/50 kayıt varken Yeni Oyun'un 0/50 başlaması.
- Ayrı dosyada gerçek disk kaydı, ardından Play Mode yeniden açılarak gerçek **Devam Et** akışında 17/50 yüklenmesi.
- Kayıtlı yenmiş tohumların tekrar görünmemesi ve yüklemenin sayacı artırmaması.
- 1920×1080, 1280×720, 1440×1080, 2560×1080 panel renderlarında ortalama, ikon yerleşimi ve sağ ikonun alt kenarının taşmaması.
- HUD üzerinde Canvas / UnityEngine.UI bileşeni bulunmaması; UIDocument + UXML + USS + custom VisualElement kullanılması.

Son testlerde Unity Console hatası yok. Testler kullanıcının `farm-save.json` kaydını yazmadı; ayrı `growth-hud-test-...` dosyaları kullandı. Windows build testi yapılmadı. Görsel test çıktıları `Temp/GrowthHUDReview` klasöründedir.
