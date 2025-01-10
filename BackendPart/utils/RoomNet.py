import os
os.environ['CUDA_VISIBLE_DEVICES'] = '-1'
import tensorflow as tf
import matplotlib.pyplot as plt
from tensorflow.keras.applications.convnext import ConvNeXtTiny
from tensorflow.keras.models import Model
from tensorflow.keras.layers import Dense
from utils.spatial_transformer import ProjectiveTransformer


def load_roomnet_model(ref_img_path, model_weights_path):

    # Load and preprocess the reference image
    ref_img = tf.io.read_file(ref_img_path)
    ref_img = tf.io.decode_png(ref_img)
    ref_img = tf.cast(ref_img, tf.float32) / 51.0  # Normalize
    ref_img = ref_img[tf.newaxis, ...]

    # Define base model and RoomNet architecture
    base_model = ConvNeXtTiny(include_top=False, weights="imagenet", input_shape=(400, 400, 3), pooling='avg')
    theta = Dense(8)(base_model.output)
    stl = ProjectiveTransformer((400, 400)).transform(ref_img, theta)
    model = Model(base_model.input, stl)

    # Load pre-trained weights
    model.load_weights(model_weights_path)
    print("RoomNet model loaded successfully.")
    return model


def visualize_results(original_image, predicted_output):

    fig, axes = plt.subplots(1, 2, figsize=(12, 6)) 
    axes[0].imshow(original_image)
    axes[0].set_title("Original Image")
    axes[0].axis("off") 

    axes[1].imshow(predicted_output)
    axes[1].set_title("Predicted Output")
    axes[1].axis("off")

    plt.tight_layout()
    plt.show()


def imagecut(img_init, crop_size=400):   
  height, width, _ = img_init.shape
  center_x, center_y = width // 2, height // 2
  x1 = center_x - crop_size // 2
  y1 = center_y - crop_size // 2
  x2 = x1 + crop_size
  y2 = y1 + crop_size
  img_cropped = img_init[y1:y2, x1:x2]
  return img_cropped

