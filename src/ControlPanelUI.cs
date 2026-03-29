using Godot;
using System.Text.Json;

public partial class ControlPanelUI : Control
{
	// References to hand controllers
	private ControlHandSkeleton controlHand;
	private PredictedHandSkeleton predictedHand;
	private LSLCommunicationController lslController;

	// Control Hand UI elements
	private HSlider speedSlider;
	private Label speedValueLabel;
	private HSlider holdingTimeSlider;
	private Label holdingTimeValueLabel;
	private HSlider restingTimeSlider;
	private Label restingTimeValueLabel;

	// Predicted Hand UI elements
	private CheckBox smoothingToggle;
	private HSlider smoothingSpeedSlider;
	private Label smoothingSpeedValueLabel;
	private VBoxContainer smoothingSpeedContainer;

	// Collapse functionality
	private Button collapseButton;
	private Panel mainPanel;
	private bool isCollapsed = true; // Start collapsed

	// Hand chirality toggle
	private CheckBox rightHandToggle;
	private Vector3 controlMeshOriginalPos;
	private Vector3 predictedMeshOriginalPos;

	// Config file buttons
	private Button openConfigButton;
	private Button loadConfigButton;
	private Label configFileLabel;

	// Freeze button
	private Button freezeButton;

	// UI scaling
	private Vector2 baseResolution = new(1152, 648); // Default window size
	private Window window;

	// Public property to access hand chirality
	public bool IsRightHand => rightHandToggle?.ButtonPressed ?? false;

	public override void _Ready()
	{
		// Get window for scaling
		window = GetWindow();

		// Get references to hand controllers
		controlHand = GetNode<ControlHandSkeleton>("/root/Main/ControlHand");
		predictedHand = GetNode<PredictedHandSkeleton>("/root/Main/PredictedHand");
		lslController = GetNode<LSLCommunicationController>("/root/Main/LSLCommunicationController");

		// Save original mesh positions
		var controlMesh = controlHand.GetNode<Node3D>("WVRLeftHand_1106_ASCII");
		var predictedMesh = predictedHand.GetNode<Node3D>("WVRLeftHand_1106_ASCII");
		controlMeshOriginalPos = controlMesh.Position;
		predictedMeshOriginalPos = predictedMesh.Position;

		// Get collapse button and panel
		collapseButton = GetNode<Button>("CollapseButton");
		mainPanel = GetNode<Panel>("Panel");

		// Set initial collapsed state
		mainPanel.Visible = !isCollapsed;
		collapseButton.Text = isCollapsed ? ">" : "<";

		// Get hand chirality toggle
		rightHandToggle = GetNode<CheckBox>("Panel/VBoxContainer/HandChiralityGroup/RightHandToggle");

		// Get config buttons
		openConfigButton = GetNode<Button>("Panel/VBoxContainer/ConfigGroup/OpenConfigButton");
		loadConfigButton = GetNode<Button>("Panel/VBoxContainer/ConfigGroup/LoadConfigButton");
		configFileLabel = GetNode<Label>("Panel/VBoxContainer/ConfigGroup/ConfigFileLabel");


		// Get freeze button
		freezeButton = GetNode<Button>("Panel/VBoxContainer/ControlHandGroup/FreezeButton");

		// Set initial config file label
		UpdateConfigFileLabel();

		// Get UI element references
		speedSlider = GetNode<HSlider>("Panel/VBoxContainer/ControlHandGroup/SpeedContainer/SpeedSlider");
		speedValueLabel = GetNode<Label>("Panel/VBoxContainer/ControlHandGroup/SpeedContainer/SpeedValue");

		holdingTimeSlider = GetNode<HSlider>("Panel/VBoxContainer/ControlHandGroup/HoldingTimeContainer/HoldingTimeSlider");
		holdingTimeValueLabel = GetNode<Label>("Panel/VBoxContainer/ControlHandGroup/HoldingTimeContainer/HoldingTimeValue");

		restingTimeSlider = GetNode<HSlider>("Panel/VBoxContainer/ControlHandGroup/RestingTimeContainer/RestingTimeSlider");
		restingTimeValueLabel = GetNode<Label>("Panel/VBoxContainer/ControlHandGroup/RestingTimeContainer/RestingTimeValue");

		smoothingToggle = GetNode<CheckBox>("Panel/VBoxContainer/PredictedHandGroup/SmoothingToggle");
		smoothingSpeedContainer = GetNode<VBoxContainer>("Panel/VBoxContainer/PredictedHandGroup/SmoothingSpeedContainer");
		smoothingSpeedSlider = GetNode<HSlider>("Panel/VBoxContainer/PredictedHandGroup/SmoothingSpeedContainer/SmoothingSpeedSlider");
		smoothingSpeedValueLabel = GetNode<Label>("Panel/VBoxContainer/PredictedHandGroup/SmoothingSpeedContainer/SmoothingSpeedValue");

		// Set initial values from hand controllers
		speedSlider.Value = controlHand.Frequency;
		holdingTimeSlider.Value = controlHand.HoldTime;
		restingTimeSlider.Value = controlHand.RestTime;
		smoothingToggle.ButtonPressed = predictedHand.EnableSmoothing;
		smoothingSpeedSlider.Value = predictedHand.SmoothingSpeed;

		// Update labels
		UpdateSpeedLabel(speedSlider.Value);
		UpdateHoldingTimeLabel(holdingTimeSlider.Value);
		UpdateRestingTimeLabel(restingTimeSlider.Value);
		UpdateSmoothingSpeedLabel(smoothingSpeedSlider.Value);
		UpdateSmoothingSpeedVisibility(smoothingToggle.ButtonPressed);

		// Connect signals
		speedSlider.ValueChanged += OnSpeedChanged;
		holdingTimeSlider.ValueChanged += OnHoldingTimeChanged;
		restingTimeSlider.ValueChanged += OnRestingTimeChanged;
		smoothingToggle.Toggled += OnSmoothingToggled;
		smoothingSpeedSlider.ValueChanged += OnSmoothingSpeedChanged;
		collapseButton.Pressed += OnCollapseToggled;
		rightHandToggle.Toggled += OnHandChiralityToggled;
		openConfigButton.Pressed += OnOpenConfigPressed;
		loadConfigButton.Pressed += OnLoadConfigPressed;
		freezeButton.Pressed += OnFreezePressed;
	}

