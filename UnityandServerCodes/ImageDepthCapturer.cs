using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.MagicLeap;
using UnityEngine.InputSystem;
using NativeWebSocket;
using static UnityEngine.XR.MagicLeap.MLCameraBase.Metadata;
using System.Diagnostics;
using System.IO;
using static UnityEngine.XR.MagicLeap.MLMediaRecorder;

public class IntegratedCaptureAndSend : MonoBehaviour
{


    [SerializeField] private RawImage colorImageDisplay;       // 可选，用于显示捕获的彩色图像
    [SerializeField] private TextMeshProUGUI statusText;       // 显示状态信息
    [SerializeField] private TextMeshProUGUI depthStatusText;
    [SerializeField] private TextMeshProUGUI serverMessageText;// 显示服务器回复信息

    [SerializeField] private TextMeshProUGUI depthExtrinsic;

    [SerializeField] private Image bumperPressed = null;
    [SerializeField]
    private InputAction bumperInputAction = new InputAction(binding: "<XRController>/gripPressed", expectedControlType: "Button");

    [SerializeField] private Image websocketStatusImage;
    [SerializeField] private TextMeshProUGUI dataSizeText;

    private long totalBytesSent = 0; // 用于累积发送数据的总字节数

    private bool isbumperPressed = false;
    public static bool isSending = false;

    // 权限相关
    private HashSet<string> requestedPermissions = new HashSet<string>();
    private bool anyPermissionDenied = false;
    private readonly MLPermissions.Callbacks permissionCallbacks = new MLPermissions.Callbacks();

    // WebSocket
    private WebSocket websocket;
    private string serverUrl = "ws://192.168.1.101:8765";
    private string latestServerMessage = "";
    private bool hasNewMessage = false;

    // RGB相机相关变量
    private bool isCameraConnected = false;
    private MLCamera colorCamera;
    private bool cameraDeviceAvailable = false;
    private bool isCapturingImage = false;
    private bool colorImageCaptured = false;
    private Texture2D capturedColorTexture;

    // 深度相机相关变量
    private bool depthCameraInitialized = false;
    private ulong timeout = 300;
    private MLDepthCamera.Data lastDepthData = default; // 保存最近获取的深度数据
    private float[] rawDepthData;
    private int depthWidth;
    private int depthHeight;

    // 相机参数相关
    private string intrinsicsJson = "";
    private string extrinsicsJson = "";
    private string depthIntrinsicsJson = "";
    private string depthExtrinsicsJson = "";

    // 初始帧捕获相关（用于计算相对位姿）
    // 彩色相机初始位姿
    private bool initialFrameCapturedColor = false;
    private Vector3 initialPositionColor = Vector3.zero;
    private Quaternion initialRotationColor = Quaternion.identity;

    // 深度相机初始位姿
    private bool initialFrameCapturedDepth = false;
    private Vector3 initialPositionDepth = Vector3.zero;
    private Quaternion initialRotationDepth = Quaternion.identity;

    private Vector3 currentPosition_C;
    private Quaternion currentRotation_C;
    private Vector3 currentPosition_D;
    private Quaternion currentRotation_D;

    private MLCamera.ResultExtras result_extras;
    public GameObject camera;

    private UnityEngine.AudioSource audioSource;

    // TO AVOID BLOCKING


    private string txtFolderPath;
    private int txtFileCounter = 0; // 用于生成唯一文件名

    private Stopwatch stopwatch;
    private void Awake()
    {
        // 注册权限回调
        audioSource = GetComponent<UnityEngine.AudioSource>();
        permissionCallbacks.OnPermissionGranted += OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied += OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain += OnPermissionDenied;

        // 启用并注册 Bumper 按钮事件
        bumperInputAction.Enable();
        bumperInputAction.performed += OnBumperPressed;

        // 初始化 WebSocket
        websocket = new WebSocket(serverUrl);
        websocket.OnOpen += () =>
        {
            UpdateDebug("WebSocket connection opened.", Color.green);
            SetWebSocketStatusColor(Color.green);
        };
        websocket.OnError += (e) =>
        {
            UpdateDebug("WebSocket error: " + e, Color.red);
            SetWebSocketStatusColor(Color.red);
        };
        websocket.OnClose += (e) =>
        {
            UpdateDebug("WebSocket connection closed.", Color.yellow);
            SetWebSocketStatusColor(Color.red);
        };
        websocket.OnMessage += (bytes) =>
        {
            latestServerMessage = System.Text.Encoding.UTF8.GetString(bytes);
            
            UpdateDebug("Message received from server.", Color.blue);
            SetWebSocketStatusColor(Color.blue);
            SaveMessageToTxtFile(latestServerMessage);

            hasNewMessage = true;
        };

        websocket.Connect();

        UpdateDebug("Awake completed. Waiting for permissions...", Color.cyan);

        txtFolderPath = Path.Combine(Application.persistentDataPath, "ReceivedTxtFiles");
        if (!Directory.Exists(txtFolderPath))
        {
            Directory.CreateDirectory(txtFolderPath);
        }

    }

    private void Start()
    {
        requestedPermissions.Clear();
        anyPermissionDenied = false;

        requestedPermissions.Add(MLPermission.Camera);
        requestedPermissions.Add(MLPermission.DepthCamera);

        MLResult resultCam = MLPermissions.RequestPermission(MLPermission.Camera, permissionCallbacks);
        if (!resultCam.IsOk)
        {
            UpdateDebug($"Error requesting camera permission: {resultCam}", Color.red);
            enabled = false;
            return;
        }

        MLResult resultDepth = MLPermissions.RequestPermission(MLPermission.DepthCamera, permissionCallbacks);
        if (!resultDepth.IsOk)
        {
            UpdateDebug($"Error requesting depth camera permission: {resultDepth}", Color.red);
            enabled = false;
            return;
        }

        UpdateDebug("Permission requests sent. Waiting for user response...", Color.yellow);
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif
        if (hasNewMessage)
        {
            if (serverMessageText != null)
            {
                serverMessageText.text = "Server: " + latestServerMessage;
            }
            hasNewMessage = false;
        }
        depthExtrinsic.text = lastDepthData.Position.ToString();
    }

