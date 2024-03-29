# Virtual Hand Interface
Developed by Dominik I. Braun.

## What is it?
The virtual hand interface can be used in experiments where the user needs to show the subjects a specific hand movement and then asks the subject to follow the movement of the "control" hand. The predicted movement by a classifier/machine learning/... is then seen at the "prediction" hand. 

## Run the application
1) Download the repository 
2) Open the "Builds" folder within the repository
3) Select the respective folder for your OS
4) Run the Executables
4.1) MacOS: Run VirtualHandInterface.app
4.2) Windows: Open Build folder and run VirtualHandInterface.exe
6) Choose Classifier in the application

## Stream data to the predicted hand
The streaming interface to the predicted hand is based on the User Datagram Protocol (UDP). The IP of your computer is automatically read and used as default. If you want to run the VirtualHandInterface and your sorce program on the same device, toggle the IP toggle to change to Localhost ("127.0.0.1"). Click "connect" to receive data from your software running in the background and "disconnect" to stop the incoming data. 

## Format of the data
The application is written to receive a string with 9 comma separated values in the shape of an array. The values represent:
- value 0: thumb flexion
- value 1: thumb abduction
- value 2: index flexion
- value 3: middle flexion
- value 4: ring flexion
- value 5: pinky flexion
- value 6: wrist flexion/extension # Not working atm
- value 7: Empty
- value 8: wrist supination/pronation # Not working atm

Example String: "[value 0, value 1, value 2, value 3, value 4, value 5, value 6, value 7, value 8]". The string has to be encoded into utf-8 bytes. 

## Record rotations of control and predicted hand
Click on the "record" button on the left side to start recording data and press "stop recording" when you want to stop the recordings. The recorded data is stored in the ".\Build\VirtualHandInterface_Data\Data\" folder. The name of the recorded file for the control hand is called "Predicted_Unity.csv" and for the predicted hand "Control_Unity.csv".

If multiple recordings are needed, pay attention and rename the recorded files after each recording session as they are overwritten. 

## Change the frequency, holding and resting phases of the control hand
If changes of the movement frequency, holding and resting phases are need adjust the values of the sliders on the right side. The movement of the control hand is written as a sine curve. 
