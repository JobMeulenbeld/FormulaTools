import json
import matplotlib.pyplot as plt
import os
import tkinter as tk
from tkinter import ttk, BooleanVar
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg, NavigationToolbar2Tk
from matplotlib.gridspec import GridSpec
import base64
import numpy as np
import copy
import re

# Function to load and parse data from selected files
def load_and_parse_data(file_paths):
    data = {"LapDistance": [], "LapTime" : []}
    
    for file_path in file_paths:
        match = re.search(r'lap_(\d+)_data', file_path)
        idx = 0
        if match:
            idx = int(match.group(1))

        with open(file_path, "r") as file:
            content = json.load(file)
            data_points = content.get("dataPoints", {}).get(str(idx), [])
            lap_distances = [point["LapDistance"] for point in data_points if point["LapDistance"] >= 0]
            lap_time = [point["LapTime"] for point in data_points if point["LapDistance"] >= 0]

            data["LapDistance"].append(lap_distances)
            data["LapTime"].append(lap_time)

            for key, value in data_points[0]["CarTelemetryData"].items():
                if isinstance(value, list):
                    # Handle nested arrays
                    if all(isinstance(sub_val, (int, float)) for sub_val in value):
                        key_values = [[point["CarTelemetryData"].get(key, [0])[i] for point in data_points if point["LapDistance"] >= 0] for i in range(len(value))]
                        for i, sub_values in enumerate(key_values):
                            full_key = f"{key}[{i}]"
                            if full_key not in data:
                                data[full_key] = []
                            data[full_key].append(sub_values)
                    else:
                        # Flatten nested lists or arrays
                        for i, sub_value in enumerate(value):
                            full_key = f"{key}[{i}]"
                            sub_key_values = [point["CarTelemetryData"].get(key, [])[i] for point in data_points if point["LapDistance"] >= 0]
                            if full_key not in data:
                                data[full_key] = []
                            data[full_key].append(sub_key_values)
                else:
                    key_values = [point["CarTelemetryData"].get(key, 0) for point in data_points if point["LapDistance"] >= 0]
                    if key not in data:
                        data[key] = []
                    data[key].append(key_values)
            
            # Process CarMotionData
            for key, value in data_points[0]["CarMotionData"].items():
                if isinstance(value, list):
                    if all(isinstance(sub_val, (int, float)) for sub_val in value):
                        key_values = [[point["CarMotionData"].get(key, [0])[i] for point in data_points if point["LapDistance"] >= 0] for i in range(len(value))]
                        for i, sub_values in enumerate(key_values):
                            full_key = f"{key}[{i}]"
                            if full_key not in data:
                                data[full_key] = []
                            data[full_key].append(sub_values)
                else:
                    key_values = [point["CarMotionData"].get(key, 0) for point in data_points if point["LapDistance"] >= 0]
                    if key not in data:
                        data[key] = []
                    data[key].append(key_values)
            

    return data



# Function to format lap time into "min:sec:milli"
def format_lap_time(lap_time):
    minutes = lap_time // 60000
    seconds = (lap_time % 60000) // 1000
    milliseconds = lap_time % 1000
    return f"{minutes}:{seconds:02}:{milliseconds:03}"

# Get all JSON files in the current directory
json_files = [f for f in os.listdir() if f.endswith(".json")]

json_files = sorted(json_files, key=lambda x: int(x.split('_')[1]))
# Format the file names for the listbox and store mapping in a dictionary
def get_formatted_file_names(json_files):
    formatted_files = {}
    for file in json_files:
        with open(file, "r") as f:
            try:
                content = json.load(f)
                lap_time = content.get("laptime", 0)
                formatted_time = format_lap_time(lap_time)
                match = re.search(r'lap_(\d+)_data', file)
                idx = 0
                if match:
                    idx = int(match.group(1))
                new_filename = f"Lap {idx}: {formatted_time}"
                formatted_files[new_filename] = file
            except Exception as e:
                print(f"Error reading file {file}: {e}")
                new_filename = f"Lap ({len(formatted_files) + 1}): Invalid Format"
                formatted_files[new_filename] = file
    return formatted_files

def find_formatted_file(selected_file, formatted_files):
    match = re.search(r'lap_(\d+)_data', selected_file)
    idx = 0
    if match:
        idx = int(match.group(1))
    
    file = [f for f in formatted_files if f"Lap {idx}:" in f]
    return file[0]

def is_close(a, b, tolerance):
    return abs(a - b) <= tolerance


