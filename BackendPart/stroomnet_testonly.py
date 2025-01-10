import os
import cv2
import sys
import warnings
warnings.filterwarnings("ignore")
import logging
import numpy as np
from tensorflow.keras.applications.convnext import preprocess_input
from utils.RoomNet import load_roomnet_model, visualize_results, imagecut


BASE_PATH = r"C:\\Users\\zoe\\Desktop\\mixed_reality\\Version3"

path_dict = {
    "backend_part": f"{BASE_PATH}\\BackendPart",
    "received_data": f"{BASE_PATH}\\report",
    "utils": f"{BASE_PATH}\\BackendPart\\utils",
    "lines": f"{BASE_PATH}\\monitored_folder"
}

roomnet_weights_path = f"{path_dict['backend_part']}\\Weight_ST_RroomNet_ConvNext.h5"
roomnet_ref_img = f"{path_dict['utils']}\\ref_img2.png"


vis_roomnet = True
save_roomnet = True

roomnet_model = load_roomnet_model(roomnet_ref_img, roomnet_weights_path)

def GetRoomNetResult(entry, imgs_folder=path_dict['received_data']):

    os.environ['CUDA_VISIBLE_DEVICES'] = '-1'

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

GetRoomNetResult('3716ab1b-ec61-4299-82a5-1f0c69fb0149')


# report\color_5b0484d9-e493-4ec6-87a0-6a19be04709e.png
# report\color_6a656b57-5605-46c4-9054-010dd2649fa1.png
# report\color_5742d836-8b73-4d23-ac96-07904bbfed6f.png
# report\color_a6967d6c-9439-4d46-ba50-f2672d6fda55.png
# report\color_bb14eff2-813f-47e3-95a5-a408cd378e9b.png
# report\color_f7f8a23d-2f95-486c-8487-53ebe22c86c6.png
# report\color_f3977b9d-609b-47a2-8292-a1b64e202df3.png
# report\color_3716ab1b-ec61-4299-82a5-1f0c69fb0149.png
# report\color_19173d43-17d3-4ebb-b0d5-7e9a4aec49e9.png