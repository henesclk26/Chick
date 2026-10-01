# Manuel gelişim alma sistemi

## Davranış

İlk barın mevcut 50 tohum hedefi korunur. Bar dolduğunda çift zıplama otomatik açılmaz; fazla yemek sonraki aşamaya taşınmaz. Barın altında 1,5 saniyelik “GELİŞİM HAZIR!” pop/fade bildirimi, ardından kalıcı “Z GELİŞTİR” gösterilir.

Z bir saniye tutulduğunda ödül yalnızca bir kez alınır. Erken bırakılırsa ödül verilmez ve halka 0,2 saniyede sıfıra döner. Menü, duraklatma, geçiş veya odak kaybı sırasında tutuş iptal edilir. Kamera sallama veya hareket müdahalesi yoktur.

Ödülle çift zıplama açılır, bar mantıksal olarak hemen sıfırlanır ve mevcut 0,26 saniyelik doluluk animasyonuyla boşalır. Sağ ikon kullanıcının gönderdiği tavuk görseli olur; sol civciv ikonu ve mevcut bar stili korunur.

**Kullanıcının son kararına göre ikinci bar yalnızca birikir.** Dolduğunda Z istemi veya tavuk dönüşümü tetiklenmez. Mevcut Ü ile form değiştirme denemesi değiştirilmemiştir.

## UI Toolkit ve durumlar

UXML'e `UpgradeNotificationRoot`, `GrowthReadyLabel`, `UpgradePrompt`, `UpgradeHoldIndicator`, `UpgradeKey` ve `UpgradeText` eklendi. USS yalnızca yeni bildirimin renk, boşluk, yazı ve halka boyutlarını tanımlar.

`UpgradeHoldIndicator`, `generateVisualContent` / `MeshGenerationContext` ile koyu arka halka ve yeşil ilerleme yayını çizer. Yay saat 12 yönünden başlayarak saat yönünde dolar; tam çemberde 96 geometri bölümü kullanılır. Z harfi UI Toolkit Label'dır. Canvas, UGUI veya TextMeshProUGUI eklenmedi.

Durum akışı: `Progressing → GrowthReadyMessage → WaitingForUpgrade → HoldingUpgrade → UpgradeCompleted → Progressing`. Erken bırakma `WaitingForUpgrade` durumuna döner. Tamamlanmış ilk ödül stage kontrolüyle tekrar alınamaz.

## Zıplama bağlantısı

`ChickPlayerController` içinde yalnızca çift zıplama kilidi, sayaç ve ikinci hava zıplaması dalı eklendi. İlk zıplamanın hızı/yüksekliği, hareket, dönüş ve kamera davranışı değiştirilmedi. İnişte sayaç sıfırlanır; üçüncü zıplama engellenir.

Mevcut sistem yükseklik tabanlıdır. İkinci kalkış hızı `sqrt(ilkYükseklik * 0.8 * -2 * gravity)` ile hesaplanır. Böylece hedef ek yükseklik ilk zıplamanın %80'idir; hızın kendisi yaklaşık %89,4 olur. Havadaki mevcut dikey hız yeni kalkış hızıyla değiştirilir, üstüne sınırsız kuvvet eklenmez.

## Kayıt bağlantısı

Mevcut `Saving` ve `Loaded` olaylarına bağlanıldı; disk kayıt mekanizması değiştirilmedi. `FarmSaveData` içine isteğe bağlı `growthUpgradeVersion`, `doubleJumpUnlocked`, `currentGrowthStage`, `currentGrowthProgress`, `growthUpgradePending` alanları eklendi. Eski `eatenSeedCount` alanı korunur.

Eski kayıtlar ilk aşama olarak açılır. Dolu ama alınmamış ödül yüklenince doğrudan Z istemi gösterilir; bildirim yeniden oynatılmaz. Alınmış ödül yüklenince çift zıplama, ikinci aşamanın ilerlemesi ve tavuk ikonu geri gelir. Kısmi Z tutuşu kaydedilmez. %100 altındaki bir kayıt yuvarlama nedeniyle ödüle dönüşmez. Mevcut tohum adımları %2 olduğundan %99 yükleme kontrolü tamamlanmamış en yakın tohum sayısında kalır.

