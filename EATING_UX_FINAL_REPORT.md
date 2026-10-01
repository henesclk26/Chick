# Yeme girişi, hedefleme ve küçük kamera mesafesi rötuşu

## Kullanım ve sonuç

**W / WASD basılıyken sol tıkla:** uygun hedef varsa civciv kısa gagalama için durur, temas anında tek tane yer ve tuş hâlâ basılıysa kendiliğinden yürümeye/koşmaya devam eder. W'yi bırakıp yeniden basmak gerekmiyor.

Kesintisiz **W+Shift + 20 tıklama → 20/20 buğday** testi geçti. Çapraz W+D, geniş yan hedefler, geç toparlanma tıklaması, orbit, zoom ve zıplama kontrolleri de geçti. Bu kontrollü bir oynanış testidir; uzun süreli insan kullanımının yerine geçen bir UX garantisi değildir.

## 1–5. Giriş önceliği ve basılı hareket

1. Önceki engel `canEat` içindeki **`!hasMovementInput`** koşuluydu. W/A/S/D basılıyken false olduğu için hedef aramasına bile girilmiyordu.
2. Yalnızca bu hareket koşulu çıkarıldı. Yeni koşul: `eatPressed && grounded && !actionLocked && !jumping`. Yere basma, zıplama ve mevcut eylem kilitleri korundu.
3. `ReadEatRequest()` hareket uygulanmadan önce çağrılır. Uygun hedef varsa TryBeginEat hedefi hemen kilitler; mevcut kısa eylem kilidi o karede yatay hareket katkısını durdurur. Öncelik **yeme > normal yürüyüş/koşu**; jump mekaniği yeniden tasarlanmadı.
4. Klavyenin basılı durumu silinmez. Mevcut hareket okuması her kare devam eder; yalnızca eylem sırasında uygulanacak yer değiştirme sıfırlanır. Eylem kilidi bitince aynı basılı tuşlar ilk uygun karede yeniden hareket üretir.
5. **0,12 saniyelik tek yuvalı tampon** eklendi. Yalnızca mevcut toparlanma geçişinin son 0,12 saniyesindeki tık saklanır. Hazır olunca yeni hedef bir kez aranır. Erken gagalama tıklamaları sıra oluşturmaz; sol tıkı basılı tutmak otomatik yemeye dönüşmez. İptal/devre dışı kalma tamponu temizler; hedef yoksa istek harcanır, boşta tam animasyon başlamaz.

## 6–14. Hedef ve temas ayarları

| Ayar | Önce | Şimdi |
|---|---|---|
| Algılama şekli | NonAlloc küre | Aynı yerel NonAlloc küre |
| Temel algılama yarıçapı | 0,105 | **0,12** |
| Wheat InteractionAssistMultiplier | Yok | **1,35** (metadata sınırı 0,5–1,5; başka yiyecekler için varsayılan 1) |
| Buğdayın etkin yarıçapı | 0,105 | **0,162** |
| Sorgu üst sınırı | 0,105 | Metadata tavanını kapsayan **0,18**, ardından hedefe özel dar kontrol |
| İleri offset | 0,115 | **0,09** |
| Yükseklik offset'i | 0,015 | Aynı |
| Ön açı | ±65° | **±80°** |
| Başlamak için gagaya hizalanma | Örnek temas noktasına en fazla 0,075 | **Kaldırıldı**; oyun mesafesi ve ön bölge kontrol ediliyor |
| Kökten mutlak yatay menzil | Temas sınırıyla dolaylı sınırlı | **0,25**; birkaç gövde boyu uzaktan yeme yok |
| Yükseklik farkı sınırı | Küreye bağlı | Temas zemini çevresinde **0,04** |
| Temel görsel düzeltme sınırı | 0,075 | **0,13** |
| Buğdayın görsel düzeltme sınırı | 0,075 | **0,1755** = 0,13 × 1,35 |
| Temas desteği süresi | 0,10 s | **0,14 s**, mevcut darbeye senkron |
| Otomatik görsel dönüş | Yok | **0°; gerekmedi** |
| Mikro oyuncu yaklaşması | Yok | **Yok; gerekmedi** |

