# Daha toleranslı yiyecek seçimi ve gaga temas desteği

## Sonuç ve kullanım

Buğdaya yaklaş, kabaca yönel, dur ve mevcut **sol tık** ile ye. Ön/sağ/sol küçük hizalama hataları artık tane görselinin kısa bir düzeltmesiyle karşılanır. Uzaktaki ya da arkadaki yiyecek çekilmez. Karaktere otomatik dönüş eklenmedi.

20 tanelik yürüme testinde, taneler oyuncunun gidiş çizgisinden sağa/sola 0,045 birim dağılmışken, her biri yaklaşık 0,12 birim önden yenebildi. Test gerçek Input System W/sol tık girişleriyle yapıldı; her tanede ışınlanma veya yana ince düzeltme kullanılmadı. Bu kontrollü bir oynanış testidir; uzun süreli insan UX değerlendirmesinin yerini tutmaz.

## Ayarlar ve ölçüm

| İstenen rapor bilgisi | Uygulama |
|---|---|
| Önceki algılama | Öne 0,09 kaydırılmış, yarıçapı 0,045 olan NonAlloc küre; ayrıca kökten 0,15 mesafe sınırı |
| Yeni algılama | Öne **0,115** kaydırılmış, yarıçapı **0,105** olan `OverlapSphereNonAlloc` küresi; Edible katmanı |
| Ölçülen civciv | Renderer yaklaşık **0,14990 × 0,21317 × 0,20278**; kafa kemiğine ağırlıklı mesh noktalarında kafa genişliği yaklaşık **0,07296** |
| Ön açı | İleri yönün iki yanında **65°** |
| Maksimum görsel düzeltme | **0,075** Unity birimi; ayrıca hedef baştan bu kısa düzeltmeyle ulaşılabilir olmalı |
| Temas desteği süresi | Darbenin hemen önündeki **0,10 saniye** |
| Dikey sınır | İlk %75'te başlangıç yer yüksekliği; son %25'te en fazla ±0,006 düzeltme |
| Yeme hızı | Onaylı **1,4×**, değiştirilmedi |
| Tüketim | Mevcut 21. kare / normalize 0,2625 `OnEatImpact()` olayı; değiştirilmedi |
| Collider | **Devre dışı bırakılmaz.** Kök ve mevcut trigger BoxCollider yerinde kalır; yalnızca Visual çocuğu hareket eder |
| Otomatik karakter dönüşü | **Yok** |
| Elle Inspector kurulumu | **Gerekmiyor**; sahne ve prefab bağlantıları kaydedildi |

Algılama yarıçapı yaklaşık 1,44 kafa genişliğidir. Ancak geniş sorgu tek başına yeme yetkisi vermez: ön açı ve maksimum 0,075 temas düzeltmesi de geçmelidir. Bu yüzden uzaktan yemek ya da taneyi uzun mesafe uçurmak mümkün değildir. Eski dar küreden daha toleranslı olan bu son düzeltme sınırı, bilerek algılama küresinden küçüktür.

## BeakEatPoint

Hiyerarşi:

`ChickPlayer/Chick/root/body/chest/neck/head/beak_top/BeakEatPoint`

Kemiğe göre localPosition: **(0,00000682; 0,00020851; -0,00009465)**. Küçük değerlerin nedeni kemik hiyerarşisinin yaklaşık 100 ölçeğidir. Noktanın localScale'i 0,01; rig veya mevcut kemik dönüşümleri değiştirilmedi.

Mevcut klip 21. karede örneklenerek nokta ayarlandı. Oyuncu köküne göre temas konumu yaklaşık **(-0,007; 0,011; 0,087)**. Bu örnek konum yalnızca başlamadan önce erişilebilirlik tahmini içindir. Gerçek görsel hedef her karede **animasyonla hareket eden BeakEatPoint** konumudur; kamera hedefi değildir.

## Seçim, zamanlama ve güvenlik

Hedef skoru:

`gagaya uzaklık² + 0,003 × (1 − ileri yön hizası) + 2 × yükseklik farkı²`

En düşük skorlu uygun hedef seçilir; eşit skorda instance ID ile tutarlı bağ çözülür. Physics'in döndürdüğü ilk collider doğrudan seçilmez. Başlangıçta hedef kilitlenir, eylem sırasında daha yakını belirse bile değişmez.

