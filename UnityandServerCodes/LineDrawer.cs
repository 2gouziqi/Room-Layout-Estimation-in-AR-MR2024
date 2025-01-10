using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using MixedReality.Toolkit.UX;
using TMPro;
using MixedReality.Toolkit.SpatialManipulation;

public class LineDrawer : MonoBehaviour
{
    public Material lineMaterial;
    private float lineWidth = 0.05f;
    private float lineColorHue = 0f;
    private float lineOpacity = 1f;
    private bool linesVisible = true;
    private GameObject linesGroup;
    [SerializeField] private TextMeshProUGUI debugText;

    private string txtFolderPath;
    private HashSet<string> processedFiles = new HashSet<string>();

    


    void Awake()
    {
        // 设置文件夹路径
        txtFolderPath = Path.Combine(Application.persistentDataPath, "ReceivedTxtFiles");

        // 检查文件夹是否存在，如果不存在则创建
        if (!Directory.Exists(txtFolderPath))
        {
            Directory.CreateDirectory(txtFolderPath);
            Debug.Log($"[LineDrawer] 创建目录: {txtFolderPath}");
            if (debugText != null)
            {
                debugText.text = "创建目录: " + txtFolderPath;
            }
        }
        else
        {
            // 如果文件夹存在，删除所有 .txt 文件
            try
            {
                string[] existingFiles = Directory.GetFiles(txtFolderPath, "*.txt");
                foreach (string file in existingFiles)
                {
                    File.Delete(file);
                    Debug.Log($"[LineDrawer] 删除文件: {file}");
                    if (debugText != null)
                    {
                        debugText.text += "\n删除文件: " + file;
                    }
                }

                // 更新 Debug 文本
                if (debugText != null)
                {
                    if (existingFiles.Length > 0)
                    {
                        debugText.text = $"已删除 {existingFiles.Length} 个现有的 .txt 文件。";
                    }
                    else
                    {
                        debugText.text = "文件夹中没有现有的 .txt 文件。";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LineDrawer] 删除文件时发生错误: {ex.Message}");
                if (debugText != null)
                {
                    debugText.text = "删除文件时发生错误: " + ex.Message;
                }
            }
        }
    }


    void Start()
    {
        linesGroup = new GameObject("LinesGroup");

        txtFolderPath = Path.Combine(Application.persistentDataPath, "ReceivedTxtFiles");
        if (!Directory.Exists(txtFolderPath))
        {
            Directory.CreateDirectory(txtFolderPath);
            debugText.text = "Created directory at: " + txtFolderPath;
        }
        else
        {
            debugText.text = "Directory exists at: " + txtFolderPath;
        }
    }

    void Update()
    {
        if (!IntegratedCaptureAndSend.isSending)
        {
            CheckForNewFiles();

            foreach (Transform cylinder in linesGroup.transform)
            {
                // Get the current Y scale (height) of the cylinder
                float currentHeight = cylinder.transform.localScale.y;

                // Keep the X and Z scale constant for the thickness
                cylinder.transform.localScale = new Vector3(lineWidth, currentHeight, lineWidth);
            }
        }
    }

    private void CheckForNewFiles()
    {
        if (!Directory.Exists(txtFolderPath))
        {
            debugText.text = "Directory not found: " + txtFolderPath;
            return;
        }

        string[] files = Directory.GetFiles(txtFolderPath, "*.txt");
        debugText.text = "Found " + files.Length + " files in: " + txtFolderPath; 

        foreach (string file in files)
        {
            if (!processedFiles.Contains(file))
            {
                debugText.text += "\nProcessing file: " + file;
                ProcessFile(file);
                processedFiles.Add(file);
            }
        }
    }

    private void ProcessFile(string filePath)
    {
        try
        {
            string fileContents = File.ReadAllText(filePath);
            debugText.text += "\nSuccessfully read file: " + filePath;
            DrawLinesFromText(fileContents);
        }
        catch (Exception ex)
        {
            debugText.text += "\nError processing file " + filePath + ": " + ex.Message;
            Debug.LogError("Error processing file " + filePath + ": " + ex.Message);
        }
    }

    public void DrawLinesFromText(string fileContents)
    {
        if (string.IsNullOrEmpty(fileContents))
        {
            debugText.text += "\nReceived empty file content.";
            Debug.LogError("Received empty file content.");
            return;
        }

        string[] lines = fileContents.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        debugText.text += "\nFile contains " + lines.Length + " lines.";

        if (lines.Length < 2)
        {
            debugText.text += "\nNot enough points to form lines.";
            Debug.LogError("Not enough points to form lines.");
            return;
        }

        for (int i = 0, index = linesGroup.transform.childCount; i < lines.Length - 1; i += 2, index++)
        {
            try
            {
                Vector3 startPoint = StringToVector3(lines[i]);
                Vector3 endPoint = StringToVector3(lines[i + 1]);
                debugText.text += "\nDrawing line from " + startPoint + " to " + endPoint;
                DrawLineAsCylinder(startPoint, endPoint, index);
            }
            catch (Exception ex)
            {
                debugText.text += "\nError drawing line: " + ex.Message;
            }
        }
    }

    Vector3 StringToVector3(string line)
    {
        try
        {
            string[] values = line.Trim().Split(' ');
            if (values.Length != 3)
            {
                debugText.text += "\nInvalid line format: " + line;
                Debug.LogError("Invalid line format: " + line);
                return Vector3.zero;
            }

            float x = float.Parse(values[0], CultureInfo.InvariantCulture);
            float y = float.Parse(values[1], CultureInfo.InvariantCulture);
            float z = float.Parse(values[2], CultureInfo.InvariantCulture);

            return new Vector3(x, y, z);
        }
        catch (Exception ex)
        {
            debugText.text += "\nError parsing line to Vector3: " + line + ". " + ex.Message;
            Debug.LogError("Error parsing line to Vector3: " + line + ". " + ex.Message);
            return Vector3.zero;
        }
    }

    void DrawLineAsCylinder(Vector3 start, Vector3 end, int index)
    {
        try
        {
            Vector3 position = (start + end) / 2;
            Vector3 direction = (end - start).normalized;
            float distance = Vector3.Distance(start, end);

            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "Line_" + index;

            cylinder.transform.position = position;
            cylinder.transform.localScale = new Vector3(lineWidth, distance / 2, lineWidth);
            cylinder.transform.up = direction;

            Renderer renderer = cylinder.GetComponent<Renderer>();
            renderer.material = lineMaterial;
            renderer.material.color = Color.HSVToRGB(lineColorHue, 1f, 1f);
            renderer.material.color = new Color(renderer.material.color.r, renderer.material.color.g, renderer.material.color.b, lineOpacity);
            renderer.enabled = linesVisible;

            cylinder.transform.SetParent(linesGroup.transform);

            // Add ObjectManipulator component to the cylinder
            ObjectManipulator manipulator = cylinder.AddComponent<ObjectManipulator>();

            // Set the rotation anchors to be around the center
            manipulator.RotationAnchorNear = ObjectManipulator.RotateAnchorType.RotateAboutObjectCenter;
            manipulator.RotationAnchorFar = ObjectManipulator.RotateAnchorType.RotateAboutObjectCenter;

            // Disable any physics-based interactions
            Rigidbody rb = cylinder.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = cylinder.AddComponent<Rigidbody>();
            }

            rb.useGravity = false;   // Disable gravity
            rb.isKinematic = true;   // Disable physics interactions (like collisions and forces)

            debugText.text += "\nSuccessfully drew line: " + cylinder.name;
        }
        catch (Exception ex)
        {
            debugText.text += "\nError drawing cylinder line: " + ex.Message;
        }
    }
    public void OnLineWidthSliderMove(SliderEventData eventData)
    {
        lineWidth = eventData.NewValue;

        // Adjust the scale of all cylinders in the linesGroup
        foreach (Transform line in linesGroup.transform)
        {
            line.localScale = new Vector3(lineWidth, line.localScale.y, lineWidth);
        }
    }