    private void OnDisable()
    {
        permissionCallbacks.OnPermissionGranted -= OnPermissionGranted;
        permissionCallbacks.OnPermissionDenied -= OnPermissionDenied;
        permissionCallbacks.OnPermissionDeniedAndDontAskAgain -= OnPermissionDenied;

        if (colorCamera != null && isCameraConnected)
        {
            colorCamera.Disconnect();
            isCameraConnected = false;
        }

        if (MLDepthCamera.IsConnected)
        {
            MLDepthCamera.Disconnect();
        }

        bumperInputAction.Disable();

    }

    private void OnPermissionGranted(string permission)
    {
        if (requestedPermissions.Contains(permission))
        {
            requestedPermissions.Remove(permission);
        }

        if (requestedPermissions.Count == 0)
        {
            if (!anyPermissionDenied)
            {
                UpdateDebug("All requested permissions granted. Initializing cameras...", Color.green);
                // 所有权限都允许，开始初始化相机
                StartCoroutine(InitializeColorCamera());
                StartCoroutine(InitializeDepthCamera());
                StartCoroutine(CaptureAndSendRoutine());
            }
            else
            {
                UpdateDebug("Some permissions denied. Cannot proceed.", Color.red);
                enabled = false;
            }
        }
    }

    private void OnPermissionDenied(string permission)
    {
        if (requestedPermissions.Contains(permission))
        {
            requestedPermissions.Remove(permission);
        }

        anyPermissionDenied = true;

        if (requestedPermissions.Count == 0)
        {
            UpdateDebug("Some permissions denied. Cannot proceed.", Color.red);
            enabled = false;
        }
    }

    private IEnumerator InitializeColorCamera()
    {
        UpdateDebug("Initializing Color Camera...", Color.cyan);

        while (!cameraDeviceAvailable)
        {
            MLResult result = MLCamera.GetDeviceAvailabilityStatus(MLCamera.Identifier.Main, out cameraDeviceAvailable);
            if (!(result.IsOk && cameraDeviceAvailable))
            {
                UpdateDebug("Waiting for color camera device availability...", Color.yellow);
                yield return new WaitForSeconds(1.0f);
            }
        }

        UpdateDebug("Color camera device is available. Connecting camera...", Color.green);

        MLCamera.ConnectContext context = MLCamera.ConnectContext.Create();
        context.EnableVideoStabilization = false;
        context.Flags = MLCameraBase.ConnectFlag.CamOnly;

        // 使用原有逻辑：直接等待任务完成，检查返回值是否为null
        var cameraTask = MLCamera.CreateAndConnectAsync(context);
        while (!cameraTask.IsCompleted)
        {
            yield return null;
        }

        colorCamera = cameraTask.Result;
        if (colorCamera == null)
        {
            UpdateDebug("Failed to connect to color camera: returned null.", Color.red);
            yield break;
        }

        colorCamera.OnRawImageAvailable += OnCaptureRawImageComplete;

        isCameraConnected = true;
        UpdateDebug("Color camera connected successfully.", Color.green);

        var imageConfig = new[]
        {
        new MLCamera.CaptureStreamConfig
        {
            OutputFormat = MLCamera.OutputFormat.JPEG,
            CaptureType = MLCamera.CaptureType.Image,
            Width = 640,
            Height = 480
        }

    };

        var captureConfig = new MLCamera.CaptureConfig
        {
            StreamConfigs = imageConfig,
            CaptureFrameRate = MLCamera.CaptureFrameRate._60FPS
        };

        MLResult prepareCaptureResult = colorCamera.PrepareCapture(captureConfig, out _);
        if (!prepareCaptureResult.IsOk)
        {
            UpdateDebug("Failed to prepare camera for capture.", Color.red);
            yield break;
        }

        UpdateDebug("Color camera initialized and ready for capture.", Color.green);
    }