Mevcut klibin `OnEatImpact` olayı Awake sırasında okunur. Sürekli sayaçla tahmini tüketim yapmak yerine, Animator normalize zamanı ve gerçek playback çarpanı üzerinden temasa kalan süre hesaplanır. Son 0,10 saniyede SmoothStep ile yalnızca seçili Visual çocuğuna küçük düzeltme uygulanır. Darbe callback'i, LateUpdate'ten önce gelebileceği için son görsel düzeltmeyi de yapar; yakın temas doğrulanırsa Consume çağrılır. Beklenmeyen bir poz değişimi gagayı temas sınırından uzaklaştırırsa uzaktan tüketmek yerine tane korunur.

Mantıksal kök/collider oynamaz; collider kapatılıp açık unutulamaz. Başlangıç Visual localPosition saklanır. İptalde, başarısız hedefte ve tüketim sonrası havuzda yeniden kullanıma hazırlık için geri yüklenir. Hedef dışarıdan taşınır, döndürülür veya ölçeklenirse düzeltme bırakılır; dış sistemin kök konumu geri alınmaz. Tek eylem tek tane tüketir.

Gizmo'lar: sarı algılama küresi, camgöbeği ileri yön/açı yayı, yeşil erişilebilir temas düzeltme alanı, mor hareketli gaga noktası, kırmızı kilitli hedef/bağlantı çizgisi. Üretim UI veya grafik efekti eklenmedi.

## Testler

- Doğrudan temas: geçti; yaklaşık 0,007 görsel düzeltme.
- Önden rahat yakınlık (z=0,15): geçti; yaklaşık 0,063 düzeltme.
- Ön-sol / ön-sağ (x=±0,055, z=0,11): geçti.
- Temas sınırı (x=0,065, z=0,105): geçti; en büyük gözlenen düzeltme yaklaşık 0,074.
- Arkada (z=-0,12) ve fazla uzakta (z=0,28): eylem başlamadan reddedildi.
- Kilitli hedefe sonradan daha yakın tane ekleme, ek tıklama, iptal ve dışarıdan hedef taşıma: son izole kontrolde geçti.
- 20 dağınık tane: **20/20**, sadece ileri yürüyüp durma ve sol tık; yana hassas düzeltme yok.
- Dikey görsel hareket testlerde yaklaşık **0,001** veya altında kaldı; tüketim anındaki nokta hatası normal testlerde sıfıra yakındı.
- Önceki 10-tane yeme, çift tüketim, WASD/koşma/dönüş/zıplama, wing_idle dönüşü, RMB ön/arka orbit, tekerlek zoom ve Edible kamera katmanı testleri yeniden **PASS**.
- 500 tane ile 1000 yerel sorgu testi: **PASS**, test reflection çağrıları dahil son toplam yaklaşık **5,02 ms**. Bu kapsamlı profiler garantisi değildir.
- Arka, yan ve ön/üç çeyrek açılardan yakın temas ekran görüntüleri incelendi. Arka görüşte gaga/tane doğal olarak gövdenin arkasında kalabilir; yan ve üç çeyrek görüntüler bağlantıyı gösterir.

İlk karma testte hedef kilidi ve onu izleyen iptal kontrolünde iki başarısız sonuç görüldü. Sonraki testte senaryoların hedef temizliği ayrıldı; hem yeni 20-tane paketi hem eski 10-tane regresyon paketi geçti. İlk tekil hedef-kilidi başarısızlığının kök nedeni kesin olarak saptanmadı; tekrarlarsa özellikle editör kare takılmaları sırasındaki temas doğrulaması incelenmeli. Bu not, nihai başarılı testleri uzun süreli kusursuzluk garantisi gibi sunmamak içindir.

## Değişen dosyalar ve korunan sistemler

Değiştirilen runtime kodları yalnızca:

- `Assets/Scripts/Eating/ChickEatingController.cs`
- `Assets/Scripts/Eating/EdibleObject.cs` — yalnızca hareket ettirilebilir görsel referansı

Yeni araç/test:

- `Assets/Editor/Eating/EdibleTargetingSetup.cs`
- `Assets/Tests/Eating/EdibleTargetingPlayVerification.cs` — editor-only, sahnede kalıcı değil

Sahneye BeakEatPoint ve ayar referansları; WheatGrain_Test prefabına Visual referansı; test alanına beş ek ön/yan/sınır/arka/uzak tane eklendi (toplam 20). Mevcut 15 tanenin yerleşimi korundu.

**ChickPlayerController, kamera scripti, Animator Controller, onaylı yeme klibi, DayCycle scriptleri, saat/geçiş UI ve sky shader SHA-256 karşılaştırmasında değişmedi.** Yeme giriş tuşu, hız, hareket, yönlenme, zıplama ve kamera değerlerine dokunulmadı. Son Console kontrolünde hata yok. İlerleme, ses, VFX, yeni yiyecek türü veya UI eklenmedi.

