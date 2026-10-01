# Parçaları ayrı yenebilen yiyecekler

Project > Assets > Assets > EdibleFoods klasöründeki mavi prefabları sahneye sürükleyin. Her prefab gövdeyi ve tüm child parçalarını birlikte getirir; ayrı parçaları tek tek eklemek gerekmez. İçe aktarılan orijinal FBX ve materyaller korunmuştur.

Her prefabın yapısı:

- `Body`: Çekirdek/süsleme içermeyen kalıcı gövde ve gövdeye uyan collider.
- `EdiblePieces/Piece_001`, `Piece_002`, ...: Birbirinden bağımsız yenebilir parçalar.
  - `Visual`: Orijinal yüzey, UV, normal ve materyalle oluşturulmuş parça; pivotu parçanın merkezindedir.
  - `BitePoint`: Yeme hedefi.
  - Her Piece üzerinde EdibleObject ve Edible katmanında trigger BoxCollider vardır.

Bir parçayı kapatmak/yemek yalnızca o parçayı gizler. Tüm EdiblePieces grubunu kapatmak çekirdeksiz gövdeyi gösterir. Asıl parçaların geometrisi Body içinde tekrar bulunmaz.

Watermelon_Slice_1, Watermelon_Slice_2 ve Tomato_Piece sahnede dik durur. Ön ve arka yüzün çekirdekleri ayrı ayrı hedeflenir; bir taraftan yemek diğer taraftaki çekirdekleri gizlemez. Pumpkin_Quarter'ın yan yüz çekirdekleri ise civciv yerdeyken veya kabağın üstündeyken erişilebilir.

| Prefab | Parça sayısı |
| --- | ---: |
| Watermelon_002 | 24 |
| Watermelon_003 | 12 |
| Watermelon_Slice_2 | 10 |
| Tomato_Piece | 16 |
| Pumpkin_Quarter | 8 |
| Cookie_1 | 7 |
| Apple_Red_Half_2 | 2 |
| Watermelon_Slice_1 | 22 |
| Cookie_2 | 8 |
| Croissant | 11 |
| Donut | 19 |
| Bread_1 | 30 |

Toplam: 169 parça. Domatesin iç dolgusu, kruvasanın hamur katmanları ve donut kaplaması Body içinde kalır.

## Yeme ve kayıt

Mevcut ChickEatingController erişebildiği parçaları hedefler. Mevcut mesafe/yükseklik sınırlamaları geçerlidir; yüksek parçalar için civcivin uygun yüksekliğe ulaşması gerekir.

EdibleFoodSourceIdentity editörde her sahne örneğine ayrı kayıt kimliği atar. Prefab sürükleme ve sahne örneği çoğaltma desteklenir. Sahneyi kaydedin. EdiblePieces altındaki Piece adları kayıt anahtarına dahildir; yayımlanmış kayıtlarla uyumluluk gerekiyorsa bu adları değiştirmeyin. Oyun çalışırken dinamik oluşturulan nesnelere kalıcı dünya kimliği verme bu editör aracının kapsamı dışındadır.

## İleride tüm modeli yeme altyapısı

Her prefabın kökünde kapalı bir EdibleObject bulunur. EdibleFoodSource üzerindeki Whole Food Eating Enabled varsayılan olarak kapalıdır. Child parçalar bittiğinde bu kilit kendiliğinden açılmaz. Bugünkü civciv sadece child parçaları yer.

Gelecekte büyüme sistemi `SetWholeFoodEatingEnabled(true)` ile izin verebilir. Büyük yiyecek etkileşimi, animasyonun temas anında `TryConsumeWholeFood(eatLevel)` çağırır. Hem açık izin hem yeterli seviye gerekir. Başlangıç eşik değeri 1'dir; ileride oyun dengesine göre değiştirilebilir. Bu çağrı gövdeyi ve kalan tüm parçaları birlikte kapatır; kökün yenme durumu mevcut consumedEdibleIds kaydıyla saklanır. Büyük model için hedef seçimi, erişim ve animasyon bağlama ileride eklenecektir; bugün etkin değildir.

## Doğrulama

12 prefabda toplam üçgen sayısı, tepe konumları, normaller ve UV koordinatları kaynak modellerle karşılaştırıldı. Geçici kopyalarda bağımsız tüketme, tüketilen kimliklerin geri yüklenmesi, tüm parçalar bitince gövdenin kalması ve kopyaların farklı kayıt kimlikleri kullanması doğrulandı. Parçalı ve boş gövdeler görsel olarak incelendi. Bu kontroller tam oyun içi sol tık/animasyon veya Windows build testi değildir.

Hazır prefablar ikişer kez sahneye eklenerek de kontrol edildi. 169 child parça, erişim mesafesine getirildiğinde mevcut ChickEatingController hedef doğrulamasından geçti. Ana model kilidi, ileride açık izin + seviye kontrolüyle tüm modelin tüketilmesi ve hem child hem ana modelin kayıt durumunun kopyaları etkilemeden geri yüklenmesi doğrulandı. Deneme nesneleri temizlendi; sahnedeki asıl modeller kilitli ve tüm parçaları görünür bırakıldı.
