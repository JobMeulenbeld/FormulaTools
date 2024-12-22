# FormulaTools
This project contains 3 parts:
- Collect data from the game and forward it to an MQTT server (F1-23-Telemetry)
- Listen to the MQTT server and gather all car telemetry data into JSON files per lap (F1-23-Engineer)
- Read in the JSON files and graphically plot the data (F1-23-Telemetry-Plotter)
  
## Setup

- Install mosquitto MQTT
- Create custom Services
- Enable custom Services
- Play the game
- Plot the data

### Install MQTT

Install mosqtuitto MQTT "https://mosquitto.org/download/".
Follow the install and enable all featurs like Add to path

### In CMD (Administrator mode):

- sc create F1-23-Telemetry binPath="C:\Your\Path\To\FormulaTools\F1-23-Telemetry\F1-23-Telemetry\bin\Release\net8.0\win-x64\F1-23-Telemetry.exe"
- sc create F1-23-Engineer binPath="C:\Your\Path\To\FormulaTools\F1-23-Engineer\F1-23-Engineer\bin\Release\net8.0\win-x64\F1-23-Engineer.exe"

### Go to Windows Services

Find the F1-23-Telemetry and F1-23-Engineer and start them.

### Check if working properly "optional"

- Press Windows + R then type eventvwr
- Go to Windows Logs -> Application
- See the error messages (yeah I know) for the Engineer look for: "Subscribing to ....." and for the Telemetry look for: "Listening for telemetry data on port 20777"
- If you get other (real) errors, try to turn them on and off in the Services (a programmers best friend :D)

### Settings in game

In the game, go to settings and then Telemetry settings. Enable the telemetry settings and put it on 60Hz (lower is possible if you notice performance issues).

### Start the Telemetry plotter

All lap files will be stored in C:\Users\F1_Telemetry. Get the lap files you want and place them with the main.py in the F1-23-Telemetry-Plotter folder.
Run main.py using python (install the needed packages). This will now display your laps and the telemetry received from it!