Buğdaylara Update/LateUpdate eklenmedi. 64/256 collider'lık mevcut NonAlloc tamponları korundu. Sadece oyuncudaki mevcut LateUpdate içinde aktif hedef görseli işlenir. Görseli ileride değiştirirken `Contact Visual` referansı kökten ayrı bir çocukta kalmalı; mantıksal bite noktası hareket etmeyen kök/anchor olmalıdır.

Görüntüler: `Captures/target-assist-start.png`, `target-assist-three-quarter.png`, `target-assist-side.png`, `target-assist-rear.png`.

## Son hedefleme testinin ham sonucu

```text
PASS
PASS Direct: initiation, food retained at input
PASS Direct: expected best target
PASS Direct: consumed at event and recovered
PASS Direct: late bounded assist, shift=0,0071 lift=0,0009
PASS Direct: impact beak distance=0,00000
PASS Direct: static root / visual reset for reuse
PASS Direct: no camera auto-rotation
PASS Forward comfortable: initiation, food retained at input
PASS Forward comfortable: expected best target
PASS Forward comfortable: consumed at event and recovered
PASS Forward comfortable: late bounded assist, shift=0,0631 lift=0,0009
PASS Forward comfortable: impact beak distance=0,00000
PASS Forward comfortable: static root / visual reset for reuse
PASS Forward comfortable: no camera auto-rotation
PASS Front-left: initiation, food retained at input
PASS Front-left: expected best target
PASS Front-left: consumed at event and recovered
PASS Front-left: late bounded assist, shift=0,0530 lift=0,0009
PASS Front-left: impact beak distance=0,00000
PASS Front-left: static root / visual reset for reuse
PASS Front-left: no camera auto-rotation
PASS Front-right: initiation, food retained at input
PASS Front-right: expected best target
PASS Front-right: consumed at event and recovered
PASS Front-right: late bounded assist, shift=0,0661 lift=0,0009
PASS Front-right: impact beak distance=0,00000
PASS Front-right: static root / visual reset for reuse
PASS Front-right: no camera auto-rotation
PASS Contact edge: initiation, food retained at input
PASS Contact edge: expected best target
PASS Contact edge: consumed at event and recovered
PASS Contact edge: late bounded assist, shift=0,0742 lift=0,0010
PASS Contact edge: impact beak distance=0,00000
PASS Contact edge: static root / visual reset for reuse
PASS Contact edge: no camera auto-rotation
PASS Behind: initiation, food retained at input
PASS Too far: initiation, food retained at input
PASS Target remains locked despite closer newcomer and extra click
PASS One input consumes only locked target: chosen=True newcomer=False impact=True error=9,313226E-10 state=-601574123
PASS Assist actually started before cancellation test
PASS Cancel restores visual and preserves collider
PASS External move aborts assist without undoing external root move
PASS Walking scatter 1: initiation, food retained at input
PASS Walking scatter 1: expected best target
PASS Walking scatter 1: consumed at event and recovered
PASS Walking scatter 1: late bounded assist, shift=0,0502 lift=0,0009
PASS Walking scatter 1: impact beak distance=0,00000
PASS Walking scatter 1: static root / visual reset for reuse
PASS Walking scatter 1: no camera auto-rotation
PASS Walking scatter 2: initiation, food retained at input
PASS Walking scatter 2: expected best target
PASS Walking scatter 2: consumed at event and recovered
PASS Walking scatter 2: late bounded assist, shift=0,0645 lift=0,0009
PASS Walking scatter 2: impact beak distance=0,00000
PASS Walking scatter 2: static root / visual reset for reuse
PASS Walking scatter 2: no camera auto-rotation
PASS Walking scatter 3: initiation, food retained at input
PASS Walking scatter 3: expected best target
PASS Walking scatter 3: consumed at event and recovered
PASS Walking scatter 3: late bounded assist, shift=0,0526 lift=0,0010
PASS Walking scatter 3: impact beak distance=0,00000
PASS Walking scatter 3: static root / visual reset for reuse
PASS Walking scatter 3: no camera auto-rotation
PASS Walking scatter 4: initiation, food retained at input
PASS Walking scatter 4: expected best target
PASS Walking scatter 4: consumed at event and recovered
PASS Walking scatter 4: late bounded assist, shift=0,0648 lift=0,0010
PASS Walking scatter 4: impact beak distance=0,00000
PASS Walking scatter 4: static root / visual reset for reuse
PASS Walking scatter 4: no camera auto-rotation
PASS Walking scatter 5: initiation, food retained at input
PASS Walking scatter 5: expected best target
PASS Walking scatter 5: consumed at event and recovered
PASS Walking scatter 5: late bounded assist, shift=0,0538 lift=0,0009
PASS Walking scatter 5: impact beak distance=0,00000
PASS Walking scatter 5: static root / visual reset for reuse
PASS Walking scatter 5: no camera auto-rotation
PASS Walking scatter 6: initiation, food retained at input
PASS Walking scatter 6: expected best target
PASS Walking scatter 6: consumed at event and recovered
PASS Walking scatter 6: late bounded assist, shift=0,0625 lift=0,0010
PASS Walking scatter 6: impact beak distance=0,00000
PASS Walking scatter 6: static root / visual reset for reuse
PASS Walking scatter 6: no camera auto-rotation
PASS Walking scatter 7: initiation, food retained at input
PASS Walking scatter 7: expected best target
PASS Walking scatter 7: consumed at event and recovered
PASS Walking scatter 7: late bounded assist, shift=0,0509 lift=0,0009
PASS Walking scatter 7: impact beak distance=0,00000
PASS Walking scatter 7: static root / visual reset for reuse
PASS Walking scatter 7: no camera auto-rotation
PASS Walking scatter 8: initiation, food retained at input
PASS Walking scatter 8: expected best target
PASS Walking scatter 8: consumed at event and recovered
PASS Walking scatter 8: late bounded assist, shift=0,0623 lift=0,0008
PASS Walking scatter 8: impact beak distance=0,00000
PASS Walking scatter 8: static root / visual reset for reuse
PASS Walking scatter 8: no camera auto-rotation
PASS Walking scatter 9: initiation, food retained at input
PASS Walking scatter 9: expected best target
PASS Walking scatter 9: consumed at event and recovered
PASS Walking scatter 9: late bounded assist, shift=0,0534 lift=0,0010
PASS Walking scatter 9: impact beak distance=0,00000
PASS Walking scatter 9: static root / visual reset for reuse
PASS Walking scatter 9: no camera auto-rotation
PASS Walking scatter 10: initiation, food retained at input
PASS Walking scatter 10: expected best target
PASS Walking scatter 10: consumed at event and recovered
PASS Walking scatter 10: late bounded assist, shift=0,0626 lift=0,0010
PASS Walking scatter 10: impact beak distance=0,00000
PASS Walking scatter 10: static root / visual reset for reuse
PASS Walking scatter 10: no camera auto-rotation
PASS Walking scatter 11: initiation, food retained at input
PASS Walking scatter 11: expected best target
PASS Walking scatter 11: consumed at event and recovered
PASS Walking scatter 11: late bounded assist, shift=0,0515 lift=0,0009
PASS Walking scatter 11: impact beak distance=0,00000
PASS Walking scatter 11: static root / visual reset for reuse
PASS Walking scatter 11: no camera auto-rotation
PASS Walking scatter 12: initiation, food retained at input
PASS Walking scatter 12: expected best target
PASS Walking scatter 12: consumed at event and recovered
PASS Walking scatter 12: late bounded assist, shift=0,0655 lift=0,0010
PASS Walking scatter 12: impact beak distance=0,00000
PASS Walking scatter 12: static root / visual reset for reuse
PASS Walking scatter 12: no camera auto-rotation
PASS Walking scatter 13: initiation, food retained at input
PASS Walking scatter 13: expected best target
PASS Walking scatter 13: consumed at event and recovered
PASS Walking scatter 13: late bounded assist, shift=0,0521 lift=0,0010
PASS Walking scatter 13: impact beak distance=0,00000
PASS Walking scatter 13: static root / visual reset for reuse
PASS Walking scatter 13: no camera auto-rotation
PASS Walking scatter 14: initiation, food retained at input
PASS Walking scatter 14: expected best target
PASS Walking scatter 14: consumed at event and recovered
PASS Walking scatter 14: late bounded assist, shift=0,0651 lift=0,0010
PASS Walking scatter 14: impact beak distance=0,00000
PASS Walking scatter 14: static root / visual reset for reuse
PASS Walking scatter 14: no camera auto-rotation
PASS Walking scatter 15: initiation, food retained at input
PASS Walking scatter 15: expected best target
PASS Walking scatter 15: consumed at event and recovered
PASS Walking scatter 15: late bounded assist, shift=0,0552 lift=0,0010
PASS Walking scatter 15: impact beak distance=0,00000
PASS Walking scatter 15: static root / visual reset for reuse
PASS Walking scatter 15: no camera auto-rotation
PASS Walking scatter 16: initiation, food retained at input
PASS Walking scatter 16: expected best target
PASS Walking scatter 16: consumed at event and recovered
PASS Walking scatter 16: late bounded assist, shift=0,0629 lift=0,0009
PASS Walking scatter 16: impact beak distance=0,00000
PASS Walking scatter 16: static root / visual reset for reuse
PASS Walking scatter 16: no camera auto-rotation
PASS Walking scatter 17: initiation, food retained at input
PASS Walking scatter 17: expected best target
PASS Walking scatter 17: consumed at event and recovered
PASS Walking scatter 17: late bounded assist, shift=0,0539 lift=0,0009
PASS Walking scatter 17: impact beak distance=0,00000
PASS Walking scatter 17: static root / visual reset for reuse
PASS Walking scatter 17: no camera auto-rotation
PASS Walking scatter 18: initiation, food retained at input
PASS Walking scatter 18: expected best target
PASS Walking scatter 18: consumed at event and recovered
PASS Walking scatter 18: late bounded assist, shift=0,0628 lift=0,0009
PASS Walking scatter 18: impact beak distance=0,00000
PASS Walking scatter 18: static root / visual reset for reuse
PASS Walking scatter 18: no camera auto-rotation
PASS Walking scatter 19: initiation, food retained at input
PASS Walking scatter 19: expected best target
PASS Walking scatter 19: consumed at event and recovered
PASS Walking scatter 19: late bounded assist, shift=0,0557 lift=0,0009
PASS Walking scatter 19: impact beak distance=0,00000
PASS Walking scatter 19: static root / visual reset for reuse
PASS Walking scatter 19: no camera auto-rotation
PASS Walking scatter 20: initiation, food retained at input
PASS Walking scatter 20: expected best target
PASS Walking scatter 20: consumed at event and recovered
PASS Walking scatter 20: late bounded assist, shift=0,0646 lift=0,0008
PASS Walking scatter 20: impact beak distance=0,00000
PASS Walking scatter 20: static root / visual reset for reuse
PASS Walking scatter 20: no camera auto-rotation
PASS 20 scattered grains eaten with forward walking only, no tiny side adjustments: 20/20
PASS Scatter test needed no sideways repositioning
PASS Approved 1.4x eat / 1x global speed preserved
```