	private void OnSpeedChanged(double value)
	{
		controlHand.Frequency = (float)value;
		UpdateSpeedLabel(value);
	}

	private void OnHoldingTimeChanged(double value)
	{
		controlHand.HoldTime = (float)value;
		UpdateHoldingTimeLabel(value);
	}

	private void OnRestingTimeChanged(double value)
	{
		controlHand.RestTime = (float)value;
		UpdateRestingTimeLabel(value);
	}

	private void OnSmoothingToggled(bool enabled)
	{
		predictedHand.EnableSmoothing = enabled;
		UpdateSmoothingSpeedVisibility(enabled);
	}

	private void OnSmoothingSpeedChanged(double value)
	{
		predictedHand.SmoothingSpeed = (float)value;
		UpdateSmoothingSpeedLabel(value);
	}

	private void UpdateSpeedLabel(double value)
	{
		speedValueLabel.Text = $"{value:F2}";
	}

	private void UpdateHoldingTimeLabel(double value)
	{
		holdingTimeValueLabel.Text = $"{value:F1}s";
	}

	private void UpdateRestingTimeLabel(double value)
	{
		restingTimeValueLabel.Text = $"{value:F1}s";
	}

	private void UpdateSmoothingSpeedLabel(double value)
	{
		smoothingSpeedValueLabel.Text = $"{value:F1}";
	}

	private void UpdateSmoothingSpeedVisibility(bool visible)
	{
		smoothingSpeedContainer.Visible = visible;
	}

	private void OnCollapseToggled()
	{
		isCollapsed = !isCollapsed;
		mainPanel.Visible = !isCollapsed;
		collapseButton.Text = isCollapsed ? ">" : "<";

		// Send menu state when collapsing (user finished editing)
		if (isCollapsed)
		{
			SendMenuStateUpdate();
		}
	}

