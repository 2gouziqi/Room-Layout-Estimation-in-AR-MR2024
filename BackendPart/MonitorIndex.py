import time
import json
import os
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler
import cv2
import sys
from LinesToFile import GetLines
import warnings
warnings.filterwarnings("ignore")
import logging
import numpy as np
from tensorflow.keras.applications.convnext import preprocess_input
from utils.RoomNet import load_roomnet_model, visualize_results, imagecut


BASE_PATH = r"C:\\Users\\zoe\\Desktop\\mixed_reality\\Version3"

path_dict = {
    "backend_part": f"{BASE_PATH}\\BackendPart",
    "received_data": f"{BASE_PATH}\\received_data",
    "utils": f"{BASE_PATH}\\BackendPart\\utils",
    "lines": f"{BASE_PATH}\\monitored_folder"
}

roomnet_weights_path = f"{path_dict['backend_part']}\\Weight_ST_RroomNet_ConvNext.h5"
roomnet_ref_img = f"{path_dict['utils']}\\ref_img2.png"
INDEX_FILE_PATH = f"{path_dict['received_data']}\\index.json"
COMBINED_VERSION2_PATH = path_dict["backend_part"]

def load_path(entry):
    metadata_json_path = f"{path_dict['received_data']}\\metadata_{entry}.json"
    depth_file_path = f"{path_dict['received_data']}\\flattened_{entry}.npy"
    color_image_path = f"{path_dict['received_data']}\\color_{entry}.png"
    return metadata_json_path, depth_file_path, color_image_path

vis_roomnet = False
save_roomnet = False

vis_roomline2d = False
vis_cropped_depth = False
DongGanGuangBo = False

logging.info("Loading RoomNet model...")
roomnet_model = load_roomnet_model(roomnet_ref_img, roomnet_weights_path)

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    handlers=[logging.StreamHandler(sys.stdout)]
)

def GetRoomNetResult(entry, imgs_folder=path_dict['received_data']):

    os.environ['CUDA_VISIBLE_DEVICES'] = '-1'

    logging.info(f"Processing entry: {entry}")
    color_file = os.path.join(imgs_folder, f"color_{entry}.png")
    img_roomnet = cv2.imread(color_file)
    if img_roomnet is None:
        logging.error(f"Failed to read {color_file}.")
        sys.exit(1)

    logging.info(f"Successfully loaded image for entry: {entry}")
 
    color_img_cropped_roomnet = imagecut(img_roomnet)
    img_rgb = cv2.cvtColor(color_img_cropped_roomnet, cv2.COLOR_BGR2RGB)
    img_preprocessed = preprocess_input(img_rgb)
    img_preprocessed = np.expand_dims(img_preprocessed, axis=0)  

    # RoomNet prediction
    logging.info("Running RoomNet prediction...")
    roomnet_out = roomnet_model.predict(img_preprocessed)
    roomnet_out = np.rint(roomnet_out[0,:,:,0])
    roomnet_save_path = os.path.join(imgs_folder, f"saved_data\\roomnet_{entry}")
    roomnet_out_adjusted = roomnet_out - np.min(roomnet_out) + 1  

    if save_roomnet:
        np.save(roomnet_save_path, roomnet_out_adjusted)
        logging.info(f"RoomNet segmentation saved as NumPy file at {roomnet_save_path}")

    if vis_roomnet:
        visualize_results(img_rgb, roomnet_out)

    return roomnet_out_adjusted


def load_index():
    try:
        with open(INDEX_FILE_PATH, 'r') as f:
            data = json.load(f)
            return set(data)
    except Exception as e:
        print(f"Error loading index.json: {e}")
        return set()


def process_entry(entry):
    print(f"Processing new entry: {entry}")

    metadata_json_path, depth_file_path, color_image_path = load_path(entry)
    roomnet_result = GetRoomNetResult(entry)
    line_saved_folder=path_dict["lines"]
    GetLines(entry, roomnet_result, metadata_json_path, depth_file_path, color_image_path, line_saved_folder, vis_roomline2d, vis_cropped_depth, DongGanGuangBo)   

class IndexChangeHandler(FileSystemEventHandler):
    """Handler for monitoring changes to index.json."""

    def __init__(self, initial_entries):
        super().__init__()
        self.seen_entries = initial_entries

    def on_modified(self, event):
        """Called when index.json is modified."""
        if os.path.abspath(event.src_path) != os.path.abspath(INDEX_FILE_PATH):
            return  # Ignore other files

        print("Detected change in index.json. Checking for new entries...")
        current_entries = load_index()
        new_entries = current_entries - self.seen_entries

        if new_entries:
            for entry in new_entries:
                process_entry(entry)
            self.seen_entries.update(new_entries)
        else:
            print("No new entries found.")

def main():
    """Set up monitoring of index.json for new entries."""
    print("Starting MonitorIndex.py...")
    seen_entries = load_index()
    print(f"Loaded {len(seen_entries)} existing entries.")

    event_handler = IndexChangeHandler(seen_entries)
    observer = Observer()
    observer.schedule(event_handler, path=os.path.dirname(INDEX_FILE_PATH), recursive=False)
    observer.start()
    print("Monitoring started. Waiting for new entries...")

    try:
        while True:
            time.sleep(1)  # Keep the script running
    except KeyboardInterrupt:
        print("Stopping monitoring.")
        observer.stop()
    observer.join()

if __name__ == "__main__":
    main()
