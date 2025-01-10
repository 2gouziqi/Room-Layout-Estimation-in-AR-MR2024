import asyncio
import websockets
import json
import numpy as np
import cv2
import os
import threading
import concurrent.futures

# Define depth map dimensions (adjust according to your needs)
DEPTH_WIDTH = 544
DEPTH_HEIGHT = 480
EXPECTED_FLATTENED_LENGTH = DEPTH_WIDTH * DEPTH_HEIGHT * 3

buffers = {}
chunks_received = {}
total_chunks_map = {}
stop_event = threading.Event()

# Global list to record the message_id of each received data
all_received_ids = []

# Create a thread pool executor for handling blocking operations
executor = concurrent.futures.ThreadPoolExecutor(max_workers=4)

# Folder to monitor for new .txt files
MONITOR_FOLDER = os.path.join(os.getcwd(), "monitored_folder")
# Directory to store received data
SAVE_DIR = os.path.join(os.getcwd(), "received_data")
os.makedirs(SAVE_DIR, exist_ok=True)  # Ensure directory exists

# Path to the index.json file
INDEX_FILE_PATH = os.path.join(SAVE_DIR, "index.json")

def update_index_file(message_id):
    """
    Updates the received_data/index.json file to include the given message_id.
    """
    try:
        # Check if index.json exists, if not, create a new list
        if os.path.exists(INDEX_FILE_PATH):
            with open(INDEX_FILE_PATH, 'r', encoding='utf-8') as f:
                index_data = json.load(f)
        else:
            index_data = []

        # Add the new message_id if it doesn't exist in the list
        if message_id not in index_data:
            index_data.append(message_id)
            with open(INDEX_FILE_PATH, 'w', encoding='utf-8') as f:
                json.dump(index_data, f, ensure_ascii=False, indent=4)
            print(f"Updated index file with message_id: {message_id}")
        else:
            print(f"Message ID {message_id} is already in the index file.")
    except Exception as e:
        print(f"Error updating index file: {e}")

def delete_existing_txt_files():
    """
    Deletes all .txt files in the MONITOR_FOLDER directory.
    """
    if not os.path.exists(MONITOR_FOLDER):
        os.makedirs(MONITOR_FOLDER)

    for filename in os.listdir(MONITOR_FOLDER):
        if filename.endswith(".txt"):
            file_path = os.path.join(MONITOR_FOLDER, filename)
            try:
                os.remove(file_path)
                print(f"Deleted existing file: {file_path}")
            except Exception as e:
                print(f"Error while deleting file {file_path}: {e}")


async def monitor_and_send_txt_files(websocket):
    """
    Monitors the MONITOR_FOLDER for new .txt files and sends them to the connected WebSocket client.
    """
    existing_files = set(os.listdir(MONITOR_FOLDER))
    print(f"Started monitoring folder for new .txt files: {MONITOR_FOLDER}")
    
    try:
        while not stop_event.is_set():
            current_files = set(os.listdir(MONITOR_FOLDER))
            new_files = current_files - existing_files
            
            for filename in new_files:
                if filename.endswith(".txt"):
                    file_path = os.path.join(MONITOR_FOLDER, filename)
                    try:
                        with open(file_path, 'r', encoding='utf-8') as f:
                            content = f.read()
                        await websocket.send(content)
                        print(f"Sent new file to client: {file_path}")
                        
                        os.remove(file_path)  # Delete the file after sending
                        print(f"Deleted file after sending: {file_path}")
                    except Exception as e:
                        print(f"Error while processing file {file_path}: {e}")
            
            existing_files = current_files
            await asyncio.sleep(2)  # Check for new files every 2 seconds
    except Exception as e:
        print(f"Error while monitoring files: {e}")