def find_delta(selected_files):
    fastest_time = 10000000
    fastest_file = ""
    for lap in selected_files:
        with open(lap, "r") as file:
            content = json.load(file)
            laptime = content.get("laptime", 0)
            if laptime < fastest_time: 
                fastest_time = laptime
                fastest_file = lap

    # Iterate throuh other laps and compare to closest time point
    
    
    reference_delta = load_and_parse_data([fastest_file])
    reference_arrays = reference_delta.get("LapDistance", 0)
    reference_time_arrays = reference_delta.get("LapTime", 0)
    reference_time_array = reference_time_arrays[0]
    reference_array = reference_arrays[0]

    reference_timed_array = []
    reference_distances_array = []
    compare_array = np.linspace(0, int(reference_array[len(reference_array)-1]), 1000)

    for i, val in enumerate(reference_array):
        # Calculate the absolute difference with all values in compare_array
        differences = np.abs(compare_array - val)
        # Find the index of the smallest difference
        closest_index = np.argmin(differences)
        # Append the closest value to the mapped array
        closest_value = compare_array[closest_index]
        # Append the value if it doesn't already exist in the mapped array
        if closest_value not in reference_distances_array:
            reference_timed_array.append(reference_time_array[i])
            reference_distances_array.append(closest_value)

    files_selected = copy.deepcopy(selected_files)
    files_selected.remove(fastest_file)

    data = load_and_parse_data(files_selected)
    lap_distance_arrays = data.get("LapDistance")
    lap_time_arrays = data.get("LapTime")

    lap_delta_distance_arrays = []
    lap_delta_time_arrays = []

    for j, array in enumerate(lap_distance_arrays):
        lap_delta_distance_arrays.append([])
        lap_delta_time_arrays.append([])

        for i, val in enumerate(array):
            # Calculate the absolute difference with all values in compare_array
            differences = np.abs(compare_array - val)
            # Find the index of the smallest difference
            closest_index = np.argmin(differences)
            # Append the closest value to the mapped array
            closest_value = compare_array[closest_index]
            # Append the value if it doesn't already exist in the mapped array
            if closest_value not in lap_delta_distance_arrays[j]:
                lap_delta_time_arrays[j].append(lap_time_arrays[j][i] - reference_timed_array[len(lap_delta_time_arrays[j])])
                lap_delta_distance_arrays[j].append(closest_value)
    
    return lap_delta_distance_arrays, lap_delta_time_arrays

