using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using MixedReality.Toolkit.UX;
using TMPro;

public class XRRigPose : MonoBehaviour
{

    [SerializeField] private TextMeshProUGUI rigPoseTMP;
    [SerializeField] private TextMeshProUGUI cameraPoseTMP;
    [SerializeField] private TextMeshProUGUI countTMP;
    public GameObject xrRig;
    public GameObject camera;
    public int count=0;

    //private Vector3 rigPosition;
    //private Vector3 cameraPosition;

    //private Quaternion rigRot;
    //private Quaternion cameraRot;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        count++;

        Vector3 rigPosition = xrRig.transform.position;
        Quaternion rigRot = xrRig.transform.rotation;
        Vector3 cameraPosition = camera.transform.position;
        Quaternion cameraRot = camera.transform.rotation;

        string message = "XRRig:";
        message += "\n Position: " + rigPosition;
        message += "\n Rotation: " + rigRot;

        string message2 = "Camera:";
        message2 += "\n Position: " + cameraPosition;
        message2 += "\n Rotation: " + cameraRot;

        string message3 = count.ToString();

        rigPoseTMP.text = message;
        cameraPoseTMP.text = message2;
        countTMP.text = message3;
    }
}
