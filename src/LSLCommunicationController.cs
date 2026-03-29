using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
// NOTE: NOT using SharpLSL directly to avoid static initialization hang
// Instead using LSLWrapper which loads LSL via reflection

public partial class LSLCommunicationController : Node
{
	[Export] public string PredictionStreamName = "MyoGestic_Output";
	[Export] public string PredictionStreamType = "MyoGestic_9DVector";
	[Export] public string ControlOutletName = "VHI_Control";
	[Export] public string PredictedOutletName = "VHI_Predict";
	[Export] public string MovementStateOutletName = "VHI_MovementState";
	[Export] public string MenuStateOutletName = "VHI_MenuState";
	[Export] public int ExpectedChannels = 9;
	[Export] public bool EnableOutlets = true;  // Can disable outlets if they cause issues

	private object predictionInlet;  // Actually StreamInlet, but using object to avoid type reference
	private object controlOutlet;    // Actually StreamOutlet
	private object predictedOutlet;  // Actually StreamOutlet
	private object movementStateOutlet;  // Actually StreamOutlet
	private object menuStateOutlet;  // Actually StreamOutlet

	// References to hand skeletons for reading current state
	private ControlHandSkeleton controlHand;
	private PredictedHandSkeleton predictedHand;

	private List<float> receivedDataControl = [];
	private List<float> receivedDataPredicted = [];
	private float[] sampleBuffer;

	public new bool IsConnected { get; private set; } = false;  // 'new' to hide base class member
	public int LastInputFPS { get; private set; } = 0;
	public int LastOutputFPS { get; private set; } = 0;

	private DateTime lastInputTime;
	private DateTime lastOutputTime;
	private DateTime lastConnectionAttempt;
	private int inputFrameCount = 0;
	private int outputFrameCount = 0;
	private float connectionRetryInterval = 5.0f;
	private bool isConnecting = false;  // Flag to track if connection attempt is in progress
	private volatile bool isShuttingDown = false;  // Signal background tasks to stop
	private readonly object connectionLock = new();  // Lock for thread-safe connection status

	public override void _Ready()
	{
		GD.Print("=== LSL Communication Controller _Ready() START ===");

		// Get references to hand skeletons
		controlHand = GetNode<ControlHandSkeleton>("/root/Main/ControlHand");
		predictedHand = GetNode<PredictedHandSkeleton>("/root/Main/PredictedHand");
		GD.Print("  Hand skeleton references obtained");

		// Initialize LSL wrapper
		GD.Print("  Initializing LSL wrapper...");
		LSLWrapper.Initialize();

		// Initialize sample buffer
		sampleBuffer = new float[ExpectedChannels];
		GD.Print("  Sample buffer initialized");

		// Initialize timestamps
		lastInputTime = DateTime.Now;
		lastOutputTime = DateTime.Now;
		lastConnectionAttempt = DateTime.Now.AddSeconds(-connectionRetryInterval); // Allow immediate first attempt
		GD.Print("  Timestamps initialized");

		// Create LSL outlets (optional, can be disabled)
		if (EnableOutlets)
		{
			GD.Print("  Creating LSL outlets...");
			CreateOutlets();
		}
		else
		{
			GD.Print("  LSL outlets DISABLED");
		}
		GD.Print("=== LSL Communication Controller _Ready() COMPLETE ===");
	}

