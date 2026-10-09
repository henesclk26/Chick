using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public sealed class FarmSaveData
{
    public int version = 1;
    public int day = 1;
    public float minutes = 420;
    public Vector3 playerPosition;
    public Quaternion playerRotation = Quaternion.identity;
    public float zoom = 0.78f;
    public bool hasCameraOrbit;
    public float cameraYaw;
    public float cameraPitch;
    public int playerForm;
    public int eatenSeedCount;
    // Optional extension: version-1 saves without these fields still load at zero growth.
    public int growthUpgradeVersion;
    public float currentGrowthProgress;
    public bool growthUpgradePending;
    public string[] consumedEdibleIds = new string[0];
    public FoodStatSaveEntry[] foodStatistics = new FoodStatSaveEntry[0];
    // Journal upgrades; saves without these fields load with nothing bought.
    public int spentEggs;
    // Optional wallet grants; older saves default to zero.
    public int bonusEggs;
    // Laid egg wallet counts; saves without these fields default to zero.
    public int whiteEggsLaid;
    public int goldenEggsLaid;
    // Egg cycle state is owned by the GrowthProgressController.
    public int eggLayingVersion;
    public int eggCycleSeedCount;
    public bool eggCycleSelected;
    public bool nextEggGolden;
    public GroundEggSaveEntry[] groundEggs = new GroundEggSaveEntry[0];
    // Eggs piled in the barn's basket (true = golden); saves without it start with an empty basket.
    public bool[] basketEggs = new bool[0];
    public UpgradeLevelSaveEntry[] upgradeLevels = new UpgradeLevelSaveEntry[0];
    // Partly eaten multi-peck food (watermelon slices); saves without it load every slice whole.
    public FoodBiteSaveEntry[] foodBites = new FoodBiteSaveEntry[0];
}

[Serializable]
public sealed class FoodBiteSaveEntry
{
    public string id;
    public int stage;
    public int bites;
    public int bitesNeeded;
}

[Serializable]
public sealed class GroundEggSaveEntry
{
    public string id;
    public Vector3 position;
    public float yaw;
    public bool golden;
}

[Serializable]
public sealed class UpgradeLevelSaveEntry
{
    public string id;
    public int level;
}

[Serializable]
public sealed class FoodStatSaveEntry
{
    public string key;
    public string displayName;
    public int count;
}

public sealed class FarmSaveSystem : MonoBehaviour
{
    public event Action<FarmSaveData> Loaded;
    public event Action<FarmSaveData> Saving;
    public FarmSaveData LastLoadedData { get; private set; }
    [SerializeField] private string fileName = "farm-save.json";
    public string SavePath => Path.Combine(Application.persistentDataPath, Path.GetFileName(fileName));
    public FarmSaveData Load()
    {
        if (!File.Exists(SavePath)) return NotifyLoaded(null);
        FarmSaveData data;
        try { data = Read(SavePath); }
        catch (Exception primary)
        {
            if (!File.Exists(SavePath + ".bak")) throw;
            Debug.LogWarning("Primary save unreadable; loading backup: " + primary.Message);
            data = Read(SavePath + ".bak");
        }
        return NotifyLoaded(data);
    }
    private FarmSaveData NotifyLoaded(FarmSaveData data)
    {
        LastLoadedData = data;
        Loaded?.Invoke(data);
        return data;
    }
    private static FarmSaveData Read(string path)
    {
        var data = JsonUtility.FromJson<FarmSaveData>(File.ReadAllText(path));
        if (data == null || data.version != 1 || data.day < 1 ||
            float.IsNaN(data.minutes) || data.minutes < 0 || data.minutes > 1140 ||
            !Finite(data.playerPosition.x) || !Finite(data.playerPosition.y) ||
            !Finite(data.playerPosition.z) || !Finite(data.zoom) ||
            (data.hasCameraOrbit && (!Finite(data.cameraYaw) || !Finite(data.cameraPitch))))
            throw new InvalidDataException("Invalid or unsupported farm save.");
        if (data.consumedEdibleIds == null) data.consumedEdibleIds = new string[0];
        if (data.foodStatistics == null) data.foodStatistics = new FoodStatSaveEntry[0];
        if (data.upgradeLevels == null) data.upgradeLevels = new UpgradeLevelSaveEntry[0];
        if (data.foodBites == null) data.foodBites = new FoodBiteSaveEntry[0];
        return data;
    }
    private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    public Task SaveAsync(FarmSaveData data)
    {
        // Capture Unity data on the main thread; only file I/O runs on the worker.
        Saving?.Invoke(data);
        string json = JsonUtility.ToJson(data, true);
        string path = SavePath;
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        });
    }
}
