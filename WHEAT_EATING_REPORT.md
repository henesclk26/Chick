# İlk buğday yeme sistemi — uygulama ve doğrulama

## Kullanım

`SampleScene` içinde civcivi buğdayın hemen önüne getir, WASD'yi bırak ve **sol tıkla**. Bir eylem bir tane tüketir. Yakında uygun hedef yoksa animasyon veya hareket kilidi başlamaz. Hareket sırasında yeme bu ilk sürümde kapalıdır. E tuşundaki mevcut ekstra gagalama animasyonu değiştirilmedi; yiyecek tüketimi sol tıktadır.

Sahneye 15 prefab örneği yerleştirildi. Inspector'da elle bağlantı yapmak gerekmiyor. Play Mode testleri bitirildi; sahne Edit Mode'da bırakıldı.

## Animasyon ve temas

| İstenen bilgi | Sonuç |
|---|---|
| Mevcut durum | `ChickGameplayController / Base Layer / eat` |
| Kaynak klip | `Assets/Animals_3D/Chicken/AnimationClips/chick/twoLayer/eat.anim` |
| Önceki hız | Durum 1×, Animator 1× |
| Son hız | Yalnızca eat durumu 1,4×; `EatPlaybackSpeed` parametresi |
| Inspector ayarı | ChickPlayer → ChickEatingController → Eat Animation Speed, 1,3–1,5 aralığı |
| Kaynak süre | 1,333333 saniye; 60 FPS, 80 kare zaman aralığı |
| Tam klibin 1,4× süresi | 0,952381 saniye |
| Temas | 21. kare, klip zamanı 0,350 s, normalize zaman **0,2625** |
| Temasın teorik oynanış zamanı | 0,350 / 1,4 = **0,250 s**; giriş/kare işlenmesi ölçüme eklenir |
| Temas olayı | `OnEatImpact()` Animation Event |
| İlk toparlanma | 30. kare, klip zamanı 0,500 s, normalize zaman **0,375**; `OnEatRecovery()` |
| Eat giriş geçişi | 0,05 saniye |
| Normal poza dönüş | Mevcut 0,16 saniyelik idle blend; bitene kadar kısa eylem kilidi |
| Ölçülen tekrar hazır olma | Son 10-tane testinde **0,528–0,538 saniye** |
| Ölçülen kaybolma | Sol tıktan yaklaşık **0,260–0,268 saniye** sonra |

Klipte üç gagalama var. Tam klibi 1,4× oynatmak yaklaşık 0,95 saniye süreceğinden, tek tanelik etkileşim ilk gagalama ve ilk toparlanma sonrası idle'a harmanlanır; kalan iki gagalama beklenmez. Kaynak klip, rig ve eğriler değiştirilmedi. `Assets/Animations/ChickEat_Gameplay.anim`, aynı eğrilerin yalnızca oyun olayları eklenmiş kopyasıdır; böylece başka demo Animator'ları etkilenmez. Başka animasyonların hızları ve geçişleri değişmedi; Animator'ın genel hızı 1×.

Temas noktası kemiğin her karede örneklenmesi ve yakın plan görüntüyle seçildi. İlk yerel minimum: beak_top 21. karede yaklaşık (-0,00765, 0,03388, 0,08599). Buğdayın 0,011 yükseklik / 0,09 ön mesafe yerleşimi görsel olarak kontrol edildi. Tüketim `WaitForSeconds` ile değil Animation Event ile yapılır. Sonraki yinelenen callback'ler güvenle reddedilir.

## Kod ve varlıklar

Yeni kodlar:

- `Assets/Scripts/Eating/EdibleObject.cs`: CanBeEaten, Consume, IsConsumed, RequiredEatLevel ve açık ResetForReuse havuzlama bağlantısı.
- `Assets/Scripts/Eating/ChickEatingController.cs`: Input Action, yerel hedef seçimi, hedef referansı, Eating/Recovering/Ready durumu, animasyon olayları ve temizlenme.
- `Assets/Editor/Eating/WheatSliceSetup.cs`: Açıkça çağrılan, yeniden çalıştırılabilir editör kurulum menüsü; oyun başında çalışmaz.
- `Assets/Tests/Eating/EatingSlicePlayVerification.cs`: İsteğe bağlı Play Mode test koşucusu; sahneye kaydedilmez ve player build'ine dahil edilmez. Test sırasında Input System ayarlarının geçici kopyasını kullanır, ardından orijinali geri bağlar.

