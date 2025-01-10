import numpy as np
import matplotlib.pyplot as plt
from skimage.segmentation import find_boundaries
import cv2
from sklearn.decomposition import PCA
from pylsd.lsd import lsd
import json
import matplotlib.cm as cm
from scipy.spatial.transform import Rotation as R
from sklearn.cluster import DBSCAN 
from sklearn.linear_model import LinearRegression, RANSACRegressor      

def Boundaries(roomnet_segmentation):
    roomnet_boundaries = find_boundaries(roomnet_segmentation, mode='thick', connectivity=1, background=0)
    # plt.imshow(roomnet_boundaries, cmap='gray')
    # plt.axis('off')
    # plt.show()

    boundary_image = (roomnet_boundaries.astype(np.uint8)) * 255

    kernel = np.ones((3,3), np.uint8)
    boundary_image = cv2.dilate(boundary_image, kernel, iterations=1)
    boundary_image = cv2.erode(boundary_image, kernel, iterations=1)

    linesLSD = lsd(boundary_image.astype(np.float32))
    if linesLSD is None:
        lines = np.empty((0,4), dtype=int)
    else:
        lines = linesLSD[:, :4].astype(int) 
    print(f"Number of Lines Detected: {len(lines)}")

    return lines


def group_lines_by_orientation_and_distance(initial_lines, orientation_threshold=15, distance_threshold=25):

    def line_orientation(line):
        x1, y1, x2, y2 = line
        angle = np.degrees(np.arctan2((y2 - y1), (x2 - x1)))
        # Normalize angle to [0, 180)
        angle = angle % 180
        return angle

    def shortest_distance_between_segments(l1, l2):
        def point_line_segment_dist(px, py, ax, ay, bx, by):
            ABx, ABy = (bx - ax), (by - ay)
            APx, APy = (px - ax), (py - ay)
            magAB2 = ABx**2 + ABy**2
            if magAB2 == 0.0:
                return np.hypot(px - ax, py - ay)
            t = (APx * ABx + APy * ABy) / magAB2
            # Clamp t to [0, 1] so it lies on the segment
            t = max(0.0, min(1.0, t))
            # Projection coordinates
            projx = ax + t * ABx
            projy = ay + t * ABy
            return np.hypot(px - projx, py - projy)

        x1, y1, x2, y2 = l1
        x3, y3, x4, y4 = l2

        # Distances to check:
        d1 = point_line_segment_dist(x1, y1, x3, y3, x4, y4)
        d2 = point_line_segment_dist(x2, y2, x3, y3, x4, y4)
        d3 = point_line_segment_dist(x3, y3, x1, y1, x2, y2)
        d4 = point_line_segment_dist(x4, y4, x1, y1, x2, y2)

        return min(d1, d2, d3, d4)

    # --------------------------------------------------
    # 2. GROUP BY ORIENTATION
    # --------------------------------------------------
    orientation_groups = []  # Each element is (rep_angle, [list_of_lines])

    for line in initial_lines:
        angle_line = line_orientation(line)

        # Try to place the line into an existing orientation bin
        placed = False
        for idx, (rep_angle, lines_in_bin) in enumerate(orientation_groups):
            # Calculate minimal angular difference considering wrap-around at 180 degrees
            angle_diff = abs(angle_line - rep_angle)
            minimal_diff = min(angle_diff, 180 - angle_diff)

            if minimal_diff <= orientation_threshold:
                # If within orientation_threshold, add to this group
                lines_in_bin.append(line)
                placed = True
                break

        if not placed:
            # Create a new orientation group with this line
            orientation_groups.append((angle_line, [line]))

    # --------------------------------------------------
    # 3. SPLIT EACH ORIENTATION GROUP BY DISTANCE
    # --------------------------------------------------
    all_groups = []  # Will hold final groups across all orientation bins

    for rep_angle, lines_in_bin in orientation_groups:
        distance_groups = []  # Subgroups for lines with same orientation

        for line in lines_in_bin:
            found_group = False
            for dg in distance_groups:
                # If line is close (< distance_threshold) to any line in dg, add it
                if any(shortest_distance_between_segments(line, member) < distance_threshold for member in dg):
                    dg.append(line)
                    found_group = True
                    break

            if not found_group:
                # Create a new distance-based subgroup
                distance_groups.append([line])

        # Append all distance-based subgroups to the global list
        all_groups.extend(distance_groups)

    # --------------------------------------------------
    # 4. PLOT EACH GROUP IN A DIFFERENT COLOR
    # --------------------------------------------------
    # plt.figure(figsize=(8, 8))
    # plt.axis('equal')  # Keep x/y scale consistent if desired

    # num_groups = len(all_groups)
    # if num_groups > 0:
    #     cmap = cm.get_cmap('hsv')  # Continuous colormap
    #     colors = [cmap(i / num_groups) for i in range(num_groups)]
    # else:
    #     colors = []

    # for g_idx, group in enumerate(all_groups):
    #     group_color = colors[g_idx]
    #     for line in group:
    #         x1, y1, x2, y2 = line
    #         plt.plot([x1, x2], [y1, y2], color=group_color, linewidth=2)
    #         plt.scatter([x1, x2], [y1, y2], color=group_color, s=30)

    # plt.title(f"Grouped Lines (Orientation ±{orientation_threshold}°, Dist < {distance_threshold})")
    # plt.gca().invert_yaxis()  # Adjust based on image coordinate system
    # plt.show()

    # Optionally, return all_groups if needed
    return all_groups