    /*private IEnumerator InitializeDepthCamera()
    {
        UpdateDebug("Initializing Depth Camera...", Color.cyan);

        MLDepthCamera.StreamConfig[] config = new MLDepthCamera.StreamConfig[2];

        int i = (int)MLDepthCamera.FrameType.LongRange;
        config[i].Flags = (uint)MLDepthCamera.CaptureFlags.DepthImage;
        config[i].Exposure = 1600;
        config[i].FrameRateConfig = MLDepthCamera.FrameRate.FPS_5;

        i = (int)MLDepthCamera.FrameType.ShortRange;
        config[i].Flags = (uint)MLDepthCamera.CaptureFlags.DepthImage;
        config[i].Exposure = 375;
        config[i].FrameRateConfig = MLDepthCamera.FrameRate.FPS_5;

        var settings = new MLDepthCamera.Settings()
        {
            Streams = MLDepthCamera.Stream.LongRange,
            StreamConfig = config
        };

        try
        {
            UpdateDebug("Calling MLDepthCamera.SetSettings() for depth camera...", Color.cyan);
            MLDepthCamera.SetSettings(settings);
            UpdateDebug("Successfully called SetSettings for depth camera. Now connecting...", Color.green);
        }
        catch (Exception ex)
        {
            UpdateDebug($"SetSettings for depth camera failed with error: {ex.Message}", Color.magenta);
            yield break;
        }

        UpdateDebug("Attempting to connect to depth camera...", Color.yellow);
        MLResult result;
        try
        {
            result = MLDepthCamera.Connect();
        }
        catch (Exception ex)
        {
            UpdateDebug($"ConnectDepthCamera failed with error: {ex.Message}", Color.magenta);
            yield break;
        }

        if (!result.IsOk)
        {
            UpdateDebug($"Failed to connect to depth camera: {result.Result}", Color.red);
            PrintDetailedError(result.Result);
            yield break;
        }

        UpdateDebug("Depth camera connected successfully.", Color.green);
        depthCameraInitialized = true;

        yield break;
    }
    */
    private IEnumerator InitializeDepthCamera()
    {
        UpdateDebug("Initializing Depth Camera...", Color.cyan, true);

        MLDepthCamera.StreamConfig[] config = new MLDepthCamera.StreamConfig[2];

        int i = (int)MLDepthCamera.FrameType.LongRange;
        config[i].Flags = (uint)MLDepthCamera.CaptureFlags.DepthImage;
        config[i].Exposure = 1600;
        config[i].FrameRateConfig = MLDepthCamera.FrameRate.FPS_5;

        i = (int)MLDepthCamera.FrameType.ShortRange;
        config[i].Flags = (uint)MLDepthCamera.CaptureFlags.DepthImage;
        config[i].Exposure = 375;
        config[i].FrameRateConfig = MLDepthCamera.FrameRate.FPS_5;

        var settings = new MLDepthCamera.Settings()
        {
            Streams = MLDepthCamera.Stream.LongRange,
            StreamConfig = config
        };

        try
        {
            UpdateDebug("Calling MLDepthCamera.SetSettings() for depth camera...", Color.cyan, true);
            MLDepthCamera.SetSettings(settings);
            UpdateDebug("Successfully called SetSettings for depth camera. Now connecting...", Color.green, true);
        }
        catch (Exception ex)
        {
            UpdateDebug($"SetSettings for depth camera failed with error: {ex.Message}", Color.magenta, true);
            yield break;
        }

        UpdateDebug("Attempting to connect to depth camera...", Color.yellow, true);
        MLResult result;
        try
        {
            result = MLDepthCamera.Connect();
        }
        catch (Exception ex)
        {
            UpdateDebug($"ConnectDepthCamera failed with error: {ex.Message}", Color.magenta, true);
            yield break;
        }

        if (!result.IsOk)
        {
            UpdateDebug($"Failed to connect to depth camera: {result.Result}", Color.red, true);
            PrintDetailedError(result.Result);
            yield break;
        }

        UpdateDebug("Depth camera connected successfully.", Color.green, true);
        depthCameraInitialized = true;

        yield break;
    }
    private void PrintDetailedError(MLResult.Code resultCode)
    {
        switch (resultCode)
        {
            case MLResult.Code.InvalidParam:
                UpdateDebug("Invalid parameter passed to MLDepthCamera.Connect().", Color.red);
                break;
            case MLResult.Code.PermissionDenied:
                UpdateDebug("Permission to use the Depth Camera was denied.", Color.red);
                break;
            case MLResult.Code.LicenseError:
                UpdateDebug("License error. Check Magic Leap license status.", Color.red);
                break;
            case MLResult.Code.UnspecifiedFailure:
                UpdateDebug("Unspecified failure. Possible API bug or unknown issue.", Color.red);
                break;
            default:
                UpdateDebug($"Unknown error code: {resultCode}", Color.red);
                break;
        }
    }

    private void OnBumperPressed(InputAction.CallbackContext context)
    {
        if (isSending)
        {
            UpdateDebug("Sending data... Please wait", Color.red);
            isSending = true;
        }
        else 
        { 
            if (!isCameraConnected || !depthCameraInitialized)
                {
                    UpdateDebug("Cameras not fully initialized. Cannot capture yet.", Color.red);
                    return;
                }
            if (audioSource != null)
            {
                audioSource.Play();
            }
            isbumperPressed = !isbumperPressed;
            bumperPressed.color = isbumperPressed ? Color.blue : Color.green;

            UpdateDebug("Bumper pressed. Starting capture and send routine...", Color.yellow);
            StartCoroutine(CaptureAndSendRoutine());
        }
       

    }

    private IEnumerator CaptureAndSendRoutine()
    {
        UpdateDebug("Starting capture and send routine...", Color.yellow);
        stopwatch = new Stopwatch();
        stopwatch.Start();
        yield return StartCoroutine(CaptureDepthData());
        if (lastDepthData.DepthImage == null)
        {
            // 没有成功获取深度数据，中途退出
            yield break;
        }
        stopwatch.Stop();
        UpdateDebug("Depth data captured successfully, proceeding to capture color image." + stopwatch.ElapsedMilliseconds, Color.green, true);
        stopwatch = new Stopwatch();
        stopwatch.Start();
        yield return StartCoroutine(CaptureColorImage());
        if (!colorImageCaptured || capturedColorTexture == null)
        {
            // 没有成功获取彩色图像，中途退出
            yield break;
        }
        stopwatch.Stop();
        UpdateDebug("Color image captured successfully. Retrieving camera parameters..." + stopwatch.ElapsedMilliseconds, Color.green);
        stopwatch = new Stopwatch();
        stopwatch.Start();
        GetIntrinsicsAndExtrinsics();
        if (string.IsNullOrEmpty(intrinsicsJson) || string.IsNullOrEmpty(extrinsicsJson)
            || string.IsNullOrEmpty(depthIntrinsicsJson) || string.IsNullOrEmpty(depthExtrinsicsJson))
        {
            UpdateDebug("Failed to retrieve camera parameters. Aborting capture and send routine.", Color.red);
            yield break;
        }
        stopwatch.Stop();
        UpdateDebug("Camera parameters retrieved successfully. Creating JSON message..." + stopwatch.ElapsedMilliseconds, Color.green);
        stopwatch = new Stopwatch();
        stopwatch.Start();
        float[] flatten = alignDepthmap();
        stopwatch.Stop();
        UpdateDebug("Depth map aligned" + stopwatch.ElapsedMilliseconds, Color.green, true);
        stopwatch = new Stopwatch();
        stopwatch.Start();
        string jsonMessage = CreateStructuredJsonMessage(intrinsicsJson, extrinsicsJson, depthIntrinsicsJson, depthExtrinsicsJson);
        if (string.IsNullOrEmpty(jsonMessage))
        {
            UpdateDebug("Failed to create JSON message. Aborting capture and send routine.", Color.red);
            yield break;
        }
        stopwatch.Stop();
        UpdateDebug("JSON metadata message created. Packing all data..." + stopwatch.ElapsedMilliseconds, Color.green);
        stopwatch = new Stopwatch();
        
        byte[] finalMessage = PackAllDataIntoOneMessage(jsonMessage, capturedColorTexture, flatten);/////////
        if (finalMessage == null || finalMessage.Length == 0)
        {
            UpdateDebug("Failed to pack all data into one message. Aborting capture and send routine.", Color.red);
            yield break;
        }
        UpdateDebug("Final message packed successfully. Sending data over WebSocket...", Color.green);

        yield return SendDataInChunks(finalMessage);
        
        UpdateDebug("Data sent successfully. Capture and send routine completed." + stopwatch.ElapsedMilliseconds, Color.green);
        isSending = false;

    }

