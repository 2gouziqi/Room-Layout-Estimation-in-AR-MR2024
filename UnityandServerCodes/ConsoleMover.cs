using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using UnityEngine.SceneManagement;


public class ConcoleMover : MonoBehaviour
{
    [SerializeField] private GameObject consoleObject; 
    [SerializeField] private Vector3 offset = new Vector3(0, -0.15f, 1.2f);
    public int loadingTime = 15;

    // Input action for the Menu button
    [SerializeField]
    private InputAction menuInputAction = new InputAction(binding: "<MagicLeapController>/menu", expectedControlType: "Button");

    private Camera mainCamera; // The camera to reference for positioning

    void Start()
    {
        // Make sure we have the main camera
        mainCamera = Camera.main;
    }
    private void Awake()
    {
        // Enable the InputAction
        menuInputAction.Enable();

        // Add callback for when the Menu button is pressed
        menuInputAction.performed += OnMenuButtonPressed;

        StartCoroutine(InitializeConsolePose(loadingTime));
    }

    private IEnumerator InitializeConsolePose(int loadingTime)
    {
        // Wait for 15 seconds to play the intro voiceline and load camera 
        yield return new WaitForSecondsRealtime(loadingTime);

        // Activate the GameObjects if they are not null
        MoveConsoleToCameraPosition();
    }

    private void OnDestroy()
    {
        // Make sure to clean up and disable the InputAction when the object is destroyed
        menuInputAction.Disable();
    }

    private void OnMenuButtonPressed(InputAction.CallbackContext context)
    {
        // Add the functionality you want to trigger when the button is pressed
        Debug.Log("Menu button pressed!");

        MoveConsoleToCameraPosition();
    }

    // This function is called when the button is pressed
    private void MoveConsoleToCameraPosition()
    {
        if (mainCamera != null && consoleObject != null)
        {
            // Get the position of the camera
            Vector3 cameraPosition = mainCamera.transform.position;
            Vector3 cameraForward = Camera.main.transform.forward;

            // Move the console to a new position based on the camera's position and looking direction
            Vector3 targetPosition = cameraPosition + new Vector3(cameraForward.x,0,cameraForward.z).normalized * offset.z;
            targetPosition += new Vector3(0, offset.y, 0);
            consoleObject.transform.position = targetPosition;


            // Rotate the console to face the user horizontally (around the X-axis)
            Vector3 directionToFace = new Vector3(targetPosition.x - cameraPosition.x, 0, targetPosition.z - cameraPosition.z).normalized;

            // Rotate the console to face the user horizontally
            consoleObject.transform.rotation = Quaternion.LookRotation(directionToFace);

            Debug.Log("Console moved to position: " + consoleObject.transform.position);
        }
        else
        {
            Debug.LogError("Main Camera or Console Object is missing!");
        }
    }
}