## Mevcut yeme/hareket/kamera regresyonunun ham sonucu

```text
PASS
PASS No target: no action / no lock
PASS Behind and distant targets rejected
PASS Action 1: visible at input
PASS Closest forward target chosen
PASS Rapid extra click cannot replace target
PASS Action 1: one impact, clean exit, consumed=1
  impact=0,277s normalized=0,2682 ready=0,562s
PASS Consumed grain rejects duplicate Consume
PASS Action 2: visible at input
PASS Action 2: one impact, clean exit, consumed=2
  impact=0,281s normalized=0,2653 ready=0,559s
PASS Consumed grain rejects duplicate Consume
PASS Action 3: visible at input
PASS Action 3: one impact, clean exit, consumed=3
  impact=0,274s normalized=0,2674 ready=0,546s
PASS Consumed grain rejects duplicate Consume
PASS Action 4: visible at input
PASS Action 4: one impact, clean exit, consumed=4
  impact=0,269s normalized=0,2625 ready=0,533s
PASS Consumed grain rejects duplicate Consume
PASS Action 5: visible at input
PASS Action 5: one impact, clean exit, consumed=5
  impact=0,271s normalized=0,2666 ready=0,535s
PASS Consumed grain rejects duplicate Consume
PASS Action 6: visible at input
PASS Action 6: one impact, clean exit, consumed=6
  impact=0,274s normalized=0,2701 ready=0,537s
PASS Consumed grain rejects duplicate Consume
PASS Action 7: visible at input
PASS Action 7: one impact, clean exit, consumed=7
  impact=0,278s normalized=0,2700 ready=0,551s
PASS Consumed grain rejects duplicate Consume
PASS Action 8: visible at input
PASS Action 8: one impact, clean exit, consumed=8
  impact=0,284s normalized=0,2737 ready=0,543s
PASS Consumed grain rejects duplicate Consume
PASS Action 9: visible at input
PASS Action 9: one impact, clean exit, consumed=9
  impact=0,271s normalized=0,2681 ready=0,544s
PASS Consumed grain rejects duplicate Consume
PASS Action 10: visible at input
PASS Action 10: one impact, clean exit, consumed=10
  impact=0,269s normalized=0,2638 ready=0,542s
PASS Consumed grain rejects duplicate Consume
10-grain duration range=0,533..0,562
PASS Eating does not move/rotate camera; max shift=0,000000
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
1000 queries incl. reflection overhead=5,02ms
PASS Prefab has no Rigidbody/Animator; EdibleObject has no frame callbacks
```