async def handle_client(websocket):
    print("Client connected.")

    # Start a separate task to monitor and send .txt files
    asyncio.create_task(monitor_and_send_txt_files(websocket))

    try:
        async for msg in websocket:
            if isinstance(msg, str):
                print("Text message received, ignoring.")
                continue

            if len(msg) < 4:
                print("Received message is too short.")
                continue

            header_len = int.from_bytes(msg[:4], 'little')
            if len(msg) < 4 + header_len:
                print("Received message length is insufficient for the full header.")
                continue

            header_bytes = msg[4:4+header_len]
            data_chunk = msg[4+header_len:]
            try:
                header = json.loads(header_bytes)
            except json.JSONDecodeError:
                print("Header is not valid JSON.")
                continue

            message_id = header.get("message_id")
            chunk_index = header.get("chunk_index")
            total_chunks = header.get("total_chunks")

            if message_id is None or chunk_index is None or total_chunks is None:
                print("Header is missing required fields.")
                continue

            if message_id not in buffers:
                buffers[message_id] = [None] * total_chunks
                chunks_received[message_id] = 0
                total_chunks_map[message_id] = total_chunks

            if 0 <= chunk_index < total_chunks:
                if buffers[message_id][chunk_index] is None:
                    buffers[message_id][chunk_index] = data_chunk
                    chunks_received[message_id] += 1

            if chunks_received[message_id] == total_chunks_map[message_id]:
                full_message = b"".join(buffers[message_id])

                del buffers[message_id]
                del chunks_received[message_id]
                del total_chunks_map[message_id]

                print(f"Received complete message, size: {len(full_message)} bytes, message_id: {message_id}")

                asyncio.create_task(process_full_message(full_message, message_id, websocket))

    except websockets.ConnectionClosed:
        print("Client disconnected.")
    except Exception as e:
        print(f"Error while handling client connection: {e}")


async def process_full_message(full_message, message_id, websocket):
    loop = asyncio.get_event_loop()
    try:
        await loop.run_in_executor(executor, handle_message_processing, full_message, message_id)
        update_index_file(message_id)  # Update index.json with the received message ID
    except Exception as e:
        print(f"Error while processing complete message: {e}")


def handle_message_processing(full_message, message_id):
    """
    Processes the complete message, saving JSON metadata, color image, and flattened depth data.
    """
    try:
        pos = 0

        # Parse JSON metadata
        json_len = int.from_bytes(full_message[pos:pos+4], 'little')
        pos += 4
        json_data = full_message[pos:pos+json_len].decode('utf-8')
        pos += json_len
        metadata = json.loads(json_data)
        
        save_dir = "received_data"
        os.makedirs(save_dir, exist_ok=True)

        # Save JSON metadata
        metadata_path = os.path.join(save_dir, f"metadata_{message_id}.json")
        with open(metadata_path, 'w', encoding='utf-8') as f:
            json.dump(metadata, f, ensure_ascii=False, indent=4)
        print(f"Metadata saved: {metadata_path}")

        # Parse and save JPEG image
        image_len = int.from_bytes(full_message[pos:pos+4], 'little')
        pos += 4
        jpeg_data = full_message[pos:pos+image_len]
        pos += image_len

        color_image = cv2.imdecode(np.frombuffer(jpeg_data, np.uint8), cv2.IMREAD_COLOR)
        if color_image is not None:
            color_image_path = os.path.join(save_dir, f"color_{message_id}.png")
            cv2.imwrite(color_image_path, color_image)
            print(f"Color image saved: {color_image_path}")

        # Parse and save flattened depth data
        flattened_len = int.from_bytes(full_message[pos:pos+4], 'little')
        pos += 4
        flattened_bytes = full_message[pos:pos+flattened_len]
        flattened_data = np.frombuffer(flattened_bytes, dtype=np.float32)
        
        if flattened_data.size == DEPTH_WIDTH * DEPTH_HEIGHT * 3:
            flattened_data = flattened_data.reshape((DEPTH_HEIGHT, DEPTH_WIDTH, 3))
            npy_path = os.path.join(save_dir, f"flattened_{message_id}.npy")
            np.save(npy_path, flattened_data)
            print(f"Flattened data saved: {npy_path}")

    except Exception as e:
        print(f"Error while processing complete message: {e}")


def stdin_monitor(loop):
    while not stop_event.is_set():
        try:
            command = input("Type 'quit' to stop the server: ")
            if command.strip().lower() == 'quit':
                asyncio.run_coroutine_threadsafe(stop_server(loop), loop)
                break
        except EOFError:
            break


async def stop_server(loop):
    print("Stopping server...")
    stop_event.set()
    loop.stop()


async def main():
    delete_existing_txt_files()

    server = await websockets.serve(handle_client, "0.0.0.0", 8765)
    print("WebSocket server started at ws://0.0.0.0:8765")

    loop = asyncio.get_running_loop()
    thread = threading.Thread(target=stdin_monitor, args=(loop,), daemon=True)
    thread.start()

    await asyncio.Event().wait()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("Server stopped by KeyboardInterrupt.")
