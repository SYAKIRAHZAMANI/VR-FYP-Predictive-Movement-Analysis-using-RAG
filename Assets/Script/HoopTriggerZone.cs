using UnityEngine;

public class HoopTriggerZone : MonoBehaviour
{
    private BasketDetector parentDetector;
    private Collider myCollider;

    private void Awake()
    {
        // Find the main detector on the parent object
        parentDetector = GetComponentInParent<BasketDetector>();
        myCollider = GetComponent<Collider>();

        if (parentDetector == null)
        {
            Debug.LogError($"[HoopTriggerZone] Could not find BasketDetector script on parent of {gameObject.name}!");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (parentDetector != null)
        {
            parentDetector.ReportTriggerEnter(myCollider, other);
        }
    }
}