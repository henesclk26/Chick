using UnityEngine;

// Presentation only: the growth controller owns timing, pausing, and the concealed form change.
[DisallowMultipleComponent]
public sealed class GrowthSmokeBurst : MonoBehaviour
{
    private Transform[] puffs;
    public const int PuffCount = 21;
    private void Awake()
    {
        puffs = new Transform[transform.childCount];
        for (int i = 0; i < puffs.Length; i++) puffs[i] = transform.GetChild(i);
        Sample(0f);
    }
    private static float Ease(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));
    public void Sample(float t)
    {
        if (puffs == null) Awake();
        t = Mathf.Clamp01(t);
        float swell = Ease(0f, .17f, t);
        for (int i = 0; i < puffs.Length; i++)
        {
            if (i == 0)
            {
                float dissolve = Ease(.60f, .86f, t);
                puffs[i].localPosition = new Vector3(0f, .32f + dissolve * .17f, .035f);
                puffs[i].localRotation = Quaternion.Euler(0, t * 240, 0);
                puffs[i].localScale = new Vector3(.34f, .35f, .39f) * swell * (1f - dissolve);
                continue;
            }
            bool wisp = i > 14;
            float phase = i * 2.399963f;
            float release = Ease(wisp ? .55f : .60f, 1f, t);
            float angle = phase + t * (wisp ? -9.5f : 10.5f) + .22f * Mathf.Sin(t * 17f + phase);
            float ring = (wisp ? .34f : .22f) + (i % 3) * .026f;
            float distance = ring + release * (wisp ? .72f : .49f);
            float y = wisp ? .14f + (i % 3) * .15f : .15f + ((i - 1) % 3) * .185f;
            y += Mathf.Sin(t * 21f + phase) * .035f * swell + release * (.18f + (i % 4) * .065f);
            puffs[i].localPosition = new Vector3(Mathf.Cos(angle) * distance, y, Mathf.Sin(angle) * distance);
            float size = wisp ? .095f + (i % 3) * .019f : .18f + (i % 4) * .021f;
            float pulse = 1f + .09f * Mathf.Sin(t * 25f + phase);
            float appear = Ease(wisp ? .05f : .015f * (i % 3), .19f, t);
            float vanish = Mathf.Pow(1f - release, 1.25f);
            puffs[i].localScale = new Vector3(1f, .91f + (i % 3) * .07f, 1f) * size * appear * pulse * vanish;
            puffs[i].localRotation = Quaternion.Euler(i * 27 + t * 90, phase * Mathf.Rad2Deg + t * 180, i * 13 + t * 45);
        }
    }
}