# Function to plot data dynamically in subplots
def plot_data(selected_files, selected_keys, main_canvas, main_figure, map_canvas, map_figure, combine_plots):
    data = load_and_parse_data(selected_files)

    main_figure.clear()
    map_figure.clear()

    # Dictionaries to store plots and their data for interactivity
    main_plot_dict = {}
    map_plot_dict = {}

    # Load the fastest lap for the map
    fastest_time = float('inf')
    fastest_file = ""
    for lap in json_files:
        with open(lap, "r") as file:
            content = json.load(file)
            laptime = content.get("laptime", 0)
            if laptime < fastest_time: 
                fastest_time = laptime
                fastest_file = lap

    data_map = load_and_parse_data([fastest_file])

    # Plot the map in the map figure
    ax_map = map_figure.add_subplot(111)
    for i, lap_distances in enumerate(data_map["LapDistance"]):
        world_position_x = data_map["WorldPositionX"][i]
        world_position_z = [-z for z in data_map["WorldPositionZ"][i]]  # Inverted to make sense
        line, = ax_map.plot(world_position_x, world_position_z, marker="o", markersize=1, label=f"Map {find_formatted_file(selected_files[i], formatted_file_names)}")
        map_plot_dict[line] = {"x": world_position_x, "y": world_position_z, "shared": lap_distances, "ax": ax_map, "is_primary": (i == 0)}
    
    # Remove axis labels and ticks for the map
    ax_map.set_xticks([])
    ax_map.set_yticks([])
    ax_map.tick_params(axis="both", which="both", length=0)

    # Plot telemetry data in the main figure
    if not combine_plots:
        num_plots = len(selected_keys)
        cols = 2 if num_plots > 1 else 1
        rows = -(-num_plots // cols)

        gs = GridSpec(rows, cols, figure=main_figure)

        for idx, key in enumerate(selected_keys):
            ax = main_figure.add_subplot(gs[idx])

            if key == "Delta":
                distances, times = find_delta(selected_files)
                for i, distance_array in enumerate(distances):
                    line, = ax.plot(distance_array, times[i], marker="o", markersize=1, label=f"Delta {find_formatted_file(selected_files[i], formatted_file_names)}")
                    main_plot_dict[line] = {"x": distance_array, "y": times[i], "shared": distance_array, "ax": ax, "is_primary": (i == 0)}
            else:
                for i, lap_distances in enumerate(data["LapDistance"]):
                    line, = ax.plot(lap_distances, data[key][i], marker="o", markersize=1, label=f"{key} {find_formatted_file(selected_files[i], formatted_file_names)}")
                    main_plot_dict[line] = {"x": lap_distances, "y": data[key][i], "shared": lap_distances, "ax": ax, "is_primary": (i == 0)}

            ax.set_title(f"{key}", fontsize=16-rows)
            ax.tick_params(axis="both", which="major", labelsize=16-rows)
            ax.legend()
            ax.grid()
    else:
        ax = main_figure.add_subplot(111)
        for key in selected_keys:
            if key == "Delta":
                distances, times = find_delta(selected_files)
                for i, distance_array in enumerate(distances):
                    line, = ax.plot(distance_array, times[i], marker="o", markersize=1, label=f"Delta {find_formatted_file(selected_files[i], formatted_file_names)}")
                    main_plot_dict[line] = {"x": distance_array, "y": times[i], "shared": distance_array, "ax": ax, "is_primary": (i == 0)}
            else:
                for i, lap_distances in enumerate(data["LapDistance"]):
                    line, = ax.plot(lap_distances, data[key][i], marker="o", markersize=1, label=f"{key} {find_formatted_file(selected_files[i], formatted_file_names)}")
                    main_plot_dict[line] = {"x": lap_distances, "y": data[key][i], "shared": lap_distances, "ax": ax, "is_primary": (i == 0)}

            ax.tick_params(axis="both", which="major", labelsize=16)
            ax.legend()
            ax.grid()

    # Add interactive highlighting
    highlight_points_main = []
    highlight_points_map = []

    for ax in main_figure.axes:
        highlight_point, = ax.plot([], [], "ro", markersize=8, zorder=5)
        highlight_points_main.append(highlight_point)

    for ax in map_figure.axes:
        highlight_point, = ax.plot([], [], "ro", markersize=8, zorder=5)
        highlight_points_map.append(highlight_point)

    # Hover event handler
    def on_hover(event):
        if combine_plots:
            return  # Disable hover functionality if separate_plots is False

        # Check if hovering over the main figure
        if event.canvas.figure == main_figure and event.inaxes:
            for line, data in main_plot_dict.items():
                if data.get("is_primary", False) and line.contains(event)[0]:  # Only hover on primary lines
                    ind = line.contains(event)[1]["ind"][0]
                    shared_value = data["shared"][ind]

                    # Highlight points in the main figure
                    for highlight, h_data in zip(highlight_points_main, main_plot_dict.values()):
                        idx = np.abs(np.array(h_data["shared"]) - shared_value).argmin()
                        highlight.set_data([h_data["x"][idx]], [h_data["y"][idx]])
                        highlight.set_visible(True)

                    # Highlight points in the map figure
                    for highlight, h_data in zip(highlight_points_map, map_plot_dict.values()):
                        idx = np.abs(np.array(h_data["shared"]) - shared_value).argmin()
                        highlight.set_data([h_data["x"][idx]], [h_data["y"][idx]])
                        highlight.set_visible(True)

                    main_figure.canvas.draw_idle()
                    map_figure.canvas.draw_idle()
                    return

        # Check if hovering over the map figure
        if event.canvas.figure == map_figure and event.inaxes:
            for line, data in map_plot_dict.items():
                if line.contains(event)[0]:  # Check if the mouse is hovering over a line in the map
                    ind = line.contains(event)[1]["ind"][0]
                    shared_value = data["shared"][ind]

                    # Highlight points in the map figure
                    for highlight, h_data in zip(highlight_points_map, map_plot_dict.values()):
                        idx = np.abs(np.array(h_data["shared"]) - shared_value).argmin()
                        highlight.set_data([h_data["x"][idx]], [h_data["y"][idx]])
                        highlight.set_visible(True)

                    # Highlight points in the main figure
                    for highlight, h_data in zip(highlight_points_main, main_plot_dict.values()):
                        idx = np.abs(np.array(h_data["shared"]) - shared_value).argmin()
                        highlight.set_data([h_data["x"][idx]], [h_data["y"][idx]])
                        highlight.set_visible(True)

                    main_figure.canvas.draw_idle()
                    map_figure.canvas.draw_idle()
                    return

        # Hide highlights if no valid hover
        for highlight in highlight_points_main:
            highlight.set_visible(False)
        for highlight in highlight_points_map:
            highlight.set_visible(False)
        main_figure.canvas.draw_idle()
        map_figure.canvas.draw_idle()

    # Connect hover events to both figures
    main_figure.canvas.mpl_connect("motion_notify_event", on_hover)
    map_figure.canvas.mpl_connect("motion_notify_event", on_hover)

    # Draw both canvases
    main_figure.tight_layout()
    main_canvas.draw()
    map_figure.tight_layout()
    map_canvas.draw()



# Load a sample file to dynamically populate the checkboxes
if json_files:
    with open(json_files[0], "r") as sample_file:
        sample_data = json.load(sample_file)
        data_dict = sample_data["dataPoints"]
        for lap, data_points in data_dict.items():
            if data_points:
                # Extract keys, accounting for nested arrays
                keys = []
                if len(json_files) > 1:
                    keys.append("Delta")
                for key, value in data_points[0]["CarTelemetryData"].items():
                    if isinstance(value, list):
                        # Add a key for each index of the array
                        for i in range(len(value)):
                            keys.append(f"{key}[{i}]")
                    else:
                        # Add regular keys
                        keys.append(key)
            else:
                keys = []
else:
    keys = []

formatted_file_dict = get_formatted_file_names(json_files)
formatted_file_names = list(formatted_file_dict.keys())

# Create GUI
root = tk.Tk()
root.title("F1-23 Telemetry Plotter")
root.geometry("1920x1080")

# Handle proper application exit
def on_close():
    root.quit()
    root.destroy()

root.protocol("WM_DELETE_WINDOW", on_close)

# Create frames for layout
main_frame = tk.Frame(root)
main_frame.pack(side=tk.TOP, fill=tk.BOTH, expand=True)

left_frame = tk.Frame(main_frame)
left_frame.pack(side=tk.LEFT, fill=tk.Y, padx=10, pady=10)

right_frame = tk.Frame(main_frame)
right_frame.pack(side=tk.RIGHT, fill=tk.BOTH, expand=False, padx=10, pady=10)

middle_frame = tk.Frame(main_frame)
middle_frame.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True, padx=10, pady=10)

