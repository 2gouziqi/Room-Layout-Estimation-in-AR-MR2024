@echo off

REM Suppress TensorFlow numerical warnings (if applicable)
set TF_ENABLE_ONEDNN_OPTS=0

REM Navigate to the virtual environment Scripts directory
cd "C:\Users\zoe\Desktop\mixed_reality\Version3\BackendPart\st_roomnet_env\Scripts"

REM Activate the virtual environment using activate.bat
call activate.bat

REM Navigate to the directory containing the monitoring script
cd "C:\Users\zoe\Desktop\mixed_reality\Version3\BackendPart"

REM Run the MonitorIndex.py script
echo Starting MonitorIndex.py to watch for new entries in index.json...
python MonitorIndex.py

REM Pause to keep the batch script window open if MonitorIndex.py exits
pause
