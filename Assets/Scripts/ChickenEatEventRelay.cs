using UnityEngine;

// Animation events are delivered to the object that owns the active Animator.
public sealed class ChickenEatEventRelay : MonoBehaviour
{
    private ChickEatingController eater;
    public void Bind(ChickEatingController owner) => eater = owner;
    public void OnEatImpact() { if (eater != null) eater.OnEatImpact(); }
    public void OnEatRecovery() { if (eater != null) eater.OnEatRecovery(); }
}