Renderer ölçüsü yaklaşık **0,14990 × 0,21317 × 0,20278**; gövde uzunluğu yaklaşık 0,203. Yeni buğday yakalama yarıçapı yaklaşık 0,8 gövde uzunluğudur. Sadece sorgu değil, gerçek eski 0,075 gaga-hizalama kapısının kaldırılması da rahatlığı artırır. Arkadaki hedefler hâlâ reddedilir.

Skor:

`etkileşim merkezine uzaklık² + 0,2 × gagaya uzaklık² + 0,0015 × (1 − ileri hizası) + 2 × yükseklik farkı²`

Mesafe ağırlıklıdır; en düşük skorlu uygun hedef seçilir, physics listesinin ilk elemanı kullanılmaz. Seçim başlangıçta kilitlenir; gagalama sırasında yeni hedef aranıp değiştirilmez.

Mevcut `BeakEatPoint` ve `OnEatImpact` korundu. Tane görseli, başın alçalması sırasında son 0,14 saniyede SmoothStep ile yere yakın kayar. İlk %75'te başlangıç yüksekliğinde kalır; son %25'te ±0,006 dikey düzeltme sınırı korunur. Tüketim yalnızca mevcut **21. kare / normalize 0,2625** Animation Event'inde yapılır. Temas doğrulaması korunur. Collider trigger olarak kökte sabittir; sadece seçilen Visual çocuğu kayar. İptalde görsel geri döner.

## 15–17. Oynanış testleri

- **W+Shift basılıyken sol tık:** hedef hemen kabul edildi, yatay hareket kilitte sabit kaldı, tane temas olayında tüketildi, W bırakılmadan koşu devam etti.
- **W+D basılıyken sol tık:** aynı davranış doğrulandı.
- **Yan hedefler:** x=±0,13, z=0,065 konumlarındaki taneler yenebildi; görsel temas doğrulandı.
- **20-tanelik rota:** taneler x=±0,09, z boyunca 0,30 aralıkla dağıtıldı. W+Shift hiç bırakılmadı; 20 ayrı tıklama 20 ayrı tane tüketti. Yana ince ayar veya her taneye ışınlanma kullanılmadı.
- **Geç toparlanma tıklaması:** bir sonraki hedef için tek istek saklandı ve W basılıyken işlendi.
- **Sol tıkı 1,1 saniye basılı tutma:** yalnızca bir tane tüketildi; istenmeyen otomatik zincir oluşmadı.
- **Arkada / uzakta:** reddedildi; boş yeme ve uzaktan çekme yok.
- **Zıplama, inişte wing_idle, RMB bağımsız orbit:** geçti.

İlk test rotası zeminin dışına uzanıyordu ve editörün fiziksel girişleri sanal cihazların `current` seçimini değiştirebiliyordu. Test rotası zeminde tutuldu, yalnızca test koşucusunda sanal cihaz seçimi sabitlendi. Tek çentik testinde sentetik ham 120 değeri de projenin normalize `1` çentik birimine düzeltildi. Bu test düzeltmeleri için oynanışın hareket/zoom mantığı değiştirilmedi. Son tüm test paketi **PASS**.

## 18–21. Kamera ve kurulum

- Önceki **Default / Maximum: 0,78 / 0,78**.
- Yeni **Default / Maximum: 0,83 / 0,83**; artış yaklaşık **%6,4**.
- Minimum **0,45**; FOV **58**; pitch, yükseklik, CameraTarget, orbit hassasiyeti/yumuşatması, RMB, framing ve çarpışma mantığı değişmedi.
- Kamera scriptinde yalnızca iki varsayılan mesafe sabiti değişti; sahnede aynı iki serialized değer güncellendi. Kaydırma koduna dokunulmadı.
- Tekerlek testi: **0,83 → 0,79**, minimumda **0,45**, maksimumda **0,83**; tümü geçti.
- Yeni oluşturulan kamera başlangıcı **0,83** olarak ayrıca doğrulandı.
- **Mevcut kayıt dosyasındaki oyuncu zoom tercihi korunur.** Test kaydında 0,78 saklıydı ve yüklemede aynen geri geldi. Yeni varsayılanı görmek için kayıtlı oturumda tekerlekle dışarı kaydırılabilir; yeni maksimum 0,83'tür. Kayıt dosyası veya kayıt sistemi değiştirilmedi.
- Elle Inspector bağlantısı gerekmiyor. Ayarlar sahneye ve WheatGrain_Test prefabına kaydedildi. Play Mode'dan çıkıldı; geçici test nesneleri kalıcı sahnede yok.