def create_unique_lines_and_plot(all_groups, data):

    unique_lines = []

    for group in all_groups:
        if not group:
            continue  # Skip empty groups

        # Extract all endpoints from the group
        points = []
        for line in group:
            x1, y1, x2, y2 = line
            points.append((x1, y1))
            points.append((x2, y2))
        points = np.array(points)

        # Calculate the centroid of all points
        centroid = points.mean(axis=0)

        # Perform Principal Component Analysis (PCA) to find the main direction
        centered_points = points - centroid
        covariance_matrix = np.cov(centered_points, rowvar=False)
        eigenvalues, eigenvectors = np.linalg.eigh(covariance_matrix)
        principal_component = eigenvectors[:, np.argmax(eigenvalues)]

        # Project all points onto the principal component
        projections = centered_points @ principal_component

        # Find the minimum and maximum projections
        min_proj = projections.min()
        max_proj = projections.max()

        # Calculate the endpoints of the representative line
        start_point = centroid + principal_component * min_proj
        end_point = centroid + principal_component * max_proj

        # Convert to integer coordinates and format as tuples of tuples
        unique_line = (
            (int(start_point[0]), int(start_point[1])),
            (int(end_point[0]), int(end_point[1]))
        )
        unique_lines.append(unique_line)

    return unique_lines


def get_Boundaries_img(roomnet_segmentation, distance_threshold=10):
    lines = Boundaries(roomnet_segmentation)
    all_groups = group_lines_by_orientation_and_distance(lines, orientation_threshold=5, distance_threshold=20)
    unique_lines = create_unique_lines_and_plot(all_groups, roomnet_segmentation)
    return unique_lines


def extract_intrinsics(camera):
    intrinsics = camera["intrinsics"]
    width = intrinsics["width"]
    height = intrinsics["height"]
    fov = intrinsics["fov"]
    focal_length = {
        "fx": intrinsics["focalLength"]["fx"],
        "fy": intrinsics["focalLength"]["fy"]
    }
    principal_point = {
        "cx": intrinsics["principalPoint"]["cx"],
        "cy": intrinsics["principalPoint"]["cy"]
    }
    return {
        "width": width,
        "height": height,
        "fov": fov,
        "focal_length": focal_length,
        "principal_point": principal_point
    }


def read_intrinsics(metadata_json_path):
    with open(metadata_json_path, 'r') as f:
        metadata = json.load(f)
    color_intrinsics = extract_intrinsics(metadata["color_camera"])
    depth_intrinsics = extract_intrinsics(metadata["depth_camera"])
    return color_intrinsics, depth_intrinsics


def extract_extrinsics(metadata):
    extrinsics = metadata["extrinsics"]

    position = {
        "x": extrinsics["position"]["x"],
        "y": extrinsics["position"]["y"],
        "z": extrinsics["position"]["z"]
    }

    # Extract rotation (quaternion)
    rotation = {
        "x": extrinsics["rotation"]["x"],
        "y": extrinsics["rotation"]["y"],
        "z": extrinsics["rotation"]["z"],
        "w": extrinsics["rotation"]["w"]
    }

    return {
        "position": position,
        "rotation": rotation
    }


