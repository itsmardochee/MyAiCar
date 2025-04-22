using UnityEngine;

public class PlayerComtroller : MonoBehaviour
{
    private float speed = 20.0f; // Speed of the vehicle
    private float turnSpeed = 45.0f; // Speed of the vehicle
    private float horizontalInput; // Horizontal input for turning
    private float verticalInput; // Vertical input for moving forward/backward

    void Start()
    {

    }

    void Update()
    {
        horizontalInput = Input.GetAxis("Horizontal"); // Get the horizontal input (A/D or Left/Right arrow keys)
        verticalInput = Input.GetAxis("Vertical"); // Get the vertical input (W/S or Up/Down arrow keys)
        
        //Move the vehicle forward
        transform.Translate(speed * Time.deltaTime * Vector3.forward * verticalInput);
        
        // Move the vehicle backward
        transform.Rotate(Vector3.up, horizontalInput * Time.deltaTime * turnSpeed);
    }
}