    public void OnColorSliderMove(SliderEventData eventData)
    {
        lineColorHue = eventData.NewValue;

        // Update the line width based on the slider's value


        // Adjust the scale of all cylinders in the linesGroup
        foreach (Transform line in linesGroup.transform)
        {
            Renderer renderer = line.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color currentColor = renderer.material.color;
                Color lineColor = Color.HSVToRGB(lineColorHue, 1f, 1f);
                lineColor.a = currentColor.a;
                renderer.material.color = lineColor;
            }
        }
    }

    public void OnVisibilityToggle()
    {
        // Toggle Visibility parameter
        linesVisible = !linesVisible;

        // Loop through all cylinders in the linesGroup
        foreach (Transform line in linesGroup.transform)
        {
            Renderer renderer = line.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = linesVisible;
            }
        }
    }

    public void OnOpactiySliderMove(SliderEventData eventData)
    {
        lineOpacity = eventData.NewValue;

        foreach (Transform line in linesGroup.transform)
        {
            Renderer renderer = line.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Get the current material color
                Color currentColor = renderer.material.color;

                // Update the alpha (opacity) value
                currentColor.a = Mathf.Clamp01(lineOpacity);

                // Set the updated color back to the material
                renderer.material.color = currentColor;
            }
        }
    }

    public void ClearLines()
    {
        foreach (Transform child in linesGroup.transform)
        {
            Destroy(child.gameObject);
        }
    }
}