def transform_camera_to_world(extrinsics, point_camera):

    # Extract position and rotation
    position = extrinsics["position"]
    rotation = extrinsics["rotation"]

    # Convert quaternion to rotation matrix
    quat = [rotation["x"], rotation["y"], rotation["z"], rotation["w"]]
    rotation_matrix = R.from_quat(quat).as_matrix()

    p_camera = np.array([point_camera["x"], point_camera["y"], point_camera["z"]])
    p_world = rotation_matrix.dot(p_camera) + np.array([position["x"], position["y"], position["z"]])

    # Return as dictionary
    return {"x": p_world[0], "y": p_world[1], "z": p_world[2]}


def depth_to_point_cloud(depth, intrinsics):
    fx, fy = intrinsics['focal_length']['fx'], intrinsics['focal_length']['fy']
    cx, cy = intrinsics['principal_point']['cx'], intrinsics['principal_point']['cy']
    height, width = depth.shape
    i, j = np.meshgrid(np.arange(height), np.arange(width), indexing='ij')
    z = depth
    x = (j - cx) * z / fx
    y = (i - cy) * z / fy  
    y += 0.3

    points = np.stack((x, y, z), axis=-1)  
    valid_mask = points[..., 2] > 0  

    return points, valid_mask, i, j


def project_to_image(points, intrinsics):
    fx, fy = intrinsics['focal_length']['fx'], intrinsics['focal_length']['fy']
    cx, cy = intrinsics['principal_point']['cx'], intrinsics['principal_point']['cy']
    
    x = points[..., 0]
    y = points[..., 1]
    z = points[..., 2]

    epsilon = 1e-6
    z_safe = np.where(z > 0, z, epsilon)

    u = (x * fx) / z_safe + cx
    v = (y * fy) / z_safe + cy
    u = np.round(u).astype(int)
    v = np.round(v).astype(int)

    return u, v


def align_depth_to_color(depth_data, depth_intrinsics, color_intrinsics):

    points, valid_depth_mask, i_indices, j_indices = depth_to_point_cloud(
        depth_data, 
        depth_intrinsics
    )
    
    color_u, color_v = project_to_image(points, color_intrinsics)
    
    color_height, color_width = color_intrinsics["height"], color_intrinsics["width"]
    aligned_depth = np.zeros((color_height, color_width), dtype=depth_data.dtype)

    flat_color_u = color_u.flatten()
    flat_color_v = color_v.flatten()
    flat_depth = depth_data.flatten()
    flat_valid_depth = valid_depth_mask.flatten()
    flat_i = i_indices.flatten()
    flat_j = j_indices.flatten()
    
    valid_projection_mask = (
        (flat_color_u >= 0) & (flat_color_u < color_width) &
        (flat_color_v >= 0) & (flat_color_v < color_height) &
        flat_valid_depth
    )
    
    depth_i = flat_i[valid_projection_mask]
    depth_j = flat_j[valid_projection_mask]
    
    color_i = flat_color_v[valid_projection_mask]
    color_j = flat_color_u[valid_projection_mask]
    
    aligned_depth[color_i, color_j] = depth_data[depth_i, depth_j]
    
    return aligned_depth


