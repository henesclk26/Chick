using UnityEngine;

// The fire of a wall torch: its warm light flickers (brightness and reach wander on Perlin noise, with the odd quick
// dip) and the light sways a little with the flame. The flame itself is a particle system set up with it
// (BarnInteriorProps); this only animates the light.
[DisallowMultipleComponent]
[RequireComponent(typeof(Light))]
public sealed class TorchFlame : MonoBehaviour
{
    [SerializeField] private float intensity = 1.6f;
    [SerializeField, Range(0f, .6f)] private float flicker = .3f;
    [SerializeField] private float speed = 7f;
    [SerializeField, Tooltip("How far (m) the light sways with the flame.")]
    private float sway = .02f;

    private Light fire;
    private float range, seed;
    private Vector3 rest;

    private void Awake()
    {
        fire = GetComponent<Light>();
        range = fire.range;
        rest = transform.localPosition;
        seed = Random.value * 100f;
    }

    private void Update()
    {
        float t = Time.time * speed + seed;
        float n = Mathf.PerlinNoise(t, seed) * .7f + Mathf.PerlinNoise(t * 2.7f, seed + 5f) * .3f;   // 0..1
        float k = 1f - flicker + n * flicker * 2f;
        fire.intensity = intensity * k;
        fire.range = range * (.92f + n * .16f);
        transform.localPosition = rest + new Vector3(Mathf.PerlinNoise(t * .6f, seed + 9f) - .5f, 0f, Mathf.PerlinNoise(seed + 3f, t * .6f) - .5f) * sway * 2f;
    }
}
