# Room-Layout-Estimation-in-AR-MR2024

![演示视频](./assets/demo.gif)

This repository provides a streamlined pipeline for room layout estimation in AR, optimized for Magic Leap 2. By integrating lightweight 2D boundary segmentation (ST-RoomNet), refined 2D and 3D line generation, and user-centric rendering, the system delivers robust performance and intuitive interaction. Clutter is reduced and structural clarity is enhanced, resulting in a comfortable, user-friendly experience. Future directions include exploring long-range depth sensing, advanced room layout models, and improved AR interfaces.

## 1. Environment Setup
1. Clone this repository to your local machine.
4. Install dependencies with:
   ```bash
   pip install -r requirements.txt

## 2. Testing the Backend Part Locally

1. Open and run **`test.ipynb`** to see the entire process demonstrated with an example dataset.
2. This notebook walks through:
   - Initializing the environment
   - Loading images and metadata
   - Estimating layouts
   - Rendering results for inspection

## 3. Running on Magic Leap 2

For real-world usage on Magic Leap 2, follow these steps:

1. **Start the Monitoring Script**  
   Run **`run_monitor.bat`** to begin monitoring for new images. This script continuously checks for fresh data coming from the Magic Leap 2 device.

2. **Run the Data-Capture Script**  
   Execute **`newtxt.py`** in parallel. This script handles capturing new images from the device.

3. **Automatic Indexing**  
   Each newly captured image is automatically assigned an index in **`index.json`** located under the **`received_data`** folder, alongside:
   - The color image  
   - The depth image  
   - Associated metadata  

4. **Rendering**  
   Once the new images and metadata are received, the room layout estimation and rendering pipeline will process them for visualization.
  
## 4. Testing the Workflow Locally

1. Run **`run_monitor.bat`** and **`newtxt.py`** as explained above.
2. Manually add an index in **`index.json`**.
3. **Important Note**: You may find slight differences between how rendered lines are plotted here compared to the methods shown in Section 2. This is to make sure that the lines are rendered correctly in Magic Leap 2 because of coordinate convention differenences.

**Enjoy exploring room layout estimation for AR/MR applications!**
