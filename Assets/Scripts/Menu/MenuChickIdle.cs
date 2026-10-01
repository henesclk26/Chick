using UnityEngine;

// Decorative menu instance only: no player input, gameplay or animation asset changes.
public sealed class MenuChickIdle : MonoBehaviour
{
    private void Start()
    {
        var animator = GetComponent<Animator>();
        animator.Play("idle", 0);
        if (animator.layerCount > 1) animator.Play("wing_idle", 1);
    }
}