    private IEnumerator CaptureDepthData()
    {
        UpdateDebug("Capturing depth data...", Color.yellow);

        MLResult result = MLDepthCamera.GetLatestDepthData(timeout, out MLDepthCamera.Data data);
        if (!result.IsOk || data.DepthImage == null)
        {
            UpdateDebug($"Failed to retrieve depth data: {result}", Color.red, true);
            yield break;
        }

        // 成功获取深度数据，将其暂存在类变量中
        lastDepthData = data;
        rawDepthData = null;

        UpdateDebug("Depth data captured and stored for later processing.", Color.green, true);
        yield break;
    }

    private IEnumerator CaptureColorImage()
    {
        UpdateDebug("Capturing color image...", Color.yellow);

        if (colorCamera == null || !isCameraConnected)
        {
            UpdateDebug("Color camera not ready.", Color.red);
            yield break;
        }

        if (isCapturingImage)
        {
            UpdateDebug("Color image capture is already in progress.", Color.red);
            yield break;
        }

        isCapturingImage = true;
        colorImageCaptured = false;
        capturedColorTexture = null;

        // 不使用await，使用Task和WaitUntil配合
        Task<MLResult> aeawbTask = colorCamera.PreCaptureAEAWBAsync();
        yield return new WaitUntil(() => aeawbTask.IsCompleted);
        MLResult aeawbResult = aeawbTask.Result;
        if (!aeawbResult.IsOk)
        {
            UpdateDebug("PreCaptureAEAWBAsync failed. Cannot capture image.", Color.red);
            isCapturingImage = false;
            yield break;
        }

        Task<MLResult> captureTask = colorCamera.CaptureImageAsync(1);
        yield return new WaitUntil(() => captureTask.IsCompleted);
        MLResult captureResult = captureTask.Result;
        if (!captureResult.IsOk)
        {
            UpdateDebug("Image capture request failed.", Color.red);
            isCapturingImage = false;
            yield break;
        }

        float waitStartTime = Time.time;
        float waitTimeout = 5f;
        while (!colorImageCaptured && Time.time - waitStartTime < waitTimeout)
        {
            yield return null;
        }

        isCapturingImage = false;

        if (!colorImageCaptured || capturedColorTexture == null)
        {
            UpdateDebug("No color image received within timeout period.", Color.red);
            yield break;
        }

        UpdateDebug($"Color image captured successfully: {capturedColorTexture.width}x{capturedColorTexture.height}", Color.green);

        // 显示在UI上（可选）
        if (colorImageDisplay != null && capturedColorTexture != null)
        {
            colorImageDisplay.texture = capturedColorTexture;
        }
    }


    private void OnCaptureRawImageComplete(MLCamera.CameraOutput capturedImage, MLCamera.ResultExtras resultExtras, MLCamera.Metadata metadataHandle)
    {
        UpdateDebug("OnCaptureRawImageComplete triggered. Processing captured image.", Color.yellow);

        if (capturedImage.Format == MLCameraBase.OutputFormat.JPEG && capturedImage.Planes.Length > 0)
        {
            Texture2D tempTexture = new Texture2D(0, 0, TextureFormat.RGBA32, false);
            bool loaded = tempTexture.LoadImage(capturedImage.Planes[0].Data);
            if (!loaded || tempTexture.width == 0 || tempTexture.height == 0)
            {
                UpdateDebug("Failed to load captured image into texture.", Color.red);
                return;
            }
            capturedColorTexture = tempTexture;
        }
        else
        {
            UpdateDebug("Captured image is not in JPEG format or no data available.", Color.red);
            return;
        }

        // 获取相机内参
        string intrinsicParamsText;
        result_extras = resultExtras;
        if (resultExtras.Intrinsics != null)
        {
            var intr = resultExtras.Intrinsics.Value;
            intrinsicParamsText = $@"
            {{
                ""width"": {intr.Width},
                ""height"": {intr.Height},
                ""fov"": {intr.FOV},
                ""focalLength"": {{
                    ""fx"": {intr.FocalLength.x},
                    ""fy"": {intr.FocalLength.y}
                }},
                ""principalPoint"": {{
                    ""cx"": {intr.PrincipalPoint.x},
                    ""cy"": {intr.PrincipalPoint.y}
                }}
            }}";
        }
        else
        {
            intrinsicParamsText = "null";
        }

        intrinsicsJson = intrinsicParamsText;

        // 获取相机外参
        MLResult poseResult = MLCVCamera.GetFramePose(resultExtras.VCamTimestamp, out Matrix4x4 outMatrix);
        string extrinsicParamsText;
        if (poseResult.IsOk)
        {
            currentPosition_C = outMatrix.GetPosition();
            currentRotation_C = outMatrix.rotation;
            currentRotation_C.Normalize();

            if (!initialFrameCapturedColor)
            {
                initialPositionColor = currentPosition_C - new Vector3(0f, 1.6f, 0f);
                initialRotationColor = currentRotation_C;
                initialFrameCapturedColor = true;
            }

            //Vector3 relativePosition = currentPosition_C; //- initialPositionColor;
            //Quaternion relativeRotation = currentRotation_C; //Quaternion.Inverse(initialRotationColor) * currentRotation_C;

            // Use Unity Camera
            Vector3 relativePosition = camera.transform.position;
            Quaternion relativeRotation = camera.transform.rotation;

            extrinsicParamsText = $@"
            {{
                ""position"": {{
                    ""x"": {relativePosition.x},
                    ""y"": {relativePosition.y},
                    ""z"": {relativePosition.z}
                }},
                ""rotation"": {{
                    ""x"": {relativeRotation.x},
                    ""y"": {relativeRotation.y},
                    ""z"": {relativeRotation.z},
                    ""w"": {relativeRotation.w}
                }}
            }}";
        }
        else
        {
            extrinsicParamsText = "null";
        }

        extrinsicsJson = extrinsicParamsText;

        colorImageCaptured = true;
        UpdateDebug("Captured color image and camera parameters successfully.", Color.green);
    }