	private void OnHandChiralityToggled(bool isRightHand)
	{
		// Mirror hands by flipping the mesh children, not the parent nodes
		// This keeps the hands in their original positions while changing chirality
		// Right hand: positive scale (1.0), original position
		// Left hand: negative scale (-1.0), compensated position
		// Note: Due to mesh rotation, we flip Z-axis to get visual X-axis mirroring

		// TODO: Enable hand flipping when meshes are fixed
		return; // Temporarily disable hand flipping

		float Scale = isRightHand ? 1.0f : -1.0f;

		// Find mesh child nodes
		var controlMesh = controlHand.GetNode<Node3D>("WVRLeftHand_1106_ASCII");
		var predictedMesh = predictedHand.GetNode<Node3D>("WVRLeftHand_1106_ASCII");

		// Apply scale
		controlMesh.Scale = new Vector3(Scale, 1.0f, 1.0f);
		predictedMesh.Scale = new Vector3(Scale, 1.0f, 1.0f);

		// Compensate position: when flipped, mirror the X position to keep hand in place
		if (isRightHand)
		{
			controlMesh.Position = controlMeshOriginalPos;
			predictedMesh.Position = predictedMeshOriginalPos;
		}
		else
		{
			// When flipped, negate X to compensate for the flip around wrist origin
			controlMesh.Position = new Vector3(controlMeshOriginalPos.X, controlMeshOriginalPos.Y, controlMeshOriginalPos.Z);
			predictedMesh.Position = new Vector3(predictedMeshOriginalPos.X, predictedMeshOriginalPos.Y, predictedMeshOriginalPos.Z);
		}
	}

	private void SendMenuStateUpdate()
	{
		if (lslController == null) return;

		// Create a dictionary with all menu settings
		var menuState = new System.Collections.Generic.Dictionary<string, object>
		{
			{ "speed", controlHand.Frequency },
			{ "holdingTime", controlHand.HoldTime },
			{ "restingTime", controlHand.RestTime },
			{ "smoothingEnabled", predictedHand.EnableSmoothing },
			{ "smoothingSpeed", predictedHand.SmoothingSpeed },
			{ "isRightHand", rightHandToggle.ButtonPressed }
		};

		// Serialize to JSON
		string json = JsonSerializer.Serialize(menuState);

		// Send via LSL
		lslController.SendMenuState(json);
	}

	private void OnFreezePressed()
	{
		controlHand.ToggleFreeze();
		freezeButton.Text = controlHand.IsFrozen ? "Unfreeze (Space)" : "Freeze Pose (Space)";
	}

	private void UpdateConfigFileLabel()
	{
		string path = controlHand.ConfigFilePath;
		// Show just the filename
		configFileLabel.Text = path.GetFile();
		configFileLabel.TooltipText = ProjectSettings.GlobalizePath(path);
	}

	private void OnLoadConfigPressed()
	{
		string currentPath = ProjectSettings.GlobalizePath(controlHand.ConfigFilePath);
		string dir = System.IO.Path.GetDirectoryName(currentPath);
		if (!System.IO.Directory.Exists(dir))
			dir = OS.GetSystemDir(OS.SystemDir.Desktop);

		var callback = Callable.From<bool, string[], int>(OnNativeFileSelected);
		DisplayServer.FileDialogShow(
			"Load Movement Config",
			dir,
			"",
			false,
			DisplayServer.FileDialogMode.OpenFile,
			["*.toml"],
			callback
		);
	}

	private void OnNativeFileSelected(bool selected, string[] paths, int filterIndex)
	{
		if (selected && paths.Length > 0)
		{
			controlHand.LoadConfigFile(paths[0]);
			UpdateConfigFileLabel();
		}
	}

	private void OnOpenConfigPressed()
	{
		// Get the config file path from ControlHandSkeleton
		string configPath = controlHand.ConfigFilePath;

		// Convert Godot path (user://) to absolute system path
		string absolutePath = ProjectSettings.GlobalizePath(configPath);

		// Check if file exists, create it if it doesn't
		if (!System.IO.File.Exists(absolutePath))
		{
			GD.Print($"Config file not found at {absolutePath}, generating...");
			MovementConfigGenerator.GenerateDefaultConfig(absolutePath);
		}

		// Open with default system editor
		try
		{
			OS.ShellOpen(absolutePath);
			GD.Print($"Opening config file: {absolutePath}");
		}
		catch (System.Exception e)
		{
			GD.PrintErr($"Failed to open config file: {e.Message}");
		}
	}
}