## Dosyalar

Oluşturulanlar:

- `Assets/Scripts/UI/GrowthProgressController.Upgrades.cs`: durum makinesi, Z tutuşu, UI animasyonları, aşama ve kayıt bağlantıları.
- `Assets/Scripts/UI/UpgradeHoldIndicator.cs`: UI Toolkit dairesel gösterge.
- `Assets/UI/GrowthProgress/ChickenIcon.png`: gönderilen tavuk PNG'sinin değiştirilmeden kopyası; şeffaf kenar boşlukları Sprite UV'siyle elenir.
- `Assets/Tests/UI/GrowthUpgradePlayVerification.cs`: isteğe bağlı, yalnızca Editor'da derlenen Play Mode doğrulama aracı; kayıtlı sahneye ekli değildir.
- Bu rapor ve yeni Unity varlıklarının `.meta` dosyaları.

Değiştirilenler:

- `Assets/Scripts/UI/GrowthProgressController.cs`
- `Assets/Scripts/ChickPlayerController.cs`
- `Assets/Scripts/DayCycle/FarmSaveSystem.cs` (yalnızca veri alanları)
- `Assets/UI/GrowthProgress/GrowthProgressHUD.uxml`
- `Assets/UI/GrowthProgress/GrowthProgressHUD.uss`
- `Assets/Editor/GrowthProgressSetup.cs` (yeni ikon/oyuncu referanslarının kurulum desteği)
- `Assets/Scenes/SampleScene.unity` (yeni ikon, oyuncu ve tutuş ayarı bağlantıları)
- `Assets/Animals_3D/Chicken/Resources/Prefabs/chick.prefab` (son ayar: ikinci zıplama yüksekliği çarpanı 0,8)

## Inspector

Manuel atama gerekmiyor. `SampleScene` içindeki `Growth Progress HUD` üzerinde tavuk ikonu ve oyuncu bağlandı. `Upgrade Hold Duration = 1` saniye; sahne ve civciv prefab'ında `ChickPlayerController / Second Jump Height Multiplier = 0.8`. İki ayar Inspector'dan değiştirilebilir. Yeni sahne oluşturulmadı.

## Doğrulama

Unity MCP üzerinden Play Mode'da 33 kontrol geçti:

- Eksik bar/99% kayıt için istem yok; son tohum bildirimi başlatır ve otomatik ödül vermez.
- 1,5 saniyelik bildirim, kalıcı Z istemi, kısa basış, yarım tutuş, yumuşak halka sıfırlanması ve odak/duraklatma iptali.
- Bir saniyelik tutuş, tek ödül, bar sıfırlama, ikon değişimi ve basılı tutmaya devam ederken tekrar ödül verilmemesi.
- Fazla tohumun taşınmaması ve ikinci aşamanın dönüşüm olmadan dolması.
- Kayıt JSON'u diske yazılıp gerçek kayıt okuyucusu ve `Loaded` bağlantılarıyla geri yüklenerek alınmamış ödül/açılmış çift zıplama/aşama/ilerleme test edildi. Eski kayıt uyumluluğu da geçti.
- Input System klavye olaylarıyla gerçek player Update/CharacterController akışında tek zıplama, ikinci zıplama, üçüncünün reddi ve inişte sayaç sıfırlama doğrulandı. İkinci kalkış hızı %80 yükseklik formülüyle karşılaştırıldı.
- UI Toolkit türleri ve Canvas bulunmaması kontrol edildi; halka ve tavuk aşaması 1920×1080 panel çıktısında görsel olarak incelendi.

Tutuş durumları deterministik input örnekleriyle test edildi. Kullanıcının gerçek kayıt dosyası değiştirilmedi; test JSON'u ve panel çıktıları `Temp/GrowthUpgradeReview` altında tutuldu. Menüden gerçek disk `SaveAsync → Continue` turu ve bağımsız build testi bu doğrulamaya dahil değildir. Unity Console'da yeni hata yok.