    private void GetIntrinsicsAndExtrinsics()
    {
        UpdateDebug("Retrieving camera parameters...", Color.yellow);

        if (string.IsNullOrEmpty(intrinsicsJson) || string.IsNullOrEmpty(extrinsicsJson))
        {
            UpdateDebug("intrinsicsJson or extrinsicsJson (color camera) is empty. Check if OnCaptureRawImageComplete was called.", Color.red, true);
            return;
        }

        // 检查深度数据是否有效
        // 由于Intrinsics是一个struct，无法对null进行检查。如需判断其有效性，可根据Width、Height是否为0判断
        var dIntr = lastDepthData.Intrinsics;
        if (dIntr.Width <= 0 || dIntr.Height <= 0)
        {
            UpdateDebug("Depth camera intrinsics not valid. Width or Height is zero.", Color.red, true);
            return;
        }

        // 构建深度相机内参JSON
        depthIntrinsicsJson = $@"
    {{
        ""width"": {dIntr.Width},
        ""height"": {dIntr.Height},
        ""fov"": {dIntr.FoV},
        ""focalLength"": {{
            ""fx"": {dIntr.FocalLength.x},
            ""fy"": {dIntr.FocalLength.y}
        }},
        ""principalPoint"": {{
            ""cx"": {dIntr.PrincipalPoint.x},
            ""cy"": {dIntr.PrincipalPoint.y}
        }}
    }}";

        // 获取深度相机位姿
        currentPosition_D = lastDepthData.Position;
        currentRotation_D = lastDepthData.Rotation;

        // 计算相对于初始深度相机位姿的相对位姿
        if (!initialFrameCapturedDepth)
        {
            initialPositionDepth = currentPosition_D - new Vector3(0f, 1.6f, 0f);
            initialRotationDepth = currentRotation_D;
            initialFrameCapturedDepth = true;
        }

        Vector3 relativePositionDepth = currentPosition_D - initialPositionDepth;
        Quaternion relativeRotationDepth = Quaternion.Inverse(initialRotationDepth) * currentRotation_D;

        depthExtrinsicsJson = $@"
    {{
        ""position"": {{
            ""x"": {relativePositionDepth.x},
            ""y"": {relativePositionDepth.y},
            ""z"": {relativePositionDepth.z}
        }},
        ""rotation"": {{
            ""x"": {relativeRotationDepth.x},
            ""y"": {relativeRotationDepth.y},
            ""z"": {relativeRotationDepth.z},
            ""w"": {relativeRotationDepth.w}
        }}
    }}";

        UpdateDebug("Camera parameters retrieved and depth camera parameters constructed.", Color.green, true);
    }

    
    private string CreateStructuredJsonMessage(string intrinsics, string extrinsics, string depthIntrinsics, string depthExtrinsics)
    {
        UpdateDebug("Creating structured JSON message...", Color.yellow);

        // 检查深度图数据是否可用
        if (!lastDepthData.DepthImage.HasValue || lastDepthData.DepthImage.Value.Data == null)
        {
            UpdateDebug("No depth image data available. Cannot create JSON message.", Color.red);
            return null;
        }

        // 从深度数据中获取信息
        // 注意：Intrinsics为非空struct，直接使用
        var dIntr = lastDepthData.Intrinsics;
        depthWidth = (int)dIntr.Width;
        depthHeight = (int)dIntr.Height;

        int byteLength = lastDepthData.DepthImage.Value.Data.Length;
        int floatCount = byteLength / sizeof(float);
        if (floatCount != depthWidth * depthHeight)
        {
            UpdateDebug($"Depth data size mismatch: {byteLength} bytes not equal to {depthWidth}x{depthHeight} floats.", Color.red);
            return null;
        }

        rawDepthData = new float[floatCount];
        Buffer.BlockCopy(lastDepthData.DepthImage.Value.Data, 0, rawDepthData, 0, byteLength);

        float minDepth = rawDepthData.Min();
        float maxDepth = rawDepthData.Max();

        // 在深度相机的内参中不包含FOV字段，因为Intrinsics中无FOV定义
        // JSON结构中去掉FOV行即可
        string jsonStr = $@"
{{
    ""color_camera"": {{
        ""extrinsics"": {extrinsics},
        ""intrinsics"": {intrinsics}
    }},
    ""depth_camera"": {{
        ""intrinsics"": {depthIntrinsics},
        ""width"": {depthWidth},
        ""height"": {depthHeight},
        ""min_depth"": {minDepth},
        ""max_depth"": {maxDepth}
    }}
}}";

        UpdateDebug("JSON message created successfully.", Color.green);
        return jsonStr;
    }
    
