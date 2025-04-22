using UnityEngine;

public class FollowPlayer : MonoBehaviour
{
    public GameObject player; // Reference to the player GameObject 
    private Vector3 offset = new Vector3(0, 4, -7); // Offset from the player position

    void Start()
    {
        
    }

    void LateUpdate()
    {
        //transform.position = player.transform.position + offset;
        // Place la cam�ra derri�re le v�hicule, selon son orientation
        transform.position = player.transform.TransformPoint(offset);
        // Oriente la cam�ra vers le v�hicule
        transform.LookAt(player.transform.position + Vector3.up * 1.5f); // Ajuste la hauteur du regard si besoin
    }
}