def crop_center(image, target_height, target_width):

    height, width = image.shape[:2]
    center_y, center_x = height // 2, width // 2

    start_y = max(center_y - target_height // 2, 0)
    start_x = max(center_x - target_width // 2, 0)

    end_y = start_y + target_height
    end_x = start_x + target_width

    return image[start_y:end_y, start_x:end_x]


def get_cropped_depth(depth_data, depth_intrinsics, color_intrinsics, crop_size=400):
   
    aligned_depth = align_depth_to_color(
    depth_data, 
    depth_intrinsics, 
    color_intrinsics
    )
    print(f"Aligned depth shape: {aligned_depth.shape}")

    cropped_depth = crop_center(aligned_depth, crop_size, crop_size)

    return cropped_depth

# def generate_straight_line_pca(points, num_new_points=50):

#     if len(points) < 2:
#         raise ValueError("Less than two points for PCA")
    
#     pca = PCA(n_components=1)
#     pca.fit(points)
#     line_direction = pca.components_[0]
#     line_point = pca.mean_

#     projections = np.dot(points - line_point, line_direction)
#     min_proj, max_proj = projections.min(), projections.max()

#     t_new = np.linspace(min_proj, max_proj, num_new_points)
#     interpolated_points = line_point + np.outer(t_new, line_direction)
    
#     return interpolated_points


# def remove_outliers_pca(points, threshold=0.1):

#     if len(points) < 2:
#         return points

#     pca = PCA(n_components=1)
#     pca.fit(points)
#     line_direction = pca.components_[0]
#     line_point = pca.mean_

#     vectors = points - line_point
#     projections = np.dot(vectors, line_direction)[:, np.newaxis] * line_direction
#     perpendicular_vectors = vectors - projections
#     distances = np.linalg.norm(perpendicular_vectors, axis=1)

#     inliers = points[distances <= threshold]
#     return inliers


import numpy as np
from sklearn.linear_model import LinearRegression, RANSACRegressor
from scipy.spatial.transform import Rotation as R

def refine_line_z(points_camera_array, 
                 outlier_threshold=0.02, 
                 use_ransac=True, 
                 top_n=200,
                 clustering_eps=0.05,
                 clustering_min_samples=2):
    """
    Refines the z-values of a set of 3D points by fitting a line, removing outliers,
    and ensuring that proximate points in the x,y plane share the same depth value.

    Before refinement, selects the top_n points with the largest depth (z) values.

    Parameters:
    - points_camera_array: np.ndarray of shape (N, 3)
        The input array of 3D points (x, y, z).
    - outlier_threshold: float, optional (default=0.02)
        Threshold to determine outliers in z-values.
    - use_ransac: bool, optional (default=True)
        Whether to use RANSAC for robust line fitting.
    - top_n: int, optional (default=20)
        Number of top points to select based on largest z-values.
    - clustering_eps: float, optional (default=0.05)
        The maximum distance between two samples for one to be considered
        as in the neighborhood of the other in DBSCAN.
    - clustering_min_samples: int, optional (default=2)
        The number of samples in a neighborhood for a point to be considered
        as a core point in DBSCAN.

    Returns:
    - refined_points: np.ndarray of shape (min(N, top_n), 3)
        The refined set of 3D points after outlier removal and clustering.
    """
    if points_camera_array.shape[1] != 3:
        raise ValueError("Input points_camera_array must have shape (N, 3)")

    # Step 1: Select the top_n points with the largest z-values
    sorted_indices = np.argsort(points_camera_array[:, 2])[::-1]  # Descending order
    top_indices = sorted_indices[:top_n] if len(sorted_indices) >= top_n else sorted_indices
    top_points = points_camera_array[top_indices]

    # Extract x, y, z coordinates
    xs = top_points[:, 0]
    ys = top_points[:, 1]
    zs = top_points[:, 2]
    
    # Define the start and end points based on the sorted subset
    x_start, y_start = xs[0], ys[0]
    x_end, y_end = xs[-1], ys[-1]
    line_vec = np.array([x_end - x_start, y_end - y_start])
    line_length = np.linalg.norm(line_vec)
    if line_length < 1e-9:
        # If the line length is too small, return the top_points as is
        return top_points.copy()

    # Calculate the direction vector of the line
    line_dir = line_vec / line_length  
    dx, dy = line_dir

    # Project points onto the line to get parameter t
    ts = (xs - x_start) * dx + (ys - y_start) * dy

    if use_ransac:
        # Reshape ts for sklearn compatibility
        ts_reshape = ts.reshape(-1, 1)
        ransac = RANSACRegressor(LinearRegression(), 
                                 residual_threshold=outlier_threshold, 
                                 max_trials=100)
        ransac.fit(ts_reshape, zs)
        z_pred = ransac.predict(ts_reshape)
    else:
        # Median-based outlier removal
        z_median = np.median(zs)
        abs_dev = np.abs(zs - z_median)
        inliers_mask = abs_dev < outlier_threshold
        ts_in = ts[inliers_mask].reshape(-1, 1)
        zs_in = zs[inliers_mask]
        if len(zs_in) < 2:
            # Not enough inliers to fit a line; use original z-values
            z_pred = zs.copy()
        else:
            # Fit a linear regression to inliers
            lin_reg = LinearRegression()
            lin_reg.fit(ts_in, zs_in)
            z_pred = lin_reg.predict(ts.reshape(-1, 1))
    
    # Combine the original x and y with the refined z
    refined_points = np.column_stack((xs, ys, z_pred))

    # Step 2: Cluster points in the x,y plane to ensure proximate points share the same z
    # Define the feature space for clustering (x and y coordinates)
    xy_features = refined_points[:, :2]

    # Initialize DBSCAN with specified parameters
    dbscan = DBSCAN(eps=clustering_eps, min_samples=clustering_min_samples)
    cluster_labels = dbscan.fit_predict(xy_features)

    # Handle noise points (cluster_label == -1)
    # For noise points, we can choose to keep their z as is or handle them separately
    # Here, we'll keep their z values unchanged

    # Iterate over each cluster and assign the median z value to all points in the cluster
    unique_clusters = set(cluster_labels)
    for cluster in unique_clusters:
        if cluster == -1:
            # Noise point; skip or handle separately if needed
            continue
        # Find indices of points in the current cluster
        cluster_indices = np.where(cluster_labels == cluster)[0]
        # Compute the median z value for the cluster
        median_z = np.max(refined_points[cluster_indices, 2])
        # Assign the median z value to all points in the cluster
        refined_points[cluster_indices, 2] = median_z

    return refined_points


def extract_refined_3d_points(metadata, unique_line_segments, cropped_depth, intrinsics, num_points=15):
    extrinsics = extract_extrinsics(metadata["color_camera"])
   
    three_d_lines_world = []
    fx, fy = intrinsics['focal_length']['fx'], intrinsics['focal_length']['fy']
    cx, cy = intrinsics['principal_point']['cx'], intrinsics['principal_point']['cy']

    orig_width = intrinsics["width"]
    orig_height = intrinsics["height"]

    crop_size = 400
    center_x, center_y = orig_width // 2, orig_height // 2
    x1 = center_x - crop_size // 2
    y1 = center_y - crop_size // 2 
    
    all_colors = []
    line_id = 0

    for line in unique_line_segments:
        (x1_cropped, y1_cropped), (x2_cropped, y2_cropped) = line
        
        x_points = np.linspace(x1_cropped, x2_cropped, num_points)
        y_points = np.linspace(y1_cropped, y2_cropped, num_points)
        points_3d_camera = []

        for x_cropped, y_cropped in zip(x_points, y_points):
            x_int_cropped = int(round(x_cropped))
            y_int_cropped = int(round(y_cropped))
            if 0 <= x_int_cropped < cropped_depth.shape[1] and 0 <= y_int_cropped < cropped_depth.shape[0]:
                z = cropped_depth[y_int_cropped, x_int_cropped]-0.6
                if z > 0:
                    x_int_orig = x_int_cropped + x1
                    y_int_orig = y_int_cropped + y1
                    z = z
                    # X = (x_int_orig - cx) * z / fx
                    # Y = (y_int_orig - cy) * z / fy
                    point_camera = {"x": x_int_orig, "y": y_int_orig, "z": z}
                    points_3d_camera.append(point_camera)

        if len(points_3d_camera) >= 2:
            points_camera_array = np.array([[p['x'], p['y'], p['z']] for p in points_3d_camera])
            refined_line_points = refine_line_z(points_camera_array, outlier_threshold=0.01, use_ransac=True)

            interpolated_points_world = []
            for point in refined_line_points:
                point_camera = {"x": (point[0]-cx)*point[2]/fx, "y": (point[1]-cy)*point[2]/fy, "z": point[2]}
                point_world = transform_camera_to_world(extrinsics, point_camera)
                interpolated_points_world.append([point_world["x"], point_world["y"], point_world["z"]])

            three_d_lines_world.append(np.array(interpolated_points_world))
            all_colors.append(line_id)
            line_id += 1
            
    return three_d_lines_world

    # if plot:
    #     color_img = cv2.imread(color_image_path, cv2.IMREAD_COLOR)
    #     color_img = cv2.cvtColor(color_img, cv2.COLOR_BGR2RGB)
        
    #     plt.figure(figsize=(10, 10))
    #     plt.imshow(color_img)
    #     plt.scatter(x_int_orig_list, y_int_orig_list_list, c='red', s=10, label='Original Projected Points')

    #     for refined_points in refined_line_points_list:
    #         if refined_points.ndim != 2 or refined_points.shape[1] != 3:
    #             print(f"Skipping invalid segment with shape {refined_points.shape}")
    #             continue  

    #         refined_X = refined_points[:, 0]
    #         refined_Y = refined_points[:, 1]
    #         refined_Z = refined_points[:, 2]

    #         non_zero_mask = refined_Z > 1e-9
    #         if not np.any(non_zero_mask):
    #             continue 

    #         refined_u = (refined_X[non_zero_mask] * fx / refined_Z[non_zero_mask]) + cx
    #         refined_v = (refined_Y[non_zero_mask] * fy / refined_Z[non_zero_mask]) + cy

    #         refined_u = refined_u[(refined_u >= 0) & (refined_u < intrinsics["width"])]
    #         refined_v = refined_v[(refined_v >= 0) & (refined_v < intrinsics["height"])]

    #         plt.scatter(refined_u, refined_v, c='blue', s=10, label='Refined Projected Points')

    #     plt.title("Projected Points on Original Image - Before & After Refinement")
    #     plt.legend()
    #     plt.axis('off')
    #     plt.show()


def project_points_3d_to_2d(points_3d, intrinsics):

    fx, fy = intrinsics['focal_length']['fx'], intrinsics['focal_length']['fy']
    cx, cy = intrinsics['principal_point']['cx'], intrinsics['principal_point']['cy']
    
    X = points_3d[:, 0]
    Y = points_3d[:, 1]
    Z = points_3d[:, 2]
    
    Z[Z == 0] = 1e-6
    
    u = (fx * X) / Z + cx
    v = (fy * Y) / Z + cy
    
    points_2d = np.stack([u, v], axis=-1)
    return points_2d

def read_camera(color_image_path, three_d_lines, plot=False):
    color_image = cv2.imread(color_image_path)
    color_image = cv2.cvtColor(color_image, cv2.COLOR_BGR2RGB)
    image_height, image_width, _ = color_image.shape

    cmap = plt.get_cmap('viridis')
    num_lines = len(three_d_lines)
    colors = cmap(np.linspace(0, 1, num_lines))


    # plt.figure(figsize=(10, 8))
    # plt.imshow(color_image)
    # plt.axis('off')
    
    return num_lines, image_height, image_width, colors



def plot_lines_with_depth(cropped_depth, unique_line_segments, num_points=30):

    # Normalize the depth image for visualization
    depth_visual = (cropped_depth - np.min(cropped_depth)) / (np.max(cropped_depth) - np.min(cropped_depth))
    depth_visual = (depth_visual * 255).astype(np.uint8)
    depth_colored = cv2.applyColorMap(depth_visual, cv2.COLORMAP_JET)

    plt.figure(figsize=(10, 10))
    plt.imshow(depth_colored, cmap='jet')
    plt.title("Cropped Depth with Line Points and Depths")

    for line in unique_line_segments:
        (x1, y1), (x2, y2) = line

        # Draw the line
        cv2.line(depth_colored, (int(x1), int(y1)), (int(x2), int(y2)), color=(255, 255, 255), thickness=2)

        # Divide the line into points
        x_points = np.linspace(x1, x2, num_points)
        y_points = np.linspace(y1, y2, num_points)

        for x, y in zip(x_points, y_points):
            # Get the depth value at each point
            x_int, y_int = int(round(x)), int(round(y))
            if 0 <= x_int < cropped_depth.shape[1] and 0 <= y_int < cropped_depth.shape[0]:
                depth = cropped_depth[y_int, x_int]
                plt.text(x, y, f"{depth:.2f}", color="white", fontsize=8, ha='center', va='center', bbox=dict(facecolor='black', alpha=0.5))

            # Mark the point on the line
            plt.plot(x, y, 'ro', markersize=4)

    plt.axis('off')
    plt.show()



import numpy as np
from sklearn.decomposition import PCA

def fit_line_pca(points):

    C = np.mean(points, axis=0)
    X_centered = points - C
    
    pca = PCA(n_components=1)
    pca.fit(X_centered)
    
    D = pca.components_[0]
    ts = X_centered.dot(D)
    
    t_min, t_max = ts.min(), ts.max()
    
    start_point = C + t_min * D
    end_point = C + t_max * D
    
    line_segment = np.vstack((start_point, end_point))
    
    return line_segment

def generate_final_lines(three_d_lines_world):

    final_lines = []
    for idx, line_points in enumerate(three_d_lines_world):
        if line_points.shape[0] < 2:
            print(f"Line {idx} has less than 2 points. Skipping.")
            continue
        line_segment = fit_line_pca(line_points)
        final_lines.append(line_segment)
    return final_lines