Değiştirilen mevcut kod: yalnızca `Assets/Scripts/ChickPlayerController.cs`. Gerekçe: sol tıkı Input Action'dan okumak, eski sabit 1,1 saniyelik yeme kilidini animasyonla yönetilen kısa eyleme bağlamak, hedef olmadan yemeyi engellemek ve yalnızca eat giriş blend'ini 0,05 saniye yapmak. Hareket, yönlenme, yerçekimi/zıplama hesapları ve animasyon seçme metodu aynen korundu.

Diğer yeni/değişen varlıklar:

- Prefab: `Assets/Prefabs/Edibles/WheatGrain_Test.prefab`.
- Ortak mesh: `Assets/Art/Edibles/WheatGrain_Test.asset`; yassı, uzatılmış, düz yüzeyli 48 üçgenlik geçici tane.
- Ortak altın renkli URP malzeme: `Assets/Art/Edibles/WheatGold_Test.mat`; GPU instancing açık.
- Prefab kökünde `EdibleObject` ve trigger `BoxCollider`; Visual çocuğunda MeshFilter/Renderer. Rigidbody ve Animator yok.
- Collider boyutu: (0,030, 0,022, 0,046). Görsel boyutu yaklaşık (0,028, 0,020, 0,044).
- Mevcut **Edible / layer 9** kullanıldı; requiredEatLevel = 0, mevcut civcivle uyumlu.
- `SampleScene` içine `Wheat Test Patch` (15 tane) ve ChickPlayer üzerine yeme bileşeni kaydedildi.
- Mevcut Animator'ın yalnızca eat motion/speed parameter alanı ve yeni EatPlaybackSpeed float parametresi değişti.

## Hedef seçimi ve tüketim

Oyuncu sol tıkta `OverlapSphereNonAlloc` ile yalnızca Edible katmanını tarar. Merkez: oyuncu konumu + ileri yön × **0,09** + dünya yukarısı × **0,015**. Yarıçap **0,045**, oyuncudan maksimum mesafe **0,15** Unity birimi. Hedefin bite noktası da bu küçük kürenin içinde olmalıdır. Arkadaki, uzaktaki, pasif veya gereken seviyesi uygun olmayan hedefler reddedilir. Merkeze uzaklık ve yanal sapma ile en uygun tane seçilir; aynı hedef eylem boyunca tutulur.

Temasta hedef yeniden doğrulanır. Başarılı Consume önce IsConsumed işaretler, ardından **SetActive(false)** ile bütün görsel ve collider'ları hemen devre dışı bırakır. Tekrar Consume false döndürür. Tam havuz sistemi yoktur; ResetForReuse gelecekte havuzdan yeniden kullanım için açıktır. Tüketimde Destroy kullanılmaz. Sadece yeniden etkinleştirmek tüketilmiş bir taneyi yeniden yenilebilir yapmaz.

Ek tıklamalar sıraya alınmaz. Hedef taşınır veya devre dışı kalırsa animasyon güvenle tamamlanır. Oyuncu/Animator devre dışı kalırsa eylem temizlenir. Animasyon olayları eksik olsa bile klip bitişi için kilidin açık kalmasını önleyen durum tabanlı çıkış vardır.

## Test sonucu ve sınırlar

Son otomatik Play Mode testi: **PASS**. Testler sanal Input System mouse/keyboard üzerinden gerçek kontrol akışını çalıştırdı.

- 10 ardışık eylem → tam 10 tane, her eylemde bir tüketim. Başlangıçta görünürlük, doğru temas, hızlı ek tık ve çift Consume denemesi geçti.
- Hedef yok / arkada / uzakta / devre dışı / temas öncesi taşınmış / gereksinim seviyesi yüksek senaryoları geçti.
- Yemek sonrası WASD, koşma, karakter dönmesi, Space zıplaması ve wing_idle'a dönüş geçti.
- Yemek boyunca kamera konumundaki maksimum fark yaklaşık **0,000001** birim; dönme oluşmadı.
- Sağ tık ön/arka orbit, oyuncuyu döndürmeme, tekerlek yakınlaştırma/uzaklaştırma ve Edible'ı kamera çarpışmasından dışlama geçti.
- 500 ek tane (250 aktif statik, 250 pasif) varken 1000 yerel sorgu doğru hedefi buldu. Bu makinedeki son ölçüm, test reflection çağrıları dahil toplam **3,32 ms**. Bu bir mimari kontrolüdür, kapsamlı profiler veya hedef donanım performans garantisi değildir.
- Buğdaylarda Update/LateUpdate, sürekli sorgu, Animator veya Rigidbody bulunmaz. İki önceden ayrılmış sorgu tamponu 64 ve 256 collider tutar. Tek küçük hacme 256'dan fazla collider sıkıştırılırsa sonuç kümesi sınırlıdır; LastQuerySaturated bunu bildirir. Bu ilk sürüm böyle bir yoğun kümeyi hedeflemiyor.
- Kamera, tüm DayCycle scriptleri, saat/geçiş UI dosyaları ve gökyüzü shader'ı önce/sonra SHA-256 ile değişmemiş olarak doğrulandı. Player hareket ve yönlenme metotları metin olarak da aynı.
- Son kontrol: Console hata/uyarı yok. Geçici test nesneleri ve input cihazları temizlendi; test sahnesi kalıcı değiştirilmedi.

