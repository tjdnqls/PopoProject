using UnityEngine;
using System.Collections.Generic;

public class BoxSensorTrigger : MonoBehaviour
{
    private HashSet<Collider2D> boxesOnSensor = new HashSet<Collider2D>();

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Pushable"))
        {
            boxesOnSensor.Add(other);
            UpdateTraps(true);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Pushable"))
        {
            boxesOnSensor.Remove(other);
            if (boxesOnSensor.Count == 0)
            {
                UpdateTraps(false);
            }
        }
    }

    private void UpdateTraps(bool activate)
    {
        foreach (Transform child in transform)
        {
            TrapMover mover = child.GetComponent<TrapMover>();
            if (mover != null)
                  mover.Activate();
                else   {
                if (activate)
           
                    mover.Deactivate();
            }
        }
    }
}