bottom_frame = tk.Frame(root)
bottom_frame.pack(side=tk.BOTTOM, fill=tk.X, padx=10, pady=10)

# Create a matplotlib figure and canvas
main_figure = plt.figure(figsize=(10, 10))
main_canvas = FigureCanvasTkAgg(main_figure, master=middle_frame)
main_canvas_widget = main_canvas.get_tk_widget()
main_canvas_widget.pack(fill=tk.BOTH, expand=True)

# Add Matplotlib toolbar
toolbar = NavigationToolbar2Tk(main_canvas, bottom_frame)
toolbar.update()

# Create a listbox to select files
file_listbox = tk.Listbox(left_frame, selectmode=tk.MULTIPLE, height=20)
for formatted_file in formatted_file_names:
    file_listbox.insert(tk.END, formatted_file)
file_listbox.pack(fill=tk.BOTH, expand=True)

# Create checkboxes for selecting keys
checkbox_frame = ttk.LabelFrame(right_frame, text="Telemetry Keys")
checkbox_frame.pack(fill=tk.Y, padx=10, pady=10)

key_vars = {}
for key in keys:
    var = BooleanVar()
    chk = ttk.Checkbutton(checkbox_frame, text=key, variable=var)
    chk.pack(anchor="w", pady=2)
    key_vars[key] = var

map_figure = plt.figure(figsize=(10, 10))
map_canvas = FigureCanvasTkAgg(map_figure, master=right_frame)
map_canvas_widget = map_canvas.get_tk_widget()
map_canvas_widget.pack(fill=tk.BOTH, expand=False)
right_frame.pack_propagate(False)  # Prevent dynamic resizing of the frame
# Set a fixed size for the right_frame
right_frame.config(width=300, height=300)  # Adjust the size (pixels) as needed



# Add a checkbox for separate plots
comnbine_plots_var = BooleanVar()
combine_plots_chk = ttk.Checkbutton(left_frame, text="Combine plots", variable=comnbine_plots_var)
combine_plots_chk.pack(pady=10)

# Bind selection event to update plot
def on_selection_change(event=None):
    selected_files = [formatted_file_dict[file_listbox.get(idx)] for idx in file_listbox.curselection()]
    selected_keys = [key for key, var in key_vars.items() if var.get()]

    if selected_files and selected_keys:
        plot_data(selected_files, selected_keys, main_canvas, main_figure, map_canvas, map_figure, comnbine_plots_var.get())

file_listbox.bind("<<ListboxSelect>>", on_selection_change)
for var in key_vars.values():
    var.trace_add("write", lambda *args: on_selection_change(None))
comnbine_plots_var.trace_add("write", lambda *args: on_selection_change(None))

# Run the GUI
root.mainloop()