Görüntüler: `Captures/eating-ux-camera-078.png`, `Captures/eating-ux-camera-083.png` (aynı sabit poz), `Captures/eating-ux-side-contact.png`. Karşılaştırmada çevre görünümü hafif genişledi. Geniş sağ hedefin temas anı da yakın planla kontrol edildi; son temas hata ölçümü yaklaşık sıfırdı.

## Değişiklik kapsamı ve performans

Değişen runtime dosyaları:

- `Assets/Scripts/ChickPlayerController.cs`: yalnızca giriş okuma ve hareketten bağımsız yeme aktivasyonu.
- `Assets/Scripts/Eating/ChickEatingController.cs`: yakalama, skor, metadata çarpanı, kısa buffer ve temas desteği mesafesi/zamanı.
- `Assets/Scripts/Eating/EdibleObject.cs`: yalnızca yeni hafif assist metadata'sı.
- `Assets/Scripts/FreeOrbitThirdPersonCamera.cs`: yalnızca defaultDistance / maximumDistance başlangıç değerleri.

`Assets/Editor/Eating/EdibleTargetingSetup.cs` yeni değerleri koruyacak şekilde güncellendi. Yeni `Assets/Tests/Eating/EatingUxPlayVerification.cs` opt-in editor-only test koşucusudur; build'e dahil edilmez.

DayCycle scriptleri, saat/geçiş UI, sky shader, onaylı yeme klibi ve Animator Controller SHA-256 karşılaştırmasıyla korundu. Player kodunun kalan bölümü metin karşılaştırmasında aynı; kamera kodunun kalan bölümü de aynı. Yeme hızı **1,4×**, Animator genel hızı **1×**.

Edible üzerinde Update/LateUpdate yok. Oyuncu taraflı, 64/256 önceden ayrılmış collider tamponlu NonAlloc yerel sorgu korunuyor. Sadece kilitli tanenin görseli işleniyor. Binlerce nesneye uygun pasif mimari korundu; bu görev kapsamında yeni kapsamlı profiler testi yapılmadı. 256'dan fazla collider'ın aynı küçük sorgu hacmine yığılması hâlinde mevcut LastQuerySaturated sınırlaması geçerlidir.

İlerleme, ses, VFX, yeni yiyecek veya UI eklenmedi. Son Console kontrolünde hata/uyarı yok.

## Son test çıktısı

