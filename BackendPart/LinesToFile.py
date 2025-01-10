import os
import argparse

import numpy as np
import matplotlib.pyplot as plt
import cv2
import sys
import json
import sys
import json
import numpy as np
from sklearn.linear_model import LinearRegression, RANSACRegressor
from scipy.spatial.transform import Rotation as R

from utils.PostProcess import get_Boundaries_img, read_intrinsics, get_cropped_depth, extract_refined_3d_points, read_camera, project_points_3d_to_2d, plot_lines_with_depth, generate_final_lines

def GetLines(entry, roomnet_segmentation, json_path, depth_file_path, color_image_path, line_saved_folder, vis_roomline2d=False, vis_cropped_depth=False, DongGanGuangBo=False):
    
    with open(json_path, 'r') as file:
        metadata = json.load(file)
    
    try:
        # Load the depth data
        bad_depth_data = np.load(depth_file_path)
        print(f"Depth data shape: {bad_depth_data.shape}")

        # Ensure the file contains multiple layers and extract the last layer
        if bad_depth_data.ndim == 3:
            depth_data = bad_depth_data[:, :, -1]  # Extract the last layer
            print(f"Extracted real depth layer shape: {depth_data.shape}")
        else:
            print(f"Error: Depth data does not have the expected 3D shape. Shape: {bad_depth_data.shape}")
    except Exception as e:
        print(f"Error loading depth file: {e}")    



    unique_line_segments = get_Boundaries_img(roomnet_segmentation, distance_threshold=66)

    if vis_roomline2d:
        blank_image = np.ones((400, 400, 3), dtype=np.uint8) * 255  
        cmap = plt.cm.get_cmap('hsv', len(unique_line_segments) + 1)

        for idx, line in enumerate(unique_line_segments):
            (x1, y1), (x2, y2) = line
            color = tuple(int(255 * c) for c in cmap(idx)[:3])  
            cv2.line(blank_image, (x1, y1), (x2, y2), color, thickness=2)
        blank_image_rgb = cv2.cvtColor(blank_image, cv2.COLOR_BGR2RGB)

        plt.figure(figsize=(10, 10))
        plt.imshow(blank_image_rgb)
        plt.title("Unique Lines with Unique Colors")
        plt.axis('off')
        plt.show()

    color_intrinsics, depth_intrinsics = read_intrinsics(json_path)
    cropped_depth = get_cropped_depth(depth_data, depth_intrinsics, color_intrinsics, crop_size=400)

    if vis_cropped_depth:
        plot_lines_with_depth(cropped_depth, unique_line_segments, num_points=15)

    # num_lines, image_height, image_width, colors = read_camera(color_image_path, three_d_lines, False)

    three_d_lines_world = extract_refined_3d_points(metadata, unique_line_segments, cropped_depth, depth_intrinsics, num_points=500)
    final_lines = generate_final_lines(three_d_lines_world)
    print(f"Number of 3D lines: {len(final_lines)}")
    for i, line in enumerate(final_lines):
        print(f"Line {i}: Shape {line.shape}")
        print(f"Line {i}: First few points (world coordinates):\n{line[:5]}")
    
    
    if DongGanGuangBo:  
        
        def transform_world_to_camera(extrinsics, point_world):

            position = extrinsics["position"]
            rotation = extrinsics["rotation"]

            quat = [rotation["x"], rotation["y"], rotation["z"], rotation["w"]]
            rotation_matrix = R.from_quat(quat).as_matrix()

            p_world = np.array([point_world["x"], point_world["y"], point_world["z"]])

            p_camera = rotation_matrix.T.dot(p_world - np.array([position["x"], position["y"], position["z"]]))

            return {"x": p_camera[0], "y": p_camera[1], "z": p_camera[2]}

        def project_camera_to_image(point_camera, intrinsics):

            fx, fy = intrinsics['focal_length']['fx'], intrinsics['focal_length']['fy']
            cx, cy = intrinsics['principal_point']['cx'], intrinsics['principal_point']['cy']

            X, Y, Z = point_camera["x"], point_camera["y"], point_camera["z"]

            if Z <= 0:
                return None  

            u = (X * fx / Z) + cx
            v = (Y * fy / Z) + cy

            return (u, v)

        def project_world_lines_to_image(three_d_lines_world, extrinsics, intrinsics, image_path, output_path=None):


            color_img = cv2.imread(image_path, cv2.IMREAD_COLOR)
            if color_img is None:
                raise FileNotFoundError(f"Can not read: {image_path}")
            color_img = cv2.cvtColor(color_img, cv2.COLOR_BGR2RGB)

            plt.figure(figsize=(10, 10))
            plt.imshow(color_img)
            plt.axis('off')


            cmap = plt.get_cmap('tab20')
            num_colors = len(three_d_lines_world)
            colors = [cmap(i / num_colors) for i in range(num_colors)]

            for idx, line_world in enumerate(three_d_lines_world):
                projected_points = []
                for point_world in line_world:
            
                    point_world_dict = {"x": point_world[0], "y": point_world[1], "z": point_world[2]}

                    point_camera = transform_world_to_camera(extrinsics, point_world_dict)

                    uv = project_camera_to_image(point_camera, intrinsics)
                    if uv is not None:
                        projected_points.append(uv)
                
                if len(projected_points) >= 2:

                    projected_points = np.array(projected_points)

                    u, v = projected_points[:,0], projected_points[:,1]
                    valid_mask = (u >= 0) & (u < intrinsics["width"]) & (v >= 0) & (v < intrinsics["height"])
                    u, v = u[valid_mask], v[valid_mask]
                    # if len(u) >= 2:
                    #     plt.plot(u, v, color=colors[idx], linewidth=2, label=f'Line {idx+1}' if idx < 20 else None)

            if num_colors > 0:
                plt.legend(loc='upper right', fontsize='small', ncol=2)

            if output_path:
                plt.savefig(output_path, bbox_inches='tight')
            # plt.show()

        project_world_lines_to_image(final_lines, extrinsics=metadata["color_camera"]["extrinsics"],intrinsics=depth_intrinsics, image_path=color_image_path, output_path=None)


    def save_three_d_lines_to_txt(three_d_lines, filename=f'lines_{entry}.txt', line_saved_folder=line_saved_folder):
        filepath = os.path.join(line_saved_folder, filename)

        with open(filepath, 'w') as file:
            
            for line in three_d_lines:
                if len(line) < 2:
                    continue
                start_point = line[0]
                end_point = line[-1]
                
                start_str = f"{start_point[0]} {start_point[1]} {start_point[2]}"
                end_str = f"{end_point[0]} {end_point[1]} {end_point[2]}"
                
                file.write(start_str + "\n")
                file.write(end_str + "\n")

    save_three_d_lines_to_txt(final_lines)