	public override void _Process(double delta)
	{
		// Try to connect if not connected and not already trying
		if (predictionInlet == null && !isConnecting && !isShuttingDown &&
			(DateTime.Now - lastConnectionAttempt).TotalSeconds >= connectionRetryInterval)
		{
			// Start connection attempt on background thread
			lock (connectionLock)
			{
				if (!isConnecting)  // Double-check inside lock
				{
					isConnecting = true;
					lastConnectionAttempt = DateTime.Now;
					Task.Run(() => ConnectToInletAsync());
				}
			}
		}

		// Pull data if connected
		if (predictionInlet != null)
		{
			try
			{
				// Pull ALL available samples to avoid choppy visualization
				// Keep only the most recent sample
				double timestamp = 0;
				int samplesThisFrame = 0;
				
				while (true)
				{
					double ts = LSLWrapper.PullSample(predictionInlet, sampleBuffer, 0.0);
					if (ts <= 0)
						break;  // No more samples available
					
					timestamp = ts;
					samplesThisFrame++;
					
					// Update data with latest sample
					List<float> data = [.. sampleBuffer];
					receivedDataControl = [.. data];
					receivedDataPredicted = [.. data];
				}

				if (samplesThisFrame > 0)
				{
					// Update input FPS
					inputFrameCount += samplesThisFrame;
					var timeSinceLastInput = (DateTime.Now - lastInputTime).TotalSeconds;
					if (timeSinceLastInput >= 1.0)
					{
						LastInputFPS = (int)(inputFrameCount / timeSinceLastInput);
						inputFrameCount = 0;
						lastInputTime = DateTime.Now;
					}
				}
			}
			catch (Exception e)
			{
				GD.PrintErr($"Error pulling LSL sample: {e.Message}");
				predictionInlet = null;
				IsConnected = false;
				// Clear stale data when disconnected
				receivedDataControl.Clear();
				receivedDataPredicted.Clear();
			}
		}

		// Update output FPS
		var timeSinceLastOutput = (DateTime.Now - lastOutputTime).TotalSeconds;
		if (timeSinceLastOutput >= 1.0)
		{
			LastOutputFPS = (int)(outputFrameCount / timeSinceLastOutput);
			outputFrameCount = 0;
			lastOutputTime = DateTime.Now;
		}
	}

	private void ConnectToInletAsync()
	{
		try
		{
			if (isShuttingDown) { lock (connectionLock) isConnecting = false; return; }

			// This runs on a background thread, so use CallDeferred for GD.Print
			CallDeferred(nameof(LogMessage), $"🔍 Searching for LSL stream: {PredictionStreamName}...");

			// Only resolve by name to avoid connecting to VHI outlets
			var availableStreams = LSLWrapper.Resolve("name", PredictionStreamName, 1.0);

			if (isShuttingDown) { lock (connectionLock) isConnecting = false; return; }

			if (availableStreams != null && availableStreams.Length > 0)
			{
				// Use the first matching stream
				lock (connectionLock)
				{
					predictionInlet = LSLWrapper.CreateStreamInlet(availableStreams[0]);
					IsConnected = true;
				}

				string streamName = LSLWrapper.GetStreamInfoName(availableStreams[0]);
				int channelCount = LSLWrapper.GetStreamInfoChannelCount(availableStreams[0]);
				CallDeferred(nameof(LogMessage), $"✅ Connected to LSL inlet: {streamName} ({channelCount} channels)");
			}
			else
			{
				// Don't print "not found" message every time to avoid spam
				// Only print on first attempt or every 10th attempt
				lock (connectionLock)
				{
					IsConnected = false;
				}
				// Clear any stale data when no stream is found
				CallDeferred(nameof(ClearReceivedData));
			}
		}
		catch (Exception e)
		{
			CallDeferred(nameof(LogError), $"❌ Error connecting to LSL inlet: {e.Message}");
			lock (connectionLock)
			{
				predictionInlet = null;
				IsConnected = false;
			}
		}
		finally
		{
			// Always reset the connecting flag
			lock (connectionLock)
			{
				isConnecting = false;
			}
		}
	}

	// Helper methods for thread-safe logging
	private void LogMessage(string message)
	{
		GD.Print(message);
	}

	private void LogError(string message)
	{
		GD.PrintErr(message);
	}

	private void ClearReceivedData()
	{
		receivedDataControl.Clear();
		receivedDataPredicted.Clear();
	}