```text
PASS
PASS Camera default/max .83
PASS Camera minimum .45 / FOV 58 preserved
Startup user zoom=0,78 (saved user preference may override fresh default)
PASS Run W+Shift held: LMB accepted immediately while moving
PASS Run W+Shift held: consumed, no stuck state
PASS Run W+Shift held: short action brake, drift=0,0000
PASS Run W+Shift held: held movement resumed without another key press
PASS Diagonal W+D held: LMB accepted immediately while moving
PASS Diagonal W+D held: consumed, no stuck state
PASS Diagonal W+D held: short action brake, drift=0,0000
PASS Diagonal W+D held: held movement resumed without another key press
PASS Wide side-front -0,13 reaches beak at event
PASS Wide side-front 0,13 reaches beak at event
PASS Behind and distant food rejected
PASS One late recovery click buffers next target while W held
PASS Held LMB never becomes auto-eat or a queued combo
PASS Continuous running grain 1: LMB accepted immediately while moving
PASS Continuous running grain 1: consumed, no stuck state
PASS Continuous running grain 1: short action brake, drift=0,0000
PASS Continuous running grain 2: LMB accepted immediately while moving
PASS Continuous running grain 2: consumed, no stuck state
PASS Continuous running grain 2: short action brake, drift=0,0000
PASS Continuous running grain 3: LMB accepted immediately while moving
PASS Continuous running grain 3: consumed, no stuck state
PASS Continuous running grain 3: short action brake, drift=0,0000
PASS Continuous running grain 4: LMB accepted immediately while moving
PASS Continuous running grain 4: consumed, no stuck state
PASS Continuous running grain 4: short action brake, drift=0,0000
PASS Continuous running grain 5: LMB accepted immediately while moving
PASS Continuous running grain 5: consumed, no stuck state
PASS Continuous running grain 5: short action brake, drift=0,0000
PASS Continuous running grain 6: LMB accepted immediately while moving
PASS Continuous running grain 6: consumed, no stuck state
PASS Continuous running grain 6: short action brake, drift=0,0000
PASS Continuous running grain 7: LMB accepted immediately while moving
PASS Continuous running grain 7: consumed, no stuck state
PASS Continuous running grain 7: short action brake, drift=0,0000
PASS Continuous running grain 8: LMB accepted immediately while moving
PASS Continuous running grain 8: consumed, no stuck state
PASS Continuous running grain 8: short action brake, drift=0,0000
PASS Continuous running grain 9: LMB accepted immediately while moving
PASS Continuous running grain 9: consumed, no stuck state
PASS Continuous running grain 9: short action brake, drift=0,0000
PASS Continuous running grain 10: LMB accepted immediately while moving
PASS Continuous running grain 10: consumed, no stuck state
PASS Continuous running grain 10: short action brake, drift=0,0000
PASS Continuous running grain 11: LMB accepted immediately while moving
PASS Continuous running grain 11: consumed, no stuck state
PASS Continuous running grain 11: short action brake, drift=0,0000
PASS Continuous running grain 12: LMB accepted immediately while moving
PASS Continuous running grain 12: consumed, no stuck state
PASS Continuous running grain 12: short action brake, drift=0,0000
PASS Continuous running grain 13: LMB accepted immediately while moving
PASS Continuous running grain 13: consumed, no stuck state
PASS Continuous running grain 13: short action brake, drift=0,0000
PASS Continuous running grain 14: LMB accepted immediately while moving
PASS Continuous running grain 14: consumed, no stuck state
PASS Continuous running grain 14: short action brake, drift=0,0000
PASS Continuous running grain 15: LMB accepted immediately while moving
PASS Continuous running grain 15: consumed, no stuck state
PASS Continuous running grain 15: short action brake, drift=0,0000
PASS Continuous running grain 16: LMB accepted immediately while moving
PASS Continuous running grain 16: consumed, no stuck state
PASS Continuous running grain 16: short action brake, drift=0,0000
PASS Continuous running grain 17: LMB accepted immediately while moving
PASS Continuous running grain 17: consumed, no stuck state
PASS Continuous running grain 17: short action brake, drift=0,0000
PASS Continuous running grain 18: LMB accepted immediately while moving
PASS Continuous running grain 18: consumed, no stuck state
PASS Continuous running grain 18: short action brake, drift=0,0000
PASS Continuous running grain 19: LMB accepted immediately while moving
PASS Continuous running grain 19: consumed, no stuck state
PASS Continuous running grain 19: short action brake, drift=0,0000
PASS Continuous running grain 20: LMB accepted immediately while moving
PASS Continuous running grain 20: consumed, no stuck state
PASS Continuous running grain 20: short action brake, drift=0,0000
PASS Continuous W+Shift, 20 clicks, 20 grains, never released movement: 20/20
PASS No manual lateral/beak alignment during patch test
PASS Wheel one notch .83 to .79, actual=0,79
PASS Wheel clamps at unchanged minimum .45
PASS Wheel clamps at new maximum .83
PASS RMB orbit still independent of character
PASS RMB returns to rear view unchanged
PASS Jump still works after eating
PASS Landing restores wing idle
PASS Eating speed unchanged
```
