using MicahW.PointGrass;
using UnityEngine;

// Lets the chick part the Point Grass Renderer blades it walks through. Keeps a PointGrassDisplacer at the bottom of the
// CharacterController, sized from the capsule like FoliageBendDriver, so the chicken form parts a wider path than the
// chick. The blades are pushed by the PointGrassDisplacementManager in the scene (ChickenYardPointGrass adds one).
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PointGrassChickPusher : MonoBehaviour
{
    [SerializeField, Tooltip("Push radius as a multiple of the CharacterController radius.")]
    private float radiusScale = 2.4f;
    [SerializeField, Range(0f, 2f), Tooltip("How hard the blades are pushed at the centre.")]
    private float strength = 1f;

    private CharacterController capsule;
    private PointGrassDisplacer displacer;

    private void Awake()
    {
        capsule = GetComponent<CharacterController>();
        displacer = GetComponent<PointGrassDisplacer>();
        if (displacer == null) displacer = gameObject.AddComponent<PointGrassDisplacer>();
        UpdateDisplacer();
    }

    private void LateUpdate() => UpdateDisplacer();

    // The blades bend away from a sphere; centred at the feet it reaches the roots around the chick.
    private void UpdateDisplacer()
    {
        Vector3 scale = transform.lossyScale;
        float horizontal = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        displacer.localPosition = capsule.center - Vector3.up * (capsule.height * 0.5f);
        displacer.radius = capsule.radius * horizontal * radiusScale;
        displacer.strength = strength;
    }
}