	private void CreateOutlets()
	{
		GD.Print("    ENTERING CreateOutlets()...");
		try
		{
			// Get the absolute path to the movements config file
			string configPath = ProjectSettings.GlobalizePath("user://movements.toml");
			
			GD.Print("    Creating control hand StreamInfo...");
			// Create control hand outlet
			var controlInfo = LSLWrapper.CreateStreamInfo(
				name: ControlOutletName,
				type: "MyoGestic_9DVector",
				channelCount: ExpectedChannels,
				nominalSrate: 60.0,
				channelFormat: "Float",
				sourceId: "control_hand_001"
			);
			GD.Print("    StreamInfo created successfully");
			
			// Add channel labels and config path
			string[] channelLabels = [
				"ThumbFlexion", "ThumbAbduction", "IndexFlexion",
				"MiddleFlexion", "RingFlexion", "PinkyFlexion",
				"WristFlexion", "WristAbduction", "WristRotation"
			];
			GD.Print($"    Setting {channelLabels.Length} channel labels...");
			LSLWrapper.SetChannelLabels(controlInfo, channelLabels);
			LSLWrapper.SetStreamMetadata(controlInfo, "config_file", configPath);

			GD.Print("    Creating StreamOutlet...");
			controlOutlet = LSLWrapper.CreateStreamOutlet(controlInfo);
			GD.Print($"    \u2705 Control outlet created successfully!");

			GD.Print("    Creating predicted hand StreamInfo...");
			// Create predicted hand outlet
			var predictedInfo = LSLWrapper.CreateStreamInfo(
				name: PredictedOutletName,
				type: "MyoGestic_9DVector",
				channelCount: ExpectedChannels,
				nominalSrate: 60.0,
				channelFormat: "Float",
				sourceId: "predicted_hand_001"
			);
			GD.Print("    StreamInfo created successfully");

			LSLWrapper.SetChannelLabels(predictedInfo, channelLabels);
			LSLWrapper.SetStreamMetadata(predictedInfo, "config_file", configPath);

			GD.Print("    Creating StreamOutlet...");
			predictedOutlet = LSLWrapper.CreateStreamOutlet(predictedInfo);
			GD.Print($"    \u2705 Predicted outlet created successfully!");

			// Movement state outlet will be created on-demand when first movement is initiated

			GD.Print("    Creating menu state StreamInfo...");
			// Create menu state outlet (irregular JSON string stream)
			// JSON format: {"speed":float, "holdingTime":float, "restingTime":float,
			//               "smoothingEnabled":bool, "smoothingSpeed":float, "isRightHand":bool}
			var menuStateInfo = LSLWrapper.CreateStreamInfo(
				name: MenuStateOutletName,
				type: "JSON",
				channelCount: 1,
				nominalSrate: 0.0,  // Irregular stream
				channelFormat: "String",
				sourceId: "vhi_menustate_001"
			);
			GD.Print("    StreamInfo created successfully");

			// Build initial state from current hand configuration
			string initialState = $@"{{""speed"":{controlHand.Frequency},""holdingTime"":{controlHand.HoldTime},""restingTime"":{controlHand.RestTime},""smoothingEnabled"":{(predictedHand.EnableSmoothing ? "true" : "false")},""smoothingSpeed"":{predictedHand.SmoothingSpeed},""isRightHand"":false}}";
			LSLWrapper.SetStreamMetadata(menuStateInfo, "initial_state", initialState);

			GD.Print("    Creating StreamOutlet...");
			menuStateOutlet = LSLWrapper.CreateStreamOutlet(menuStateInfo);
			GD.Print($"    \u2705 Menu state outlet created successfully!");

			// Send initial state
			string[] initialMenuSample = [initialState];
			LSLWrapper.PushSample(menuStateOutlet, initialMenuSample);
			GD.Print("    Initial menu state sent");
		}
		catch (Exception e)
		{
			GD.PrintErr($"\u274c Error creating LSL outlets: {e.Message}");
			GD.PrintErr($"    Stack trace: {e.StackTrace}");
		}
		GD.Print("    EXITING CreateOutlets()");
	}