    /*
    private string CreateStructuredJsonMessage(string intrinsics, string extrinsics)
    {
        UpdateDebug("Creating structured JSON message...", Color.yellow);

        // 构建仅包含颜色相机信息的JSON结构
        string jsonStr = $@"
    {{
        ""color_camera"": {{
            ""intrinsics"": {intrinsics},
            ""extrinsics"": {extrinsics}
        }}
    }}";

        UpdateDebug("JSON message created successfully.", Color.green);
        return jsonStr;
    }
    */

    
    /*
    private byte[] PackAllDataIntoOneMessage(string jsonMetadata, Texture2D colorTexture, float[] depthData, int width, int height)
    {
        UpdateDebug("Packing all data into one message...", Color.yellow);

        if (string.IsNullOrEmpty(jsonMetadata))
        {
            UpdateDebug("JSON metadata is empty. Cannot pack data.", Color.red);
            return null;
        }

        if (colorTexture == null)
        {
            UpdateDebug("Color texture is null. Cannot pack data.", Color.red);
            return null;
        }

        if (depthData == null || depthData.Length == 0)
        {
            UpdateDebug("Depth data is empty. Cannot pack data.", Color.red);
            return null;
        }

        // 将彩色图像转为JPEG
        byte[] jpegData = colorTexture.EncodeToJPG();

        // 将float数组转为字节数组
        byte[] depthBytes = new byte[depthData.Length * sizeof(float)];
        Buffer.BlockCopy(depthData, 0, depthBytes, 0, depthBytes.Length);

        // 将jsonMetadata转为字节数组
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonMetadata);

        // 长度头：4字节长度描述
        byte[] jsonLength = BitConverter.GetBytes(jsonBytes.Length);
        byte[] imageLength = BitConverter.GetBytes(jpegData.Length);
        byte[] depthLength = BitConverter.GetBytes(depthBytes.Length);

        byte[] finalMessage = new byte[4 + jsonBytes.Length + 4 + jpegData.Length + 4 + depthBytes.Length];
        //byte[] finalMessage = new byte[4 + jsonBytes.Length];
        int offset = 0;
        Buffer.BlockCopy(jsonLength, 0, finalMessage, offset, 4);
        offset += 4;
        Buffer.BlockCopy(jsonBytes, 0, finalMessage, offset, jsonBytes.Length);
        offset += jsonBytes.Length;
        Buffer.BlockCopy(imageLength, 0, finalMessage, offset, 4);
        offset += 4;
        Buffer.BlockCopy(jpegData, 0, finalMessage, offset, jpegData.Length);
        offset += jpegData.Length;
        Buffer.BlockCopy(depthLength, 0, finalMessage, offset, 4);
        offset += 4;
        Buffer.BlockCopy(depthBytes, 0, finalMessage, offset, depthBytes.Length);
        offset += depthBytes.Length;

        UpdateDebug("Data packed successfully.", Color.green);
        return finalMessage;
    }

    */
    private byte[] PackAllDataIntoOneMessage(string jsonMetadata, Texture2D colorTexture, float[] flattenedData)
    {
        UpdateDebug("Packing all data into one message...", Color.yellow);

        // 检查 JSON 元数据是否为空
        if (string.IsNullOrEmpty(jsonMetadata))
        {
            UpdateDebug("JSON metadata is empty. Cannot pack data.", Color.red);
            return null;
        }

        // 检查彩色图像是否为空
        if (colorTexture == null)
        {
            UpdateDebug("Color texture is null. Cannot pack data.", Color.red);
            return null;
        }

        // 检查 flattenedData 是否为空
        if (flattenedData == null || flattenedData.Length == 0)
        {
            UpdateDebug("Flattened data is empty. Cannot pack data.", Color.red);
            return null;
        }

        try
        {
            // 将彩色图像编码为 JPEG 格式
            byte[] jpegData = colorTexture.EncodeToJPG();

            // 将 float 数组（flattenedData）转换为字节数组
            byte[] flattenedBytes = new byte[flattenedData.Length * sizeof(float)];
            Buffer.BlockCopy(flattenedData, 0, flattenedBytes, 0, flattenedBytes.Length);

            // 将 JSON 元数据转换为 UTF-8 字节数组
            byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(jsonMetadata);

            // 长度头：4 字节描述 JSON 大小
            byte[] jsonLength = BitConverter.GetBytes(jsonBytes.Length);

            // 长度头：4 字节描述 JPEG 图像大小
            byte[] imageLength = BitConverter.GetBytes(jpegData.Length);

            // 长度头：4 字节描述 flattenedData 大小
            byte[] flattenedLength = BitConverter.GetBytes(flattenedBytes.Length);

            // 计算最终消息的总长度
            byte[] finalMessage = new byte[4 + jsonBytes.Length + 4 + jpegData.Length + 4 + flattenedBytes.Length];

            int offset = 0;

            // 复制 JSON 长度
            Buffer.BlockCopy(jsonLength, 0, finalMessage, offset, 4);
            offset += 4;

            // 复制 JSON 数据
            Buffer.BlockCopy(jsonBytes, 0, finalMessage, offset, jsonBytes.Length);
            offset += jsonBytes.Length;

            // 复制 JPEG 图像长度
            Buffer.BlockCopy(imageLength, 0, finalMessage, offset, 4);
            offset += 4;

            // 复制 JPEG 图像数据
            Buffer.BlockCopy(jpegData, 0, finalMessage, offset, jpegData.Length);
            offset += jpegData.Length;

            // 复制 flattenedData 长度
            Buffer.BlockCopy(flattenedLength, 0, finalMessage, offset, 4);
            offset += 4;

            // 复制 flattenedData 数据
            Buffer.BlockCopy(flattenedBytes, 0, finalMessage, offset, flattenedBytes.Length);
            offset += flattenedBytes.Length;

            UpdateDebug("Data packed successfully.", Color.green);
            return finalMessage;
        }
        catch (Exception ex)
        {
            UpdateDebug($"Error packing data: {ex.Message}", Color.red);
            return null;
        }
    }