Görüntüler: `Captures/wheat-before-impact.png`, `Captures/wheat-at-impact.png` ve `Captures/wheat-gameplay.png`. Yakın plan çift, altın renk son rötuşundan önce çekildi; temas/yerleşim aynı.

İlerleme, XP, büyüme, dönüşüm, envanter, ses, VFX veya UI eklenmedi. Yenmiş yiyeceklerin kayıt dosyasına kalıcı yazılması da bu kapsamda yok; mevcut gün/kayıt sistemi değiştirilmedi. Sahneyi yeniden başlatınca test buğdayları geri gelir.

## Ham son Play Mode doğrulaması

```text
PASS
PASS No target: no action / no lock
PASS Behind and distant targets rejected
PASS Action 1: visible at input
PASS Closest forward target chosen
PASS Rapid extra click cannot replace target
PASS Action 1: one impact, clean exit, consumed=1
  impact=0,263s normalized=0,2641 ready=0,538s
PASS Consumed grain rejects duplicate Consume
PASS Action 2: visible at input
PASS Action 2: one impact, clean exit, consumed=2
  impact=0,268s normalized=0,2677 ready=0,535s
PASS Consumed grain rejects duplicate Consume
PASS Action 3: visible at input
PASS Action 3: one impact, clean exit, consumed=3
  impact=0,263s normalized=0,2644 ready=0,531s
PASS Consumed grain rejects duplicate Consume
PASS Action 4: visible at input
PASS Action 4: one impact, clean exit, consumed=4
  impact=0,264s normalized=0,2661 ready=0,528s
PASS Consumed grain rejects duplicate Consume
PASS Action 5: visible at input
PASS Action 5: one impact, clean exit, consumed=5
  impact=0,260s normalized=0,2630 ready=0,532s
PASS Consumed grain rejects duplicate Consume
PASS Action 6: visible at input
PASS Action 6: one impact, clean exit, consumed=6
  impact=0,263s normalized=0,2626 ready=0,536s
PASS Consumed grain rejects duplicate Consume
PASS Action 7: visible at input
PASS Action 7: one impact, clean exit, consumed=7
  impact=0,261s normalized=0,2626 ready=0,533s
PASS Consumed grain rejects duplicate Consume
PASS Action 8: visible at input
PASS Action 8: one impact, clean exit, consumed=8
  impact=0,263s normalized=0,2674 ready=0,533s
PASS Consumed grain rejects duplicate Consume
PASS Action 9: visible at input
PASS Action 9: one impact, clean exit, consumed=9
  impact=0,263s normalized=0,2641 ready=0,530s
PASS Consumed grain rejects duplicate Consume
PASS Action 10: visible at input
PASS Action 10: one impact, clean exit, consumed=10
  impact=0,267s normalized=0,2678 ready=0,538s
PASS Consumed grain rejects duplicate Consume
10-grain duration range=0,528..0,538
PASS Eating does not move/rotate camera; max shift=0,000001
PASS Disabled target safely finishes without consuming
PASS Target moved out of reach safely rejected at impact
PASS Future requirement hook rejects level 1; current chick is 0
PASS WASD movement restored after eating
PASS Run animation restored
PASS Space jump restored
PASS Jump wing layer returns to idle
PASS Character turning still responds after eating
PASS Mouse wheel zoom in still works
PASS Mouse wheel zoom out restores distance
PASS RMB orbit reaches opposite/front view
PASS RMB orbit does not rotate player
PASS RMB orbit returns to rear view
PASS Camera collision still excludes Edible layer
PASS Disabling player cancels pending action without eating
PASS 500-grain stress: 1000 local queries keep selecting nearby grain
1000 queries incl. reflection overhead=3,32ms
PASS Prefab has no Rigidbody/Animator; EdibleObject has no frame callbacks
```