	private void CreateMovementStateOutlet()
	{
		if (movementStateOutlet != null)
			return; // Already created

		GD.Print("    Creating movement state StreamInfo...");
		try
		{
			// Create movement state outlet (irregular string stream)
			// Sends movement name strings: OpenHand, CloseHand, Pinch, ThumbsUp, Point, etc.
			var movementStateInfo = LSLWrapper.CreateStreamInfo(
				name: MovementStateOutletName,
				type: "String",
				channelCount: 1,
				nominalSrate: 0.0,  // Irregular stream
				channelFormat: "String",
				sourceId: "vhi_movementstate_001"
			);
			GD.Print("    StreamInfo created successfully");

			// Get current movement from control hand
			string currentMovement = controlHand?.GetCurrentMovementName() ?? "Rest";
			
			// Add initial state as metadata
			LSLWrapper.SetStreamMetadata(movementStateInfo, "initial_state", currentMovement);

			GD.Print("    Creating StreamOutlet...");
			movementStateOutlet = LSLWrapper.CreateStreamOutlet(movementStateInfo);
			GD.Print($"    \u2705 Movement state outlet created successfully!");

			// Send initial state
			string[] initialSample = [currentMovement];
			LSLWrapper.PushSample(movementStateOutlet, initialSample);
			GD.Print($"    Initial movement state '{currentMovement}' sent");
		}
		catch (Exception e)
		{
			GD.PrintErr($"\u274c Error creating movement state outlet: {e.Message}");
			GD.PrintErr($"    Stack trace: {e.StackTrace}");
		}
	}

	public List<float> GetReceivedDataControl()
	{
		var data = receivedDataControl;
		receivedDataControl = [];
		return data;
	}

	public List<float> GetReceivedDataPredicted()
	{
		var data = receivedDataPredicted;
		receivedDataPredicted = [];
		return data;
	}

	public void SendControlData(List<float> data)
	{
		if (controlOutlet != null && data.Count == ExpectedChannels)
		{
			try
			{
				float[] sample = [.. data];
				LSLWrapper.PushSample(controlOutlet, sample);
				outputFrameCount++;
			}
			catch (Exception e)
			{
				GD.PrintErr($"❌ Error sending control data: {e.Message}");
			}
		}
	}

	public void SendPredictedData(List<float> data)
	{
		if (predictedOutlet != null && data.Count == ExpectedChannels)
		{
			try
			{
				float[] sample = [.. data];
				LSLWrapper.PushSample(predictedOutlet, sample);
				outputFrameCount++;
			}
			catch (Exception e)
			{
				GD.PrintErr($"❌ Error sending predicted data: {e.Message}");
			}
		}
	}

	public void SendMovementState(string movementName)
	{
		// Create outlet on first use (lazy initialization)
		if (movementStateOutlet == null)
		{
			CreateMovementStateOutlet();
		}

		if (movementStateOutlet != null && !string.IsNullOrEmpty(movementName))
		{
			try
			{
				string[] sample = [movementName];
				LSLWrapper.PushSample(movementStateOutlet, sample);
			}
			catch (Exception e)
			{
				GD.PrintErr($"\u274c Error sending movement state: {e.Message}");
			}
		}
	}

	public void SendMenuState(string jsonData)
	{
		if (menuStateOutlet != null && !string.IsNullOrEmpty(jsonData))
		{
			try
			{
				string[] sample = [jsonData];
				LSLWrapper.PushSample(menuStateOutlet, sample);
			}
			catch (Exception e)
			{
				GD.PrintErr($"❌ Error sending menu state: {e.Message}");
			}
		}
	}

	public override void _ExitTree()
	{
		// Signal background tasks to stop immediately
		isShuttingDown = true;

		// Dispose LSL resources — don't wait for network cleanup
		try
		{
			LSLWrapper.Dispose(predictionInlet);
			LSLWrapper.Dispose(controlOutlet);
			LSLWrapper.Dispose(predictedOutlet);
			LSLWrapper.Dispose(movementStateOutlet);
			LSLWrapper.Dispose(menuStateOutlet);
		}
		catch (Exception) { }

		GD.Print("LSL Communication Controller stopped");
	}

	public override void _Notification(int what)
	{
		// Force exit when the window close is requested
		// LSL background threads can prevent clean shutdown
		if (what == NotificationWMCloseRequest)
		{
			isShuttingDown = true;
			GetTree().Quit();

			// Give a moment for cleanup, then force exit
			Task.Run(async () =>
			{
				await Task.Delay(1000);
				System.Environment.Exit(0);
			});
		}
	}
}