    private IEnumerator SendDataInChunks(byte[] finalMessage, int chunkSize = 983040)
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            UpdateDebug("WebSocket is not connected.", Color.red);
            yield break;
        }

        // 生成一个唯一ID标识本次数据发送，会话中可能多次发送数据
        string messageId = Guid.NewGuid().ToString();
        int totalChunks = (int)Mathf.Ceil((float)finalMessage.Length / chunkSize);
        UpdateDebug($"Sending data in {totalChunks} chunks, total size: {finalMessage.Length} bytes", Color.yellow);

        int offset = 0;
        for (int i = 0; i < totalChunks; i++)
        {
            int currentChunkSize = Math.Min(chunkSize, finalMessage.Length - offset);
            byte[] chunkData = new byte[currentChunkSize];
            Buffer.BlockCopy(finalMessage, offset, chunkData, 0, currentChunkSize);

            offset += currentChunkSize;

            // 构建分片头信息（JSON形式）
            string headerJson = $@"{{
            ""message_id"": ""{messageId}"",
            ""chunk_index"": {i},
            ""total_chunks"": {totalChunks}
        }}";

            byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(headerJson);
            byte[] headerLength = BitConverter.GetBytes(headerBytes.Length);

            // 最终发送的数据格式：[4字节header长度][header][chunkData]
            byte[] sendBuffer = new byte[4 + headerBytes.Length + chunkData.Length];
            int pos = 0;
            Buffer.BlockCopy(headerLength, 0, sendBuffer, pos, 4);
            pos += 4;
            Buffer.BlockCopy(headerBytes, 0, sendBuffer, pos, headerBytes.Length);
            pos += headerBytes.Length;
            Buffer.BlockCopy(chunkData, 0, sendBuffer, pos, chunkData.Length);

            
            stopwatch.Start();
            Task sendTask = websocket.Send(sendBuffer);
            yield return new WaitUntil(() => sendTask.IsCompleted);
            stopwatch.Stop();

            if (sendTask.IsFaulted || sendTask.IsCanceled)
            {
                UpdateDebug("Failed to send a chunk of data.", Color.red);
                yield break;
            }
            else
            {
                UpdateDebug($"Chunk {i + 1}/{totalChunks} sent." + stopwatch.ElapsedMilliseconds, Color.green);
            }
        }

        UpdateDebug("All chunks sent successfully.", Color.green);
    }
    /*
    private IEnumerator SendDataOverWebSocket(byte[] finalMessage)
    {
        UpdateDebug("Sending data over WebSocket...", Color.yellow);
        SetWebSocketStatusColor(Color.yellow);

        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            UpdateDebug("WebSocket is not connected.", Color.red);
            SetWebSocketStatusColor(Color.red);
            yield break;
        }

        // 尝试发送数据，但不在try块中yield
        Task sendTask = null;
        try
        {
            sendTask = websocket.Send(finalMessage);
        }
        catch (Exception ex)
        {
            UpdateDebug($"Failed to initiate data send: {ex.Message}", Color.red);
            SetWebSocketStatusColor(Color.red);
            yield break;
        }

        // 在try外等待任务完成
        yield return new WaitUntil(() => sendTask.IsCompleted);

        // 现在检查任务结果
        if (sendTask.IsFaulted || sendTask.IsCanceled)
        {
            if (sendTask.Exception != null)
            {
                UpdateDebug($"Failed to send data: {sendTask.Exception.Message}", Color.red);
            }
            else
            {
                UpdateDebug("Failed to send data: Task was canceled or faulted without exception details.", Color.red);
            }
            SetWebSocketStatusColor(Color.red);
        }
        else
        {
            totalBytesSent += finalMessage.Length;
            UpdateDataSizeText(totalBytesSent);
            UpdateDebug("Data sent to server successfully.", Color.green);
            SetWebSocketStatusColor(Color.green);
        }
    }
    */
    private void SetWebSocketStatusColor(Color c)
    {
        if (websocketStatusImage != null)
        {
            websocketStatusImage.color = c;
        }
    }

    private void UpdateDataSizeText(long bytesSent)
    {
        if (dataSizeText != null)
        {
            dataSizeText.text = $"Total Bytes Sent: {bytesSent}";
        }
    }

    private void UpdateDebug(string message, Color color, bool isDepthCameraDebug = false)
    {
        // 如果是深度相机的debug信息，则更新depthStatusText，否则更新statusText
        TextMeshProUGUI targetText = isDepthCameraDebug ? depthStatusText : statusText;
        if (targetText != null)
        {
            targetText.text = message;
            // 如果想要改变文字颜色，可以在这里进行：
            // targetText.color = color;
        }
    }

    private Matrix4x4 TransposeMatrix4x4(Matrix4x4 matrix)
    {
        Matrix4x4 transposed = new Matrix4x4();
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                transposed[i, j] = matrix[j, i];
            }
        }
        return transposed;
    }

    public Vector3 Calculate3DCoordinates(int u, int v)
    {
        // Extract depth value from lastDepthData for pixel (u, v)
        float Z = GetDepthFromLastDepthData(u, v);

        if (Z <= 0)
        {
            //Debug.LogWarning($"Invalid depth value at ({u}, {v}). Z = {Z}");
            return Vector3.zero; // No depth information available
        }

        // Extract intrinsics from the lastDepthData
        var dIntr = lastDepthData.Intrinsics;

        if (dIntr.Width <= 0 || dIntr.Height <= 0)
        {
            //Debug.LogError("Depth camera intrinsics not valid. Width or Height is zero.");
            return Vector3.zero;
        }

        float fx = dIntr.FocalLength.x; // Focal length in x-axis
        float fy = dIntr.FocalLength.y; // Focal length in y-axis
        float cx = dIntr.PrincipalPoint.x; // Principal point x (center of image in x-axis)
        float cy = dIntr.PrincipalPoint.y; // Principal point y (center of image in y-axis)

        // Compute the 3D coordinates (X, Y, Z) using the formulas
        float X = Z * (u - cx) / fx;
        float Y = Z * (v - cy) / fy;

        return new Vector3(X, Y, Z);
    }

    private float GetDepthFromLastDepthData(int u, int v)
    {
        //if (lastDepthData == null || lastDepthData.DepthImage.Value.Data == null)
        //{
        //    //Debug.LogError("No depth data available.");
        //    return -1f;
        //}

        int depthWidth = (int)lastDepthData.Intrinsics.Width;
        int depthHeight = (int)lastDepthData.Intrinsics.Height;

        // Check if the pixel (u, v) is within the image bounds
        if (u < 0 || u >= depthWidth || v < 0 || v >= depthHeight)
        {
            //Debug.LogError($"Pixel ({u}, {v}) is outside the image bounds ({depthWidth}x{depthHeight}).");
            return -1f;
        }

        // Check if the raw depth data is already initialized, if not, initialize it

        // Convert the byte array to a float array
        if (rawDepthData == null || rawDepthData.Length != depthWidth * depthHeight)
        {
            // Convert the byte array to a float array
            int byteLength = lastDepthData.DepthImage.Value.Data.Length;
            int floatCount = byteLength / sizeof(float);

            if (floatCount != depthWidth * depthHeight)
            {
                return -1f;
            }

            rawDepthData = new float[floatCount];
            Buffer.BlockCopy(lastDepthData.DepthImage.Value.Data, 0, rawDepthData, 0, byteLength);
        }


        // Calculate the 1D index from 2D pixel coordinates (row-major)
        int index = v * depthWidth + u;

        // Get the depth value from the depth image (as a float)
        float Z = rawDepthData[index];


        //// Normalize depth to Z
        //var cx = result_extras.Intrinsics.Value.PrincipalPoint.x;
        //var cy = result_extras.Intrinsics.Value.PrincipalPoint.y;
        //var fx = result_extras.Intrinsics.Value.FocalLength.x;
        //var fy = result_extras.Intrinsics.Value.FocalLength.y;

        //// Calculate the denominator with a square root
        //float denominator = (float)Math.Sqrt(fx * fx + fy * fy + (v - cy) * (v - cy) + (u - cx) * (u - cx));
        //Z = Z * fx * fy / denominator;

        return Z;
    }

    private float[] alignDepthmap()
    {
        //// Step 1: Extract rotation matrix from quaternion (for C and D)
        //Matrix4x4 rotmat_C = Matrix4x4.Rotate(currentRotation_C);
        //Matrix4x4 rotmat_D = Matrix4x4.Rotate(currentRotation_D);

        //// Step 2: Transpose the rotation matrix for C
        //Matrix4x4 rotmat_C_transpose = TransposeMatrix4x4(rotmat_C);

        //// Step 3: Calculate the camera-to-depth vector
        //Vector3 camera2depth_vector = rotmat_C_transpose.MultiplyPoint3x4(currentPosition_D - currentPosition_C);

        //// Step 4: Calculate the relative rotation matrix
        //Matrix4x4 relative_rotmat = rotmat_D * rotmat_C_transpose;

        //// Step 5: Calculate 3D coordinates for every pixel of the depth image
        //int depthWidth = (int)lastDepthData.Intrinsics.Width;
        //int depthHeight = (int)lastDepthData.Intrinsics.Height;
        // Flattened data to store all (X, Y, Z) for each pixel in a 1D array
        float[] flattenedData = new float[depthWidth * depthHeight * 3];

        //var cx = result_extras.Intrinsics.Value.PrincipalPoint.x;
        //var cy = result_extras.Intrinsics.Value.PrincipalPoint.y;
        //var fx = result_extras.Intrinsics.Value.FocalLength.x;
        //var fy = result_extras.Intrinsics.Value.FocalLength.y;

        int index = 0;
        for (int v = 0; v < depthHeight; v++)
        {
            for (int u = 0; u < depthWidth; u++)
            {
                // Step 5: Calculate the 3D coordinates (X, Y, Z) for pixel (u, v)
                Vector3 depthPoint = Calculate3DCoordinates(u, v);

                //// Step 6: Transform to the camera frame of reference
                //Vector3 transformedPoint = camera2depth_vector + relative_rotmat.MultiplyPoint3x4(depthPoint);

                // Step 7: Project the 3D point to 2D image plane (u_c, v_c)
                //if (transformedPoint.z != 0)
                //{
                //    float u_c = (fx * transformedPoint.x / transformedPoint.z) + cx;
                //    float v_c = (fy * transformedPoint.y / transformedPoint.z) + cy;

                //    // Store (u_c, v_c, Z) in the flattened array
                //    flattenedData[index++] = u_c;
                //    flattenedData[index++] = v_c;
                //    flattenedData[index++] = transformedPoint.z;

                    flattenedData[index++] = u;
                    flattenedData[index++] = v;
                    flattenedData[index++] = depthPoint.z;
                //}
                //else
                //{
                //    flattenedData[index++] = 0;
                //    flattenedData[index++] = 0;
                //    flattenedData[index++] = 0;
                //}
            }
        }

        // Step 8: Transmit the projected 2D points via WebSocket
        //await TransmitProjectedData(flattenedData);
        return flattenedData;
    }
    private void SaveMessageToTxtFile(string message)
    {
        try
        {
            // 生成唯一文件名，例如：received_20240427_153000.txt
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            string fileName = $"received_{timestamp}_{txtFileCounter}.txt";
            txtFileCounter++;

            string filePath = Path.Combine(txtFolderPath, fileName);
            File.WriteAllText(filePath, message);
            UpdateDebug($"Saved message to {filePath}", Color.green);
        }
        catch (Exception ex)
        {
            UpdateDebug($"Error saving message to file: {ex.Message}", Color.red);
        }
    }


}




//private Vector3 currentPosition_C;
//private Quaternion currentRotation_C;
//private Vector3 currentPosition_D;
//private Quaternion currentRotation_D;
//private MLDepthCamera.Data lastDepthData = default;

//I need to use these variables given above to perform the following pseudo algorithm

//extract current_rotation_C rotation matrix from quaternion, named rotmat_C
//camera2depth_vector = rotmat_C.transpose * (currentPosition_D-currentPosition_C)
//relative_rotmat = rotmat_D  * rotmat_C.transpose

//Now i want a function to caclulate the coordinates X, Y from the given data of the lastDepthData variable.
//It includes the intrinsics matrix and also the Z coordinate depth data.
//The coordinates on the image plane are u and v and the 3d coordinates on the depth camera frame of reference are calculated in the normal way like so:
//X = Z * u/ax , where ax is from the intrinsics calibration matrix top left corner, u is the pixel in width dimension and Z comes from the lastDepthdata data
//Y = Z * v/ay , where ay is from the intrinsics calibration matrix middle center , v is the pixel in height dimension and Z comes from the lastDepthdata data
//You can extract all information needed from the private MLDepthCamera.Data lastDepthData variable i think